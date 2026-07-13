using TUnit.Core;
using Wails.Net.Application.Clipboard;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Platform.ServerMode;

namespace Wails.Net.Application.Linux.Tests;

/// <summary>
/// PlatformFactory 在 Linux 平台上的单元测试（TUnit）。
/// 验证反射加载 Linux 平台实现的正确性。
/// 注意：PlatformFactory 使用静态状态（环境变量），因此不并行执行。
/// </summary>
[NotInParallel]
public sealed class PlatformFactoryLinuxTests
{
    private const string ServerModeEnvVar = "WAILS_SERVER_MODE";

    /// <summary>
    /// 保存环境变量的原始值，用于测试结束后恢复。
    /// </summary>
    private string? _originalEnvVarValue;

    [Before(Test)]
    public void Setup()
    {
        // 保存原始值并清除环境变量，确保从非 Server 模式开始
        _originalEnvVarValue = Environment.GetEnvironmentVariable(ServerModeEnvVar);
        Environment.SetEnvironmentVariable(ServerModeEnvVar, null);
    }

    [After(Test)]
    public void Teardown()
    {
        // 恢复环境变量的原始值
        Environment.SetEnvironmentVariable(ServerModeEnvVar, _originalEnvVarValue);
    }

    [Test]
    public async Task CreatePlatformApp_OnLinux_ReturnsLinuxPlatformApp()
    {
        // 操作：通过工厂创建平台应用
        var result = PlatformFactory.CreatePlatformApp(new ApplicationOptions());

        // 断言：返回非空且类型为 LinuxPlatformApp（强类型检查，避免字符串匹配误判）
        await Assert.That(result).IsNotNull();
        await Assert.That(result is LinuxPlatformApp).IsTrue();
    }

    [Test]
    public async Task CreateClipboard_OnLinux_ReturnsLinuxClipboard()
    {
        // 操作：通过工厂创建剪贴板
        var result = PlatformFactory.CreateClipboard();

        // 断言：返回非空且类型为 LinuxClipboard（强类型检查，避免字符串匹配误判）
        await Assert.That(result).IsNotNull();
        await Assert.That(result is LinuxClipboard).IsTrue();
    }

    [Test]
    public async Task IsServerMode_ReturnsFalse_ByDefault()
    {
        // 安排：环境变量已在 Setup 中清除

        // 操作与断言：默认非 Server 模式
        await Assert.That(PlatformFactory.IsServerMode()).IsFalse();
    }

    [Test]
    public async Task IsServerMode_ReturnsTrue_WhenEnvVarSet()
    {
        // 安排：设置环境变量为 "true"
        Environment.SetEnvironmentVariable(ServerModeEnvVar, "true");

        // 操作与断言：Server 模式启用
        await Assert.That(PlatformFactory.IsServerMode()).IsTrue();
    }

    [Test]
    public async Task IsServerMode_ReturnsFalse_WhenEnvVarSetToFalse()
    {
        // 安排：设置环境变量为 "false"
        Environment.SetEnvironmentVariable(ServerModeEnvVar, "false");

        // 操作与断言：Server 模式未启用
        await Assert.That(PlatformFactory.IsServerMode()).IsFalse();
    }

    [Test]
    public async Task CreatePlatformApp_InServerMode_ReturnsServerPlatformApp()
    {
        // 安排：启用 Server 模式
        Environment.SetEnvironmentVariable(ServerModeEnvVar, "true");

        // 操作：通过工厂创建平台应用
        var result = PlatformFactory.CreatePlatformApp(new ApplicationOptions());

        // 断言：返回 ServerPlatformApp 类型（强类型检查）
        await Assert.That(result).IsNotNull();
        await Assert.That(result is ServerPlatformApp).IsTrue();
    }

    [Test]
    public async Task CreateClipboard_InServerMode_ReturnsServerClipboard()
    {
        // 安排：启用 Server 模式
        Environment.SetEnvironmentVariable(ServerModeEnvVar, "true");

        // 操作：通过工厂创建剪贴板
        var result = PlatformFactory.CreateClipboard();

        // 断言：返回 ServerClipboard 类型（强类型检查）
        await Assert.That(result).IsNotNull();
        await Assert.That(result is ServerClipboard).IsTrue();
    }
}
