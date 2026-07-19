using Wails.Net.Application.Bindings;

namespace Wails.Net.Demo.CancelAsync.Services;

/// <summary>
/// 长时间运行任务服务，演示 CancellationToken 在绑定方法中的传递与取消。
/// 对应 Demo 18：前端发起调用后，可通过 cancel 机制中断后台任务。
/// </summary>
public sealed class LongRunningService
{
    /// <summary>
    /// 当前任务进度（0-100）。线程安全。
    /// </summary>
    private int _progress;

    /// <summary>
    /// 当前任务是否正在运行。线程安全（0=未运行，1=运行中）。
    /// </summary>
    private int _running;

    /// <summary>
    /// 当前任务的 CancellationTokenSource（用于服务端主动取消）。
    /// </summary>
    private CancellationTokenSource? _cts;

    /// <summary>
    /// 同步锁，保护 _cts 字段的读写。
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// 启动一个长时间运行任务，模拟每秒推进进度，支持 CancellationToken 取消。
    /// </summary>
    /// <param name="durationSeconds">任务总时长（秒）。</param>
    /// <param name="ct">由 MessageProcessor 自动注入的取消令牌；
    /// 前端调用 _wailsCancelCall 时会触发此 token。</param>
    /// <returns>任务结束信息（completed / cancelled）。</returns>
    [Binding]
    public async Task<string> StartLongTask(int durationSeconds, CancellationToken ct)
    {
        if (durationSeconds <= 0)
        {
            durationSeconds = 1;
        }

        // 重置状态
        Interlocked.Exchange(ref _progress, 0);
        Interlocked.Exchange(ref _running, 1);

        // 创建本地 CTS 以支持服务端取消，并链接到外部 ct
        var localCts = new CancellationTokenSource();
        CancellationTokenSource linkedCts;
        lock (_lock)
        {
            _cts = localCts;
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(localCts.Token, ct);
        }

        try
        {
            var totalSteps = durationSeconds;
            for (var i = 1; i <= totalSteps; i++)
            {
                // 检查取消（来自前端或服务端）
                linkedCts.Token.ThrowIfCancellationRequested();

                // 等待 1 秒，期间可被取消
                await Task.Delay(TimeSpan.FromSeconds(1), linkedCts.Token);

                // 推进进度
                var pct = (int)((double)i / totalSteps * 100);
                Interlocked.Exchange(ref _progress, pct);
            }

            return $"completed: 耗时 {durationSeconds} 秒";
        }
        catch (OperationCanceledException)
        {
            return "cancelled: 任务已被取消";
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
            linkedCts.Dispose();
            localCts.Dispose();
            lock (_lock)
            {
                _cts = null;
            }
        }
    }

    /// <summary>
    /// 获取当前进度（0-100）。
    /// </summary>
    /// <returns>进度百分比。</returns>
    [Binding]
    public int GetProgress()
    {
        return Interlocked.CompareExchange(ref _progress, 0, 0);
    }

    /// <summary>
    /// 查询任务是否正在运行。
    /// </summary>
    /// <returns>true=运行中，false=空闲。</returns>
    [Binding]
    public bool IsRunning()
    {
        return Interlocked.CompareExchange(ref _running, 0, 0) == 1;
    }

    /// <summary>
    /// 服务端主动取消当前任务。
    /// 用于演示后端发起的取消（不同于前端 _wailsCancelCall）。
    /// </summary>
    [Binding]
    public void CancelFromServer()
    {
        lock (_lock)
        {
            _cts?.Cancel();
        }
    }
}
