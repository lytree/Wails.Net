using Wails.Net.Application.Bindings;

namespace Wails.Net.Demo.Clipboard.Services;

/// <summary>
/// 剪贴板统计服务，演示绑定方法与内置 ClipboardPlugin 配合使用。
/// ClipboardPlugin 负责 clipboard.setText/getText 等原生操作，
/// 本服务负责维护复制次数计数（线程安全）。
/// </summary>
public sealed class ClipboardStatsService
{
    /// <summary>
    /// 复制次数计数器，使用 <see cref="Interlocked"/> 保证线程安全。
    /// </summary>
    private int _copyCount;

    /// <summary>
    /// 获取累计复制次数。
    /// </summary>
    /// <returns>复制次数。</returns>
    [Binding]
    public int GetCopyCount() => _copyCount;

    /// <summary>
    /// 累加复制次数，由前端在调用 clipboard.setText 后调用。
    /// </summary>
    [Binding]
    public void IncrementCount() => Interlocked.Increment(ref _copyCount);

    /// <summary>
    /// 重置复制次数为 0。
    /// </summary>
    [Binding]
    public void ResetCount() => Interlocked.Exchange(ref _copyCount, 0);
}
