using Wails.Net.Application.Bindings;
// 框架通知服务（与 Demo 自己的 NotificationService 类型区分）
using FrameworkNotificationService = Wails.Net.Application.Services.NotificationService;

namespace Wails.Net.Demo.Notifications.Services;

/// <summary>
/// 通知记录，保存已发送通知的元数据。
/// 注意：本类型位于 Demo 命名空间，与框架的 Wails.Net.Application.Services.NotificationRecord 区分。
/// </summary>
/// <param name="Title">通知标题。</param>
/// <param name="Body">通知正文。</param>
/// <param name="SentAt">发送时间戳。</param>
public sealed record NotificationRecord(string Title, string Body, DateTime SentAt);

/// <summary>
/// 通知演示服务，封装框架 NotificationService 的调用，并提供历史记录与延迟发送能力。
/// 注意：与框架的 Wails.Net.Application.Services.NotificationService 区分，本服务仅用于 Demo 演示。
/// </summary>
public sealed class NotificationService
{
    /// <summary>
    /// 框架通知服务，负责实际发送系统通知（Windows Toast / Linux notify-send）。
    /// </summary>
    private readonly FrameworkNotificationService _frameworkNotification;

    /// <summary>
    /// 通知历史记录列表，使用锁保证线程安全。
    /// </summary>
    private readonly List<NotificationRecord> _history = new();

    /// <summary>
    /// 历史记录锁。
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// 构造 NotificationService 实例。
    /// </summary>
    /// <param name="frameworkNotification">框架通知服务，由 DI 注入。</param>
    public NotificationService(FrameworkNotificationService frameworkNotification)
    {
        _frameworkNotification = frameworkNotification;
    }

    /// <summary>
    /// 立即发送系统通知，并记录到历史。
    /// </summary>
    /// <param name="title">通知标题。</param>
    /// <param name="body">通知正文。</param>
    [Binding]
    public async Task SendNotification(string title, string body)
    {
        await Task.Run(() => _frameworkNotification.SendNotification(title, body));
        AddHistory(title, body);
    }

    /// <summary>
    /// 延迟发送系统通知。等待指定秒数后发送通知并记录到历史。
    /// </summary>
    /// <param name="title">通知标题。</param>
    /// <param name="body">通知正文。</param>
    /// <param name="delaySeconds">延迟秒数。</param>
    [Binding]
    public async Task ScheduleNotification(string title, string body, int delaySeconds)
    {
        await Task.Delay(delaySeconds * 1000);
        await Task.Run(() => _frameworkNotification.SendNotification(title, body));
        AddHistory(title, body);
    }

    /// <summary>
    /// 获取通知历史记录列表的副本。
    /// </summary>
    /// <returns>通知历史记录列表。</returns>
    [Binding]
    public List<NotificationRecord> GetHistory()
    {
        lock (_lock)
        {
            return new List<NotificationRecord>(_history);
        }
    }

    /// <summary>
    /// 清空通知历史记录。
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
    /// 添加一条通知到历史记录。
    /// </summary>
    /// <param name="title">通知标题。</param>
    /// <param name="body">通知正文。</param>
    private void AddHistory(string title, string body)
    {
        lock (_lock)
        {
            _history.Add(new NotificationRecord(title, body, DateTime.Now));
            if (_history.Count > 50)
            {
                _history.RemoveAt(0);
            }
        }
    }
}
