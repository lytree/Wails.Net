using System.Text.Json;
using Wails.Net.Application.Bindings;
using Timer = System.Threading.Timer;

namespace Wails.Net.Demo.Server.Services;

/// <summary>
/// 后台任务服务，演示 Server 模式下的定时后台处理。
/// 对应 Demo 16：使用 Timer 模拟周期性后台任务，并提供状态查询绑定方法。
/// </summary>
public sealed class BackgroundTaskService : IDisposable
{
    /// <summary>
    /// 后台定时器（每秒触发一次）。
    /// </summary>
    private Timer? _timer;

    /// <summary>
    /// 已处理任务数计数器（线程安全）。
    /// </summary>
    private int _processedCount;

    /// <summary>
    /// 当前运行状态：running / stopped。
    /// </summary>
    private string _status = "stopped";

    /// <summary>
    /// 状态锁，保护 _status 与 _processedCount 的并发访问。
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// 获取当前运行状态。
    /// </summary>
    /// <returns>"running" 或 "stopped"。</returns>
    [Binding]
    public string GetStatus()
    {
        lock (_lock)
        {
            return _status;
        }
    }

    /// <summary>
    /// 获取累计已处理任务数。
    /// </summary>
    /// <returns>已处理任务数。</returns>
    [Binding]
    public int GetProcessedCount()
    {
        return Interlocked.CompareExchange(ref _processedCount, 0, 0);
    }

    /// <summary>
    /// 启动后台处理。若已在运行则忽略。
    /// </summary>
    [Binding]
    public void StartProcessing()
    {
        lock (_lock)
        {
            if (_status == "running")
            {
                return;
            }
            _status = "running";
            _timer ??= new Timer(OnTick, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        }
    }

    /// <summary>
    /// 停止后台处理。
    /// </summary>
    [Binding]
    public void StopProcessing()
    {
        lock (_lock)
        {
            _status = "stopped";
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    /// <summary>
    /// 定时回调：累加计数器。
    /// </summary>
    /// <param name="state">定时器状态。</param>
    private void OnTick(object? state)
    {
        Interlocked.Increment(ref _processedCount);
    }

    /// <summary>
    /// 释放定时器资源。
    /// </summary>
    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
