using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TUnit.Core;
using Wails.Net.Application.Hosting;

namespace Wails.Net.Application.Tests.Hosting;

/// <summary>
/// <see cref="DesktopHostOptions"/> 的单元测试（TUnit）。
/// 验证默认值、配置节绑定（"Wails" 节）、嵌套选项结构。
/// 对应 AGENTS.md §1.1.1 统一配置节命名：根节为 <c>Wails</c>。
/// </summary>
public sealed class DesktopHostOptionsTests
{
    [Test]
    public async Task Defaults_AreApplied()
    {
        // 安排与操作
        var opts = new DesktopHostOptions();

        // 断言
        await Assert.That(opts.ApplicationName).IsEqualTo("Wails.Net Application");
        await Assert.That(opts.AssetsDirectory).IsEqualTo("dist");
        await Assert.That(opts.DevServerUrl).IsNull();
        await Assert.That(opts.SingleInstance).IsFalse();
        await Assert.That(opts.Permissions).IsNotNull();
        await Assert.That(opts.Permissions.Count).IsEqualTo(0);
        await Assert.That(opts.Assets).IsNotNull();
        await Assert.That(opts.Window).IsNotNull();
    }

    [Test]
    public async Task WindowDefaults_AreApplied()
    {
        // 安排与操作
        var window = new DesktopHostOptions.WindowOptions();

        // 断言
        await Assert.That(window.Width).IsEqualTo(1280);
        await Assert.That(window.Height).IsEqualTo(720);
        await Assert.That(window.Title).IsEqualTo("Wails.Net");
        await Assert.That(window.Frameless).IsFalse();
    }

    [Test]
    public async Task AssetsDefaults_AreApplied()
    {
        // 安排与操作
        var assets = new DesktopHostOptions.AssetsOptions();

        // 断言
        await Assert.That(assets.RootPath).IsEqualTo(string.Empty);
        await Assert.That(assets.DefaultDocument).IsEqualTo("index.html");
        await Assert.That(assets.EnableSpaFallback).IsTrue();
    }

    [Test]
    public async Task BindConfiguration_FromWailsSection_PopulatesOptions()
    {
        // 安排：构造内存配置源，模拟 appsettings.json 中的 "Wails" 节
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Wails:ApplicationName"] = "MyTestApp",
                ["Wails:SingleInstance"] = "true",
                ["Wails:Window:Frameless"] = "true",
                ["Wails:Window:Title"] = "Custom Title",
                ["Wails:Assets:RootPath"] = "frontend-dist",
                ["Wails:Assets:DefaultDocument"] = "default.html",
                ["Wails:Assets:EnableSpaFallback"] = "false",
                ["Wails:Permissions:0"] = "core:default",
                ["Wails:Permissions:1"] = "window:default",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddOptions<DesktopHostOptions>()
            .Bind(config.GetSection("Wails"));
        var provider = services.BuildServiceProvider();

        // 操作
        var opts = provider.GetRequiredService<IOptions<DesktopHostOptions>>().Value;

        // 断言
        await Assert.That(opts.ApplicationName).IsEqualTo("MyTestApp");
        await Assert.That(opts.SingleInstance).IsTrue();
        await Assert.That(opts.Window.Frameless).IsTrue();
        await Assert.That(opts.Window.Title).IsEqualTo("Custom Title");
        await Assert.That(opts.Assets.RootPath).IsEqualTo("frontend-dist");
        await Assert.That(opts.Assets.DefaultDocument).IsEqualTo("default.html");
        await Assert.That(opts.Assets.EnableSpaFallback).IsFalse();
        await Assert.That(opts.Permissions.Count).IsEqualTo(2);
        await Assert.That(opts.Permissions[0]).IsEqualTo("core:default");
        await Assert.That(opts.Permissions[1]).IsEqualTo("window:default");
    }

    [Test]
    public async Task IOptionsMonitor_ReflectsLatestValue_AfterChange()
    {
        // 安排：使用 IOptionsMonitor<T> 支持热重载（对应主题 A-7）
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Wails:ApplicationName"] = "InitialName",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddOptions<DesktopHostOptions>()
            .Bind(config.GetSection("Wails"));
        var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<DesktopHostOptions>>();

        // 断言初始值
        await Assert.That(monitor.CurrentValue.ApplicationName).IsEqualTo("InitialName");

        // 操作：动态修改配置源
        config["Wails:ApplicationName"] = "UpdatedName";

        // 断言：IOptionsMonitor 反映最新值（ConfigurationRoot 重载后）
        // 注意：内存配置源需要触发重新加载
        if (config is IConfigurationRoot root)
        {
            root.Reload();
        }
        await Assert.That(monitor.CurrentValue.ApplicationName).IsEqualTo("UpdatedName");
    }
}
