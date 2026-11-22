namespace Wails.Net.Application.Transport;

/// <summary>
/// WebSocket 传输层指标（R2-3 融合审计：控制通道 + 受控并发）。
/// <para>
/// 通过 <see cref="Interlocked"/> 计数器暴露以下维度（所有方法线程安全）：
/// <list type="bullet">
/// <item><c>CallsInFlight</c>：当前正在执行的调用数。</item>
/// <item><c>CallsRejected</c>：因并发上限被拒绝的调用数（拒绝策略命中）。</item>
/// <item><c>CallsCompleted</c>：已完成的调用数（成功 + 失败 + 取消）。</item>
/// <item><c>ControlProcessed</c>：控制消息处理次数（cancel 等）。</item>
/// <item><c>ControlRejected</c>：控制消息处理失败次数（用于诊断控制通道异常）。</item>
/// </list>
/// </para>
/// <para>
/// 对应 docs/architecture/fusion-architecture-audit.md §3.7：定义队列满时的丢弃、拒绝或断连策略，并输出指标。
/// </para>
/// </summary>
public sealed class WebSocketTransportMetrics
{
    private long _callsInFlight;
    private long _callsRejected;
    private long _callsCompleted;
    private long _controlProcessed;
    private long _controlRejected;

    /// <summary>当前正在执行的调用数。</summary>
    public long CallsInFlight => Interlocked.Read(ref _callsInFlight);

    /// <summary>因并发上限被拒绝的调用数。</summary>
    public long CallsRejected => Interlocked.Read(ref _callsRejected);

    /// <summary>已完成的调用数（成功 + 失败 + 取消）。</summary>
    public long CallsCompleted => Interlocked.Read(ref _callsCompleted);

    /// <summary>控制消息处理次数（cancel 等）。</summary>
    public long ControlProcessed => Interlocked.Read(ref _controlProcessed);

    /// <summary>控制消息处理失败次数。</summary>
    public long ControlRejected => Interlocked.Read(ref _controlRejected);

    /// <summary>调用开始执行时递增在途计数。</summary>
    public void IncrementCallsInFlight() => Interlocked.Increment(ref _callsInFlight);

    /// <summary>调用结束时递减在途计数。</summary>
    public void DecrementCallsInFlight() => Interlocked.Decrement(ref _callsInFlight);

    /// <summary>调用因并发上限被拒绝时递增拒绝计数。</summary>
    public void IncrementCallsRejected() => Interlocked.Increment(ref _callsRejected);

    /// <summary>调用完成（成功 / 失败 / 取消）时递增完成计数。</summary>
    public void IncrementCallsCompleted() => Interlocked.Increment(ref _callsCompleted);

    /// <summary>控制消息（cancel 等）处理成功时递增处理计数。</summary>
    public void IncrementControlProcessed() => Interlocked.Increment(ref _controlProcessed);

    /// <summary>控制消息处理失败时递增失败计数。</summary>
    public void IncrementControlRejected() => Interlocked.Increment(ref _controlRejected);

    /// <summary>捕获指标快照，便于周期性导出与断言。</summary>
    public WebSocketTransportMetricsSnapshot Snapshot() => new(
        CallsInFlight,
        CallsRejected,
        CallsCompleted,
        ControlProcessed,
        ControlRejected);
}

/// <summary>
/// <see cref="WebSocketTransportMetrics"/> 的不可变快照。
/// </summary>
public readonly record struct WebSocketTransportMetricsSnapshot(
    long CallsInFlight,
    long CallsRejected,
    long CallsCompleted,
    long ControlProcessed,
    long ControlRejected);
