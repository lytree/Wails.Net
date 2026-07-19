using Wails.Net.Application.Bindings;

namespace Wails.Net.Demo.Screen.Services;

/// <summary>
/// 屏幕查询日志服务，记录用户对屏幕信息的查询历史。
/// 与内置 ScreenPlugin 配合使用：ScreenPlugin 提供 screen.getAll / screen.getPrimary 命令，
/// 本服务负责记录查询日志，便于审计与调试。
/// </summary>
public sealed class ScreenLogService
{
    /// <summary>
    /// 查询日志列表，使用锁保证线程安全。
    /// </summary>
    private readonly List<string> _queryLog = new();

    /// <summary>
    /// 日志锁。
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// 记录一次屏幕查询。前端在调用 screen.getAll / screen.getPrimary 后调用此方法。
    /// </summary>
    /// <param name="queryType">查询类型（getPrimary / getAll）。</param>
    [Binding]
    public void LogScreenQuery(string queryType)
    {
        lock (_lock)
        {
            _queryLog.Add($"[{DateTime.Now:HH:mm:ss}] 查询 {queryType}");
            // 最多保留 50 条
            if (_queryLog.Count > 50)
            {
                _queryLog.RemoveAt(0);
            }
        }
    }

    /// <summary>
    /// 获取查询日志列表的副本。
    /// </summary>
    /// <returns>查询日志列表。</returns>
    [Binding]
    public List<string> GetQueryLog()
    {
        lock (_lock)
        {
            return new List<string>(_queryLog);
        }
    }
}
