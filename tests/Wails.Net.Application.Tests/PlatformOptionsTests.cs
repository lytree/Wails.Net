using TUnit.Core;
using Wails.Net.Application.Options;

namespace Wails.Net.Application.Tests;

/// <summary>
/// 平台特定 Options 的单元测试（TUnit）。
/// 对应项 7：WindowsOptions / LinuxOptions 类与 ApplicationOptions.Windows / Linux 属性。
/// 验证平台选项的字段默认值、赋值行为，以及与 ApplicationOptions 的集成。
/// </summary>
[NotInParallel]
public sealed class PlatformOptionsTests
{
    /// <summary>
    /// WindowsOptions 默认所有字段为 null。
    /// </summary>
    [Test]
    public async Task WindowsOptions_Default_AllFieldsNull()
    {
        // 安排
        var opts = new WindowsOptions();

        // 断言
        await Assert.That(opts.WebviewUserDataPath).IsNull();
        await Assert.That(opts.WebviewBrowserPath).IsNull();
        await Assert.That(opts.AdditionalBrowserArgs).IsNull();
        await Assert.That(opts.EnabledFeatures).IsNull();
        await Assert.That(opts.DisabledFeatures).IsNull();
        await Assert.That(opts.WndClass).IsNull();
    }

    /// <summary>
    /// WindowsOptions 各字段可独立赋值。
    /// </summary>
    [Test]
    public async Task WindowsOptions_SetProperties_AllFieldsRetained()
    {
        // 安排
        var opts = new WindowsOptions
        {
            WebviewUserDataPath = @"C:\Temp\WebView2Data",
            WebviewBrowserPath = @"C:\WebView2Runtime",
            AdditionalBrowserArgs = new List<string> { "--remote-debugging-port=9222", "--disable-gpu" },
            EnabledFeatures = new List<string> { "ExperimentalFeature" },
            DisabledFeatures = new List<string> { "DeprecatedFeature" },
            WndClass = "CustomWailsWindow",
        };

        // 断言
        await Assert.That(opts.WebviewUserDataPath).IsEqualTo(@"C:\Temp\WebView2Data");
        await Assert.That(opts.WebviewBrowserPath).IsEqualTo(@"C:\WebView2Runtime");
        await Assert.That(opts.AdditionalBrowserArgs).Count().IsEqualTo(2);
        await Assert.That(opts.AdditionalBrowserArgs![0]).IsEqualTo("--remote-debugging-port=9222");
        await Assert.That(opts.AdditionalBrowserArgs[1]).IsEqualTo("--disable-gpu");
        await Assert.That(opts.EnabledFeatures).Count().IsEqualTo(1);
        await Assert.That(opts.EnabledFeatures![0]).IsEqualTo("ExperimentalFeature");
        await Assert.That(opts.DisabledFeatures).Count().IsEqualTo(1);
        await Assert.That(opts.DisabledFeatures![0]).IsEqualTo("DeprecatedFeature");
        await Assert.That(opts.WndClass).IsEqualTo("CustomWailsWindow");
    }

    /// <summary>
    /// LinuxOptions 默认所有字段为 null。
    /// </summary>
    [Test]
    public async Task LinuxOptions_Default_AllFieldsNull()
    {
        // 安排
        var opts = new LinuxOptions();

        // 断言
        await Assert.That(opts.WebviewUserDataPath).IsNull();
        await Assert.That(opts.AdditionalBrowserArgs).IsNull();
        await Assert.That(opts.EnabledFeatures).IsNull();
        await Assert.That(opts.DisabledFeatures).IsNull();
        await Assert.That(opts.WndClass).IsNull();
    }

    /// <summary>
    /// LinuxOptions 各字段可独立赋值。
    /// </summary>
    [Test]
    public async Task LinuxOptions_SetProperties_AllFieldsRetained()
    {
        // 安排
        var opts = new LinuxOptions
        {
            WebviewUserDataPath = "/home/user/.wails/webkit",
            AdditionalBrowserArgs = new List<string> { "--disable-gpu", "--no-sandbox" },
            EnabledFeatures = new List<string> { "FeatureA" },
            DisabledFeatures = new List<string> { "FeatureB" },
            WndClass = "WailsGtkWindow",
        };

        // 断言
        await Assert.That(opts.WebviewUserDataPath).IsEqualTo("/home/user/.wails/webkit");
        await Assert.That(opts.AdditionalBrowserArgs).Count().IsEqualTo(2);
        await Assert.That(opts.AdditionalBrowserArgs![0]).IsEqualTo("--disable-gpu");
        await Assert.That(opts.AdditionalBrowserArgs[1]).IsEqualTo("--no-sandbox");
        await Assert.That(opts.EnabledFeatures).Count().IsEqualTo(1);
        await Assert.That(opts.EnabledFeatures![0]).IsEqualTo("FeatureA");
        await Assert.That(opts.DisabledFeatures).Count().IsEqualTo(1);
        await Assert.That(opts.DisabledFeatures![0]).IsEqualTo("FeatureB");
        await Assert.That(opts.WndClass).IsEqualTo("WailsGtkWindow");
    }

    /// <summary>
    /// ApplicationOptions.Windows 默认为 null。
    /// </summary>
    [Test]
    public async Task ApplicationOptions_Windows_DefaultNull()
    {
        // 安排
        var opts = new ApplicationOptions();

        // 断言
        await Assert.That(opts.Windows).IsNull();
    }

    /// <summary>
    /// ApplicationOptions.Linux 默认为 null。
    /// </summary>
    [Test]
    public async Task ApplicationOptions_Linux_DefaultNull()
    {
        // 安排
        var opts = new ApplicationOptions();

        // 断言
        await Assert.That(opts.Linux).IsNull();
    }

    /// <summary>
    /// ApplicationOptions.Windows 可赋值并保留 WindowsOptions 实例。
    /// </summary>
    [Test]
    public async Task ApplicationOptions_Windows_CanBeAssigned()
    {
        // 安排
        var winOpts = new WindowsOptions
        {
            WebviewUserDataPath = @"C:\AppData\Wails",
            WebviewBrowserPath = @"C:\FixedRuntime",
        };
        var opts = new ApplicationOptions
        {
            Windows = winOpts,
        };

        // 断言
        await Assert.That(opts.Windows).IsSameReferenceAs(winOpts);
        await Assert.That(opts.Windows!.WebviewUserDataPath).IsEqualTo(@"C:\AppData\Wails");
        await Assert.That(opts.Windows.WebviewBrowserPath).IsEqualTo(@"C:\FixedRuntime");
    }

    /// <summary>
    /// ApplicationOptions.Linux 可赋值并保留 LinuxOptions 实例。
    /// </summary>
    [Test]
    public async Task ApplicationOptions_Linux_CanBeAssigned()
    {
        // 安排
        var linuxOpts = new LinuxOptions
        {
            WebviewUserDataPath = "/tmp/wails-webkit",
        };
        var opts = new ApplicationOptions
        {
            Linux = linuxOpts,
        };

        // 断言
        await Assert.That(opts.Linux).IsSameReferenceAs(linuxOpts);
        await Assert.That(opts.Linux!.WebviewUserDataPath).IsEqualTo("/tmp/wails-webkit");
    }

    /// <summary>
    /// ApplicationOptions 可同时设置 Windows 和 Linux 平台选项（互不影响）。
    /// </summary>
    [Test]
    public async Task ApplicationOptions_BothPlatforms_Coexist()
    {
        // 安排
        var winOpts = new WindowsOptions { WebviewBrowserPath = @"C:\Runtime" };
        var linuxOpts = new LinuxOptions { WndClass = "GtkWindow" };
        var opts = new ApplicationOptions
        {
            Windows = winOpts,
            Linux = linuxOpts,
        };

        // 断言：两个平台选项互不影响
        await Assert.That(opts.Windows).IsSameReferenceAs(winOpts);
        await Assert.That(opts.Linux).IsSameReferenceAs(linuxOpts);
        await Assert.That(opts.Windows!.WebviewBrowserPath).IsEqualTo(@"C:\Runtime");
        await Assert.That(opts.Linux!.WndClass).IsEqualTo("GtkWindow");
    }
}
