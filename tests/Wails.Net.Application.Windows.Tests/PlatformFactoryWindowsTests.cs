using TUnit.Core;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;

namespace Wails.Net.Application.Windows.Tests;

/// <summary>
/// PlatformFactory 在 Windows 平台上的单元测试（TUnit）。
/// 验证反射加载 Windows 平台实现的正确性。
/// 注意：PlatformFactory 使用静态状态（环境变量），因此不并行执行。
/// </summary>
[NotInParallel]
public sealed class PlatformFactoryWindowsTests
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
    public async Task CreatePlatformApp_OnWindows_ReturnsWindowsPlatformApp()
    {
        // 操作：通过工厂创建平台应用
        var result = PlatformFactory.CreatePlatformApp(new ApplicationOptions());

        // 断言：返回非空且类型名包含 WindowsPlatformApp
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.GetType().Name.Contains("WindowsPlatformApp")).IsTrue();
    }

    [Test]
    public async Task CreateClipboard_OnWindows_ReturnsWindowsClipboard()
    {
        // 操作：通过工厂创建剪贴板
        var result = PlatformFactory.CreateClipboard();

        // 断言：返回非空且类型名包含 WindowsClipboard
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.GetType().Name.Contains("WindowsClipboard")).IsTrue();
    }

    [Test]
    public async Task IsServerMode_ReturnsFalse_ByDefault()
    {
        // 安排：环境变量已在 Setup 中清除

        // 操作与断言：默认非 Server 模式
        await Assert.That(PlatformFactory.IsServerMode()).IsFalse();
    }
}
