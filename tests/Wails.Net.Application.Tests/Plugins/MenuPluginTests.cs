using Wails.Net.Plugins.Menu;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TUnit.Core;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Managers;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Options;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Tests.Plugins;

/// <summary>
/// <see cref="MenuPlugin"/> 的单元测试（P1-4 Step 7）。
/// 覆盖 5 个命令的注册与执行路径：
/// <list type="bullet">
/// <item><c>menu.setApplicationMenu</c> / <c>menu.getApplicationMenu</c> — 应用菜单读写</item>
/// <item><c>menu.setContextMenu</c> — 上下文菜单注册/移除/类型校验</item>
/// <item><c>menu.popup</c> — 弹窗调用 <see cref="WebviewWindow.OpenContextMenu(Menus.ContextMenuData)"/></item>
/// <item><c>menu.updateMenuItem</c> — 菜单项属性更新</item>
/// </list>
/// </summary>
[NotInParallel]
public sealed class MenuPluginTests
{
    // ---------------------------------------------------------------------
    // 测试基础设施
    // ---------------------------------------------------------------------

    /// <summary>
    /// 创建模拟的 <see cref="IPluginContext"/>，提供 CommandRegistry。
    /// </summary>
    private static IPluginContext CreatePluginContext()
    {
        var services = new ServiceCollection();
        var commands = new CommandRegistry();
        var config = new ConfigurationBuilder().Build();
        var loggerFactory = LoggerFactory.Create(_ => { });

        var context = Substitute.For<IPluginContext>();
        context.Services.Returns(services);
        context.Commands.Returns(commands);
        context.Configuration.Returns(config);
        context.LoggerFactory.Returns(loggerFactory);
        return context;
    }

    /// <summary>
    /// 创建模拟的 <see cref="ICommandContext"/>，可指定 WindowId。
    /// </summary>
    private static ICommandContext CreateCommandContext(uint? windowId = null)
    {
        var ctx = Substitute.For<ICommandContext>();
        ctx.Services.Returns(new ServiceCollection().BuildServiceProvider());
        ctx.WindowId.Returns(windowId);
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }

    /// <summary>
    /// 调用编译期构建的强类型调用器（遵循 AGENTS.md §3.4 禁令，零反射）。
    /// </summary>
    private static object? InvokeCommand(CommandRegistry registry, string name, params object?[] args)
        => CommandTestHelper.Invoke(registry, name, args);

    /// <summary>
    /// 创建新的 Application 实例（自动设置全局静态实例）并注入 MenuManager。
    /// 返回 (app, menuManager)。
    /// </summary>
    private static (Application app, MenuManager menuManager) CreateAppWithMenuManager()
    {
        var app = new Application(new ApplicationOptions());
        var menuManager = new MenuManager(platformApp: null);
        app.MenuManager = menuManager;
        return (app, menuManager);
    }

    /// <summary>
    /// 取子菜单的菜单项 ID。
    /// <see cref="Menu.AddSubmenu"/> 的声明返回类型是 <see cref="Menu"/>，
    /// 实际实例为 <see cref="MenuItem"/>，而 ID 只定义在 <see cref="MenuItem"/> 上。
    /// </summary>
    /// <param name="menu">子菜单实例。</param>
    /// <returns>字符串形式的菜单项 ID（命令参数使用该形态）。</returns>
    private static string IdOf(Menu menu) => ((MenuItem)menu).ID.ToString();

    // ---------------------------------------------------------------------
    // 基础测试
    // ---------------------------------------------------------------------

    [Test]
    public async Task Name_ReturnsMenu()
    {
        var plugin = new MenuPlugin();
        await Assert.That(plugin.Name).IsEqualTo("menu");
    }

    [Test]
    public async Task Configure_NullContext_ThrowsArgumentNullException()
    {
        var plugin = new MenuPlugin();
        await Assert.That(() => plugin.Configure(null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task Configure_RegistersAllCommands()
    {
        var plugin = new MenuPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);

        await Assert.That(context.Commands.Find("menu.setApplicationMenu")).IsNotNull();
        await Assert.That(context.Commands.Find("menu.getApplicationMenu")).IsNotNull();
        await Assert.That(context.Commands.Find("menu.setContextMenu")).IsNotNull();
        await Assert.That(context.Commands.Find("menu.popup")).IsNotNull();
        await Assert.That(context.Commands.Find("menu.updateMenuItem")).IsNotNull();
    }

    [Test]
    public async Task Configure_RegistersMenuRoleCommands()
    {
        // MenuRole 相关命令此前无任何命令级测试覆盖，单独守门注册链路。
        var plugin = new MenuPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);

        await Assert.That(context.Commands.Find("menu.addRoleItem")).IsNotNull();
        await Assert.That(context.Commands.Find("menu.addStandardEditMenu")).IsNotNull();
        await Assert.That(context.Commands.Find("menu.addStandardWindowMenu")).IsNotNull();
        await Assert.That(context.Commands.Find("menu.addStandardHelpMenu")).IsNotNull();
    }

    // ---------------------------------------------------------------------
    // menu.setApplicationMenu / getApplicationMenu
    // ---------------------------------------------------------------------

    [Test]
    public async Task SetApplicationMenu_NoMenuManager_ThrowsInvalidOperationException()
    {
        var plugin = new MenuPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        // 创建 Application 但不注入 MenuManager
        _ = new Application(new ApplicationOptions());

        var cmdCtx = CreateCommandContext();
        await Assert.That(() => InvokeCommand(context.Commands, "menu.setApplicationMenu", cmdCtx,
            new MenuApplicationMenuOptions { Menu = new Menu("App") }))
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task SetApplicationMenu_WithMenuManager_SetsAndCachesMenu()
    {
        var plugin = new MenuPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        var (_, menuManager) = CreateAppWithMenuManager();
        var cmdCtx = CreateCommandContext();
        var menu = new Menu("AppMenu");

        InvokeCommand(context.Commands, "menu.setApplicationMenu", cmdCtx,
            new MenuApplicationMenuOptions { Menu = menu });

        // JSON 往返会产生新实例，无法保持引用相等；按值校验 Label 与类型
        var stored = menuManager.GetApplicationMenu();
        await Assert.That(stored).IsNotNull();
        await Assert.That(stored!.Label).IsEqualTo("AppMenu");
    }

    [Test]
    public async Task GetApplicationMenu_NoMenuSet_ReturnsNull()
    {
        var plugin = new MenuPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        CreateAppWithMenuManager();
        var cmdCtx = CreateCommandContext();

        var result = InvokeCommand(context.Commands, "menu.getApplicationMenu", cmdCtx);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetApplicationMenu_MenuPreviouslySet_ReturnsSameMenu()
    {
        var plugin = new MenuPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        CreateAppWithMenuManager();
        var cmdCtx = CreateCommandContext();
        var menu = new Menu("Cached");

        InvokeCommand(context.Commands, "menu.setApplicationMenu", cmdCtx,
            new MenuApplicationMenuOptions { Menu = menu });
        var result = InvokeCommand(context.Commands, "menu.getApplicationMenu", cmdCtx);

        // JSON 往返会产生新实例，无法保持引用相等；按值校验 Label 与类型
        await Assert.That(result).IsNotNull();
        await Assert.That(((Menu)result!).Label).IsEqualTo("Cached");
    }

    // ---------------------------------------------------------------------
    // menu.setContextMenu
    // ---------------------------------------------------------------------

    [Test]
    public async Task SetContextMenu_ValidContextMenu_RegistersById()
    {
        var plugin = new MenuPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        var (_, menuManager) = CreateAppWithMenuManager();
        var cmdCtx = CreateCommandContext();
        var cm = new ContextMenu();

        InvokeCommand(context.Commands, "menu.setContextMenu", cmdCtx,
            new MenuContextMenuOptions { Id = "edit-menu", Menu = cm });

        // JSON 往返会产生新实例，无法保持引用相等；验证类型正确保留为 ContextMenu
        var stored = menuManager.GetContextMenu("edit-menu");
        await Assert.That(stored).IsNotNull();
        await Assert.That(stored).IsTypeOf<ContextMenu>();
    }

    [Test]
    public async Task SetContextMenu_SameIdTwice_OverwritesPrevious()
    {
        var plugin = new MenuPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        var (_, menuManager) = CreateAppWithMenuManager();
        var cmdCtx = CreateCommandContext();
        var first = new ContextMenu();
        var second = new ContextMenu { Label = "Second" };

        InvokeCommand(context.Commands, "menu.setContextMenu", cmdCtx,
            new MenuContextMenuOptions { Id = "id1", Menu = first });
        InvokeCommand(context.Commands, "menu.setContextMenu", cmdCtx,
            new MenuContextMenuOptions { Id = "id1", Menu = second });

        // JSON 往返会产生新实例，无法保持引用相等；通过 Label 验证第二次调用覆盖了第一次
        var stored = menuManager.GetContextMenu("id1");
        await Assert.That(stored).IsNotNull();
        await Assert.That(stored).IsTypeOf<ContextMenu>();
        await Assert.That(stored!.Label).IsEqualTo("Second");
    }

    [Test]
    public async Task SetContextMenu_NullMenu_RemovesRegistration()
    {
        var plugin = new MenuPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        var (_, menuManager) = CreateAppWithMenuManager();
        var cmdCtx = CreateCommandContext();

        // 先注册
        InvokeCommand(context.Commands, "menu.setContextMenu", cmdCtx,
            new MenuContextMenuOptions { Id = "temp", Menu = new ContextMenu() });
        // 再传 null Menu 移除
        InvokeCommand(context.Commands, "menu.setContextMenu", cmdCtx,
            new MenuContextMenuOptions { Id = "temp", Menu = null });

        await Assert.That(menuManager.GetContextMenu("temp")).IsNull();
    }

    [Test]
    public async Task SetContextMenu_NullId_ThrowsArgumentException()
    {
        var plugin = new MenuPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        CreateAppWithMenuManager();
        var cmdCtx = CreateCommandContext();

        await Assert.That(() => InvokeCommand(context.Commands, "menu.setContextMenu", cmdCtx,
            new MenuContextMenuOptions { Id = null!, Menu = new ContextMenu() }))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task SetContextMenu_EmptyId_ThrowsArgumentException()
    {
        var plugin = new MenuPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        CreateAppWithMenuManager();
        var cmdCtx = CreateCommandContext();

        await Assert.That(() => InvokeCommand(context.Commands, "menu.setContextMenu", cmdCtx,
            new MenuContextMenuOptions { Id = "", Menu = new ContextMenu() }))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task SetContextMenu_WhitespaceId_ThrowsArgumentException()
    {
        var plugin = new MenuPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        CreateAppWithMenuManager();
        var cmdCtx = CreateCommandContext();

        await Assert.That(() => InvokeCommand(context.Commands, "menu.setContextMenu", cmdCtx,
            new MenuContextMenuOptions { Id = "   ", Menu = new ContextMenu() }))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task SetContextMenu_MenuNotContextMenuType_ThrowsInvalidOperationException()
    {
        var plugin = new MenuPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        CreateAppWithMenuManager();
        var cmdCtx = CreateCommandContext();
        var plainMenu = new Menu("NotAContextMenu");

        await Assert.That(() => InvokeCommand(context.Commands, "menu.setContextMenu", cmdCtx,
            new MenuContextMenuOptions { Id = "invalid", Menu = plainMenu }))
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task SetContextMenu_NoMenuManager_ThrowsInvalidOperationException()
    {
        var plugin = new MenuPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        // 创建 Application 但不注入 MenuManager
        _ = new Application(new ApplicationOptions());
        var cmdCtx = CreateCommandContext();

        await Assert.That(() => InvokeCommand(context.Commands, "menu.setContextMenu", cmdCtx,
            new MenuContextMenuOptions { Id = "id", Menu = new ContextMenu() }))
            .ThrowsExactly<InvalidOperationException>();
    }

    // ---------------------------------------------------------------------
    // menu.popup
    // ---------------------------------------------------------------------

    [Test]
    public async Task Popup_NoWindowId_ThrowsInvalidOperationException()
    {
        var plugin = new MenuPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        CreateAppWithMenuManager();
        var cmdCtx = CreateCommandContext(windowId: null);

        await Assert.That(() => InvokeCommand(context.Commands, "menu.popup", cmdCtx,
            new MenuPopupOptions { Id = "edit", X = 100, Y = 200 }))
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task Popup_UnknownWindowId_ThrowsInvalidOperationException()
    {
        var plugin = new MenuPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        CreateAppWithMenuManager();
        var cmdCtx = CreateCommandContext(windowId: 9999u);

        await Assert.That(() => InvokeCommand(context.Commands, "menu.popup", cmdCtx,
            new MenuPopupOptions { Id = "edit", X = 100, Y = 200 }))
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task Popup_ValidWindow_CallsOpenContextMenuWithData()
    {
        var plugin = new MenuPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        var (app, _) = CreateAppWithMenuManager();

        // 创建窗口并设置 Impl 桩
        var window = app.CreateWebviewWindow(new WebviewWindowOptions { Name = "PopupWindow" });
        var impl = Substitute.For<IWebviewWindowImpl>();
        window.Impl = impl;

        // 捕获 OpenContextMenu(ContextMenuData) 调用参数
        ContextMenuData? capturedData = null;
        impl.When(x => x.OpenContextMenu(Arg.Any<ContextMenuData>()))
            .Do(callInfo => capturedData = callInfo.Arg<ContextMenuData>());

        var cmdCtx = CreateCommandContext(windowId: window.ID);

        InvokeCommand(context.Commands, "menu.popup", cmdCtx,
            new MenuPopupOptions { Id = "edit-menu", X = 150, Y = 250, Data = "extra-info" });

        await Assert.That(capturedData).IsNotNull();
        await Assert.That(capturedData!.Id).IsEqualTo("edit-menu");
        await Assert.That(capturedData.X).IsEqualTo(150);
        await Assert.That(capturedData.Y).IsEqualTo(250);
        await Assert.That(capturedData.Data).IsEqualTo("extra-info");
    }

    [Test]
    public async Task Popup_NullId_PassesEmptyIdToImpl()
    {
        var plugin = new MenuPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        var (app, _) = CreateAppWithMenuManager();

        var window = app.CreateWebviewWindow(new WebviewWindowOptions { Name = "W2" });
        var impl = Substitute.For<IWebviewWindowImpl>();
        window.Impl = impl;

        ContextMenuData? capturedData = null;
        impl.When(x => x.OpenContextMenu(Arg.Any<ContextMenuData>()))
            .Do(callInfo => capturedData = callInfo.Arg<ContextMenuData>());

        var cmdCtx = CreateCommandContext(windowId: window.ID);

        InvokeCommand(context.Commands, "menu.popup", cmdCtx,
            new MenuPopupOptions { Id = null, X = 10, Y = 20, Data = null });

        await Assert.That(capturedData).IsNotNull();
        await Assert.That(capturedData!.Id).IsEqualTo(string.Empty);
        await Assert.That(capturedData.Data).IsNull();
    }

    [Test]
    public async Task Popup_WindowWithNullImpl_DoesNotThrow()
    {
        // WebviewWindow.OpenContextMenu(ContextMenuData) 使用空条件访问 Impl?.OpenContextMenu
        // 所以即使 Impl 为 null 也不应抛异常（no-op）
        var plugin = new MenuPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        var (app, _) = CreateAppWithMenuManager();

        var window = app.CreateWebviewWindow(new WebviewWindowOptions { Name = "NullImplWindow" });
        // 不设置 Impl（保持 null）
        var cmdCtx = CreateCommandContext(windowId: window.ID);

        await Assert.That(() => InvokeCommand(context.Commands, "menu.popup", cmdCtx,
            new MenuPopupOptions { Id = "test", X = 0, Y = 0 })).ThrowsNothing();
    }

    // ---------------------------------------------------------------------
    // menu.updateMenuItem
    // ---------------------------------------------------------------------

    [Test]
    public async Task UpdateMenuItem_NoMenuManager_ThrowsInvalidOperationException()
    {
        var plugin = new MenuPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        _ = new Application(new ApplicationOptions());
        var cmdCtx = CreateCommandContext();

        await Assert.That(() => InvokeCommand(context.Commands, "menu.updateMenuItem", cmdCtx,
            new MenuUpdateItemOptions { Id = "1", Properties = new() }))
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task UpdateMenuItem_EmptyId_ThrowsArgumentException()
    {
        var plugin = new MenuPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        CreateAppWithMenuManager();
        var cmdCtx = CreateCommandContext();

        await Assert.That(() => InvokeCommand(context.Commands, "menu.updateMenuItem", cmdCtx,
            new MenuUpdateItemOptions { Id = "", Properties = new() }))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task UpdateMenuItem_NotNumericId_ThrowsInvalidOperationException()
    {
        var plugin = new MenuPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        CreateAppWithMenuManager();
        var cmdCtx = CreateCommandContext();

        await Assert.That(() => InvokeCommand(context.Commands, "menu.updateMenuItem", cmdCtx,
            new MenuUpdateItemOptions { Id = "not-a-number", Properties = new() }))
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task UpdateMenuItem_UnknownId_ThrowsInvalidOperationException()
    {
        var plugin = new MenuPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        CreateAppWithMenuManager();
        var cmdCtx = CreateCommandContext();

        await Assert.That(() => InvokeCommand(context.Commands, "menu.updateMenuItem", cmdCtx,
            new MenuUpdateItemOptions { Id = "99999", Properties = new() }))
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task UpdateMenuItem_UpdatesLabelProperty()
    {
        var plugin = new MenuPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        var (app, menuManager) = CreateAppWithMenuManager();
        var cmdCtx = CreateCommandContext();

        // 构造应用菜单：File > New
        var appMenu = new Menu("App");
        var newItem = appMenu.AddMenuItem("New");
        menuManager.SetApplicationMenu(appMenu);

        InvokeCommand(context.Commands, "menu.updateMenuItem", cmdCtx,
            new MenuUpdateItemOptions
            {
                Id = newItem.ID.ToString(),
                Properties = new() { ["label"] = "Open" }
            });

        await Assert.That(newItem.Label).IsEqualTo("Open");
    }

    [Test]
    public async Task UpdateMenuItem_UpdatesEnabledProperty()
    {
        var plugin = new MenuPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        var (app, menuManager) = CreateAppWithMenuManager();
        var cmdCtx = CreateCommandContext();

        var appMenu = new Menu("App");
        var item = appMenu.AddMenuItem("Item");
        menuManager.SetApplicationMenu(appMenu);

        InvokeCommand(context.Commands, "menu.updateMenuItem", cmdCtx,
            new MenuUpdateItemOptions
            {
                Id = item.ID.ToString(),
                Properties = new() { ["enabled"] = false }
            });

        await Assert.That(item.IsDisabled).IsTrue();
    }

    [Test]
    public async Task UpdateMenuItem_UpdatesDisabledProperty()
    {
        var plugin = new MenuPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        var (app, menuManager) = CreateAppWithMenuManager();
        var cmdCtx = CreateCommandContext();

        var appMenu = new Menu("App");
        var item = appMenu.AddMenuItem("Item");
        menuManager.SetApplicationMenu(appMenu);

        InvokeCommand(context.Commands, "menu.updateMenuItem", cmdCtx,
            new MenuUpdateItemOptions
            {
                Id = item.ID.ToString(),
                Properties = new() { ["disabled"] = true }
            });

        await Assert.That(item.IsDisabled).IsTrue();
    }

    [Test]
    public async Task UpdateMenuItem_UpdatesCheckedProperty()
    {
        var plugin = new MenuPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        var (app, menuManager) = CreateAppWithMenuManager();
        var cmdCtx = CreateCommandContext();

        var appMenu = new Menu("App");
        var item = appMenu.AddMenuItem("Item");
        menuManager.SetApplicationMenu(appMenu);

        InvokeCommand(context.Commands, "menu.updateMenuItem", cmdCtx,
            new MenuUpdateItemOptions
            {
                Id = item.ID.ToString(),
                Properties = new() { ["checked"] = true }
            });

        await Assert.That(item.Checked).IsTrue();
    }

    [Test]
    public async Task UpdateMenuItem_UpdatesAcceleratorProperty()
    {
        var plugin = new MenuPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        var (app, menuManager) = CreateAppWithMenuManager();
        var cmdCtx = CreateCommandContext();

        var appMenu = new Menu("App");
        var item = appMenu.AddMenuItem("Item");
        menuManager.SetApplicationMenu(appMenu);

        InvokeCommand(context.Commands, "menu.updateMenuItem", cmdCtx,
            new MenuUpdateItemOptions
            {
                Id = item.ID.ToString(),
                Properties = new() { ["accelerator"] = "CmdOrCtrl+S" }
            });

        await Assert.That(item.Accelerator).IsEqualTo("CmdOrCtrl+S");
    }

    [Test]
    public async Task UpdateMenuItem_StringBooleanValue_ConvertsCorrectly()
    {
        var plugin = new MenuPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        var (app, menuManager) = CreateAppWithMenuManager();
        var cmdCtx = CreateCommandContext();

        var appMenu = new Menu("App");
        var item = appMenu.AddMenuItem("Item");
        menuManager.SetApplicationMenu(appMenu);

        // 字符串 "true" 应转换为布尔 true
        InvokeCommand(context.Commands, "menu.updateMenuItem", cmdCtx,
            new MenuUpdateItemOptions
            {
                Id = item.ID.ToString(),
                Properties = new() { ["checked"] = "true" }
            });

        await Assert.That(item.Checked).IsTrue();
    }

    [Test]
    public async Task UpdateMenuItem_NumericBooleanValue_ConvertsCorrectly()
    {
        var plugin = new MenuPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        var (app, menuManager) = CreateAppWithMenuManager();
        var cmdCtx = CreateCommandContext();

        var appMenu = new Menu("App");
        var item = appMenu.AddMenuItem("Item");
        menuManager.SetApplicationMenu(appMenu);

        // 数字 1 应转换为布尔 true
        InvokeCommand(context.Commands, "menu.updateMenuItem", cmdCtx,
            new MenuUpdateItemOptions
            {
                Id = item.ID.ToString(),
                Properties = new() { ["disabled"] = 1 }
            });

        await Assert.That(item.IsDisabled).IsTrue();
    }

    [Test]
    public async Task UpdateMenuItem_UnknownPropertyKey_IsIgnored()
    {
        var plugin = new MenuPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        var (app, menuManager) = CreateAppWithMenuManager();
        var cmdCtx = CreateCommandContext();

        var appMenu = new Menu("App");
        var item = appMenu.AddMenuItem("Original");
        menuManager.SetApplicationMenu(appMenu);

        // 未知属性不应抛异常，也不应改变其他属性
        await Assert.That(() => InvokeCommand(context.Commands, "menu.updateMenuItem", cmdCtx,
            new MenuUpdateItemOptions
            {
                Id = item.ID.ToString(),
                Properties = new() { ["unknown-key"] = "value" }
            })).ThrowsNothing();

        await Assert.That(item.Label).IsEqualTo("Original");
    }

    [Test]
    public async Task UpdateMenuItem_FindsItemInSubmenu()
    {
        var plugin = new MenuPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        var (app, menuManager) = CreateAppWithMenuManager();
        var cmdCtx = CreateCommandContext();

        // 构造带子菜单的应用菜单
        var appMenu = new Menu("App");
        var fileMenu = appMenu.AddSubmenu("File");
        var newItem = fileMenu.AddMenuItem("New");
        menuManager.SetApplicationMenu(appMenu);

        InvokeCommand(context.Commands, "menu.updateMenuItem", cmdCtx,
            new MenuUpdateItemOptions
            {
                Id = newItem.ID.ToString(),
                Properties = new() { ["label"] = "New File" }
            });

        await Assert.That(newItem.Label).IsEqualTo("New File");
    }

    // ---------------------------------------------------------------------
    // menu.addRoleItem
    // ---------------------------------------------------------------------

    [Test]
    public async Task AddRoleItem_AppendsRoleItemToParentAndReturnsId()
    {
        var plugin = new MenuPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        var (_, menuManager) = CreateAppWithMenuManager();
        var cmdCtx = CreateCommandContext();

        var appMenu = new Menu("App");
        var editMenu = appMenu.AddSubmenu("编辑");
        menuManager.SetApplicationMenu(appMenu);

        var result = InvokeCommand(context.Commands, "menu.addRoleItem", cmdCtx,
            new MenuAddRoleItemOptions { ParentId = IdOf(editMenu), Role = MenuRole.Copy });

        await Assert.That(editMenu.Items.Count).IsEqualTo(1);
        await Assert.That(editMenu.Items[0].Role).IsEqualTo(MenuRole.Copy);

        // 返回值是字符串形式的菜单项 ID，前端据此调用 menu.updateMenuItem。
        var created = editMenu.Items[0];
        await Assert.That(result).IsEqualTo(created.ID.ToString());
    }

    [Test]
    public async Task AddRoleItem_WithExplicitLabel_KeepsCallerLabel()
    {
        var plugin = new MenuPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        var (_, menuManager) = CreateAppWithMenuManager();
        var cmdCtx = CreateCommandContext();

        var appMenu = new Menu("App");
        var editMenu = appMenu.AddSubmenu("编辑");
        menuManager.SetApplicationMenu(appMenu);

        InvokeCommand(context.Commands, "menu.addRoleItem", cmdCtx,
            new MenuAddRoleItemOptions { ParentId = IdOf(editMenu), Role = MenuRole.Paste, Label = "粘贴为纯文本" });

        await Assert.That(editMenu.Items[0].Label).IsEqualTo("粘贴为纯文本");
    }

    [Test]
    public async Task AddRoleItem_UnknownParent_ThrowsInvalidOperationException()
    {
        var plugin = new MenuPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        CreateAppWithMenuManager();
        var cmdCtx = CreateCommandContext();

        await Assert.That(() => InvokeCommand(context.Commands, "menu.addRoleItem", cmdCtx,
            new MenuAddRoleItemOptions { ParentId = "99999", Role = MenuRole.Copy }))
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task AddRoleItem_EmptyParentId_ThrowsArgumentException()
    {
        var plugin = new MenuPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        CreateAppWithMenuManager();
        var cmdCtx = CreateCommandContext();

        await Assert.That(() => InvokeCommand(context.Commands, "menu.addRoleItem", cmdCtx,
            new MenuAddRoleItemOptions { ParentId = "", Role = MenuRole.Copy }))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task AddRoleItem_FindsParentInNestedSubmenu()
    {
        var plugin = new MenuPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        var (_, menuManager) = CreateAppWithMenuManager();
        var cmdCtx = CreateCommandContext();

        var appMenu = new Menu("App");
        var viewMenu = appMenu.AddSubmenu("视图");
        var themeMenu = viewMenu.AddSubmenu("主题");
        menuManager.SetApplicationMenu(appMenu);

        InvokeCommand(context.Commands, "menu.addRoleItem", cmdCtx,
            new MenuAddRoleItemOptions { ParentId = IdOf(themeMenu), Role = MenuRole.Fullscreen });

        await Assert.That(themeMenu.Items.Count).IsEqualTo(1);
        await Assert.That(themeMenu.Items[0].Role).IsEqualTo(MenuRole.Fullscreen);
    }

    // ---------------------------------------------------------------------
    // menu.addStandardEditMenu / addStandardWindowMenu / addStandardHelpMenu
    // ---------------------------------------------------------------------

    [Test]
    public async Task AddStandardEditMenu_AppendsUndoRedoAndClipboardRoles()
    {
        var plugin = new MenuPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        var (_, menuManager) = CreateAppWithMenuManager();
        var cmdCtx = CreateCommandContext();

        var appMenu = new Menu("App");
        var editMenu = appMenu.AddSubmenu("编辑");
        menuManager.SetApplicationMenu(appMenu);

        InvokeCommand(context.Commands, "menu.addStandardEditMenu", cmdCtx,
            new MenuParentOptions { ParentId = IdOf(editMenu) });

        // Undo/Redo/Separator/Cut/Copy/Paste/SelectAll — 顺序与 AddStandardEditMenu 一致。
        await Assert.That(editMenu.Items.Count).IsEqualTo(7);
        await Assert.That(editMenu.Items[0].Role).IsEqualTo(MenuRole.Undo);
        await Assert.That(editMenu.Items[1].Role).IsEqualTo(MenuRole.Redo);
        await Assert.That(editMenu.Items[2].Role).IsEqualTo(MenuRole.Separator);
        await Assert.That(editMenu.Items[2].IsSeparator).IsTrue();
        await Assert.That(editMenu.Items[3].Role).IsEqualTo(MenuRole.Cut);
        await Assert.That(editMenu.Items[4].Role).IsEqualTo(MenuRole.Copy);
        await Assert.That(editMenu.Items[5].Role).IsEqualTo(MenuRole.Paste);
        await Assert.That(editMenu.Items[6].Role).IsEqualTo(MenuRole.SelectAll);
    }

    [Test]
    public async Task AddStandardWindowMenu_AppendsMinimizeMaximizeAndClose()
    {
        var plugin = new MenuPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        var (_, menuManager) = CreateAppWithMenuManager();
        var cmdCtx = CreateCommandContext();

        var appMenu = new Menu("App");
        var windowMenu = appMenu.AddSubmenu("窗口");
        menuManager.SetApplicationMenu(appMenu);

        InvokeCommand(context.Commands, "menu.addStandardWindowMenu", cmdCtx,
            new MenuParentOptions { ParentId = IdOf(windowMenu) });

        // Minimize/Maximize/Separator/CloseWindow — 顺序与 AddStandardWindowMenu 一致。
        await Assert.That(windowMenu.Items.Count).IsEqualTo(4);
        await Assert.That(windowMenu.Items[0].Role).IsEqualTo(MenuRole.Minimize);
        await Assert.That(windowMenu.Items[1].Role).IsEqualTo(MenuRole.Maximize);
        await Assert.That(windowMenu.Items[2].Role).IsEqualTo(MenuRole.Separator);
        await Assert.That(windowMenu.Items[3].Role).IsEqualTo(MenuRole.CloseWindow);
    }

    [Test]
    public async Task AddStandardHelpMenu_AppendsAboutItemWithMetadata()
    {
        var plugin = new MenuPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        var (_, menuManager) = CreateAppWithMenuManager();
        var cmdCtx = CreateCommandContext();

        var appMenu = new Menu("App");
        var helpMenu = appMenu.AddSubmenu("帮助");
        menuManager.SetApplicationMenu(appMenu);

        InvokeCommand(context.Commands, "menu.addStandardHelpMenu", cmdCtx,
            new MenuAddStandardHelpOptions
            {
                ParentId = IdOf(helpMenu),
                Label = "关于本应用",
                Metadata = new AboutMetadata { Name = "Wails.Net", Version = "1.0.0" },
            });

        await Assert.That(helpMenu.Items.Count).IsEqualTo(1);
        var about = helpMenu.Items[0];
        await Assert.That(about.Role).IsEqualTo(MenuRole.About);
        await Assert.That(about.Label).IsEqualTo("关于本应用");
        await Assert.That(about.AboutMetadata).IsNotNull();
        await Assert.That(about.AboutMetadata!.Name).IsEqualTo("Wails.Net");
    }
}
