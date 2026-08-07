using Wails.Net.Application;
using Wails.Net.Application.Bindings;

namespace Wails.Net.Demo.DevRelease.Services;

/// <summary>
/// DevRelease 演示服务。
/// 通过 [Binding] 特性标记的公共方法会由源代码生成器
/// 自动生成强类型 TypeScript 调用器（无反射）。
/// </summary>
public sealed class DevReleaseService
{
    private DateTime _startTime = DateTime.UtcNow;
    private int _callCount;

    /// <summary>
    /// 获取当前模式信息（Debug / Release + 进程信息）。
    /// 前端可在 UI 顶部显示当前运行模式。
    /// </summary>
    /// <returns>包含模式、运行时长、调用次数的字典。</returns>
    [Binding]
    public ModeInfo GetModeInfo()
    {
        var isDebug = IsDebugMode();
        return new ModeInfo
        {
            Mode = isDebug ? "Debug" : "Release",
            IsDebug = isDebug,
            ProcessId = Environment.ProcessId,
            ProcessName = "Wails.Net.Demo.DevRelease",
            RuntimeVersion = Environment.Version.ToString(),
            UpTimeSeconds = (DateTime.UtcNow - _startTime).TotalSeconds,
            CallCount = _callCount,
            StartedAt = _startTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            OsDescription = Environment.OSVersion.VersionString,
        };
    }

    /// <summary>
    /// 增加调用计数并返回新值（演示有状态绑定）。
    /// </summary>
    [Binding]
    public int IncrementCall()
    {
        return Interlocked.Increment(ref _callCount);
    }

    /// <summary>
    /// 重置调用计数与启动时间。
    /// </summary>
    [Binding]
    public void Reset()
    {
        Interlocked.Exchange(ref _callCount, 0);
        _startTime = DateTime.UtcNow;
    }

    /// <summary>
    /// 异步计算两数之和（演示异步绑定 + CancellationToken）。
    /// </summary>
    /// <param name="a">第一个数。</param>
    /// <param name="b">第二个数。</param>
    /// <returns>和。</returns>
    [Binding]
    public async Task<int> AddAsync(int a, int b)
    {
        await Task.Delay(50); // 模拟异步
        return a + b;
    }

    /// <summary>
    /// 抛出异常，演示前端错误处理路径。
    /// </summary>
    [Binding]
    public string ThrowError()
    {
        throw new InvalidOperationException("DevRelease 演示：故意抛出的异常");
    }

    /// <summary>
    /// 通过 WAILS_DEBUG 环境变量检测当前是否为 Debug 模式（统一走框架 DebugMode API）。
    /// wails dev 命令会自动设置 WAILS_DEBUG=true。
    /// </summary>
    private static bool IsDebugMode() => DebugMode.IsEnvironmentEnabled();
}

/// <summary>
/// 模式信息数据传输对象。
/// </summary>
public sealed class ModeInfo
{
    /// <summary>模式名称（"Debug" 或 "Release"）。</summary>
    public string Mode { get; set; } = "Unknown";

    /// <summary>是否 Debug 模式。</summary>
    public bool IsDebug { get; set; }

    /// <summary>进程 ID。</summary>
    public int ProcessId { get; set; }

    /// <summary>进程名。</summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>.NET 运行时版本。</summary>
    public string RuntimeVersion { get; set; } = string.Empty;

    /// <summary>运行时长（秒）。</summary>
    public double UpTimeSeconds { get; set; }

    /// <summary>绑定方法累计调用次数。</summary>
    public int CallCount { get; set; }

    /// <summary>启动时间（本地时间字符串）。</summary>
    public string StartedAt { get; set; } = string.Empty;

    /// <summary>操作系统描述。</summary>
    public string OsDescription { get; set; } = string.Empty;
}
