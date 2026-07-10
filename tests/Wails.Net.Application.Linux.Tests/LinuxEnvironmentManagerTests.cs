using System.Runtime.InteropServices;
using TUnit.Core;
using Wails.Net.Application.Managers;

namespace Wails.Net.Application.Linux.Tests;

/// <summary>
/// LinuxEnvironmentManager 的单元测试（TUnit）。
/// 验证操作系统名称、架构、主目录和数据目录的正确性。
/// 注意：测试读取环境变量，因此不并行执行。
/// </summary>
[NotInParallel]
public sealed class LinuxEnvironmentManagerTests
{
    /// <summary>
    /// 被测对象。
    /// </summary>
    private readonly LinuxEnvironmentManager _manager = new();

    [Test]
    public async Task GetOS_ReturnsLinux()
    {
        // 操作与断言
        await Assert.That(_manager.GetOS()).IsEqualTo("linux");
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

        // 断言：非空字符串
        await Assert.That(homeDir).IsNotNull();
        await Assert.That(homeDir).IsNotEmpty();
    }

    [Test]
    public async Task GetHomeDir_PrefersUserProfile()
    {
        // 操作
        var homeDir = _manager.GetHomeDir();

        // 断言：应等于 Environment.SpecialFolder.UserProfile 或回退路径
        var expected = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(expected))
        {
            expected = $"/home/{Environment.UserName}";
        }
        await Assert.That(homeDir).IsEqualTo(expected);
    }

    [Test]
    public async Task GetDataDir_ReturnsNonEmpty()
    {
        // 操作
        var dataDir = _manager.GetDataDir();

        // 断言：非空字符串
        await Assert.That(dataDir).IsNotNull();
        await Assert.That(dataDir).IsNotEmpty();
    }

    [Test]
    public async Task GetDataDir_PrefersXdgDataHome_WhenSet()
    {
        // 安排：设置 XDG_DATA_HOME 环境变量
        var originalValue = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var tempPath = $"/tmp/wails-test-data-{Guid.NewGuid()}";
        try
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", tempPath);

            // 操作
            var dataDir = _manager.GetDataDir();

            // 断言：使用 XDG_DATA_HOME 的值
            await Assert.That(dataDir).IsEqualTo(tempPath);
        }
        finally
        {
            // 恢复环境变量
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", originalValue);
        }
    }

    [Test]
    public async Task GetDataDir_FallsBackToHomeLocalShare_WhenXdgDataHomeUnset()
    {
        // 安排：清除 XDG_DATA_HOME 环境变量
        var originalValue = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        try
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", null);

            // 操作
            var dataDir = _manager.GetDataDir();

            // 断言：回退到 ~/.local/share
            var expected = $"{_manager.GetHomeDir()}/.local/share";
            await Assert.That(dataDir).IsEqualTo(expected);
        }
        finally
        {
            // 恢复环境变量
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", originalValue);
        }
    }
}
