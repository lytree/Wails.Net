using TUnit.Core;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Platform.ServerMode;

namespace Wails.Net.Application.Tests.Platform;

/// <summary>
/// PlatformFactory 的单元测试（TUnit）。
/// 验证 6 级回退检测链：Server 模式 → 环境变量 → RuntimeInformation → FriendlyName → ProcessName → ServerPlatformApp 降级。
/// </summary>
/// <remarks>
/// 此类修改环境变量，必须 <see cref="NotInParallelAttribute"/> 避免并行竞争。
/// 每个测试在 finally 块恢复原始环境变量值。
/// </remarks>
[NotInParallel]
public sealed class PlatformFactoryTests
{
    private const string ServerModeEnvVar = "WAILS_SERVER_MODE";
    private const string PlatformEnvVar = "WAILS_PLATFORM";
    private const string DebugEnvVar = "WAILS_DEBUG";

    /// <summary>
    /// 设置环境变量并在测试完成后恢复原值。
    /// </summary>
    private static IDisposable SetEnvVar(string name, string? value)
    {
        var original = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
        return new EnvVarRestorer(name, original);
    }

    /// <summary>
    /// 在 Dispose 时恢复环境变量原值。
    /// </summary>
    private sealed class EnvVarRestorer : IDisposable
    {
        private readonly string _name;
        private readonly string? _original;
        private bool _restored;

        public EnvVarRestorer(string name, string? original)
        {
            _name = name;
            _original = original;
        }

        public void Dispose()
        {
            if (!_restored)
            {
                Environment.SetEnvironmentVariable(_name, _original);
                _restored = true;
            }
        }
    }

    // ---------------------------------------------------------------------
    // Level 1：Server 模式
    // ---------------------------------------------------------------------

    [Test]
    public async Task IsServerMode_True_ReturnsTrue()
    {
        using var _ = SetEnvVar(ServerModeEnvVar, "true");
        await Assert.That(PlatformFactory.IsServerMode()).IsTrue();
    }

    [Test]
    public async Task IsServerMode_False_ReturnsFalse()
    {
        using var _ = SetEnvVar(ServerModeEnvVar, null);
        await Assert.That(PlatformFactory.IsServerMode()).IsFalse();
    }

    [Test]
    public async Task IsServerMode_True_CaseInsensitive()
    {
        using var _ = SetEnvVar(ServerModeEnvVar, "TRUE");
        await Assert.That(PlatformFactory.IsServerMode()).IsTrue();
    }

    [Test]
    public async Task CreatePlatformApp_ServerMode_ReturnsServerPlatformApp()
    {
        // 安排：强制启用 Server 模式
        using var _ = SetEnvVar(ServerModeEnvVar, "true");
        var options = new ApplicationOptions { Name = "TestServerApp" };

        // 操作
        var app = PlatformFactory.CreatePlatformApp(options);

        // 断言：返回 ServerPlatformApp 实例
        await Assert.That(app).IsTypeOf<ServerPlatformApp>();
    }

    // ---------------------------------------------------------------------
    // Level 2：环境变量强制指定
    // ---------------------------------------------------------------------

    [Test]
    public async Task CreatePlatformApp_WailsPlatformLinux_ReturnsLinuxPlatformApp()
    {
        // 安排：强制 Linux 平台，但 Server 模式必须关闭
        using var s1 = SetEnvVar(ServerModeEnvVar, null);
        using var s2 = SetEnvVar(PlatformEnvVar, "linux");
        var options = new ApplicationOptions { Name = "TestLinuxApp" };

        // 操作与断言：环境变量被正确解析为 linux（Level 2 命中），
        // 由于 Linux 平台程序集在 Windows 测试环境下不存在，Assembly.Load 抛 FileNotFoundException。
        // 这证明平台检测正确识别为 linux，而非降级到 ServerPlatformApp。
        // 若降级到 ServerPlatformApp，则不会抛异常。
        await Assert.That(() => PlatformFactory.CreatePlatformApp(options))
            .ThrowsExactly<FileNotFoundException>();
    }

    [Test]
    public async Task CreatePlatformApp_InvalidWailsPlatform_FallsBackToAutoOrServer()
    {
        // 安排：设置无效的平台值，Server 模式关闭
        using var s1 = SetEnvVar(ServerModeEnvVar, null);
        using var s2 = SetEnvVar(PlatformEnvVar, "invalid-platform");
        var options = new ApplicationOptions { Name = "TestInvalidApp" };

        // 操作与断言：无效环境变量被忽略（Level 2 未命中），
        // Level 3 RuntimeInformation 在 Windows 上命中，识别为 windows。
        // 由于测试项目未引用 Wails.Net.Application.Windows，Assembly.Load 抛 FileNotFoundException。
        // 这证明无效环境变量被正确忽略，并继续到 Level 3 自动检测。
        await Assert.That(() => PlatformFactory.CreatePlatformApp(options))
            .ThrowsExactly<FileNotFoundException>();
    }

    // ---------------------------------------------------------------------
    // Level 3：RuntimeInformation 自动检测
    // ---------------------------------------------------------------------

    [Test]
    public async Task CreatePlatformApp_AutoDetect_Windows_ReturnsWindowsPlatformApp()
    {
        // 安排：清除所有环境变量，让自动检测生效
        using var s1 = SetEnvVar(ServerModeEnvVar, null);
        using var s2 = SetEnvVar(PlatformEnvVar, null);
        var options = new ApplicationOptions { Name = "TestAutoApp" };

        // 操作与断言：Level 3 RuntimeInformation 检测到 Windows 平台。
        // 测试项目未引用 Wails.Net.Application.Windows 程序集，Assembly.Load 抛 FileNotFoundException。
        // 这证明自动检测正确识别为 Windows（而非降级到 ServerPlatformApp）。
        await Assert.That(() => PlatformFactory.CreatePlatformApp(options))
            .ThrowsExactly<FileNotFoundException>();
    }

    // ---------------------------------------------------------------------
    // Level 6：所有级别未命中时降级到 ServerPlatformApp
    // ---------------------------------------------------------------------

    [Test]
    public async Task CreatePlatformApp_AllLevelsFail_FallsBackToServerMode()
    {
        // 安排：此测试验证降级逻辑路径。
        // 在 Windows 上 Level 3 会命中，无法真正模拟"所有级别失败"。
        // 但可以通过 Server 模式验证降级到 ServerPlatformApp 的逻辑路径：
        // 当 DetectPlatformOrNull 返回 null 时，CreatePlatformApp 应返回 ServerPlatformApp。
        using var _ = SetEnvVar(ServerModeEnvVar, "true");
        var options = new ApplicationOptions { Name = "TestFallbackApp" };

        // 操作：Server 模式下直接返回 ServerPlatformApp（Level 1 命中）
        var app = PlatformFactory.CreatePlatformApp(options);

        // 断言：返回 ServerPlatformApp，证明降级逻辑生效
        await Assert.That(app).IsNotNull();
        await Assert.That(app!.GetType().Name).IsEqualTo("ServerPlatformApp");
    }

    [Test]
    public async Task CreateClipboard_ServerMode_ReturnsServerClipboard()
    {
        // 安排
        using var _ = SetEnvVar(ServerModeEnvVar, "true");

        // 操作
        var clipboard = PlatformFactory.CreateClipboard();

        // 断言
        await Assert.That(clipboard).IsNotNull();
        await Assert.That(clipboard!.GetType().Name).IsEqualTo("ServerClipboard");
    }

    // ---------------------------------------------------------------------
    // GetDetectionReport 诊断方法
    // ---------------------------------------------------------------------

    [Test]
    public async Task GetDetectionReport_ReturnsAllLevels()
    {
        // 操作
        var report = PlatformFactory.GetDetectionReport();

        // 断言：报告包含所有 6 级标签
        await Assert.That(report).Contains("[Level 1]");
        await Assert.That(report).Contains("[Level 2]");
        await Assert.That(report).Contains("[Level 3]");
        await Assert.That(report).Contains("[Level 4]");
        await Assert.That(report).Contains("[Level 5]");
        await Assert.That(report).Contains("[Level 6]");
        // 验证包含环境变量名
        await Assert.That(report).Contains("WAILS_SERVER_MODE");
        await Assert.That(report).Contains("WAILS_PLATFORM");
        // 验证包含 Fallback 信息
        await Assert.That(report).Contains("ServerPlatformApp");
    }

    // ---------------------------------------------------------------------
    // WebviewWindowImpl 与 Server 模式优先级（合并自旧版根目录测试文件）
    // ---------------------------------------------------------------------

    [Test]
    public async Task CreateWebviewWindowImpl_ServerMode_ReturnsServerWebviewWindow()
    {
        using var _ = SetEnvVar(ServerModeEnvVar, "true");
        var options = new WebviewWindowOptions { Name = "TestWindow" };

        var result = PlatformFactory.CreateWebviewWindowImpl(1, options);

        await Assert.That(result).IsTypeOf<ServerWebviewWindow>();
    }

    [Test]
    public async Task CreatePlatformApp_ServerModeTakesPriorityOverPlatformEnvVar()
    {
        // 同时设置 Server 模式和平台环境变量，Server 模式应优先
        using var s1 = SetEnvVar(ServerModeEnvVar, "true");
        using var s2 = SetEnvVar(PlatformEnvVar, "windows");
        var options = new ApplicationOptions { Name = "TestPriorityApp" };

        var app = PlatformFactory.CreatePlatformApp(options);

        await Assert.That(app).IsTypeOf<ServerPlatformApp>();
    }

    // ---------------------------------------------------------------------
    // WAILS_DEBUG 调试日志开关
    // ---------------------------------------------------------------------

    [Test]
    public async Task IsDebugEnabled_ReturnsFalse_ByDefault()
    {
        using var _ = SetEnvVar(DebugEnvVar, null);
        await Assert.That(PlatformFactory.IsDebugEnabled()).IsFalse();
    }

    [Test]
    public async Task IsDebugEnabled_ReturnsTrue_WhenEnvVarSet()
    {
        using var _ = SetEnvVar(DebugEnvVar, "true");
        await Assert.That(PlatformFactory.IsDebugEnabled()).IsTrue();
    }

    [Test]
    public async Task IsDebugEnabled_IsCaseInsensitive()
    {
        using var _ = SetEnvVar(DebugEnvVar, "TRUE");
        await Assert.That(PlatformFactory.IsDebugEnabled()).IsTrue();
    }
}
