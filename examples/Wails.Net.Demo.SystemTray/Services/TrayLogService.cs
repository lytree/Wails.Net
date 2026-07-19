using Wails.Net.Application;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Services;

namespace Wails.Net.Demo.SystemTray.Services;

/// <summary>
/// 托盘事件记录，包含事件类型、消息与时间戳。
/// </summary>
/// <param name="Kind">事件类型（click / menu / info / error）。</param>
/// <param name="Message">事件描述。</param>
/// <param name="Time">事件发生时间戳。</param>
public sealed record TrayEventRecord(string Kind, string Message, DateTime Time);

/// <summary>
/// 托盘事件日志服务，维护事件历史并通过框架 NotificationService 发送通知。
/// 通过 [Binding] 暴露给前端：前端可查询历史、清空历史、发送通知。
/// </summary>
public sealed class TrayLogService
{
    /// <summary>
    /// 事件历史列表，使用锁保证线程安全。
    /// </summary>
    private readonly List<TrayEventRecord> _events = new();

    /// <summary>
    /// 历史记录锁。
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// 全局 Application 实例，由 Program.cs 在注册绑定时通过 DI 注入。
    /// 用于在发送通知时获取框架 NotificationService。
    /// 使用完全限定名以消除 Application 命名空间与 Application 类型的歧义（CS0118）。
    /// </summary>
    private readonly Wails.Net.Application.Application _app;

    /// <summary>
    /// 构造 TrayLogService 实例。
    /// </summary>
    /// <param name="app">全局 Application 实例。</param>
    public TrayLogService(Wails.Net.Application.Application app)
    {
        _app = app;
    }

    /// <summary>
    /// 记录一条托盘事件。
    /// 同时通过 Application.Events 广播 tray:event 事件到前端。
    /// </summary>
    /// <param name="kind">事件类型（click / menu / info / error）。</param>
    /// <param name="message">事件描述。</param>
    public void RecordEvent(string kind, string message)
    {
        var record = new TrayEventRecord(kind, message, DateTime.Now);
        lock (_lock)
        {
            _events.Add(record);
            if (_events.Count > 50)
            {
                _events.RemoveAt(0);
            }
        }

        _app.Events.Emit("tray:event", new { kind, message, time = record.Time.ToString("HH:mm:ss") });
    }

    /// <summary>
    /// 获取托盘事件历史记录列表的副本。
    /// </summary>
    /// <returns>事件记录列表。</returns>
    [Binding]
    public List<TrayEventRecord> GetTrayEvents()
    {
        lock (_lock)
        {
            return new List<TrayEventRecord>(_events);
        }
    }

    /// <summary>
    /// 清空托盘事件历史记录。
    /// </summary>
    [Binding]
    public void ClearEvents()
    {
        lock (_lock)
        {
            _events.Clear();
        }
    }

    /// <summary>
    /// 通过框架 NotificationService 发送系统通知。
    /// 前端可调用此绑定方法触发托盘相关通知。
    /// </summary>
    /// <param name="title">通知标题。</param>
    /// <param name="body">通知正文。</param>
    [Binding]
    public void SendTrayNotification(string title, string body)
    {
        var notifier = _app.Services.Services.OfType<NotificationService>().FirstOrDefault();
        notifier?.SendNotification(title, body);
        RecordEvent("notification", $"已发送通知：{title}");
    }
}
