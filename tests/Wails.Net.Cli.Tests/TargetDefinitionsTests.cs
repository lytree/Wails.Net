using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using Wails.Net.Cli.Build;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// <see cref="TargetDefinitions"/> 单元测试（SDK-4）。
/// 验证 <c>--all-targets</c> 的目标 RID 集合构成与跨平台特性。
/// </summary>
[NotInParallel]
public sealed class TargetDefinitionsTests
{
    [Test]
    public async Task GetAllTargets_AlwaysIncludesCrossPlatformDefaults()
    {
        // Arrange / Act
        var targets = TargetDefinitions.GetAllTargets();

        // Assert：三个跨平台默认 RID 必须始终存在
        await Assert.That(targets).Contains("win-x64");
        await Assert.That(targets).Contains("linux-x64");
        await Assert.That(targets).Contains("android-arm64");
    }

    [Test]
    public async Task GetAllTargets_OnNonMacOSHost_ExcludesOsxRids()
    {
        // Arrange / Act
        var targets = TargetDefinitions.GetAllTargets();

        // Assert：非 macOS 主机不得加入 osx-*（macOS 只能在 macOS 主机构建/打包）
        if (!OperatingSystem.IsMacOS())
        {
            await Assert.That(targets).DoesNotContain("osx-arm64");
            await Assert.That(targets).DoesNotContain("osx-x64");
        }
    }

    [Test]
    public async Task GetAllTargets_OnMacOSHost_IncludesOsxRids()
    {
        // Arrange / Act
        var targets = TargetDefinitions.GetAllTargets();

        // Assert：macOS 主机额外加入 osx-arm64 与 osx-x64
        if (OperatingSystem.IsMacOS())
        {
            await Assert.That(targets).Contains("osx-arm64");
            await Assert.That(targets).Contains("osx-x64");
        }
    }

    [Test]
    public async Task GetAllTargets_ReturnsDistinctRids()
    {
        // Arrange / Act
        var targets = TargetDefinitions.GetAllTargets();

        // Assert：无重复 RID
        await Assert.That(targets.Length).IsEqualTo(targets.Distinct().Count());
    }
}