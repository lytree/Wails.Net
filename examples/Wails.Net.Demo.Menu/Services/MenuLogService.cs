using Wails.Net.Application.Bindings;

namespace Wails.Net.Demo.Menu.Services;

/// <summary>
/// 菜单点击日志服务，记录用户在应用菜单中的点击历史。
/// 与内置 MenuPlugin 配合使用：MenuPlugin 负责菜单原生操作，
/// 本服务负责记录点击历史，前端通过 menu:clicked 事件接收实时通知。
/// </summary>
public sealed class MenuLogService
{
    /// <summary>
    /// 点击历史列表，使用锁保证线程安全。
    /// </summary>
    private readonly List<MenuClickRecord> _history = new();

    /// <summary>
    /// 历史记录锁。
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// 记录一次菜单点击。
    /// 由后端 Application.Options.OnAfterStart 中构建菜单时为每个菜单项 Callback 调用。
    /// </summary>
    /// <param name="menuId">菜单项 ID 字符串。</param>
    public void RecordClick(string menuId)
    {
        lock (_lock)
        {
            _history.Add(new MenuClickRecord(menuId, DateTime.Now));
            // 最多保留 50 条
            if (_history.Count > 50)
            {
                _history.RemoveAt(0);
            }
        }
    }

    /// <summary>
    /// 获取点击历史列表的副本。
    /// </summary>
    /// <returns>点击历史记录列表。</returns>
    [Binding]
    public List<string> GetClickHistory()
    {
        lock (_lock)
        {
            return _history.ConvertAll(r => $"[{r.Timestamp:HH:mm:ss}] {r.MenuId}");
        }
    }

    /// <summary>
    /// 清空点击历史。
    /// </summary>
    [Binding]
    public void ClearHistory()
    {
        lock (_lock)
        {
            _history.Clear();
        }
    }
}

/// <summary>
/// 菜单点击记录。
/// </summary>
/// <param name="MenuId">菜单项 ID。</param>
/// <param name="Timestamp">点击时间戳。</param>
public sealed record MenuClickRecord(string MenuId, DateTime Timestamp);
