using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wails.Net.Application.Bindings;
using Wails.Net.Errors;

namespace Wails.Net.Application.Transport;

/// <summary>
/// 每连接的消息分发器（R2-3 融合审计：控制通道 + 受控并发）。
/// <para>
/// 设计要点：
/// <list type="bullet">
/// <item><b>控制通道</b>：cancel 等控制消息走 <see cref="DispatchControl"/>，
/// 不占用调用并发槽位，确保 cancel 不会因长调用而被阻塞。</item>
/// <item><b>受控并发</b>：调用类型消息经 <see cref="DispatchCall"/> 派发，
/// 使用 <see cref="SemaphoreSlim"/> 限制同连接最大并发调用数。</item>
/// <item><b>拒绝策略</b>：调用并发槽位耗尽时立即返回错误响应（拒绝而非静默丢弃），
/// 让前端能感知到并重试。</item>
/// <item><b>指标</b>：所有派发都更新 <see cref="WebSocketTransportMetrics"/> 计数器。</item>
/// </list>
/// </para>
/// </summary>
internal sealed class ClientMessageDispatcher
{
    private readonly string _clientId;
    private readonly SemaphoreSlim _callSlots;
    private readonly MessageProcessor _processor;
    private readonly WebSocketBroadcaster _broadcaster;
    private readonly WebSocketTransportMetrics _metrics;
    private readonly ILogger<ClientMessageDispatcher>? _logger;
    private int _disposed;

    public ClientMessageDispatcher(
        string clientId,
        int maxConcurrentCalls,
        MessageProcessor processor,
        WebSocketBroadcaster broadcaster,
        WebSocketTransportMetrics metrics,
        ILogger<ClientMessageDispatcher>? logger)
    {
        if (string.IsNullOrEmpty(clientId))
            throw new ArgumentException("客户端 ID 不能为空", nameof(clientId));
        if (maxConcurrentCalls < 1)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentCalls), "并发上限必须 >= 1");
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(broadcaster);
        ArgumentNullException.ThrowIfNull(metrics);

        _clientId = clientId;
        _callSlots = new SemaphoreSlim(maxConcurrentCalls, maxConcurrentCalls);
        _processor = processor;
        _broadcaster = broadcaster;
        _metrics = metrics;
        _logger = logger;
    }

    /// <summary>
    /// 派发控制消息（cancel 等）。
    /// 总是立即处理，不占用调用并发槽位。
    /// </summary>
    /// <param name="message">控制消息。</param>
    public void DispatchControl(Message message)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await ProcessAsync(message, isControl: true).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _metrics.IncrementControlRejected();
                _logger?.LogWarning(ex, "控制消息处理失败 clientId={ClientId} type={Type}", _clientId, message.Type);
            }
        });
    }

    /// <summary>
    /// 派发调用类型消息。
    /// 若并发上限已满则立即返回错误响应（拒绝策略）。
    /// </summary>
    /// <param name="message">调用消息。</param>
    public void DispatchCall(Message message)
    {
        if (!_callSlots.Wait(0))
        {
            _metrics.IncrementCallsRejected();
            _ = SendConcurrencyLimitErrorAsync(message.Id);
            return;
        }

        _metrics.IncrementCallsInFlight();
        _ = Task.Run(async () =>
        {
            try
            {
                await ProcessAsync(message, isControl: false).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "调用消息处理失败 clientId={ClientId} callId={CallId}", _clientId, message.Id);
            }
            finally
            {
                _metrics.DecrementCallsInFlight();
                _metrics.IncrementCallsCompleted();
                try
                {
                    _callSlots.Release();
                }
                catch (ObjectDisposedException)
                {
                    // Dispatcher 已释放，忽略。
                }
            }
        });
    }

    /// <summary>
    /// 处理消息并把响应发回客户端。
    /// </summary>
    private async Task ProcessAsync(Message message, bool isControl)
    {
        var response = await _processor.ProcessAsync(message).ConfigureAwait(false);
        if (response is not null)
        {
            var json = JsonSerializer.Serialize(response, JsonOptions.DefaultSerializerOptions);
            await _broadcaster.SendToClientAsync(_clientId, json).ConfigureAwait(false);
        }

        if (isControl)
        {
            _metrics.IncrementControlProcessed();
        }
    }

    /// <summary>
    /// 发送并发上限错误响应（R2-3 拒绝策略）。
    /// 前端可据此感知并稍后重试。
    /// </summary>
    private async Task SendConcurrencyLimitErrorAsync(string callId)
    {
        try
        {
            var error = new CallError("并发调用上限已满，请稍后重试", null, CallErrorKind.RuntimeError);
            var response = new ResponseMessage
            {
                Id = callId,
                Type = MessageProcessor.MessageTypes.Error,
                Result = new Dictionary<string, object?>
                {
                    ["result"] = null,
                    ["error"] = error.ToJson()
                }
            };
            var json = JsonSerializer.Serialize(response, JsonOptions.DefaultSerializerOptions);
            await _broadcaster.SendToClientAsync(_clientId, json).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "并发上限错误响应发送失败 clientId={ClientId}", _clientId);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _callSlots.Dispose();
    }
}
