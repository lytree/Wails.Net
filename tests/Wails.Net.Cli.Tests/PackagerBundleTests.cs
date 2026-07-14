using TUnit.Core;
using Wails.Net.Cli.Build;
using Wails.Net.Cli.Config;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// Packager.ApplyBundleConfig 单元测试。
/// 对应主题 H-4.6：验证 BundleConfig 到 PackageOptions 的映射逻辑。
/// </summary>
[NotInParallel]
public sealed class PackagerBundleTests
{
    /// <summary>
    /// ApplyBundleConfig 应将 BundleConfig 的所有字段映射到 PackageOptions。
    /// </summary>
    [Test]
    public async Task ApplyBundleConfig_MapsAllFields()
    {
        // 安排
        var options = new PackageOptions();
        var bundle = new BundleConfig
        {
            Identifier = "com.example.app",
            IconPath = "icons",
            Resources = "resources",
            Copyright = "Copyright 2026",
            Category = "Developer Tools",
            ShortDescription = "Short",
            LongDescription = "Long description",
            Windows = new WindowsBundleConfig { Publisher = "Example Inc" },
        };

        // 操作
        Packager.ApplyBundleConfig(options, bundle);

        // 断言
        await Assert.That(options.BundleIdentifier).IsEqualTo("com.example.app");
        await Assert.That(options.IconPath).IsEqualTo("icons");
        await Assert.That(options.Resources).IsEqualTo("resources");
        await Assert.That(options.Copyright).IsEqualTo("Copyright 2026");
        await Assert.That(options.Category).IsEqualTo("Developer Tools");
        await Assert.That(options.ShortDescription).IsEqualTo("Short");
        await Assert.That(options.LongDescription).IsEqualTo("Long description");
        await Assert.That(options.Publisher).IsEqualTo("Example Inc");
    }

    /// <summary>
    /// ApplyBundleConfig 在 bundle 为 null 时不修改 PackageOptions。
    /// </summary>
    [Test]
    public async Task ApplyBundleConfig_NullBundle_NoChange()
    {
        // 安排
        var options = new PackageOptions
        {
            BundleIdentifier = "original-id",
            Publisher = "original-publisher",
        };

        // 操作
        Packager.ApplyBundleConfig(options, null);

        // 断言：原值不变
        await Assert.That(options.BundleIdentifier).IsEqualTo("original-id");
        await Assert.That(options.Publisher).IsEqualTo("original-publisher");
    }

    /// <summary>
    /// ApplyBundleConfig 不覆盖已设置的 PackageOptions 字段（??= 语义）。
    /// </summary>
    [Test]
    public async Task ApplyBundleConfig_DoesNotOverrideExistingValues()
    {
        // 安排：PackageOptions 已有值
        var options = new PackageOptions
        {
            BundleIdentifier = "existing-id",
            Publisher = "existing-publisher",
        };
        var bundle = new BundleConfig
        {
            Identifier = "should-not-override",
            Windows = new WindowsBundleConfig { Publisher = "should-not-override" },
        };

        // 操作
        Packager.ApplyBundleConfig(options, bundle);

        // 断言：命令行已设值不被覆盖
        await Assert.That(options.BundleIdentifier).IsEqualTo("existing-id");
        await Assert.That(options.Publisher).IsEqualTo("existing-publisher");
    }
}
