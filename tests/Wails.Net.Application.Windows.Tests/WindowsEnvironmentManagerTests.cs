using System.Runtime.InteropServices;
using TUnit.Core;
using Wails.Net.Application.Managers;

namespace Wails.Net.Application.Windows.Tests;

/// <summary>
/// WindowsEnvironmentManager 的单元测试（TUnit）。
/// 验证操作系统名称、架构、主目录和数据目录的正确性。
/// </summary>
public sealed class WindowsEnvironmentManagerTests
{
    /// <summary>
    /// 被测对象。
    /// </summary>
    private readonly WindowsEnvironmentManager _manager = new();

    [Test]
    public async Task GetOS_ReturnsWindows()
    {
        // 操作与断言
        await Assert.That(_manager.GetOS()).IsEqualTo("windows");
    }

    [Test]
    public async Task GetArch_ReturnsValidArch()
    {
        // 操作
        var arch = _manager.GetArch();

        // 断言：返回值应为 Go 风格的架构字符串之一
        await Assert.That(arch).IsEqualTo("amd64")
            .Or.IsEqualTo("arm64")
            .Or.IsEqualTo("386")
            .Or.IsEqualTo("arm");
    }

    [Test]
    public async Task GetArch_MatchesRuntimeInformation()
    {
        // 操作
        var arch = _manager.GetArch();

        // 断言：与 RuntimeInformation.ProcessArchitecture 的映射一致
        var expected = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "amd64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "386",
            Architecture.Arm => "arm",
            _ => "unknown"
        };
        await Assert.That(arch).IsEqualTo(expected);
    }

    [Test]
    public async Task GetHomeDir_ReturnsNonEmpty()
    {
        // 操作
        var homeDir = _manager.GetHomeDir();

        // 断言：非空且目录存在
        await Assert.That(homeDir).IsNotNull();
        await Assert.That(homeDir).IsNotEmpty();
        await Assert.That(Directory.Exists(homeDir)).IsTrue();
    }

    [Test]
    public async Task GetDataDir_ReturnsNonEmpty()
    {
        // 操作
        var dataDir = _manager.GetDataDir();

        // 断言：非空且目录存在
        await Assert.That(dataDir).IsNotNull();
        await Assert.That(dataDir).IsNotEmpty();
        await Assert.That(Directory.Exists(dataDir)).IsTrue();
    }
}
