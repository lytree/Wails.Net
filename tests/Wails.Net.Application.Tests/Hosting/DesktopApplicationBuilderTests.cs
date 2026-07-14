using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TUnit.Core;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;

namespace Wails.Net.Application.Tests.Hosting;

/// <summary>
/// <see cref="DesktopApplicationBuilder"/>、<see cref="DesktopApplication"/>、
/// <see cref="DesktopHostedService"/> 与 <see cref="Application.RegisterBindings{T}"/>
/// 集成路径的单元测试（TUnit）。
/// 对应主题 A-3（AddWails 一站式入口）、A-4（RegisterBindings&lt;T&gt;）、A-5（IHostApplicationLifetime 暴露）。
/// </summary>
[NotInParallel]
public sealed class DesktopApplicationBuilderTests
{
    [Test]
    public async Task CreateBuilder_ReturnsInstance_WithExposedCollections()
    {
        // 安排与操作
        var builder = DesktopApplicationBuilder.CreateBuilder();

        // 断言：Services/Configuration/Logging 暴露可用
        await Assert.That(builder.Services).IsNotNull();
        await Assert.That(builder.Configuration).IsNotNull();
        await Assert.That(builder.Logging).IsNotNull();
    }

    [Test]
    public async Task CreateBuilder_RegistersDefaultServices()
    {
        // 安排与操作
        var builder = DesktopApplicationBuilder.CreateBuilder();
        var provider = builder.Services.BuildServiceProvider();

        // 断言：默认注册的关键服务
        await Assert.That(provider.GetService<Application>()).IsNotNull();
        await Assert.That(provider.GetService<ApplicationOptions>()).IsNotNull();
        await Assert.That(provider.GetService<IHostApplicationLifetime>()).IsNotNull();
        await Assert.That(provider.GetService<ILoggerFactory>()).IsNotNull();
    }

    [Test]
    public async Task Configure_OverridesApplicationName()
    {
        // 安排
        var builder = DesktopApplicationBuilder.CreateBuilder();

        // 操作
        builder.Configure(opts => opts.ApplicationName = "CustomApp");
        var app = builder.Build();

        // 断言
        await Assert.That(app.Name).IsEqualTo("CustomApp");
    }

    [Test]
    public async Task Build_UsesDefaultApplicationName_WhenNotConfigured()
    {
        // 安排
        var builder = DesktopApplicationBuilder.CreateBuilder();

        // 操作
        var app = builder.Build();

        // 断言：默认 ApplicationName
        await Assert.That(app.Name).IsEqualTo("Wails.Net Application");
    }

    [Test]
    public async Task Build_RegistersPlatformApp_WhenRegisteredAsInstance()
    {
        // 安排：直接注册 IPlatformApp 实例（避免 UsePlatform<T> 的构造函数约束）
        var builder = DesktopApplicationBuilder.CreateBuilder();
        var platform = new FakePlatformApp();

        // 操作
        builder.Services.AddSingleton<IPlatformApp>(platform);
        var app = builder.Build();

        // 断言：平台应用已注入 DI 容器
        var resolved = app.Services.GetService<IPlatformApp>();
        await Assert.That(resolved).IsNotNull();
        await Assert.That(resolved).IsSameReferenceAs(platform);
    }

    [Test]
    public async Task Build_ConfiguresPlatformCallback()
    {
        // 安排
        var builder = DesktopApplicationBuilder.CreateBuilder();
        var platform = new FakePlatformApp();
        IPlatformApp? captured = null;

        // 操作
        builder.Services.AddSingleton<IPlatformApp>(platform);
        builder.ConfigurePlatform(p => captured = p);
        builder.Build();

        // 断言：ConfigurePlatform 回调被调用，传入注册的平台应用
        await Assert.That(captured).IsNotNull();
        await Assert.That(captured).IsSameReferenceAs(platform);
    }

    [Test]
    public async Task Build_ExposesHostApplicationLifetime()
    {
        // 安排与操作
        var builder = DesktopApplicationBuilder.CreateBuilder();
        var app = builder.Build();

        // 断言：IDesktopApplication.Lifetime 暴露 IHostApplicationLifetime
        await Assert.That(app.Lifetime).IsNotNull();
        await Assert.That(app.Lifetime).IsAssignableTo<IHostApplicationLifetime>();
    }

    [Test]
    public async Task Build_ExposesServiceAndConfiguration()
    {
        // 安排与操作
        var builder = DesktopApplicationBuilder.CreateBuilder();
        var app = builder.Build();

        // 断言
        await Assert.That(app.Services).IsNotNull();
        await Assert.That(app.Configuration).IsNotNull();
        await Assert.That(app.LoggerFactory).IsNotNull();
        await Assert.That(app.Application).IsNotNull();
    }

    [Test]
    public async Task Build_MapsDesktopHostOptions_ToApplicationOptions()
    {
        // 安排：通过 Configure 设置选项
        var builder = DesktopApplicationBuilder.CreateBuilder();
        builder.Configure(opts =>
        {
            opts.ApplicationName = "MappedApp";
            opts.SingleInstance = true;
            opts.Window.Frameless = true;
        });

        // 操作
        var app = builder.Build();

        // 断言：DesktopHostOptions 映射到 ApplicationOptions
        await Assert.That(app.Application.Options.Name).IsEqualTo("MappedApp");
        await Assert.That(app.Application.Options.SingleInstance).IsTrue();
        await Assert.That(app.Application.Options.Frameless).IsTrue();
    }

    [Test]
    public async Task Build_BindsConfigurationFromWailsSection()
    {
        // 安排：在 Configuration 中设置 "Wails" 节
        var builder = DesktopApplicationBuilder.CreateBuilder();
        builder.Configuration["Wails:ApplicationName"] = "FromConfig";
        builder.Configuration["Wails:SingleInstance"] = "true";

        // 操作
        var app = builder.Build();

        // 断言：配置被正确绑定
        await Assert.That(app.Name).IsEqualTo("FromConfig");
        await Assert.That(app.Application.Options.SingleInstance).IsTrue();
    }

    [Test]
    public async Task Build_InitializesPlugins_WithPluginContext()
    {
        // 安排
        var builder = DesktopApplicationBuilder.CreateBuilder();
        var plugin = Substitute.For<Wails.Net.Application.Plugins.IPlugin>();

        // 通过反射调用 internal AddPlugin 方法（模拟 PluginBuilderExtensions.UsePlugin 内部调用）
        var addPluginMethod = typeof(DesktopApplicationBuilder)
            .GetMethod("AddPlugin", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        addPluginMethod!.Invoke(builder, new object[] { plugin });

        // 操作
        builder.Build();

        // 断言：插件的 Configure 方法被调用一次
        await Assert.That(() => plugin.Received(1).Configure(Arg.Any<Wails.Net.Application.Plugins.IPluginContext>()))
            .ThrowsNothing();
    }

    [Test]
    public async Task RegisterBindings_RetrievesServiceFromDI_WhenInitialized()
    {
        // 安排：注册一个测试服务到 DI
        var builder = DesktopApplicationBuilder.CreateBuilder();
        builder.Services.AddSingleton<TestBindableService>();
        var app = builder.Build();

        // 操作：通过 RegisterBindings<T> 从 DI 获取实例并注册到 BindingManager
        var service = app.Application.RegisterBindings<TestBindableService>();

        // 断言：返回的实例来自 DI 容器
        await Assert.That(service).IsNotNull();
        var fromDI = app.Services.GetRequiredService<TestBindableService>();
        await Assert.That(service).IsSameReferenceAs(fromDI);
    }

    [Test]
    public async Task RegisterBindings_Throws_WhenNotInitializedFromDI()
    {
        // 安排：直接 new Application（未通过 Build() 初始化 DI）
        var app = new Application(new ApplicationOptions());

        // 操作与断言：未初始化 ServiceProvider 时应抛出 InvalidOperationException
        await Assert.That(() => app.RegisterBindings<TestBindableService>())
            .ThrowsExactly<InvalidOperationException>();
    }

    /// <summary>
    /// 测试用可绑定服务。
    /// </summary>
    private sealed class TestBindableService
    {
        public string Greet(string name) => $"Hello, {name}!";
    }
}
