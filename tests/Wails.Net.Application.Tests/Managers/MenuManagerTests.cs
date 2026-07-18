using NSubstitute;
using TUnit.Core;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Managers;
using Wails.Net.Application.Platform;

namespace Wails.Net.Application.Tests.Managers;

/// <summary>
/// <see cref="MenuManager"/> 的单元测试（P1-4）。
/// 重点覆盖上下文菜单注册表行为，应用菜单缓存的委托与读取，
/// 以及线程安全的并发注册/移除场景。
/// </summary>
[NotInParallel]
public sealed class MenuManagerTests
{
    /// <summary>
    /// 默认构造（无平台应用）场景下上下文菜单注册与查询应正常工作。
    /// </summary>
    [Test]
    public async Task RegisterContextMenu_NoPlatformApp_RegistersAndRetrieves()
    {
        // 安排
        var manager = new MenuManager(platformApp: null);
        var menu = new ContextMenu();

        // 操作
        manager.RegisterContextMenu("edit-menu", menu);
        var actual = manager.GetContextMenu("edit-menu");

        // 断言
        await Assert.That(actual).IsNotNull();
        await Assert.That(actual!).IsSameReferenceAs(menu);
    }

    /// <summary>
    /// 重复注册同一 ID 应覆盖旧值。
    /// </summary>
    [Test]
    public async Task RegisterContextMenu_DuplicateId_OverwritesExisting()
    {
        // 安排
        var manager = new MenuManager(platformApp: null);
        var first = new ContextMenu();
        var second = new ContextMenu();
        manager.RegisterContextMenu("menu-1", first);

        // 操作
        manager.RegisterContextMenu("menu-1", second);
        var actual = manager.GetContextMenu("menu-1");

        // 断言
        await Assert.That(actual).IsSameReferenceAs(second);
    }

    /// <summary>
    /// 未注册的 ID 查询返回 null。
    /// </summary>
    [Test]
    public async Task GetContextMenu_UnknownId_ReturnsNull()
    {
        // 安排
        var manager = new MenuManager(platformApp: null);

        // 操作
        var actual = manager.GetContextMenu("nonexistent");

        // 断言
        await Assert.That(actual).IsNull();
    }

    /// <summary>
    /// 注册空 ID 应抛出 ArgumentException（或其子类 ArgumentNullException）。
    /// </summary>
    [Test]
    public async Task RegisterContextMenu_NullOrWhiteSpaceId_ThrowsArgumentException()
    {
        // 安排
        var manager = new MenuManager(platformApp: null);
        var menu = new ContextMenu();

        // 断言：null 触发 ArgumentNullException（ArgumentException 子类）；空串/空白触发 ArgumentException。
        await Assert.That(() => manager.RegisterContextMenu(null!, menu))
            .Throws<ArgumentException>();
        await Assert.That(() => manager.RegisterContextMenu("", menu))
            .ThrowsExactly<ArgumentException>();
        await Assert.That(() => manager.RegisterContextMenu("   ", menu))
            .ThrowsExactly<ArgumentException>();
    }

    /// <summary>
    /// 注册 null 菜单实例应抛出 ArgumentNullException。
    /// </summary>
    [Test]
    public async Task RegisterContextMenu_NullMenu_ThrowsArgumentNullException()
    {
        // 安排
        var manager = new MenuManager(platformApp: null);

        // 断言
        await Assert.That(() => manager.RegisterContextMenu("id", null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    /// <summary>
    /// 查询空 ID 应抛出 ArgumentException（或其子类 ArgumentNullException）。
    /// </summary>
    [Test]
    public async Task GetContextMenu_NullOrWhiteSpaceId_ThrowsArgumentException()
    {
        // 安排
        var manager = new MenuManager(platformApp: null);

        // 断言：null 触发 ArgumentNullException；空串/空白触发 ArgumentException。
        await Assert.That(() => manager.GetContextMenu(null!))
            .Throws<ArgumentException>();
        await Assert.That(() => manager.GetContextMenu(""))
            .ThrowsExactly<ArgumentException>();
    }

    /// <summary>
    /// 移除已注册的菜单后查询应返回 null。
    /// </summary>
    [Test]
    public async Task RemoveContextMenu_RegisteredId_RemovesEntry()
    {
        // 安排
        var manager = new MenuManager(platformApp: null);
        var menu = new ContextMenu();
        manager.RegisterContextMenu("edit-menu", menu);

        // 操作
        manager.RemoveContextMenu("edit-menu");
        var actual = manager.GetContextMenu("edit-menu");

        // 断言
        await Assert.That(actual).IsNull();
    }

    /// <summary>
    /// 移除未注册的 ID 不抛出异常（幂等行为）。
    /// </summary>
    [Test]
    public async Task RemoveContextMenu_UnknownId_DoesNotThrow()
    {
        // 安排
        var manager = new MenuManager(platformApp: null);

        // 操作 + 断言
        await Assert.That(() => manager.RemoveContextMenu("nonexistent")).ThrowsNothing();
    }

    /// <summary>
    /// 移除空 ID 应抛出 ArgumentException（或其子类 ArgumentNullException）。
    /// </summary>
    [Test]
    public async Task RemoveContextMenu_NullOrWhiteSpaceId_ThrowsArgumentException()
    {
        // 安排
        var manager = new MenuManager(platformApp: null);

        // 断言：null 触发 ArgumentNullException；空串/空白触发 ArgumentException。
        await Assert.That(() => manager.RemoveContextMenu(null!))
            .Throws<ArgumentException>();
        await Assert.That(() => manager.RemoveContextMenu(""))
            .ThrowsExactly<ArgumentException>();
    }

    /// <summary>
    /// SetApplicationMenu 应缓存菜单，GetApplicationMenu 返回缓存值。
    /// </summary>
    [Test]
    public async Task SetApplicationMenu_NullPlatformApp_CachesMenu()
    {
        // 安排
        var manager = new MenuManager(platformApp: null);
        var menu = new Menu("File");

        // 操作
        manager.SetApplicationMenu(menu);
        var actual = manager.GetApplicationMenu();

        // 断言
        await Assert.That(actual).IsSameReferenceAs(menu);
    }

    /// <summary>
    /// 设置 null 应用菜单应允许并清空缓存。
    /// </summary>
    [Test]
    public async Task SetApplicationMenu_Null_ClearsCache()
    {
        // 安排
        var manager = new MenuManager(platformApp: null);
        manager.SetApplicationMenu(new Menu("File"));

        // 操作
        manager.SetApplicationMenu(null);
        var actual = manager.GetApplicationMenu();

        // 断言
        await Assert.That(actual).IsNull();
    }

    /// <summary>
    /// 当提供平台应用时，SetApplicationMenu 应委托给 IPlatformApp.SetApplicationMenu。
    /// </summary>
    [Test]
    public async Task SetApplicationMenu_WithPlatformApp_DelegatesToPlatformApp()
    {
        // 安排
        var platformApp = Substitute.For<IPlatformApp>();
        var manager = new MenuManager(platformApp);
        var menu = new Menu("File");

        // 操作
        manager.SetApplicationMenu(menu);

        // 断言
        platformApp.Received(1).SetApplicationMenu(menu);
    }

    /// <summary>
    /// 当提供平台应用时，SetApplicationMenu(null) 应委托 null 给 IPlatformApp。
    /// </summary>
    [Test]
    public async Task SetApplicationMenu_NullWithPlatformApp_DelegatesNullToPlatformApp()
    {
        // 安排
        var platformApp = Substitute.For<IPlatformApp>();
        var manager = new MenuManager(platformApp);

        // 操作
        manager.SetApplicationMenu(null);

        // 断言
        platformApp.Received(1).SetApplicationMenu(null);
    }

    /// <summary>
    /// 多个不同 ID 的菜单可同时注册并独立查询。
    /// </summary>
    [Test]
    public async Task RegisterContextMenu_MultipleIds_AllAccessible()
    {
        // 安排
        var manager = new MenuManager(platformApp: null);
        var edit = new ContextMenu();
        var file = new ContextMenu();
        var help = new ContextMenu();

        // 操作
        manager.RegisterContextMenu("edit", edit);
        manager.RegisterContextMenu("file", file);
        manager.RegisterContextMenu("help", help);

        // 断言
        await Assert.That(manager.GetContextMenu("edit")).IsSameReferenceAs(edit);
        await Assert.That(manager.GetContextMenu("file")).IsSameReferenceAs(file);
        await Assert.That(manager.GetContextMenu("help")).IsSameReferenceAs(help);
    }

    /// <summary>
    /// 移除其中一个菜单不影响其他菜单的查询。
    /// </summary>
    [Test]
    public async Task RemoveContextMenu_DoesNotAffectOtherMenus()
    {
        // 安排
        var manager = new MenuManager(platformApp: null);
        var edit = new ContextMenu();
        var file = new ContextMenu();
        manager.RegisterContextMenu("edit", edit);
        manager.RegisterContextMenu("file", file);

        // 操作
        manager.RemoveContextMenu("edit");

        // 断言
        await Assert.That(manager.GetContextMenu("edit")).IsNull();
        await Assert.That(manager.GetContextMenu("file")).IsSameReferenceAs(file);
    }

    /// <summary>
    /// 应用菜单和上下文菜单注册表应互不干扰。
    /// </summary>
    [Test]
    public async Task SetApplicationMenu_DoesNotAffectContextMenuRegistry()
    {
        // 安排
        var manager = new MenuManager(platformApp: null);
        var contextMenu = new ContextMenu();
        manager.RegisterContextMenu("cm", contextMenu);

        // 操作
        manager.SetApplicationMenu(new Menu("App"));
        var actualApp = manager.GetApplicationMenu();
        var actualContext = manager.GetContextMenu("cm");

        // 断言
        await Assert.That(actualApp).IsNotNull();
        await Assert.That(actualContext).IsSameReferenceAs(contextMenu);
    }
}
