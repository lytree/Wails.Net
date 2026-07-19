using Wails.Net.Application.Bindings;
// 使用别名避免 Application 类型与 Wails.Net.Application 命名空间冲突（CS0118）
using WailsApplication = Wails.Net.Application.Application;

namespace Wails.Net.Demo.Events.Services;

/// <summary>
/// 事件演示服务，通过 <see cref="WailsApplication.Events"/> 向前端广播事件，
/// 并提供绑定方法供前端控制事件流。
/// </summary>
public sealed class EventService
{
    /// <summary>
    /// 应用实例引用，用于访问 EventProcessor。
    /// </summary>
    private readonly WailsApplication _application;

    /// <summary>
    /// 后端定时器，用于周期性 emit tick 事件。
    /// </summary>
    private System.Timers.Timer? _tickTimer;

    /// <summary>
    /// 构造 EventService 实例。
    /// </summary>
    /// <param name="application">应用实例。</param>
    public EventService(WailsApplication application)
    {
        _application = application;
    }

    /// <summary>
    /// 启动定时器，每秒向后端 emit <c>demo:tick</c> 事件。
    /// </summary>
    [Binding]
    public void StartTimer()
    {
        if (_tickTimer is not null)
        {
            return;
        }

        _tickTimer = new System.Timers.Timer(1000);
        var tickCount = 0;
        _tickTimer.Elapsed += (_, _) =>
        {
            tickCount++;
            _application.Events.Emit("demo:tick", new { count = tickCount });
        };
        _tickTimer.Start();
    }

    /// <summary>
    /// 停止定时器，不再 emit <c>demo:tick</c> 事件。
    /// </summary>
    [Binding]
    public void StopTimer()
    {
        _tickTimer?.Dispose();
        _tickTimer = null;
    }

    /// <summary>
    /// 向前端 emit <c>demo:notification</c> 事件，携带消息内容。
    /// </summary>
    /// <param name="message">通知消息。</param>
    [Binding]
    public void SendNotification(string message)
    {
        _application.Events.Emit("demo:notification", new { message, timestamp = DateTime.Now });
    }
}
