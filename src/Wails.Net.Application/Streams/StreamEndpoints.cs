using System.Text;
using Wails.Net.Application.Webview;

namespace Wails.Net.Application.Streams;

/// <summary>
/// Streams 桌面传输的两个 HTTP 端点处理：<c>GET /wails/stream/poll</c> 与 <c>POST /wails/stream/send</c>。
/// 对应 Wails v3 Go 版本 <c>v3/pkg/application/stream_transport.go</c> 中的
/// <c>serveStreamPoll</c> / <c>serveStreamSend</c> / <c>readStreamBody</c>，
/// 以及 <c>v3/pkg/application/stream.go</c> 中 <c>App.sessionFor</c> 的错误映射。
/// </summary>
internal static class StreamEndpoints
{
    /// <summary>
    /// 处理长轮询端点。对应 Go <c>App.serveStreamPoll</c>。
    /// </summary>
    /// <param name="manager">Streams 管理器。</param>
    /// <param name="request">平台无关请求。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>响应。</returns>
    public static async Task<WebviewResponse> ServePollAsync(StreamManager manager, WebviewRequest request, CancellationToken ct)
    {
        // 仅 GET。HEAD 会跑同样的 drain 却被 net/http 抑制 body，导致帧被消费却从未投递。
        // 这是协议端点，不是可探测的资源。
        if (!string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            return WebviewResponse.MethodNotAllowed("GET");
        }

        var resolution = manager.ResolveSession(
            request.WindowId,
            GetHeader(request, StreamConstants.HeaderSession),
            GetHeader(request, StreamConstants.HeaderGeneration),
            create: true,
            maySupersede: true);
        if (resolution.Session is null)
        {
            return resolution.Error!;
        }

        var session = resolution.Session;
        var result = await session.AwaitFramesAsync(StreamConstants.HoldTimeout, StreamConstants.MaxResponseBytes, ct).ConfigureAwait(false);
        if (!result.Alive)
        {
            manager.RemoveSession(session.Id);
            return WebviewResponse.Gone("session closed");
        }

        var noStore = NoStore();
        if (result.Frames.Count == 0)
        {
            return WebviewResponse.NoContent(noStore);
        }

        var body = StreamFrameCodec.Encode(result.Frames, result.More);
        return WebviewResponse.Ok("application/octet-stream", body, noStore);
    }

    /// <summary>
    /// 处理发送端点。对应 Go <c>App.serveStreamSend</c>。
    /// </summary>
    /// <param name="manager">Streams 管理器。</param>
    /// <param name="request">平台无关请求。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>响应。</returns>
    public static async Task<WebviewResponse> ServeSendAsync(StreamManager manager, WebviewRequest request, CancellationToken ct)
    {
        if (!string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase))
        {
            return WebviewResponse.MethodNotAllowed("POST");
        }

        var headers = request.Headers;

        if (!StreamFrameCodec.TryParseUInt32(GetHeader(request, StreamConstants.HeaderConn), out var connId))
        {
            return WebviewResponse.BadRequest("bad connection id");
        }

        if (!StreamFrameCodec.TryParseUInt8(GetHeader(request, StreamConstants.HeaderKind), out var kind))
        {
            return WebviewResponse.BadRequest("bad frame kind");
        }

        if (kind is not (StreamFrameKind.Open or StreamFrameKind.Data or StreamFrameKind.Close))
        {
            return WebviewResponse.BadRequest("unknown frame kind");
        }

        var isBatch = !string.IsNullOrEmpty(GetHeader(request, StreamConstants.HeaderBatch));
        var chunkId = GetHeader(request, StreamConstants.HeaderChunkId);
        var hasChunks = !string.IsNullOrEmpty(chunkId);
        if (isBatch && (kind != StreamFrameKind.Data || hasChunks))
        {
            // runtime 的批量已在平台 body 上限之下有界，因此从不分片。混用两种重试协议会让
            // 末片重试下的部分批量确认变得歧义。
            return WebviewResponse.BadRequest("invalid batched frame");
        }

        var name = GetHeader(request, StreamConstants.HeaderName) ?? string.Empty;
        if (kind == StreamFrameKind.Open && (name.Length == 0 || name.Length > StreamConstants.MaxNameLen))
        {
            return WebviewResponse.BadRequest("invalid stream name");
        }

        var body = ReadBody(request);
        if (kind != StreamFrameKind.Data && (hasChunks || body.Length != 0))
        {
            // open 与 close 的所有协议数据都在 header 中。在 session 查找之前拒绝 body，
            // 使控制请求无法保留后续没有任何派发路径消费的分片。
            return WebviewResponse.BadRequest("control frame must not carry a body");
        }

        // 只有 open 帧可以造出会话。对已不存在会话的 data / close 帧，过去会静默新建一个：
        // Go 于是持有一个无连接的活会话，而页面以为其流仍开着。改为告知客户端会话已消失，让它暴露 close。
        var resolution = manager.ResolveSession(
            request.WindowId,
            GetHeader(request, StreamConstants.HeaderSession),
            GetHeader(request, StreamConstants.HeaderGeneration),
            create: kind == StreamFrameKind.Open,
            maySupersede: false);
        if (resolution.Session is null)
        {
            return resolution.Error!;
        }

        var session = resolution.Session;

        StreamConn? dataConn = null;
        if (kind == StreamFrameKind.Data)
        {
            dataConn = session.Conn(connId);
            if (dataConn is null)
            {
                return WebviewResponse.Gone("unknown connection");
            }
        }

        // body 读取 / 分片重组。
        var read = ReadStreamBody(session, request, chunkId, body);
        var readError = read.Error;
        if (readError is not null)
        {
            return readError;
        }

        if (!read.Complete)
        {
            // 落下一片；整帧尚未完整。
            return WebviewResponse.NoContent();
        }

        // 已完成的集合必须在连接收件箱回 429 后仍存活。客户端只重试被拒的那一片；
        // 若在下游接受前删除集合，会把那次重试变成新的、不完整的上传，回 204 并静默丢帧。
        var removeChunk = hasChunks;
        try
        {
            // 批量：一个 body 承载某连接的若干数据帧。在此拆分，使其余路径与单帧完全一致。
            if (isBatch)
            {
                if (!StreamFrameCodec.TryDecodeBatch(read.Body, out var frames))
                {
                    return WebviewResponse.BadRequest("unable to read frame body");
                }

                for (var i = 0; i < frames.Count; i++)
                {
                    var delivered = await dataConn!.DeliverAsync(frames[i], block: false, ct).ConfigureAwait(false);
                    if (delivered == StreamDeliverResult.Full)
                    {
                        removeChunk = false;
                        // 部分接受：告知客户端已落多少片，让它只重发剩余部分，保持顺序。
                        return WebviewResponse.Text(
                            429, "Too Many Requests", "receiver is behind",
                            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            {
                                [StreamConstants.HeaderBatch] = i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                ["Retry-After"] = "0",
                            });
                    }

                    if (delivered != StreamDeliverResult.Delivered)
                    {
                        return WebviewResponse.Gone("connection closed");
                    }
                }

                return WebviewResponse.NoContent();
            }

            switch (kind)
            {
                case StreamFrameKind.Open:
                    switch (session.Open(connId, name))
                    {
                        case SessionOpenResult.Full:
                            return WebviewResponse.TooManyRequests("stream session is at capacity");
                        case SessionOpenResult.Closed:
                            return WebviewResponse.Gone("session closed");
                        case SessionOpenResult.Duplicate:
                            return WebviewResponse.Conflict("duplicate connection id");
                        default:
                            return WebviewResponse.NoContent();
                    }

                case StreamFrameKind.Data:
                    // 响应前入队。客户端在本响应落地前不会发下一次 send，因此「先追加再响应」
                    // 正是前端发送顺序 == handler 观测顺序的保证。
                    switch (await dataConn!.DeliverAsync(read.Body, block: false, ct).ConfigureAwait(false))
                    {
                        case StreamDeliverResult.Full:
                            removeChunk = false;
                            // 背压是「通知」而非「占位等待」。在此等待会占掉一个请求槽并饿死本窗口的 poll；
                            // 改让客户端重试同一帧，从而保序且收件箱有界。
                            return WebviewResponse.TooManyRequests("receiver is behind");
                        case StreamDeliverResult.Closed:
                            return WebviewResponse.Gone("connection closed");
                        default:
                            return WebviewResponse.NoContent();
                    }

                default:
                    session.Conn(connId)?.ClosedByPeer();
                    return WebviewResponse.NoContent();
            }
        }
        finally
        {
            if (removeChunk)
            {
                session.Chunks().Remove(chunkId!);
            }
        }
    }

    /// <summary>
    /// 读取整帧 body；若客户端拆分则先重组。<c>Complete</c> 为 false 表示本请求只承载了非末片。
    /// 对应 Go <c>readStreamBody</c>。
    /// </summary>
    /// <param name="session">会话。</param>
    /// <param name="request">请求（用于读取 header）。</param>
    /// <param name="chunkId">分片 id（未分片时为空）。</param>
    /// <param name="body">已读取的请求体。</param>
    /// <returns>读取结果。</returns>
    private static (byte[] Body, bool Complete, WebviewResponse? Error) ReadStreamBody(
        StreamSession session, WebviewRequest request, string? chunkId, byte[] body)
    {
        if (body.Length > StreamConstants.MaxFrameBytes)
        {
            return ([], false, WebviewResponse.BadRequest("unable to read frame body"));
        }

        if (string.IsNullOrEmpty(chunkId))
        {
            return (body, true, null);
        }

        if (chunkId.Length > StreamConstants.MaxChunkIdLen)
        {
            return ([], false, WebviewResponse.BadRequest("invalid chunk headers"));
        }

        if (!StreamFrameCodec.TryParseInt32(GetHeader(request, StreamConstants.HeaderChunkTotal), out var total) ||
            total <= 0 || total > StreamConstants.MaxChunkTotal)
        {
            return ([], false, WebviewResponse.BadRequest("invalid chunk headers"));
        }

        if (!StreamFrameCodec.TryParseInt32(GetHeader(request, StreamConstants.HeaderChunkIndex), out var index) ||
            index < 0 || index >= total)
        {
            return ([], false, WebviewResponse.BadRequest("invalid chunk headers"));
        }

        var status = session.Chunks().Add(chunkId, index, total, body, out var assembled);
        switch (status)
        {
            case StreamChunkStatus.Partial:
                return ([], false, null);
            case StreamChunkStatus.Complete:
                return (assembled ?? [], true, null);
            case StreamChunkStatus.Full:
                return ([], false, WebviewResponse.TooManyRequests("chunk reassembly is at capacity"));
            case StreamChunkStatus.Closed:
                return ([], false, WebviewResponse.Gone("session closed"));
            case StreamChunkStatus.TooBig:
                return ([], false, WebviewResponse.BadRequest("assembled frame too large"));
            case StreamChunkStatus.Conflict:
                return ([], false, WebviewResponse.BadRequest("conflicting chunk total"));
            default:
                return ([], false, WebviewResponse.BadRequest("duplicate chunk index"));
        }
    }

    /// <summary>
    /// 读取请求体字节。优先使用二进制体；否则回退为 <see cref="WebviewRequest.RequestBody"/>
    /// 的 Latin-1 字节（1:1 保留 0-255，供平台以文本形式传递二进制体）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <returns>请求体字节；无体时为空数组。</returns>
    private static byte[] ReadBody(WebviewRequest request)
    {
        if (request.RequestBodyBytes is not null)
        {
            return request.RequestBodyBytes;
        }

        return request.RequestBody is null ? [] : Encoding.Latin1.GetBytes(request.RequestBody);
    }

    /// <summary>
    /// 读取请求 header（大小写不敏感）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="name">header 名称。</param>
    /// <returns>header 值；不存在时返回 null。</returns>
    private static string? GetHeader(WebviewRequest request, string name)
    {
        return StreamFrameCodec.GetHeader(request.Headers, name);
    }

    /// <summary>
    /// 构造 <c>Cache-Control: no-store</c> 响应头（避免 poll 响应被缓存）。
    /// </summary>
    /// <returns>响应头字典。</returns>
    private static Dictionary<string, string> NoStore()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Cache-Control"] = "no-store",
        };
    }
}
