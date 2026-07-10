using TUnit.Core;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Platform.ServerMode;

namespace Wails.Net.Application.Tests;

/// <summary>
/// PlatformFactory 的单元测试（TUnit）。
/// 使用环境变量 WAILS_SERVER_MODE 控制 Server 模式。
/// 注意：环境变量为进程级共享状态，因此此类中的测试不并行执行。
/// </summary>
[NotInParallel]
public sealed class PlatformFactoryTests
{
    private const string ServerModeEnvVar = "WAILS_SERVER_MODE";

    /// <summary>
    /// 保存环境变量的原始值，用于测试结束后恢复。
    /// </summary>
    private string? _originalEnvVarValue;

    [Before(Test)]
    public void Setup()
    {
        // 保存原始值并清除环境变量，确保每个测试从已知状态开始
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
    public async Task IsServerMode_ReturnsFalse_ByDefault()
    {
        // 安排：环境变量已在 Setup 中清除

        // 操作与断言
        await Assert.That(PlatformFactory.IsServerMode()).IsFalse();
    }

    [Test]
    public async Task IsServerMode_ReturnsTrue_WhenEnvVarSet()
    {
        // 安排：设置环境变量为 true
        Environment.SetEnvironmentVariable(ServerModeEnvVar, "true");

        // 操作与断言
        await Assert.That(PlatformFactory.IsServerMode()).IsTrue();
    }

    [Test]
    public async Task CreatePlatformApp_ReturnsServerPlatformApp_InServerMode()
    {
        // 安排：设置 Server 模式
        Environment.SetEnvironmentVariable(ServerModeEnvVar, "true");
        var options = new ApplicationOptions { Name = "TestApp" };

        // 操作
        var result = PlatformFactory.CreatePlatformApp(options);

        // 断言
        await Assert.That(result).IsTypeOf<ServerPlatformApp>();
    }

    [Test]
    public async Task CreateWebviewWindowImpl_ReturnsServerWebviewWindow_InServerMode()
    {
        // 安排：设置 Server 模式
        Environment.SetEnvironmentVariable(ServerModeEnvVar, "true");
        var options = new WebviewWindowOptions { Name = "TestWindow" };

        // 操作
        var result = PlatformFactory.CreateWebviewWindowImpl(1, options);

        // 断言
        await Assert.That(result).IsTypeOf<ServerWebviewWindow>();
    }

    [Test]
    public async Task CreateClipboard_ReturnsServerClipboard_InServerMode()
    {
        // 安排：设置 Server 模式
        Environment.SetEnvironmentVariable(ServerModeEnvVar, "true");

        // 操作
        var result = PlatformFactory.CreateClipboard();

        // 断言
        await Assert.That(result).IsTypeOf<ServerClipboard>();
    }
}
