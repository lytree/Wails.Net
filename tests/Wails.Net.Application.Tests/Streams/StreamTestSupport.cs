using System.Globalization;
using Wails.Net.Application.Streams;
using Wails.Net.Application.Webview;

namespace Wails.Net.Application.Tests.Streams;

/// <summary>
/// Streams 测试共享辅助：构造管理器 / 会话 / 连接与端点请求。
/// 直接使用 internal API（经 <c>InternalsVisibleTo</c> 访问），避免依赖全局 <c>Application</c>。
/// </summary>
internal static class StreamTestSupport
{
    /// <summary>默认流名。</summary>
    public const string StreamName = "test";

    /// <summary>保持连接存活且不消费入站帧的 handler（供测试手动 drain 收件箱）。</summary>
    public static readonly StreamHandler BlockingHandler = async conn =>
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, conn.Closed).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 连接关闭：正常结束。
        }
    };

    /// <summary>创建一个注册了默认 handler 的管理器。</summary>
    /// <param name="handler">自定义 handler；为 null 时使用 <see cref="BlockingHandler"/>。</param>
    /// <returns>管理器。</returns>
    public static StreamManager NewManager(StreamHandler? handler = null)
    {
        var manager = new StreamManager();
        manager.HandleStream(StreamName, handler ?? BlockingHandler);
        return manager;
    }

    /// <summary>创建（或取得）一个会话。</summary>
    /// <param name="manager">管理器。</param>
    /// <param name="id">session id。</param>
    /// <param name="windowId">窗口 id。</param>
    /// <param name="generation">页面代次。</param>
    /// <param name="maySupersede">是否允许退休更低代次。</param>
    /// <returns>会话。</returns>
    public static StreamSession NewSession(
        StreamManager manager, string id = "s1", uint windowId = 1, ulong generation = 1, bool maySupersede = false)
    {
        var admission = manager.SessionWithAdmission(id, windowId, generation, maySupersede);
        return admission.Session!;
    }

    /// <summary>打开一条连接并返回它。</summary>
    /// <param name="session">会话。</param>
    /// <param name="connId">连接 id。</param>
    /// <returns>连接。</returns>
    public static StreamConn OpenConn(StreamSession session, uint connId)
    {
        var result = session.Open(connId, StreamName);
        if (result != SessionOpenResult.Ok)
        {
            throw new InvalidOperationException($"open 失败：{result}");
        }

        return session.Conn(connId)!;
    }

    /// <summary>构造 poll 端点请求。</summary>
    /// <param name="windowId">窗口 id。</param>
    /// <param name="sessionId">session id。</param>
    /// <param name="generation">generation header 值；null 表示缺失。</param>
    /// <param name="method">HTTP 方法。</param>
    /// <returns>请求。</returns>
    public static WebviewRequest Poll(uint windowId, string sessionId, string? generation = "1", string method = "GET")
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [StreamConstants.HeaderSession] = sessionId,
        };
        if (generation is not null)
        {
            headers[StreamConstants.HeaderGeneration] = generation;
        }

        return new WebviewRequest
        {
            Method = method,
            Path = StreamConstants.PathPoll,
            WindowId = windowId,
            Headers = headers,
        };
    }

    /// <summary>构造 send 端点请求。</summary>
    /// <param name="windowId">窗口 id。</param>
    /// <param name="sessionId">session id。</param>
    /// <param name="connId">连接 id。</param>
    /// <param name="kind">帧类型。</param>
    /// <param name="generation">generation header 值；null 表示缺失。</param>
    /// <param name="body">请求体字节。</param>
    /// <param name="name">流名 header；null 表示缺失。</param>
    /// <param name="chunkId">分片 id；null 表示未分片。</param>
    /// <param name="chunkIndex">分片序号。</param>
    /// <param name="chunkTotal">分片总数。</param>
    /// <param name="batch">是否携带批量标记。</param>
    /// <param name="method">HTTP 方法。</param>
    /// <returns>请求。</returns>
    public static WebviewRequest Send(
        uint windowId,
        string sessionId,
        uint connId,
        byte kind,
        string? generation = "1",
        byte[]? body = null,
        string? name = null,
        string? chunkId = null,
        int chunkIndex = 0,
        int chunkTotal = 0,
        bool batch = false,
        string method = "POST")
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [StreamConstants.HeaderConn] = connId.ToString(CultureInfo.InvariantCulture),
            [StreamConstants.HeaderKind] = kind.ToString(CultureInfo.InvariantCulture),
            [StreamConstants.HeaderSession] = sessionId,
        };
        if (generation is not null)
        {
            headers[StreamConstants.HeaderGeneration] = generation;
        }

        if (name is not null)
        {
            headers[StreamConstants.HeaderName] = name;
        }

        if (batch)
        {
            headers[StreamConstants.HeaderBatch] = "1";
        }

        if (chunkId is not null)
        {
            headers[StreamConstants.HeaderChunkId] = chunkId;
            headers[StreamConstants.HeaderChunkIndex] = chunkIndex.ToString(CultureInfo.InvariantCulture);
            headers[StreamConstants.HeaderChunkTotal] = chunkTotal.ToString(CultureInfo.InvariantCulture);
        }

        return new WebviewRequest
        {
            Method = method,
            Path = StreamConstants.PathSend,
            WindowId = windowId,
            RequestBodyBytes = body,
            Headers = headers,
        };
    }

    /// <summary>构造一个批量数据帧请求体。</summary>
    /// <param name="frames">各帧载荷。</param>
    /// <returns>batch 体字节。</returns>
    public static byte[] BuildBatch(IReadOnlyList<byte[]> frames)
    {
        var size = 4;
        foreach (var frame in frames)
        {
            size += 4 + frame.Length;
        }

        var body = new byte[size];
        WriteUInt32BigEndian(body, 0, (uint)frames.Count);
        var off = 4;
        foreach (var frame in frames)
        {
            WriteUInt32BigEndian(body, off, (uint)frame.Length);
            off += 4;
            frame.CopyTo(body, off);
            off += frame.Length;
        }

        return body;
    }

    /// <summary>写入大端 u32。</summary>
    /// <param name="buffer">目标缓冲。</param>
    /// <param name="offset">偏移。</param>
    /// <param name="value">值。</param>
    public static void WriteUInt32BigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    /// <summary>读取大端 u32。</summary>
    /// <param name="buffer">源缓冲。</param>
    /// <param name="offset">偏移。</param>
    /// <returns>值。</returns>
    public static uint ReadUInt32BigEndian(byte[] buffer, int offset)
    {
        return (uint)((buffer[offset] << 24) | (buffer[offset + 1] << 16) | (buffer[offset + 2] << 8) | buffer[offset + 3]);
    }

    /// <summary>轮询等待条件成立。</summary>
    /// <param name="predicate">条件。</param>
    /// <param name="timeout">超时。</param>
    /// <returns>条件在超时前成立返回 true。</returns>
    public static async Task<bool> WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return true;
            }

            await Task.Delay(10).ConfigureAwait(false);
        }

        return predicate();
    }
}
