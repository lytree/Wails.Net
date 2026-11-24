using Wails.Net.Application.Menus;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using Menu = Wails.Net.Application.Menus.Menu;

namespace Wails.Net.Application.Windows.Tests;

/// <summary>
/// <see cref="Wails.Net.Application.Platform.Win32Menu"/> 的原生构建测试。
/// <para>
/// 与纯托管的 <c>MenuTests</c> 不同，本组测试直接创建真实的 <c>HMENU</c>
/// （<c>CreateMenu</c> / <c>CreatePopupMenu</c>）并通过 <c>GetMenuItemCount</c>、
/// <c>GetSubMenu</c>、<c>GetMenuItemID</c>、<c>GetMenuState</c> 回读原生状态，
/// 用于守门"托管菜单树 → 原生菜单"的映射正确性（项数、层级、分隔符、禁用、复选）。
/// </para>
/// <para>
/// 这些断言全部针对真实 Win32 句柄，因此仅在 Windows 宿主上有意义；
/// 本测试项目只在 CI 的 <c>test-windows</c> 作业（windows-latest）中运行。
/// </para>
/// </summary>
[NotInParallel]
public sealed class Win32MenuTests
{
    /// <summary>
    /// 读取指定位置菜单项的原生状态位（<c>GetMenuState</c> + <c>MF_BYPOSITION</c>）。
    /// </summary>
    /// <param name="hmenu">父菜单句柄。</param>
    /// <param name="position">菜单项位置索引（从 0 开始）。</param>
    /// <returns>原生状态位组合（MF_* 位掩码）。</returns>
    private static uint MenuStateAt(HMENU hmenu, int position)
        => PInvoke.GetMenuState(hmenu, (uint)position, MENU_ITEM_FLAGS.MF_BYPOSITION);

    // ---------------------------------------------------------------------
    // 结构与层级
    // ---------------------------------------------------------------------

    [Test]
    public async Task Build_TopLevelMenu_CountsSubmenuAsSingleItem()
    {
        var menu = new Menu();
        var fileMenu = menu.AddSubmenu("文件");
        fileMenu.AddMenuItem("打开");
        fileMenu.AddMenuItem("关闭");
        menu.AddMenuItem("退出");

        var impl = new Wails.Net.Application.Platform.Win32Menu(menu);
        try
        {
            impl.Build();

            // 子菜单在菜单栏中只占一项，其内部项不计入顶级计数。
            await Assert.That(PInvoke.GetMenuItemCount(impl.Hmenu)).IsEqualTo(2);
        }
        finally
        {
            impl.Destroy();
        }
    }

    [Test]
    public async Task Build_Submenu_ExposesItsChildrenAsNestedMenu()
    {
        var menu = new Menu();
        var fileMenu = menu.AddSubmenu("文件");
        var open = fileMenu.AddMenuItem("打开");
        var close = fileMenu.AddMenuItem("关闭");

        var impl = new Wails.Net.Application.Platform.Win32Menu(menu);
        try
        {
            impl.Build();

            var sub = PInvoke.GetSubMenu(impl.Hmenu, 0);
            await Assert.That(sub.IsNull).IsFalse();
            await Assert.That(PInvoke.GetMenuItemCount(sub)).IsEqualTo(2);

            // 命令项的 ID 必须是有效命令 ID（分隔符/弹出项为 -1）。
            var firstId = PInvoke.GetMenuItemID(sub, 0);
            var secondId = PInvoke.GetMenuItemID(sub, 1);
            await Assert.That(firstId).IsNotEqualTo(0xFFFFFFFFu);
            await Assert.That(secondId).IsNotEqualTo(0xFFFFFFFFu);
            await Assert.That(firstId).IsNotEqualTo(secondId);
            await Assert.That(open.ID).IsNotEqualTo(close.ID);
        }
        finally
        {
            impl.Destroy();
        }
    }

    [Test]
    public async Task Build_NestedSubmenu_CreatesThreeLevels()
    {
        var menu = new Menu();
        var view = menu.AddSubmenu("视图");
        var theme = view.AddSubmenu("主题");
        theme.AddMenuItem("浅色");

        var impl = new Wails.Net.Application.Platform.Win32Menu(menu);
        try
        {
            impl.Build();

            var viewMenu = PInvoke.GetSubMenu(impl.Hmenu, 0);
            var themeMenu = PInvoke.GetSubMenu(viewMenu, 0);
            await Assert.That(themeMenu.IsNull).IsFalse();
            await Assert.That(PInvoke.GetMenuItemCount(themeMenu)).IsEqualTo(1);
        }
        finally
        {
            impl.Destroy();
        }
    }

    // ---------------------------------------------------------------------
    // 项状态位
    // ---------------------------------------------------------------------

    [Test]
    public async Task Build_Separator_SetsSeparatorFlag()
    {
        var menu = new Menu();
        menu.AddMenuItem("A");
        menu.AddSeparator();
        menu.AddMenuItem("B");

        var impl = new Wails.Net.Application.Platform.Win32Menu(menu);
        try
        {
            impl.Build();

            var separatorState = MenuStateAt(impl.Hmenu, 1);
            await Assert.That((separatorState & (uint)MENU_ITEM_FLAGS.MF_SEPARATOR) != 0).IsTrue();

            // 分隔符没有分配命令 ID（GetMenuItemID 返回 0），因此不会被 WM_COMMAND 分发命中。
            var separatorId = PInvoke.GetMenuItemID(impl.Hmenu, 1);
            await Assert.That(separatorId).IsEqualTo(0u);
            await Assert.That(Wails.Net.Application.Platform.Win32Menu.TryDispatchCommand(separatorId)).IsFalse();
        }
        finally
        {
            impl.Destroy();
        }
    }

    [Test]
    public async Task Build_DisabledItem_SetsDisabledFlag()
    {
        var menu = new Menu();
        var disabled = menu.AddMenuItem("已禁用");
        disabled.IsDisabled = true;
        menu.AddMenuItem("可用");

        var impl = new Wails.Net.Application.Platform.Win32Menu(menu);
        try
        {
            impl.Build();

            await Assert.That((MenuStateAt(impl.Hmenu, 0) & (uint)MENU_ITEM_FLAGS.MF_DISABLED) != 0).IsTrue();
            await Assert.That((MenuStateAt(impl.Hmenu, 1) & (uint)MENU_ITEM_FLAGS.MF_DISABLED) != 0).IsFalse();
        }
        finally
        {
            impl.Destroy();
        }
    }

    [Test]
    public async Task Build_CheckedItem_SetsCheckedFlag()
    {
        var menu = new Menu();
        menu.AddCheckboxMenuItem("显示侧边栏", true);
        menu.AddCheckboxMenuItem("显示状态栏", false);

        var impl = new Wails.Net.Application.Platform.Win32Menu(menu);
        try
        {
            impl.Build();

            await Assert.That((MenuStateAt(impl.Hmenu, 0) & (uint)MENU_ITEM_FLAGS.MF_CHECKED) != 0).IsTrue();
            await Assert.That((MenuStateAt(impl.Hmenu, 1) & (uint)MENU_ITEM_FLAGS.MF_CHECKED) != 0).IsFalse();
        }
        finally
        {
            impl.Destroy();
        }
    }

    // ---------------------------------------------------------------------
    // 角色菜单项（MenuRole）
    // ---------------------------------------------------------------------

    [Test]
    public async Task Build_RoleItem_AppliesDefaultLabelAndCallback()
    {
        // Build 路径不经过 AddMenuItem，角色必须在构建时同样被应用，
        // 否则标准 Edit 菜单会渲染为空标签且点击无效。
        var menu = new Menu();
        var edit = menu.AddSubmenu("编辑");
        var copy = edit.AddRoleItem(MenuRole.Copy);
        var paste = edit.AddRoleItem(MenuRole.Paste);

        var impl = new Wails.Net.Application.Platform.Win32Menu(menu);
        try
        {
            impl.Build();

            await Assert.That(copy.Label).IsEqualTo("复制");
            await Assert.That(copy.Callback is not null).IsTrue();
            await Assert.That(paste.Label).IsEqualTo("粘贴");
            await Assert.That(paste.Callback is not null).IsTrue();
        }
        finally
        {
            impl.Destroy();
        }
    }

    [Test]
    public async Task Build_StandardEditMenu_ProducesSevenNativeItems()
    {
        var menu = new Menu();
        var edit = menu.AddSubmenu("编辑");
        edit.AddStandardEditMenu();

        var impl = new Wails.Net.Application.Platform.Win32Menu(menu);
        try
        {
            impl.Build();

            // Undo/Redo/Separator/Cut/Copy/Paste/SelectAll
            var editMenu = PInvoke.GetSubMenu(impl.Hmenu, 0);
            await Assert.That(PInvoke.GetMenuItemCount(editMenu)).IsEqualTo(7);
        }
        finally
        {
            impl.Destroy();
        }
    }

    [Test]
    public async Task Build_MacOsExclusiveRole_IsSilentlyIgnored()
    {
        // macOS 专属角色在 Windows 上应静默降级（不抛异常、不设置默认标签）。
        var menu = new Menu();
        var windowMenu = menu.AddSubmenu("窗口");
        var bringAll = windowMenu.AddRoleItem(MenuRole.BringAllToFront);

        var impl = new Wails.Net.Application.Platform.Win32Menu(menu);
        try
        {
            await Assert.That(HasThrown()).IsFalse();
            await Assert.That(bringAll.Label).IsNull();
            await Assert.That(bringAll.Callback is null).IsTrue();

            // 本地函数：构建过程不应抛出任何异常。
            bool HasThrown()
            {
                try
                {
                    impl.Build();
                    return false;
                }
                catch
                {
                    return true;
                }
            }
        }
        finally
        {
            impl.Destroy();
        }
    }

    // ---------------------------------------------------------------------
    // WM_COMMAND 分发
    // ---------------------------------------------------------------------

    [Test]
    public async Task TryDispatchCommand_KnownCommandId_InvokesCallback()
    {
        var clicked = false;
        var menu = new Menu();
        menu.AddMenuItem("运行", () => clicked = true);

        var impl = new Wails.Net.Application.Platform.Win32Menu(menu);
        try
        {
            impl.Build();
            var commandId = PInvoke.GetMenuItemID(impl.Hmenu, 0);

            var dispatched = Wails.Net.Application.Platform.Win32Menu.TryDispatchCommand(commandId);

            await Assert.That(dispatched).IsTrue();
            await Assert.That(clicked).IsTrue();
        }
        finally
        {
            impl.Destroy();
        }
    }

    [Test]
    public async Task TryDispatchCommand_CheckboxItem_TogglesModelAndNativeState()
    {
        var menu = new Menu();
        var item = menu.AddCheckboxMenuItem("显示侧边栏", false);

        var impl = new Wails.Net.Application.Platform.Win32Menu(menu);
        try
        {
            impl.Build();
            var commandId = PInvoke.GetMenuItemID(impl.Hmenu, 0);

            Wails.Net.Application.Platform.Win32Menu.TryDispatchCommand(commandId);

            // 模型状态翻转。
            await Assert.That(item.Checked).IsTrue();
            // 原生菜单的勾选标记必须同步刷新，否则用户点完看不到任何反馈。
            await Assert.That((MenuStateAt(impl.Hmenu, 0) & (uint)MENU_ITEM_FLAGS.MF_CHECKED) != 0).IsTrue();
        }
        finally
        {
            impl.Destroy();
        }
    }

    [Test]
    public async Task TryDispatchCommand_UnknownCommandId_ReturnsFalse()
    {
        var menu = new Menu();
        menu.AddMenuItem("任意项");

        var impl = new Wails.Net.Application.Platform.Win32Menu(menu);
        try
        {
            impl.Build();

            // 0xFFFE 未分配给任何菜单项（分隔符/弹出项使用 0xFFFFFFFF）。
            var dispatched = Wails.Net.Application.Platform.Win32Menu.TryDispatchCommand(0xFFFE);

            await Assert.That(dispatched).IsFalse();
        }
        finally
        {
            impl.Destroy();
        }
    }

    // ---------------------------------------------------------------------
    // 生命周期
    // ---------------------------------------------------------------------

    [Test]
    public async Task Build_Twice_RecreatesNativeMenuWithoutDuplicatingItems()
    {
        var menu = new Menu();
        menu.AddMenuItem("A");
        menu.AddMenuItem("B");

        var impl = new Wails.Net.Application.Platform.Win32Menu(menu);
        try
        {
            impl.Build();
            var firstHandle = impl.Hmenu;

            impl.Build();

            await Assert.That(PInvoke.GetMenuItemCount(impl.Hmenu)).IsEqualTo(2);
            // 重建应销毁旧句柄并创建新句柄，而不是在原句柄上追加。
            await Assert.That((IntPtr)impl.Hmenu).IsNotEqualTo((IntPtr)firstHandle);
        }
        finally
        {
            impl.Destroy();
        }
    }

    // ---------------------------------------------------------------------
    // 实现挂载与运行时增量更新
    // ---------------------------------------------------------------------

    [Test]
    public async Task Build_AttachesImplToMenuModel()
    {
        var menu = new Menu();
        menu.AddMenuItem("A");

        var impl = new Wails.Net.Application.Platform.Win32Menu(menu);
        try
        {
            impl.Build();

            // 挂载后 Menu.Append/Remove/Update 才能落到原生菜单上。
            await Assert.That(ReferenceEquals(menu.Impl, impl)).IsTrue();
            // 叶子项自身没有平台对象，必须能通过父容器上溯解析。
            await Assert.That(ReferenceEquals(menu.Items[0].OwningImpl, impl)).IsTrue();
        }
        finally
        {
            impl.Destroy();
        }
    }

    [Test]
    public async Task Destroy_DetachesImplFromMenuModel()
    {
        var menu = new Menu();
        menu.AddMenuItem("A");

        var impl = new Wails.Net.Application.Platform.Win32Menu(menu);
        impl.Build();
        impl.Destroy();

        // 销毁后不得残留指向已失效实现的挂载，否则后续更新会作用到已释放的句柄。
        await Assert.That(menu.Impl is null).IsTrue();
        await Assert.That(menu.Items[0].OwningImpl is null).IsTrue();
    }

    [Test]
    public async Task Append_AfterBuild_AddsNativeItemWithoutRebuilding()
    {
        var menu = new Menu();
        menu.AddMenuItem("A");

        var impl = new Wails.Net.Application.Platform.Win32Menu(menu);
        try
        {
            impl.Build();
            var originalHandle = impl.Hmenu;

            // 直接改模型，不重新 Build、也不重新 SetApplicationMenu。
            menu.AddMenuItem("B");

            await Assert.That(PInvoke.GetMenuItemCount(impl.Hmenu)).IsEqualTo(2);
            // 增量插入必须复用同一句柄：窗口已通过 SetMenu 绑定该句柄，重建会使其失效。
            await Assert.That((IntPtr)impl.Hmenu).IsEqualTo((IntPtr)originalHandle);
        }
        finally
        {
            impl.Destroy();
        }
    }

    [Test]
    public async Task Remove_AfterBuild_RemovesNativeItem()
    {
        var menu = new Menu();
        var first = menu.AddMenuItem("A");
        menu.AddMenuItem("B");

        var impl = new Wails.Net.Application.Platform.Win32Menu(menu);
        try
        {
            impl.Build();
            await Assert.That(PInvoke.GetMenuItemCount(impl.Hmenu)).IsEqualTo(2);

            menu.Remove(first);

            await Assert.That(PInvoke.GetMenuItemCount(impl.Hmenu)).IsEqualTo(1);
            // 剩下的是 B：其命令 ID 仍可分发。
            var remainingId = PInvoke.GetMenuItemID(impl.Hmenu, 0);
            await Assert.That(remainingId).IsNotEqualTo(0xFFFFFFFFu);
        }
        finally
        {
            impl.Destroy();
        }
    }

    [Test]
    public async Task Update_AfterModelChange_RefreshesNativeState()
    {
        var menu = new Menu();
        var item = menu.AddCheckboxMenuItem("显示侧边栏", false);

        var impl = new Wails.Net.Application.Platform.Win32Menu(menu);
        try
        {
            impl.Build();

            item.IsDisabled = true;
            item.Checked = true;
            item.Label = "显示左侧栏";
            menu.Update();

            var state = MenuStateAt(impl.Hmenu, 0);
            await Assert.That((state & (uint)MENU_ITEM_FLAGS.MF_DISABLED) != 0).IsTrue();
            await Assert.That((state & (uint)MENU_ITEM_FLAGS.MF_CHECKED) != 0).IsTrue();
        }
        finally
        {
            impl.Destroy();
        }
    }

    [Test]
    public async Task Update_SubmenuLabel_KeepsPopupStructure()
    {
        var menu = new Menu();
        var file = menu.AddSubmenu("文件");
        file.AddMenuItem("打开");

        var impl = new Wails.Net.Application.Platform.Win32Menu(menu);
        try
        {
            impl.Build();

            file.Label = "文档";
            menu.Update();

            // 按位置改写 popup 时若漏掉 MF_POPUP，子菜单会被替换成普通文本项。
            var sub = PInvoke.GetSubMenu(impl.Hmenu, 0);
            await Assert.That(sub.IsNull).IsFalse();
            await Assert.That(PInvoke.GetMenuItemCount(sub)).IsEqualTo(1);
            await Assert.That(PInvoke.GetMenuItemCount(impl.Hmenu)).IsEqualTo(1);
        }
        finally
        {
            impl.Destroy();
        }
    }

    [Test]
    public async Task FormatItemText_AppendsAcceleratorWithTab()
    {
        // Win32 菜单用制表符分隔标签与快捷键文本并右对齐显示；
        // 之前只有子菜单路径拼接过，叶子命令项看不到 Ctrl+C 之类的提示。
        await Assert.That(Wails.Net.Application.Platform.Win32Menu.FormatItemText("复制", "Ctrl+C"))
            .IsEqualTo("复制\tCtrl+C");
        await Assert.That(Wails.Net.Application.Platform.Win32Menu.FormatItemText("复制", null))
            .IsEqualTo("复制");
        await Assert.That(Wails.Net.Application.Platform.Win32Menu.FormatItemText("复制", string.Empty))
            .IsEqualTo("复制");
    }

    [Test]
    public async Task Destroy_IsIdempotent()
    {
        var menu = new Menu();
        menu.AddMenuItem("A");

        var impl = new Wails.Net.Application.Platform.Win32Menu(menu);
        impl.Build();

        impl.Destroy();

        // 第二次销毁应为幂等 no-op（_disposed 守卫）。
        var secondDestroyThrew = false;
        try
        {
            impl.Destroy();
        }
        catch
        {
            secondDestroyThrew = true;
        }

        await Assert.That(secondDestroyThrew).IsFalse();
    }
}
