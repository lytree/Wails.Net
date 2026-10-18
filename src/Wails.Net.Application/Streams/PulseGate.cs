namespace Wails.Net.Application.Streams;

/// <summary>
/// 异步版本的「条件变量广播」原语：用于生产者等待队列空间，以及等待全局额度。
/// <para>
/// 对应 Wails v3 Go 版本中 <c>sync.Cond</c> 的 <c>Wait</c>/<c>Broadcast</c> 用法
/// （<c>streamSession.space</c>、<c>streamManager.budgetCond</c>、<c>StreamConn.inCond</c>）。
/// .NET 的 <see cref="System.Threading.Monitor"/> 无法 <c>await</c>，故以
/// <see cref="TaskCompletionSource{TResult}"/> 列表实现「一次唤醒所有等待者、由等待者自行重判条件」的语义。
/// </para>
/// <para>
/// <b>使用约束</b>：<see cref="Prepare"/> / <see cref="Remove"/> / <see cref="Pulse"/> 均要求调用方
/// 持有宿主对象的锁，从而保证「检查条件 + 登记等待」与「释放资源 + 唤醒」不会交错，避免丢失唤醒。
/// </para>
/// </summary>
internal sealed class PulseGate
{
    /// <summary>当前登记的等待者列表。</summary>
    private List<TaskCompletionSource<bool>>? _waiters;

    /// <summary>
    /// 登记一个等待者并返回其 <see cref="TaskCompletionSource{TResult}"/>。
    /// 调用方必须持有宿主锁。
    /// </summary>
    /// <returns>等待者完成源；等待 <c>tcs.Task</c> 即阻塞直到被 <see cref="Pulse"/> 唤醒或取消。</returns>
    public TaskCompletionSource<bool> Prepare()
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        (_waiters ??= new List<TaskCompletionSource<bool>>()).Add(tcs);
        return tcs;
    }

    /// <summary>
    /// 移除一个等待者（等待完成、超时或取消后调用）。调用方必须持有宿主锁。
    /// </summary>
    /// <param name="tcs">要移除的等待者完成源。</param>
    public void Remove(TaskCompletionSource<bool> tcs)
    {
        _waiters?.Remove(tcs);
    }

    /// <summary>
    /// 唤醒当前全部等待者并清空登记。调用方必须持有宿主锁。
    /// </summary>
    public void Pulse()
    {
        var list = _waiters;
        if (list is null)
        {
            return;
        }

        _waiters = null;
        foreach (var waiter in list)
        {
            waiter.TrySetResult(true);
        }
    }
}
