using Wails.Net.Application.Bindings;

namespace Wails.Net.Demo.Store.Services;

/// <summary>
/// 存储操作日志服务，记录前端对 StorePlugin 的操作历史。
/// StorePlugin 负责 store.set / store.get / store.delete 等原生操作，
/// 本服务负责维护操作计数与最近 5 条操作日志（线程安全）。
/// </summary>
public sealed class StoreLogService
{
    /// <summary>
    /// 操作次数计数器（线程安全）。
    /// </summary>
    private int _operationCount;

    /// <summary>
    /// 最近操作日志列表（线程安全）。
    /// </summary>
    private readonly List<string> _recentOps = new();

    /// <summary>
    /// 同步锁。
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// 获取累计操作次数。
    /// </summary>
    /// <returns>操作次数。</returns>
    [Binding]
    public int GetOperationCount()
    {
        return Interlocked.CompareExchange(ref _operationCount, 0, 0);
    }

    /// <summary>
    /// 获取最近 5 条操作日志。
    /// </summary>
    /// <returns>日志列表（最新的在前）。</returns>
    [Binding]
    public List<string> GetRecentOperations()
    {
        lock (_lock)
        {
            return new List<string>(_recentOps);
        }
    }

    /// <summary>
    /// 记录一次操作。由前端在调用 store.* 命令后追加日志。
    /// </summary>
    /// <param name="operation">操作描述（如 "set key=foo value=bar"）。</param>
    [Binding]
    public void LogOperation(string operation)
    {
        var time = DateTime.Now.ToString("HH:mm:ss.fff");
        var entry = $"[{time}] {operation}";
        lock (_lock)
        {
            _recentOps.Insert(0, entry);
            if (_recentOps.Count > 5)
            {
                _recentOps.RemoveAt(_recentOps.Count - 1);
            }
        }
        Interlocked.Increment(ref _operationCount);
    }

    /// <summary>
    /// 清空操作日志（不影响计数）。
    /// </summary>
    [Binding]
    public void ClearLog()
    {
        lock (_lock)
        {
            _recentOps.Clear();
        }
    }
}
