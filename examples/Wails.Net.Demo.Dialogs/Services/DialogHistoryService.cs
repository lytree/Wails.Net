using Wails.Net.Application.Bindings;

namespace Wails.Net.Demo.Dialogs.Services;

/// <summary>
/// 对话框操作记录，用于演示复杂对象返回。
/// </summary>
/// <param name="Action">对话框动作名称。</param>
/// <param name="Result">对话框返回结果（字符串形式）。</param>
/// <param name="Timestamp">操作时间戳。</param>
public sealed record DialogRecord(string Action, string Result, DateTime Timestamp);

/// <summary>
/// 对话框历史服务，记录用户在原生对话框中的操作历史，
/// 与内置 DialogPlugin 配合使用：插件负责显示对话框，本服务负责记录历史。
/// </summary>
public sealed class DialogHistoryService
{
    /// <summary>
    /// 历史记录列表，使用锁保证线程安全。
    /// </summary>
    private readonly List<DialogRecord> _history = new();

    /// <summary>
    /// 历史记录锁。
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// 获取历史记录列表的副本。
    /// </summary>
    /// <returns>历史记录列表。</returns>
    [Binding]
    public List<DialogRecord> GetHistory()
    {
        lock (_lock)
        {
            return new List<DialogRecord>(_history);
        }
    }

    /// <summary>
    /// 清空历史记录。
    /// </summary>
    [Binding]
    public void ClearHistory()
    {
        lock (_lock)
        {
            _history.Clear();
        }
    }

    /// <summary>
    /// 记录一次对话框操作。前端在调用 dialog.* 命令后调用此方法保存结果。
    /// </summary>
    /// <param name="action">对话框动作名称。</param>
    /// <param name="result">对话框返回结果。</param>
    [Binding]
    public void RecordAction(string action, string result)
    {
        lock (_lock)
        {
            _history.Add(new DialogRecord(action, result, DateTime.Now));
            // 最多保留 20 条
            if (_history.Count > 20)
            {
                _history.RemoveAt(0);
            }
        }
    }
}
