namespace LyboxApp.Services.Core;

/// <summary>
/// LYBox 事件总线。解耦 [Binding] 服务与 Wails.Net 应用实例，
/// 使后端服务可在不持有 Application 引用的情况下向前端广播事件。
/// </summary>
public class LyboxEventBus
{
    private Action<string, object?>? _emit;

    /// <summary>
    /// 挂载到应用实例（在构建完成后调用）。
    /// </summary>
    /// <param name="emit">转发到 app.Events.Emit 的委托。</param>
    public void Attach(Action<string, object?> emit) => _emit = emit;

    /// <summary>
    /// 向前端广播自定义事件。
    /// </summary>
    /// <param name="name">事件名。</param>
    /// <param name="data">载荷。</param>
    public void Emit(string name, object? data = null) => _emit?.Invoke(name, data);
}
