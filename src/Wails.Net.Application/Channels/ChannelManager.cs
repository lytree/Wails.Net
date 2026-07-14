using System.Collections.Concurrent;

namespace Wails.Net.Application.Channels;

/// <summary>
/// 通道管理器：线程安全的通道注册表。
/// 负责创建、查找、移除 <see cref="IChannel"/> 实例，并支持将外部消息分发到对应通道。
/// <para>
/// 对应 Tauri v2 中 Channel 的全局注册机制：
/// 前端通过 <c>channel:open</c> 消息创建通道，<c>channel:message</c> 消息分发，
/// <c>channel:close</c> 消息关闭。
/// </para>
/// <para>
/// 默认发送委托为 no-op（不投递到任何传输层），实际使用时由 <see cref="Create(string?, Func{string, object?, CancellationToken, Task}?)"/>
/// 传入传输层特定的发送逻辑。
/// </para>
/// </summary>
public static class ChannelManager
{
    /// <summary>
    /// 通道注册表：键为通道 ID，值为通道实例。
    /// </summary>
    private static readonly ConcurrentDictionary<string, Channel> Channels = new();

    /// <summary>
    /// 默认发送委托：no-op，仅返回已完成的任务。
    /// 用于未指定发送委托的通道（如测试场景）。
    /// </summary>
    private static readonly Func<string, object?, CancellationToken, Task> DefaultSender =
        (_, _, _) => Task.CompletedTask;

    /// <summary>
    /// 当前注册的通道数量。
    /// </summary>
    public static int Count => Channels.Count;

    /// <summary>
    /// 创建并注册新通道。
    /// </summary>
    /// <param name="id">
    /// 通道唯一标识。若为 null 则自动生成 GUID（32 字符无连字符）。
    /// </param>
    /// <param name="sender">
    /// 发送委托，将消息投递到底层传输层。若为 null 则使用默认 no-op 委托。
    /// </param>
    /// <returns>新创建的通道实例。</returns>
    /// <exception cref="ArgumentException">当 <paramref name="id"/> 与已注册通道重复时抛出。</exception>
    public static IChannel Create(
        string? id = null,
        Func<string, object?, CancellationToken, Task>? sender = null)
    {
        var channelId = id ?? Guid.NewGuid().ToString("N");
        var channel = new Channel(channelId, sender ?? DefaultSender);

        if (!Channels.TryAdd(channelId, channel))
        {
            throw new ArgumentException(
                $"通道 ID '{channelId}' 已存在",
                nameof(id));
        }

        return channel;
    }

    /// <summary>
    /// 根据通道 ID 获取通道实例。
    /// </summary>
    /// <param name="id">通道 ID。</param>
    /// <returns>通道实例；若不存在返回 null。</returns>
    public static IChannel? Get(string id)
    {
        return Channels.TryGetValue(id, out var channel) ? channel : null;
    }

    /// <summary>
    /// 根据通道 ID 移除通道。
    /// </summary>
    /// <param name="id">通道 ID。</param>
    /// <returns>若通道存在并已移除返回 true；否则返回 false。</returns>
    public static bool Remove(string id)
    {
        return Channels.TryRemove(id, out _);
    }

    /// <summary>
    /// 清空所有注册的通道。
    /// 主要用于测试场景重置状态。
    /// </summary>
    public static void Clear()
    {
        Channels.Clear();
    }

    /// <summary>
    /// 将外部消息分发到指定通道。
    /// 由传输层在收到 <c>channel:message</c> 消息时调用。
    /// </summary>
    /// <param name="channelId">目标通道 ID。</param>
    /// <param name="eventId">事件标识，可为 null。</param>
    /// <param name="message">消息载荷。</param>
    /// <returns>若通道存在且消息已分发返回 true；否则返回 false。</returns>
    internal static bool DispatchMessage(string channelId, string? eventId, ChannelMessage message)
    {
        if (Channels.TryGetValue(channelId, out var channel))
        {
            try
            {
                channel.ReceiveMessage(eventId, message);
                return true;
            }
            catch (ObjectDisposedException)
            {
                // 通道已关闭，从注册表移除并返回 false
                Channels.TryRemove(channelId, out _);
                return false;
            }
        }

        return false;
    }
}
