using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TUnit.Core;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform.ServerMode;
using Wails.Net.Application.Plugins;

namespace Wails.Net.Application.Tests.Plugins;

/// <summary>
/// 插件生命周期对齐的单元测试（TUnit）。
/// 对应主题 B：IPlugin.StartupAsync/ShutdownAsync、PluginManager.StartupPluginsAsync/ShutdownPluginsAsync、
/// Application.Run/Shutdown 集成插件生命周期。
/// </summary>
[NotInParallel]
public sealed class PluginLifecycleTests
{
    /// <summary>
    /// 记录调用顺序的测试插件。每个插件维护自己的调用列表。
    /// </summary>
    private sealed class TrackingPlugin : IPlugin
    {
        public string Name { get; }
        public List<string> Calls { get; } = new();
        public bool StartupThrows { get; set; }
        public bool ShutdownThrows { get; set; }

        public TrackingPlugin(string name)
        {
            Name = name;
        }

        public void ConfigureServices(IServiceCollection services) { Calls.Add($"{Name}:ConfigureServices"); }
        public void Configure(IPluginContext context) { Calls.Add($"{Name}:Configure"); }

        public Task StartupAsync(CancellationToken cancellationToken = default)
        {
            Calls.Add($"{Name}:Startup");
            if (StartupThrows) throw new InvalidOperationException($"{Name} startup failed");
            return Task.CompletedTask;
        }

        public Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            Calls.Add($"{Name}:Shutdown");
            if (ShutdownThrows) throw new InvalidOperationException($"{Name} shutdown failed");
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 共享调用记录列表的测试插件，用于跨插件的全局调用顺序验证。
    /// 所有插件实例将调用事件追加到同一个 <paramref name="sharedCalls"/> 列表中，
    /// 从而真实反映 <see cref="PluginManager"/> 内部的执行顺序。
    /// </summary>
    private sealed class SharedTrackingPlugin : IPlugin
    {
        public string Name { get; }
        private readonly List<string> _sharedCalls;
        public bool StartupThrows { get; set; }
        public bool ShutdownThrows { get; set; }

        public SharedTrackingPlugin(string name, List<string> sharedCalls)
        {
            Name = name;
            _sharedCalls = sharedCalls;
        }

        public void ConfigureServices(IServiceCollection services) { _sharedCalls.Add($"{Name}:ConfigureServices"); }
        public void Configure(IPluginContext context) { _sharedCalls.Add($"{Name}:Configure"); }

        public Task StartupAsync(CancellationToken cancellationToken = default)
        {
            _sharedCalls.Add($"{Name}:Startup");
            if (StartupThrows) throw new InvalidOperationException($"{Name} startup failed");
            return Task.CompletedTask;
        }

        public Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            _sharedCalls.Add($"{Name}:Shutdown");
            if (ShutdownThrows) throw new InvalidOperationException($"{Name} shutdown failed");
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 使用默认实现的插件（不覆盖 StartupAsync/ShutdownAsync），
    /// 验证默认 no-op 行为。
    /// </summary>
    private sealed class DefaultLifecyclePlugin : IPlugin
    {
        public string Name => "DefaultLifecycle";
        public void ConfigureServices(IServiceCollection services) { }
        public void Configure(IPluginContext context) { }
    }

    [Test]
    public async Task IPlugin_DefaultStartupAsync_ReturnsCompletedTask()
    {
        // 安排：DefaultLifecyclePlugin 不覆盖 StartupAsync/ShutdownAsync
        IPlugin plugin = new DefaultLifecyclePlugin();

        // 操作与断言：默认实现应返回已完成的 Task（不抛异常）
        await Assert.That(() => plugin.StartupAsync()).ThrowsNothing();
        await Assert.That(() => plugin.ShutdownAsync()).ThrowsNothing();
    }

    [Test]
    public async Task PluginManager_StartupPluginsAsync_CallsAllPluginsInOrder()
    {
        // 安排：使用共享调用列表以真实反映 PluginManager 内部的执行顺序
        var sharedCalls = new List<string>();
        var manager = new PluginManager(Substitute.For<IServiceProvider>(), NullLogger<PluginManager>.Instance);
        var plugin1 = new SharedTrackingPlugin("P1", sharedCalls);
        var plugin2 = new SharedTrackingPlugin("P2", sharedCalls);
        var plugin3 = new SharedTrackingPlugin("P3", sharedCalls);
        manager.Register(plugin1);
        manager.Register(plugin2);
        manager.Register(plugin3);

        // 操作
        await manager.StartupPluginsAsync();

        // 断言：按注册顺序调用 StartupAsync（共享列表保留实际调用顺序）
        var startupOrder = sharedCalls.Where(c => c.EndsWith(":Startup")).ToList();
        await Assert.That(startupOrder.Count).IsEqualTo(3);
        await Assert.That(startupOrder[0]).IsEqualTo("P1:Startup");
        await Assert.That(startupOrder[1]).IsEqualTo("P2:Startup");
        await Assert.That(startupOrder[2]).IsEqualTo("P3:Startup");
    }

    [Test]
    public async Task PluginManager_ShutdownPluginsAsync_CallsAllPluginsInReverseOrder()
    {
        // 安排：使用共享调用列表以真实反映 PluginManager 内部的执行顺序
        var sharedCalls = new List<string>();
        var manager = new PluginManager(Substitute.For<IServiceProvider>(), NullLogger<PluginManager>.Instance);
        var plugin1 = new SharedTrackingPlugin("P1", sharedCalls);
        var plugin2 = new SharedTrackingPlugin("P2", sharedCalls);
        var plugin3 = new SharedTrackingPlugin("P3", sharedCalls);
        manager.Register(plugin1);
        manager.Register(plugin2);
        manager.Register(plugin3);

        // 操作
        await manager.ShutdownPluginsAsync();

        // 断言：按注册逆序调用 ShutdownAsync（共享列表保留实际调用顺序）
        var shutdownOrder = sharedCalls.Where(c => c.EndsWith(":Shutdown")).ToList();
        await Assert.That(shutdownOrder.Count).IsEqualTo(3);
        await Assert.That(shutdownOrder[0]).IsEqualTo("P3:Shutdown");
        await Assert.That(shutdownOrder[1]).IsEqualTo("P2:Shutdown");
        await Assert.That(shutdownOrder[2]).IsEqualTo("P1:Shutdown");
    }

    [Test]
    public async Task PluginManager_StartupPluginsAsync_ContinuesOnPluginFailure()
    {
        // 安排：P2 启动时抛异常
        var manager = new PluginManager(Substitute.For<IServiceProvider>(), NullLogger<PluginManager>.Instance);
        var plugin1 = new TrackingPlugin("P1");
        var plugin2 = new TrackingPlugin("P2") { StartupThrows = true };
        var plugin3 = new TrackingPlugin("P3");
        manager.Register(plugin1);
        manager.Register(plugin2);
        manager.Register(plugin3);

        // 操作：不应抛异常
        await Assert.That(() => manager.StartupPluginsAsync()).ThrowsNothing();

        // 断言：P1 和 P3 仍然被启动（P2 失败不中断流程）
        await Assert.That(plugin1.Calls).Contains("P1:Startup");
        await Assert.That(plugin3.Calls).Contains("P3:Startup");
    }

    [Test]
    public async Task PluginManager_ShutdownPluginsAsync_ContinuesOnPluginFailure()
    {
        // 安排：P2 关闭时抛异常
        var manager = new PluginManager(Substitute.For<IServiceProvider>(), NullLogger<PluginManager>.Instance);
        var plugin1 = new TrackingPlugin("P1");
        var plugin2 = new TrackingPlugin("P2") { ShutdownThrows = true };
        var plugin3 = new TrackingPlugin("P3");
        manager.Register(plugin1);
        manager.Register(plugin2);
        manager.Register(plugin3);

        // 操作：不应抛异常
        await Assert.That(() => manager.ShutdownPluginsAsync()).ThrowsNothing();

        // 断言：P1 和 P3 仍然被关闭（P2 失败不中断流程）
        await Assert.That(plugin1.Calls).Contains("P1:Shutdown");
        await Assert.That(plugin3.Calls).Contains("P3:Shutdown");
    }

    [Test]
    public async Task PluginManager_StartupPluginsAsync_NoPlugins_DoesNotThrow()
    {
        // 安排
        var manager = new PluginManager(Substitute.For<IServiceProvider>(), NullLogger<PluginManager>.Instance);

        // 操作与断言：无插件时不抛异常
        await Assert.That(() => manager.StartupPluginsAsync()).ThrowsNothing();
    }

    [Test]
    public async Task PluginManager_RegisterFromServices_CollectsPluginsFromDI()
    {
        // 安排：构造带 IPlugin 注册的 DI 容器
        var services = new ServiceCollection();
        var plugin1 = new TrackingPlugin("P1");
        var plugin2 = new TrackingPlugin("P2");
        services.AddSingleton<IPlugin>(plugin1);
        services.AddSingleton<IPlugin>(plugin2);
        var provider = services.BuildServiceProvider();

        // 操作
        var manager = new PluginManager(provider);
        manager.RegisterFromServices();

        // 断言：两个插件都被收集
        await Assert.That(manager.Plugins.Count).IsEqualTo(2);
        await Assert.That(manager.Plugins).Contains(plugin1);
        await Assert.That(manager.Plugins).Contains(plugin2);
    }

    [Test]
    public async Task DesktopApplicationBuilder_Build_PopulatesPluginManager()
    {
        // 安排
        var builder = DesktopApplicationBuilder.CreateBuilder();
        var plugin = Substitute.For<IPlugin>();
        plugin.Name.Returns("TestPlugin");
        builder.UsePlugin(plugin);

        // 操作
        var app = builder.Build();

        // 断言：Application.Plugins 可用且包含插件
        await Assert.That(app.Application.Plugins).IsNotNull();
        await Assert.That(app.Application.Plugins!.Plugins.Count).IsGreaterThan(0);
        await Assert.That(app.Application.Plugins.Plugins).Contains(plugin);
    }

    [Test]
    public async Task Application_Run_TriggersPluginStartup()
    {
        // 安排：使用 ServerPlatformApp（Run 阻塞直到 SignalShutdown）
        var app = new Application(new ApplicationOptions());
        var platformApp = new ServerPlatformApp(app.Options);
        app.SetPlatformApp(platformApp);

        var manager = new PluginManager(Substitute.For<IServiceProvider>(), NullLogger<PluginManager>.Instance);
        var plugin = new TrackingPlugin("TestPlugin");
        manager.Register(plugin);

        // 通过反射设置 _pluginManager 字段（模拟 InitializeFromServiceProvider 的行为）
        var field = typeof(Application).GetField("_pluginManager",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field!.SetValue(app, manager);

        // 在 OnAfterStart 中触发 SignalShutdown，让 Run() 立即返回
        app.Options.OnAfterStart = () => platformApp.SignalShutdown();

        // 操作
        app.Run();

        // 断言：插件的 StartupAsync 被调用
        await Assert.That(plugin.Calls).Contains("TestPlugin:Startup");
    }

    [Test]
    public async Task Application_Shutdown_TriggersPluginShutdown()
    {
        // 安排
        var app = new Application(new ApplicationOptions());
        var manager = new PluginManager(Substitute.For<IServiceProvider>(), NullLogger<PluginManager>.Instance);
        var plugin = new TrackingPlugin("TestPlugin");
        manager.Register(plugin);

        // 通过反射设置 _pluginManager 字段（模拟 InitializeFromServiceProvider 的行为）
        var pluginField = typeof(Application).GetField("_pluginManager",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        pluginField!.SetValue(app, manager);

        // 通过反射设置 _isRunning=true，使 Shutdown() 实际执行清理逻辑
        // （Shutdown() 第一个检查就是 if (!_isRunning) return; 直接返回）
        var isRunningField = typeof(Application).GetField("_isRunning",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        isRunningField!.SetValue(app, true);

        // 操作
        app.Shutdown();

        // 断言：插件的 ShutdownAsync 被调用
        await Assert.That(plugin.Calls).Contains("TestPlugin:Shutdown");
    }
}
