using Wails.Net.Application.Bindings;

namespace Wails.Net.Demo.Keybindings.Services;

/// <summary>
/// 快捷键触发记录，包含来源、加速键描述、消息与时间戳。
/// </summary>
/// <param name="Source">触发来源（backend / frontend / info / error）。</param>
/// <param name="Accelerator">快捷键描述（如 "Ctrl+Alt+T"）。</param>
/// <param name="Message">描述消息。</param>
/// <param name="Time">触发时间戳。</param>
public sealed record KeybindingRecord(string Source, string Accelerator, string Message, DateTime Time);

/// <summary>
/// 快捷键日志服务，记录后端与前端快捷键的触发历史。
/// </summary>
public sealed class KeybindingLogService
{
    /// <summary>
    /// 触发历史列表，使用锁保证线程安全。
    /// </summary>
    private readonly List<KeybindingRecord> _records = new();

    /// <summary>
    /// 历史记录锁。
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// 记录一条快捷键触发事件。
    /// </summary>
    /// <param name="source">触发来源（backend / frontend / info / error）。</param>
    /// <param name="message">描述消息。</param>
    /// <param name="accelerator">快捷键描述，可为 null。</param>
    public void Record(string source, string message, string? accelerator = null)
    {
        var record = new KeybindingRecord(source, accelerator ?? string.Empty, message, DateTime.Now);
        lock (_lock)
        {
            _records.Add(record);
            if (_records.Count > 100)
            {
                _records.RemoveAt(0);
            }
        }
    }

    /// <summary>
    /// 由前端 globalshortcut:pressed 事件触发时调用的绑定方法，
    /// 将前端快捷键触发记录到后端历史。
    /// </summary>
    /// <param name="accelerator">触发的快捷键描述。</param>
    [Binding]
    public void RecordFrontendPress(string accelerator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accelerator);
        Record("frontend", $"前端快捷键 {accelerator} 触发", accelerator);
    }

    /// <summary>
    /// 获取快捷键触发历史记录列表的副本。
    /// </summary>
    /// <returns>触发记录列表。</returns>
    [Binding]
    public List<KeybindingRecord> GetHistory()
    {
        lock (_lock)
        {
            return new List<KeybindingRecord>(_records);
        }
    }

    /// <summary>
    /// 清空触发历史记录。
    /// </summary>
    [Binding]
    public void ClearHistory()
    {
        lock (_lock)
        {
            _records.Clear();
        }
    }

    /// <summary>
    /// 获取按快捷键分组的触发次数统计。
    /// </summary>
    /// <returns>快捷键到触发次数的字典。</returns>
    [Binding]
    public Dictionary<string, int> GetCountByAccelerator()
    {
        lock (_lock)
        {
            return _records
                .Where(r => !string.IsNullOrEmpty(r.Accelerator))
                .GroupBy(r => r.Accelerator)
                .ToDictionary(g => g.Key, g => g.Count());
        }
    }
}
