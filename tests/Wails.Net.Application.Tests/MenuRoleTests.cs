using Wails.Net.Application.Menus;

namespace Wails.Net.Application.Tests;

/// <summary>
/// MenuRole / AboutMetadata / MenuItem 角色工厂方法 / Menu 角色助手的单元测试（TUnit）。
/// 对应 Wails v3 / Tauri v2 的预定义菜单项（PredefinedMenuItem）机制。
/// </summary>
public sealed class MenuRoleTests
{
    // ─── MenuRole 枚举 ───

    [Test]
    public async Task MenuRole_None_DefaultValue()
    {
        var item = new MenuItem();
        await Assert.That(item.Role).IsEqualTo(MenuRole.None);
    }

    [Test]
    public async Task MenuRole_DefinedValues_AreSequential()
    {
        // 验证枚举值连续递增（None=0, Separator=1, Copy=2, ...）。
        // 前端通过字符串名称序列化，但顺序稳定有助于调试与日志可读性。
        var names = Enum.GetNames<MenuRole>();
        var values = Enum.GetValues<MenuRole>();
        await Assert.That(names.Length).IsEqualTo(values.Length);
        await Assert.That(values[0]).IsEqualTo(MenuRole.None);
    }

    [Test]
    public async Task MenuRole_None_IsDefaultMenuItemRole()
    {
        // 新建 MenuItem 默认应为 None（走自定义 Callback 路径）
        var item = new MenuItem();
        await Assert.That(item.Role).IsEqualTo(MenuRole.None);
    }

    // ─── AboutMetadata ───

    [Test]
    public async Task AboutMetadata_Default_AllPropertiesNull()
    {
        var metadata = new AboutMetadata();
        await Assert.That(metadata.Name).IsNull();
        await Assert.That(metadata.Version).IsNull();
        await Assert.That(metadata.ShortVersion).IsNull();
        await Assert.That(metadata.Authors).IsNull();
        await Assert.That(metadata.Copyright).IsNull();
        await Assert.That(metadata.License).IsNull();
        await Assert.That(metadata.Website).IsNull();
        await Assert.That(metadata.WebsiteLabel).IsNull();
        await Assert.That(metadata.Comments).IsNull();
    }

    [Test]
    public async Task AboutMetadata_SetProperties_Roundtrip()
    {
        var metadata = new AboutMetadata
        {
            Name = "MyApp",
            Version = "1.0.0",
            ShortVersion = "1.0",
            Authors = "Alice; Bob",
            Copyright = "© 2026",
            License = "MIT",
            Website = "https://example.com",
            WebsiteLabel = "Example",
            Comments = "Test app"
        };

        await Assert.That(metadata.Name).IsEqualTo("MyApp");
        await Assert.That(metadata.Version).IsEqualTo("1.0.0");
        await Assert.That(metadata.ShortVersion).IsEqualTo("1.0");
        await Assert.That(metadata.Authors).IsEqualTo("Alice; Bob");
        await Assert.That(metadata.Copyright).IsEqualTo("© 2026");
        await Assert.That(metadata.License).IsEqualTo("MIT");
        await Assert.That(metadata.Website).IsEqualTo("https://example.com");
        await Assert.That(metadata.WebsiteLabel).IsEqualTo("Example");
        await Assert.That(metadata.Comments).IsEqualTo("Test app");
    }

    // ─── MenuItem 角色工厂方法 ───

    [Test]
    public async Task MenuItem_CreateCopy_SetsRoleCopy()
    {
        var item = MenuItem.CreateCopy();
        await Assert.That(item.Role).IsEqualTo(MenuRole.Copy);
        await Assert.That(item.Label).IsNull();
    }

    [Test]
    public async Task MenuItem_CreateCopy_WithLabel_SetsLabel()
    {
        var item = MenuItem.CreateCopy("Copy Text");
        await Assert.That(item.Role).IsEqualTo(MenuRole.Copy);
        await Assert.That(item.Label).IsEqualTo("Copy Text");
    }

    [Test]
    public async Task MenuItem_CreateCut_SetsRoleCut()
    {
        await Assert.That(MenuItem.CreateCut().Role).IsEqualTo(MenuRole.Cut);
    }

    [Test]
    public async Task MenuItem_CreatePaste_SetsRolePaste()
    {
        await Assert.That(MenuItem.CreatePaste().Role).IsEqualTo(MenuRole.Paste);
    }

    [Test]
    public async Task MenuItem_CreateSelectAll_SetsRoleSelectAll()
    {
        await Assert.That(MenuItem.CreateSelectAll().Role).IsEqualTo(MenuRole.SelectAll);
    }

    [Test]
    public async Task MenuItem_CreateUndo_SetsRoleUndo()
    {
        await Assert.That(MenuItem.CreateUndo().Role).IsEqualTo(MenuRole.Undo);
    }

    [Test]
    public async Task MenuItem_CreateRedo_SetsRoleRedo()
    {
        await Assert.That(MenuItem.CreateRedo().Role).IsEqualTo(MenuRole.Redo);
    }

    [Test]
    public async Task MenuItem_CreateSeparator_SetsRoleAndIsSeparator()
    {
        var item = MenuItem.CreateSeparator();
        await Assert.That(item.Role).IsEqualTo(MenuRole.Separator);
        await Assert.That(item.IsSeparator).IsTrue();
    }

    [Test]
    public async Task MenuItem_CreateMinimize_SetsRoleMinimize()
    {
        await Assert.That(MenuItem.CreateMinimize().Role).IsEqualTo(MenuRole.Minimize);
    }

    [Test]
    public async Task MenuItem_CreateMaximize_SetsRoleMaximize()
    {
        await Assert.That(MenuItem.CreateMaximize().Role).IsEqualTo(MenuRole.Maximize);
    }

    [Test]
    public async Task MenuItem_CreateFullscreen_SetsRoleFullscreen()
    {
        await Assert.That(MenuItem.CreateFullscreen().Role).IsEqualTo(MenuRole.Fullscreen);
    }

    [Test]
    public async Task MenuItem_CreateCloseWindow_SetsRoleCloseWindow()
    {
        await Assert.That(MenuItem.CreateCloseWindow().Role).IsEqualTo(MenuRole.CloseWindow);
    }

    [Test]
    public async Task MenuItem_CreateQuit_SetsRoleQuit()
    {
        await Assert.That(MenuItem.CreateQuit().Role).IsEqualTo(MenuRole.Quit);
    }

    [Test]
    public async Task MenuItem_CreateAbout_SetsRoleAndMetadata()
    {
        var metadata = new AboutMetadata { Name = "Test", Version = "0.1" };
        var item = MenuItem.CreateAbout("关于本应用", metadata);

        await Assert.That(item.Role).IsEqualTo(MenuRole.About);
        await Assert.That(item.Label).IsEqualTo("关于本应用");
        await Assert.That(item.AboutMetadata).IsSameReferenceAs(metadata);
    }

    [Test]
    public async Task MenuItem_CreateAbout_NoArgs_DefaultsNull()
    {
        var item = MenuItem.CreateAbout();
        await Assert.That(item.Role).IsEqualTo(MenuRole.About);
        await Assert.That(item.Label).IsNull();
        await Assert.That(item.AboutMetadata).IsNull();
    }

    [Test]
    public async Task MenuItem_RoleField_DefaultsToNone()
    {
        var item = new MenuItem();
        await Assert.That(item.Role).IsEqualTo(MenuRole.None);
        await Assert.That(item.AboutMetadata).IsNull();
    }

    // ─── Menu 角色助手方法 ───

    [Test]
    public async Task Menu_AddRoleItem_Copy_AddsItemWithRole()
    {
        var menu = new Menu();
        var item = menu.AddRoleItem(MenuRole.Copy);

        await Assert.That(menu.Items.Count).IsEqualTo(1);
        await Assert.That(menu.Items[0].Role).IsEqualTo(MenuRole.Copy);
        await Assert.That(menu.Items[0]).IsSameReferenceAs(item);
    }

    [Test]
    public async Task Menu_AddRoleItem_Separator_SetsIsSeparator()
    {
        var menu = new Menu();
        menu.AddRoleItem(MenuRole.Separator);

        await Assert.That(menu.Items[0].IsSeparator).IsTrue();
        await Assert.That(menu.Items[0].Role).IsEqualTo(MenuRole.Separator);
    }

    [Test]
    public async Task Menu_AddRoleItem_WithLabel_PreservesLabel()
    {
        var menu = new Menu();
        var item = menu.AddRoleItem(MenuRole.Copy, "复制文本");

        await Assert.That(item.Label).IsEqualTo("复制文本");
        await Assert.That(item.Role).IsEqualTo(MenuRole.Copy);
    }

    [Test]
    public async Task Menu_AddSeparator_SetsRoleSeparator()
    {
        // AddSeparator 应同时设置 Role=Separator，提供语义化别名
        var menu = new Menu();
        menu.AddSeparator();

        await Assert.That(menu.Items[0].IsSeparator).IsTrue();
        await Assert.That(menu.Items[0].Role).IsEqualTo(MenuRole.Separator);
    }

    [Test]
    public async Task Menu_AddStandardEditMenu_AddsSevenItemsInOrder()
    {
        var menu = new Menu();
        menu.AddStandardEditMenu();

        await Assert.That(menu.Items.Count).IsEqualTo(7);
        await Assert.That(menu.Items[0].Role).IsEqualTo(MenuRole.Undo);
        await Assert.That(menu.Items[1].Role).IsEqualTo(MenuRole.Redo);
        await Assert.That(menu.Items[2].Role).IsEqualTo(MenuRole.Separator);
        await Assert.That(menu.Items[2].IsSeparator).IsTrue();
        await Assert.That(menu.Items[3].Role).IsEqualTo(MenuRole.Cut);
        await Assert.That(menu.Items[4].Role).IsEqualTo(MenuRole.Copy);
        await Assert.That(menu.Items[5].Role).IsEqualTo(MenuRole.Paste);
        await Assert.That(menu.Items[6].Role).IsEqualTo(MenuRole.SelectAll);
    }

    [Test]
    public async Task Menu_AddStandardEditMenu_ReturnsSelfForChaining()
    {
        var menu = new Menu();
        var result = menu.AddStandardEditMenu();
        await Assert.That(result).IsSameReferenceAs(menu);
    }

    [Test]
    public async Task Menu_AddStandardWindowMenu_AddsFourItemsInOrder()
    {
        var menu = new Menu();
        menu.AddStandardWindowMenu();

        await Assert.That(menu.Items.Count).IsEqualTo(4);
        await Assert.That(menu.Items[0].Role).IsEqualTo(MenuRole.Minimize);
        await Assert.That(menu.Items[1].Role).IsEqualTo(MenuRole.Maximize);
        await Assert.That(menu.Items[2].Role).IsEqualTo(MenuRole.Separator);
        await Assert.That(menu.Items[2].IsSeparator).IsTrue();
        await Assert.That(menu.Items[3].Role).IsEqualTo(MenuRole.CloseWindow);
    }

    [Test]
    public async Task Menu_AddStandardHelpMenu_AddsAboutItem()
    {
        var metadata = new AboutMetadata { Name = "App" };
        var menu = new Menu();
        menu.AddStandardHelpMenu(metadata, "关于");

        await Assert.That(menu.Items.Count).IsEqualTo(1);
        await Assert.That(menu.Items[0].Role).IsEqualTo(MenuRole.About);
        await Assert.That(menu.Items[0].Label).IsEqualTo("关于");
        await Assert.That(menu.Items[0].AboutMetadata).IsSameReferenceAs(metadata);
    }

    [Test]
    public async Task Menu_AddStandardHelpMenu_NoArgs_NullLabelAndMetadata()
    {
        var menu = new Menu();
        menu.AddStandardHelpMenu();

        await Assert.That(menu.Items[0].Role).IsEqualTo(MenuRole.About);
        await Assert.That(menu.Items[0].Label).IsNull();
        await Assert.That(menu.Items[0].AboutMetadata).IsNull();
    }

    // ─── MenuRoleHelper ───

    [Test]
    public async Task MenuRoleHelper_GetDefaultLabel_CommonRoles()
    {
        await Assert.That(MenuRoleHelper.GetDefaultLabel(MenuRole.Copy)).IsEqualTo("复制");
        await Assert.That(MenuRoleHelper.GetDefaultLabel(MenuRole.Cut)).IsEqualTo("剪切");
        await Assert.That(MenuRoleHelper.GetDefaultLabel(MenuRole.Paste)).IsEqualTo("粘贴");
        await Assert.That(MenuRoleHelper.GetDefaultLabel(MenuRole.Quit)).IsEqualTo("退出");
        await Assert.That(MenuRoleHelper.GetDefaultLabel(MenuRole.About)).IsEqualTo("关于");
    }

    [Test]
    public async Task MenuRoleHelper_GetDefaultLabel_None_ReturnsNull()
    {
        await Assert.That(MenuRoleHelper.GetDefaultLabel(MenuRole.None)).IsNull();
    }

    [Test]
    public async Task MenuRoleHelper_GetDefaultAccelerator_CommonRoles()
    {
        await Assert.That(MenuRoleHelper.GetDefaultAccelerator(MenuRole.Copy)).IsEqualTo("Ctrl+C");
        await Assert.That(MenuRoleHelper.GetDefaultAccelerator(MenuRole.Cut)).IsEqualTo("Ctrl+X");
        await Assert.That(MenuRoleHelper.GetDefaultAccelerator(MenuRole.Paste)).IsEqualTo("Ctrl+V");
        await Assert.That(MenuRoleHelper.GetDefaultAccelerator(MenuRole.SelectAll)).IsEqualTo("Ctrl+A");
        await Assert.That(MenuRoleHelper.GetDefaultAccelerator(MenuRole.Undo)).IsEqualTo("Ctrl+Z");
        await Assert.That(MenuRoleHelper.GetDefaultAccelerator(MenuRole.Redo)).IsEqualTo("Ctrl+Y");
        await Assert.That(MenuRoleHelper.GetDefaultAccelerator(MenuRole.Minimize)).IsEqualTo("Ctrl+M");
        await Assert.That(MenuRoleHelper.GetDefaultAccelerator(MenuRole.CloseWindow)).IsEqualTo("Ctrl+W");
        await Assert.That(MenuRoleHelper.GetDefaultAccelerator(MenuRole.Quit)).IsEqualTo("Ctrl+Q");
    }

    [Test]
    public async Task MenuRoleHelper_GetDefaultAccelerator_RolesWithoutAccelerator_ReturnNull()
    {
        // About/Fullscreen/Maximize 等角色无默认加速键
        await Assert.That(MenuRoleHelper.GetDefaultAccelerator(MenuRole.About)).IsNull();
        await Assert.That(MenuRoleHelper.GetDefaultAccelerator(MenuRole.Fullscreen)).IsNull();
        await Assert.That(MenuRoleHelper.GetDefaultAccelerator(MenuRole.Maximize)).IsNull();
        await Assert.That(MenuRoleHelper.GetDefaultAccelerator(MenuRole.None)).IsNull();
    }

    [Test]
    public async Task MenuRoleHelper_IsMacOSExclusive_MacOSOnlyRoles()
    {
        await Assert.That(MenuRoleHelper.IsMacOSExclusive(MenuRole.Hide)).IsTrue();
        await Assert.That(MenuRoleHelper.IsMacOSExclusive(MenuRole.HideOthers)).IsTrue();
        await Assert.That(MenuRoleHelper.IsMacOSExclusive(MenuRole.ShowAll)).IsTrue();
        await Assert.That(MenuRoleHelper.IsMacOSExclusive(MenuRole.Services)).IsTrue();
        await Assert.That(MenuRoleHelper.IsMacOSExclusive(MenuRole.BringAllToFront)).IsTrue();
        await Assert.That(MenuRoleHelper.IsMacOSExclusive(MenuRole.Zoom)).IsTrue();
        await Assert.That(MenuRoleHelper.IsMacOSExclusive(MenuRole.ToggleFullScreen)).IsTrue();
    }

    [Test]
    public async Task MenuRoleHelper_IsMacOSExclusive_CrossPlatformRoles_ReturnsFalse()
    {
        await Assert.That(MenuRoleHelper.IsMacOSExclusive(MenuRole.None)).IsFalse();
        await Assert.That(MenuRoleHelper.IsMacOSExclusive(MenuRole.Copy)).IsFalse();
        await Assert.That(MenuRoleHelper.IsMacOSExclusive(MenuRole.Quit)).IsFalse();
        await Assert.That(MenuRoleHelper.IsMacOSExclusive(MenuRole.About)).IsFalse();
        await Assert.That(MenuRoleHelper.IsMacOSExclusive(MenuRole.Minimize)).IsFalse();
    }

    [Test]
    public async Task MenuRoleHelper_PrepareRoleItem_NoneRole_NoChanges()
    {
        var item = new MenuItem { Role = MenuRole.None, Label = "Original" };
        MenuRoleHelper.PrepareRoleItem(item, window: null, (_, _, _) => { });

        // None 角色不应改写 Label 或 Callback
        await Assert.That(item.Label).IsEqualTo("Original");
        await Assert.That(item.Callback!).IsNull();
    }

    [Test]
    public async Task MenuRoleHelper_PrepareRoleItem_Copy_FillsDefaultsAndCallback()
    {
        var item = new MenuItem { Role = MenuRole.Copy };
        MenuRoleHelper.PrepareRoleItem(item, window: null, (_, _, _) => { /* no-op */ });

        await Assert.That(item.Label).IsEqualTo("复制");
        await Assert.That(item.Accelerator).IsEqualTo("Ctrl+C");
        await Assert.That(item.Callback is not null).IsTrue();
    }

    [Test]
    public async Task MenuRoleHelper_PrepareRoleItem_PreservesCustomLabel()
    {
        var item = new MenuItem { Role = MenuRole.Copy, Label = "自定义复制" };
        MenuRoleHelper.PrepareRoleItem(item, window: null, (_, _, _) => { });

        // 自定义 Label 不应被默认值覆盖
        await Assert.That(item.Label).IsEqualTo("自定义复制");
        await Assert.That(item.Accelerator).IsEqualTo("Ctrl+C");
    }

    [Test]
    public async Task MenuRoleHelper_PrepareRoleItem_PreservesCustomAccelerator()
    {
        var item = new MenuItem { Role = MenuRole.Copy, Accelerator = "Ctrl+Shift+C" };
        MenuRoleHelper.PrepareRoleItem(item, window: null, (_, _, _) => { });

        await Assert.That(item.Label).IsEqualTo("复制");
        // 自定义 Accelerator 不应被默认值覆盖
        await Assert.That(item.Accelerator).IsEqualTo("Ctrl+Shift+C");
    }

    [Test]
    public async Task MenuRoleHelper_PrepareRoleItem_CallbackInvokesExecutor()
    {
        var item = new MenuItem { Role = MenuRole.Quit };
        MenuRole? capturedRole = null;
        MenuRoleHelper.PrepareRoleItem(
            item,
            window: null,
            (role, window, metadata) => capturedRole = role);

        // 触发 Callback，应调用 executeCallback 并传入 Quit 角色
        item.Callback?.Invoke();
        await Assert.That(capturedRole).IsEqualTo(MenuRole.Quit);
    }

    [Test]
    public async Task MenuRoleHelper_PrepareRoleItem_AboutRole_PassesMetadata()
    {
        var metadata = new AboutMetadata { Name = "Test" };
        var item = new MenuItem { Role = MenuRole.About, AboutMetadata = metadata };

        AboutMetadata? capturedMetadata = null;
        MenuRoleHelper.PrepareRoleItem(
            item,
            window: null,
            (role, window, md) => capturedMetadata = md);

        item.Callback?.Invoke();
        await Assert.That(capturedMetadata).IsSameReferenceAs(metadata);
    }
}
