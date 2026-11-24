using Wails.Net.Application.Menus;
using Wails.Net.Application.Platform;

namespace Wails.Net.Application.Linux.Tests;

/// <summary>
/// <see cref="LinuxMenu"/> 的构建测试。
/// <para>
/// 只验证与原生无关的契约：GMenu 模型 / ActionGroup 是否被创建，以及角色菜单项在
/// 「构造函数直接构建」路径上是否得到默认标签与回调。
/// </para>
/// <para>
/// 该路径与 Windows 的 <c>Win32Menu.Build</c> 对称——都不经过 <c>AddMenuItem</c>，
/// 因此角色必须由构建逻辑自身补齐。渲染层（PopoverMenuBar）需要真实 GTK 主循环与显示器，
/// 不在本组测试范围内。
/// </para>
/// </summary>
[NotInParallel]
public sealed class LinuxMenuTests
{
    [Test]
    [LinuxOnly]
    public async Task Constructor_BuildsMenuModelAndActionGroup()
    {
        var menu = new Menu("App");
        var file = menu.AddSubmenu("文件");
        file.AddMenuItem("打开");
        file.AddSeparator();
        file.AddMenuItem("退出");

        var impl = new LinuxMenu(menu);
        try
        {
            await Assert.That(impl.MenuModel).IsNotNull();
            await Assert.That(impl.ActionGroup).IsNotNull();
        }
        finally
        {
            impl.Destroy();
        }
    }

    [Test]
    [LinuxOnly]
    public async Task Constructor_AppliesRoleDefaultLabelAndCallback()
    {
        var menu = new Menu("App");
        var edit = menu.AddSubmenu("编辑");
        var copy = edit.AddRoleItem(MenuRole.Copy);

        var impl = new LinuxMenu(menu);
        try
        {
            // 默认标签由 MenuRoleHelper 提供；缺失说明构建路径漏掉了 ApplyRole。
            await Assert.That(copy.Label).IsEqualTo("复制");
            await Assert.That(copy.Callback is not null).IsTrue();
        }
        finally
        {
            impl.Destroy();
        }
    }

    [Test]
    [LinuxOnly]
    public async Task Constructor_AttachesImplToMenu()
    {
        var menu = new Menu("App");
        menu.AddSubmenu("文件");

        var impl = new LinuxMenu(menu);
        try
        {
            // 挂载后 Menu.Append/Remove/Update 才会重建 GMenu 模型。
            await Assert.That(ReferenceEquals(menu.Impl, impl)).IsTrue();
            // 嵌套子菜单不持有独立 LinuxMenu，必须能上溯到根实现。
            await Assert.That(ReferenceEquals(menu.Items[0].OwningImpl, impl)).IsTrue();
        }
        finally
        {
            impl.Destroy();
        }
    }

    [Test]
    [LinuxOnly]
    public async Task Destroy_DetachesImplFromMenu()
    {
        var menu = new Menu("App");
        menu.AddSubmenu("文件");

        var impl = new LinuxMenu(menu);
        impl.Destroy();

        await Assert.That(menu.Impl is null).IsTrue();
        await Assert.That(menu.Items[0].OwningImpl is null).IsTrue();
    }

    [Test]
    [LinuxOnly]
    public async Task Append_AfterConstruction_RebuildsMenuModel()
    {
        var menu = new Menu("App");
        var file = menu.AddSubmenu("文件");
        file.AddMenuItem("打开");

        var impl = new LinuxMenu(menu);
        try
        {
            var before = impl.MenuModel;

            // GMenu 不支持局部更新，LinuxMenu 以整树重建实现增量变更；
            // 平台层通过 NativeChanged 把新模型重新注入各窗口，否则菜单栏不刷新。
            file.AddMenuItem("关闭");

            await Assert.That(impl.MenuModel is not null).IsTrue();
            await Assert.That(ReferenceEquals(before, impl.MenuModel)).IsFalse();
        }
        finally
        {
            impl.Destroy();
        }
    }

    [Test]
    [LinuxOnly]
    public async Task Constructor_MacOsExclusiveRole_IsSilentlyIgnored()
    {
        var menu = new Menu("App");
        var windowMenu = menu.AddSubmenu("窗口");
        var bringAll = windowMenu.AddRoleItem(MenuRole.BringAllToFront);

        var impl = new LinuxMenu(menu);
        try
        {
            await Assert.That(bringAll.Label).IsNull();
            await Assert.That(bringAll.Callback is null).IsTrue();
        }
        finally
        {
            impl.Destroy();
        }
    }
}
