using TUnit.Assertions;
using TUnit.Core;
using Wails.Net.Cli.Commands;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// DoctorCommand 单元测试。
/// 验证版本比较逻辑和 doctor 命令的核心功能。
/// 对应问题 10.4：doctor-ng 深度依赖分析、WebView2 Runtime 检测。
/// </summary>
[NotInParallel]
public sealed class DoctorCommandTests
{
    [Test]
    [Arguments("120.0.2210.91", "100.0.0.0", true)]
    [Arguments("100.0.0.0", "100.0.0.0", true)]
    [Arguments("99.0.0.0", "100.0.0.0", false)]
    [Arguments("120.0.0.0", "100.0.0.0", true)]
    [Arguments("100.0.1.0", "100.0.0.0", true)]
    public async Task IsVersionAtLeast_CompareVersions_ReturnsExpected(
        string version, string minVersion, bool expected)
    {
        var result = DoctorCommand.IsVersionAtLeast(version, minVersion);
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task IsVersionAtLeast_InvalidVersion_ReturnsTrue()
    {
        // 无法解析版本时放行
        var result = DoctorCommand.IsVersionAtLeast("invalid", "100.0.0.0");
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsVersionAtLeast_BothInvalid_ReturnsTrue()
    {
        var result = DoctorCommand.IsVersionAtLeast("abc", "xyz");
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsVersionAtLeast_ShortVersion_HandlesCorrectly()
    {
        // Version.TryParse 对 "120" 解析为 120.0.0.0（缺少的部分为 -1）
        // 实际上 Version.TryParse("120") 返回 false，所以放行
        var result = DoctorCommand.IsVersionAtLeast("120", "100.0.0.0");
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsVersionAtLeast_MajorVersionComparison_ReturnsCorrect()
    {
        var result = DoctorCommand.IsVersionAtLeast("130.0.0.0", "120.0.0.0");
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsVersionAtLeast_MinorVersionComparison_ReturnsCorrect()
    {
        var result = DoctorCommand.IsVersionAtLeast("120.1.0.0", "120.0.0.0");
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsVersionAtLeast_BuildVersionComparison_ReturnsCorrect()
    {
        var result = DoctorCommand.IsVersionAtLeast("120.0.2210.91", "120.0.2210.90");
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsVersionAtLeast_EqualVersions_ReturnsTrue()
    {
        var result = DoctorCommand.IsVersionAtLeast("120.0.2210.91", "120.0.2210.91");
        await Assert.That(result).IsTrue();
    }
}
