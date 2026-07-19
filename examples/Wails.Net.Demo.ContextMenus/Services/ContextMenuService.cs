using Wails.Net.Application.Bindings;

namespace Wails.Net.Demo.ContextMenus.Services;

/// <summary>
/// 上下文菜单操作记录，演示复杂对象返回。
/// </summary>
/// <param name="Target">触发上下文菜单的目标区域名称。</param>
/// <param name="Action">用户选择的菜单动作。</param>
/// <param name="Timestamp">操作时间戳。</param>
public sealed record ContextActionRecord(string Target, string Action, DateTime Timestamp);

/// <summary>
/// 上下文菜单服务，记录用户在不同区域右键菜单中的选择历史。
/// 与内置 MenuPlugin 配合使用：MenuPlugin 提供 menu.setContextMenu / menu.popup 命令，
/// 本服务负责记录用户的上下文菜单操作历史。
/// </summary>
public sealed class ContextMenuService
{
    /// <summary>
    /// 操作历史列表，使用锁保证线程安全。
    /// </summary>
    private readonly List<ContextActionRecord> _history = new();

    /// <summary>
    /// 历史记录锁。
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// 记录一次上下文菜单操作。前端在菜单项点击后调用此方法。
    /// </summary>
    /// <param name="target">触发区域名称（input / button / text）。</param>
    /// <param name="action">用户选择的动作。</param>
    [Binding]
    public void RecordContextAction(string target, string action)
    {
        lock (_lock)
        {
            _history.Add(new ContextActionRecord(target, action, DateTime.Now));
            // 最多保留 30 条
            if (_history.Count > 30)
            {
                _history.RemoveAt(0);
            }
        }
    }

    /// <summary>
    /// 获取操作历史列表的副本。
    /// </summary>
    /// <returns>操作历史记录列表。</returns>
    [Binding]
    public List<ContextActionRecord> GetHistory()
    {
        lock (_lock)
        {
            return new List<ContextActionRecord>(_history);
        }
    }

    /// <summary>
    /// 清空操作历史。
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
