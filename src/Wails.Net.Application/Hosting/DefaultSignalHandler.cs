using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Wails.Net.Application.Hosting;

/// <summary>
/// 默认信号处理器：监听 SIGINT / SIGTERM，首个信号触发应用退出，重复信号逐级提示，
/// 达到次数上限后强制退出进程。
/// <para>
/// 对应 Wails v3 Go 版本 <c>internal/signal/signal.go</c> 的 <c>SignalHandler</c>，
/// 由 <c>pkg/application/signal_handler_desktop.go</c> 的 <c>App.setupSignalHandler</c> 在
/// <see cref="Application.Run"/> 进入平台主循环前安装。
/// </para>
/// <para>
/// 与 Go 的差异（已记录）：Go 实现在首个信号后 <c>break</c> 出循环，使后续受信号次数上限保护的
/// 强制退出分支不可达；本移植按可见分支的设计意图实现"首个信号退出、重复信号逐级提示并最终强制退出"。
/// </para>
/// </summary>
public sealed class DefaultSignalHandler : IDisposable
{
    /// <summary>默认的信号次数上限（对齐 Go <c>SignalHandler.MaxSignal</c>）。</summary>
    public const int DefaultMaxSignal = 3;

    private readonly Action _cleanup;
    private readonly Action _forceExit;
    private readonly int _maxSignal;
    private readonly ILogger? _logger;
    private readonly List<PosixSignalRegistration> _registrations = [];

    private int _received;
    private int _disposed;

    /// <summary>
    /// 构造默认信号处理器。
    /// </summary>
    /// <param name="cleanup">首个信号时执行的应用退出回调（通常为 <c>Application.Quit</c>）。</param>
    /// <param name="maxSignal">信号次数上限；达到该次数时强制退出。默认 3。</param>
    /// <param name="logger">可选日志记录器。</param>
    /// <param name="forceExit">强制退出动作（默认 <see cref="Environment.Exit(int)"/>）；测试可注入以断言。</param>
    public DefaultSignalHandler(Action cleanup, int maxSignal = DefaultMaxSignal, ILogger? logger = null, Action? forceExit = null)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSignal, 1);

        _cleanup = cleanup;
        _maxSignal = maxSignal;
        _logger = logger;
        _forceExit = forceExit ?? (() => Environment.Exit(1));
    }

    /// <summary>
    /// 开始监听 SIGINT 与 SIGTERM。重复调用为空操作。
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (_registrations.Count > 0)
        {
            return;
        }

        // PosixSignalRegistration 同时支持 Windows 与 Unix（.NET 6+）；
        // 注册后信号默认处置被抑制（与 Go signal.Notify 语义一致），由本处理器接管。
        _registrations.Add(PosixSignalRegistration.Create(PosixSignal.SIGINT, ctx => OnSignal("SIGINT", v => ctx.Cancel = v)));
        _registrations.Add(PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => OnSignal("SIGTERM", v => ctx.Cancel = v)));
    }

    /// <summary>
    /// 停止监听并恢复信号默认处置。可安全重复调用。
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        foreach (var registration in _registrations)
        {
            registration.Dispose();
        }

        _registrations.Clear();
    }

    /// <summary>
    /// 测试用：以指定信号名触发一次信号处理流程（不产生真实信号、不抑制默认处置）。
    /// </summary>
    /// <param name="signalName">信号名（如 SIGINT）。</param>
    internal void RaiseSignal(string signalName)
    {
        OnSignal(signalName, static _ => { });
    }

    /// <summary>
    /// 信号处理核心：首个信号触发退出；未达上限的重复信号仅提示；达到上限强制退出。
    /// </summary>
    /// <param name="signalName">信号名。</param>
    /// <param name="setCancel">设置"是否抑制该信号的默认处置"的回调。</param>
    private void OnSignal(string signalName, Action<bool> setCancel)
    {
        var count = Interlocked.Increment(ref _received);

        if (count == 1)
        {
            setCancel(true);
            _logger?.LogInformation("收到信号 {Signal}，正在退出应用……", signalName);
            _cleanup();
            return;
        }

        if (count < _maxSignal)
        {
            setCancel(true);
            _logger?.LogInformation("收到信号 {Signal}；再按 {Remaining} 次可强制退出。", signalName, _maxSignal - count);
            return;
        }

        _logger?.LogInformation("收到信号 {Signal}，强制退出。", signalName);
        _forceExit();
    }
}
