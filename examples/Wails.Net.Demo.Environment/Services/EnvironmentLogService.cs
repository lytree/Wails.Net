using Wails.Net.Application.Bindings;

namespace Wails.Net.Demo.Environment.Services;

/// <summary>
/// 环境查询日志服务，记录前端对 OsInfoPlugin / PathPlugin / AppInfoPlugin 的查询历史。
/// 插件负责原生信息查询，本服务负责维护查询历史（线程安全）。
/// </summary>
public sealed class EnvironmentLogService
{
    /// <summary>
    /// 查询日志列表（线程安全）。
    /// </summary>
    private readonly List<string> _queries = new();

    /// <summary>
    /// 同步锁。
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// 获取所有查询日志（最新在前）。
    /// </summary>
    /// <returns>查询日志列表。</returns>
    [Binding]
    public List<string> GetQueries()
    {
        lock (_lock)
        {
            return new List<string>(_queries);
        }
    }

    /// <summary>
    /// 记录一次查询。由前端在调用 os.* / path.* / app.* 命令后追加日志。
    /// </summary>
    /// <param name="queryType">查询类型（如 "os.platform"）。</param>
    /// <param name="result">查询结果字符串。</param>
    [Binding]
    public void LogQuery(string queryType, string result)
    {
        var time = DateTime.Now.ToString("HH:mm:ss.fff");
        var entry = $"[{time}] {queryType} → {result}";
        lock (_lock)
        {
            _queries.Insert(0, entry);
            if (_queries.Count > 50)
            {
                _queries.RemoveAt(_queries.Count - 1);
            }
        }
    }

    /// <summary>
    /// 清空查询日志。
    /// </summary>
    [Binding]
    public void ClearLog()
    {
        lock (_lock)
        {
            _queries.Clear();
        }
    }
}
