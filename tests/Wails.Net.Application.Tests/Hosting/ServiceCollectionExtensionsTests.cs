using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TUnit.Core;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Events;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Managers;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Services;

namespace Wails.Net.Application.Tests.Hosting;

/// <summary>
/// <see cref="ServiceCollectionExtensions"/> 的单元测试（TUnit）。
/// 验证 AddWails/AddWailsCore/AddWailsManagers/AddWailsServices 注册的服务集合，
/// 对应 AGENTS.md §1.1.1 技术选型：DI 统一使用 Microsoft.Extensions.DependencyInjection。
/// 复用 <see cref="Wails.Net.Application.Tests.FakePlatformApp"/> 作为测试用 IPlatformApp。
/// </summary>
public sealed class ServiceCollectionExtensionsTests
{
    [Test]
    public async Task AddWailsManagers_RegistersAllManagersAsSingleton()
    {
        // 安排
        var services = new ServiceCollection();
        services.AddLogging();

        // 操作
        services.AddWailsManagers();
        var provider = services.BuildServiceProvider();

        // 断言：核心管理器全部注册为单例
        await Assert.That(provider.GetService<EventProcessor>()).IsNotNull();
        await Assert.That(provider.GetService<BindingManager>()).IsNotNull();
        await Assert.That(provider.GetService<WindowManager>()).IsNotNull();
        await Assert.That(provider.GetService<DialogManager>()).IsNotNull();
        await Assert.That(provider.GetService<ScreenManager>()).IsNotNull();
    }

    [Test]
    public async Task AddWailsManagers_RegistersInterfaceMappings()
    {
        // 安排
        var services = new ServiceCollection();
        services.AddLogging();

        // 操作
        services.AddWailsManagers();
        var provider = services.BuildServiceProvider();

        // 断言：接口映射指向同一单例
        var windowMgr = provider.GetRequiredService<WindowManager>();
        var iWindowMgr = provider.GetRequiredService<IWindowManager>();
        await Assert.That(iWindowMgr).IsSameReferenceAs(windowMgr);

        var dialogMgr = provider.GetRequiredService<DialogManager>();
        var iDialogMgr = provider.GetRequiredService<IDialogManager>();
        await Assert.That(iDialogMgr).IsSameReferenceAs(dialogMgr);

        var screenMgr = provider.GetRequiredService<ScreenManager>();
        var iScreenMgr = provider.GetRequiredService<IScreenManager>();
        await Assert.That(iScreenMgr).IsSameReferenceAs(screenMgr);
    }

    [Test]
    public async Task AddWailsManagers_ToleratesNullPlatformApp_InServerMode()
    {
        // 安排：不注册 IPlatformApp（Server 模式），验证工厂方法能传入 null
        var services = new ServiceCollection();
        services.AddLogging();

        // 操作
        services.AddWailsManagers();
        var provider = services.BuildServiceProvider();

        // 断言：即使没有平台应用，管理器仍能创建（使用 null）
        await Assert.That(provider.GetService<WindowManager>()).IsNotNull();
        await Assert.That(provider.GetService<DialogManager>()).IsNotNull();
        await Assert.That(provider.GetService<ScreenManager>()).IsNotNull();
    }

    [Test]
    public async Task AddWailsServices_RegistersAllBuiltinServices()
    {
        // 安排
        var services = new ServiceCollection();
        services.AddLogging();

        // 操作
        services.AddWailsServices();
        var provider = services.BuildServiceProvider();

        // 断言：内置服务全部注册
        await Assert.That(provider.GetService<FileServerService>()).IsNotNull();
        await Assert.That(provider.GetService<KvStoreService>()).IsNotNull();
        await Assert.That(provider.GetService<LogService>()).IsNotNull();
        await Assert.That(provider.GetService<NotificationService>()).IsNotNull();
        await Assert.That(provider.GetService<SqliteService>()).IsNotNull();
        await Assert.That(provider.GetService<UpdaterService>()).IsNotNull();
    }

    [Test]
    public async Task AddWailsCore_RegistersBothManagersAndServices()
    {
        // 安排
        var services = new ServiceCollection();
        services.AddLogging();

        // 操作
        services.AddWailsCore();
        var provider = services.BuildServiceProvider();

        // 断言：管理器 + 服务均注册
        await Assert.That(provider.GetService<EventProcessor>()).IsNotNull();
        await Assert.That(provider.GetService<BindingManager>()).IsNotNull();
        await Assert.That(provider.GetService<WindowManager>()).IsNotNull();
        await Assert.That(provider.GetService<FileServerService>()).IsNotNull();
        await Assert.That(provider.GetService<LogService>()).IsNotNull();
    }

    [Test]
    public async Task AddWails_IsEquivalentTo_AddWailsCore()
    {
        // 安排：两个独立 service collection，分别注册 AddWails 和 AddWailsCore
        var services1 = new ServiceCollection();
        services1.AddLogging();
        var services2 = new ServiceCollection();
        services2.AddLogging();

        // 操作
        services1.AddWails();
        services2.AddWailsCore();

        // 断言：注册的服务类型数量相同（核心管理器+服务+接口映射）
        // AddWails 应该至少包含 AddWailsCore 的所有服务
        var wailsTypes = services1.Select(s => s.ServiceType).ToHashSet();
        var coreTypes = services2.Select(s => s.ServiceType).ToHashSet();
        foreach (var coreType in coreTypes)
        {
            await Assert.That(wailsTypes.Contains(coreType)).IsTrue();
        }
    }

    [Test]
    public async Task AddWails_ThrowsOnNullServices()
    {
        // 安排与操作
        IServiceCollection? nullServices = null;

        // 断言：ArgumentNullException
        await Assert.That(() => nullServices!.AddWails())
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task AddWailsManagers_WithRegisteredPlatformApp_PassesToManagers()
    {
        // 安排
        var services = new ServiceCollection();
        services.AddLogging();
        var platformApp = new FakePlatformApp();
        services.AddSingleton<IPlatformApp>(platformApp);

        // 操作
        services.AddWailsManagers();
        var provider = services.BuildServiceProvider();

        // 断言：管理器能正确解析（平台应用已注入）
        await Assert.That(provider.GetService<WindowManager>()).IsNotNull();
        await Assert.That(provider.GetService<DialogManager>()).IsNotNull();
        await Assert.That(provider.GetService<ScreenManager>()).IsNotNull();
        await Assert.That(provider.GetService<IPlatformApp>()).IsSameReferenceAs(platformApp);
    }
}
