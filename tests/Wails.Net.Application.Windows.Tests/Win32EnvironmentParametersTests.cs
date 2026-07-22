using TUnit.Core;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;

namespace Wails.Net.Application.Windows.Tests;

/// <summary>
/// Win32WebviewWindow.BuildEnvironmentParameters 的单元测试（TUnit）。
/// 对应项 7：验证 WindowsOptions 到 WebView2 环境参数的转换逻辑。
/// 纯函数测试，不依赖 WebView2 运行时，可在 CI 中安全运行。
/// </summary>
[NotInParallel]
public sealed class Win32EnvironmentParametersTests
{
    /// <summary>
    /// 所有字段为 null 时返回全 null/空列表。
    /// </summary>
    [Test]
    public async Task BuildEnvironmentParameters_AllNull_ReturnsEmptyTriple()
    {
        // 安排
        var opts = new WindowsOptions();

        // 操作
        var (browserFolder, userDataFolder, args) = Win32WebviewWindow.BuildEnvironmentParameters(opts);

        // 断言
        await Assert.That(browserFolder).IsNull();
        await Assert.That(userDataFolder).IsNull();
        await Assert.That(args).IsEmpty();
    }

    /// <summary>
    /// WebviewBrowserPath 设置后正确映射到 browserExecutableFolder。
    /// </summary>
    [Test]
    public async Task BuildEnvironmentParameters_BrowserPathSet_MapsToBrowserExecutableFolder()
    {
        // 安排
        var opts = new WindowsOptions { WebviewBrowserPath = @"C:\FixedRuntime" };

        // 操作
        var (browserFolder, userDataFolder, args) = Win32WebviewWindow.BuildEnvironmentParameters(opts);

        // 断言
        await Assert.That(browserFolder).IsEqualTo(@"C:\FixedRuntime");
        await Assert.That(userDataFolder).IsNull();
        await Assert.That(args).IsEmpty();
    }

    /// <summary>
    /// WebviewUserDataPath 设置后正确映射到 userDataFolder。
    /// </summary>
    [Test]
    public async Task BuildEnvironmentParameters_UserDataPathSet_MapsToUserDataFolder()
    {
        // 安排
        var opts = new WindowsOptions { WebviewUserDataPath = @"C:\Temp\WebView2Data" };

        // 操作
        var (browserFolder, userDataFolder, args) = Win32WebviewWindow.BuildEnvironmentParameters(opts);

        // 断言
        await Assert.That(browserFolder).IsNull();
        await Assert.That(userDataFolder).IsEqualTo(@"C:\Temp\WebView2Data");
        await Assert.That(args).IsEmpty();
    }

    /// <summary>
    /// 空字符串视为未设置，返回 null。
    /// </summary>
    [Test]
    public async Task BuildEnvironmentParameters_EmptyString_TreatedAsNull()
    {
        // 安排
        var opts = new WindowsOptions
        {
            WebviewBrowserPath = "",
            WebviewUserDataPath = "",
        };

        // 操作
        var (browserFolder, userDataFolder, args) = Win32WebviewWindow.BuildEnvironmentParameters(opts);

        // 断言
        await Assert.That(browserFolder).IsNull();
        await Assert.That(userDataFolder).IsNull();
        await Assert.That(args).IsEmpty();
    }

    /// <summary>
    /// AdditionalBrowserArgs 已带 -- 前缀时保留原样。
    /// </summary>
    [Test]
    public async Task BuildEnvironmentParameters_AdditionalArgsWithPrefix_RetainedAsIs()
    {
        // 安排
        var opts = new WindowsOptions
        {
            AdditionalBrowserArgs = new List<string> { "--remote-debugging-port=9222", "--disable-gpu" },
        };

        // 操作
        var (browserFolder, userDataFolder, args) = Win32WebviewWindow.BuildEnvironmentParameters(opts);

        // 断言
        await Assert.That(browserFolder).IsNull();
        await Assert.That(userDataFolder).IsNull();
        await Assert.That(args).Count().IsEqualTo(2);
        await Assert.That(args[0]).IsEqualTo("--remote-debugging-port=9222");
        await Assert.That(args[1]).IsEqualTo("--disable-gpu");
    }

    /// <summary>
    /// AdditionalBrowserArgs 不带 -- 前缀时自动补全。
    /// </summary>
    [Test]
    public async Task BuildEnvironmentParameters_AdditionalArgsWithoutPrefix_PrefixAdded()
    {
        // 安排
        var opts = new WindowsOptions
        {
            AdditionalBrowserArgs = new List<string> { "remote-debugging-port=9222", "disable-gpu" },
        };

        // 操作
        var (browserFolder, userDataFolder, args) = Win32WebviewWindow.BuildEnvironmentParameters(opts);

        // 断言
        await Assert.That(args).Count().IsEqualTo(2);
        await Assert.That(args[0]).IsEqualTo("--remote-debugging-port=9222");
        await Assert.That(args[1]).IsEqualTo("--disable-gpu");
    }

    /// <summary>
    /// EnabledFeatures 拼装为 --enable-features= 参数。
    /// </summary>
    [Test]
    public async Task BuildEnvironmentParameters_EnabledFeatures_JoinedAsEnableFeaturesArg()
    {
        // 安排
        var opts = new WindowsOptions
        {
            EnabledFeatures = new List<string> { "FeatureA", "FeatureB", "FeatureC" },
        };

        // 操作
        var (_, _, args) = Win32WebviewWindow.BuildEnvironmentParameters(opts);

        // 断言
        await Assert.That(args).Count().IsEqualTo(1);
        await Assert.That(args[0]).IsEqualTo("--enable-features=FeatureA,FeatureB,FeatureC");
    }

    /// <summary>
    /// DisabledFeatures 拼装为 --disable-features= 参数。
    /// </summary>
    [Test]
    public async Task BuildEnvironmentParameters_DisabledFeatures_JoinedAsDisableFeaturesArg()
    {
        // 安排
        var opts = new WindowsOptions
        {
            DisabledFeatures = new List<string> { "DeprecatedFeature" },
        };

        // 操作
        var (_, _, args) = Win32WebviewWindow.BuildEnvironmentParameters(opts);

        // 断言
        await Assert.That(args).Count().IsEqualTo(1);
        await Assert.That(args[0]).IsEqualTo("--disable-features=DeprecatedFeature");
    }

    /// <summary>
    /// 所有参数类型混合时按预期顺序拼装：AdditionalArgs → EnabledFeatures → DisabledFeatures。
    /// </summary>
    [Test]
    public async Task BuildEnvironmentParameters_AllArgsMixed_OrderPreserved()
    {
        // 安排
        var opts = new WindowsOptions
        {
            WebviewBrowserPath = @"C:\Runtime",
            WebviewUserDataPath = @"C:\Data",
            AdditionalBrowserArgs = new List<string> { "--no-sandbox" },
            EnabledFeatures = new List<string> { "FeatureX" },
            DisabledFeatures = new List<string> { "FeatureY", "FeatureZ" },
        };

        // 操作
        var (browserFolder, userDataFolder, args) = Win32WebviewWindow.BuildEnvironmentParameters(opts);

        // 断言
        await Assert.That(browserFolder).IsEqualTo(@"C:\Runtime");
        await Assert.That(userDataFolder).IsEqualTo(@"C:\Data");
        await Assert.That(args).Count().IsEqualTo(3);
        await Assert.That(args[0]).IsEqualTo("--no-sandbox");
        await Assert.That(args[1]).IsEqualTo("--enable-features=FeatureX");
        await Assert.That(args[2]).IsEqualTo("--disable-features=FeatureY,FeatureZ");
    }

    /// <summary>
    /// 空列表的 EnabledFeatures/DisabledFeatures/AdditionalBrowserArgs 不产生参数。
    /// </summary>
    [Test]
    public async Task BuildEnvironmentParameters_EmptyLists_NoArgsProduced()
    {
        // 安排
        var opts = new WindowsOptions
        {
            AdditionalBrowserArgs = new List<string>(),
            EnabledFeatures = new List<string>(),
            DisabledFeatures = new List<string>(),
        };

        // 操作
        var (browserFolder, userDataFolder, args) = Win32WebviewWindow.BuildEnvironmentParameters(opts);

        // 断言
        await Assert.That(browserFolder).IsNull();
        await Assert.That(userDataFolder).IsNull();
        await Assert.That(args).IsEmpty();
    }
}
