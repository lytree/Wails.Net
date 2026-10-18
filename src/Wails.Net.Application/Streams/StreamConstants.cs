namespace Wails.Net.Application.Streams;

/// <summary>
/// Streams 线协议的帧类型常量（双向一致）。
/// 对应 Wails v3 Go 版本 <c>v3/pkg/application/stream_session.go</c> 中的
/// <c>frameData</c> / <c>frameOpen</c> / <c>frameClose</c> / <c>frameError</c> 常量。
/// </summary>
internal static class StreamFrameKind
{
    /// <summary>数据帧：payload 为业务字节。</summary>
    public const byte Data = 0;

    /// <summary>打开帧：JS→Go 请求连接指定流；Go→JS 表示已接受（ack）。</summary>
    public const byte Open = 1;

    /// <summary>关闭帧：任一方向都表示该连接结束。</summary>
    public const byte Close = 2;

    /// <summary>错误帧：Go→JS 表示连接被拒绝，payload 为原因文本。</summary>
    public const byte Error = 3;
}

/// <summary>
/// Streams 桌面传输的全部常量、端点与 header 名称。
/// 对应 Wails v3 Go 版本 <c>v3/pkg/application/stream.go</c>（队列/额度/超时常量）、
/// <c>v3/pkg/application/stream_transport.go</c>（端点/header/帧宽上限常量）。
/// <para>
/// 控制数据全部走 header：WebKitGTK 6.0 可能把自定义 scheme 的 POST body 当作查询参数投递，
/// WebView2 则在约 2 MB 处截断 body，因此协议元数据一律不放在 body / query 中。
/// </para>
/// </summary>
internal static class StreamConstants
{
    // ---- 端点 ----

    /// <summary>Streams 端点路径前缀。</summary>
    public const string PathPrefix = "/wails/stream/";

    /// <summary>长轮询端点：<c>GET /wails/stream/poll</c>。</summary>
    public const string PathPoll = PathPrefix + "poll";

    /// <summary>发送端点：<c>POST /wails/stream/send</c>。</summary>
    public const string PathSend = PathPrefix + "send";

    // ---- 控制 header（均为小写，查找大小写不敏感） ----

    /// <summary>session id header，长度 ≤ <see cref="MaxSessionIdLen"/>。</summary>
    public const string HeaderSession = "x-wails-stream-session";

    /// <summary>页面 generation header（u64 十进制，非 0）。</summary>
    public const string HeaderGeneration = "x-wails-stream-generation";

    /// <summary>连接 id header（u32 十进制）。</summary>
    public const string HeaderConn = "x-wails-stream-conn";

    /// <summary>帧类型 header（u8 十进制）。</summary>
    public const string HeaderKind = "x-wails-stream-kind";

    /// <summary>流名 header（长度 ≤ <see cref="MaxNameLen"/>）。</summary>
    public const string HeaderName = "x-wails-stream-name";

    /// <summary>批量标记 header：非空表示 body 为批量数据帧。</summary>
    public const string HeaderBatch = "x-wails-stream-batch";

    /// <summary>分片 id header（长度 ≤ <see cref="MaxChunkIdLen"/>）。</summary>
    public const string HeaderChunkId = "x-wails-stream-chunk";

    /// <summary>分片序号 header（0 ≤ index &lt; total）。</summary>
    public const string HeaderChunkIndex = "x-wails-stream-chunk-index";

    /// <summary>分片总数 header（1 ≤ total ≤ <see cref="MaxChunkTotal"/>）。</summary>
    public const string HeaderChunkTotal = "x-wails-stream-chunk-total";

    // ---- 单窗口出站队列 ----

    /// <summary>单窗口出站缓冲字节上限（8 MiB）。对应 Go <c>streamOutQueueBytes</c>。</summary>
    public const int OutQueueBytes = 8 << 20;

    /// <summary>单窗口出站缓冲帧数上限（256）。对应 Go <c>streamOutQueueDepth</c>。</summary>
    public const int OutQueueDepth = 256;

    /// <summary>空 poll 的停放时长（20s）。对应 Go <c>streamHoldTimeout</c>。</summary>
    public static readonly TimeSpan HoldTimeout = TimeSpan.FromSeconds(20);

    /// <summary>单个 poll 响应字节上限（1 MiB）。对应 Go <c>streamMaxResponseBytes</c>。</summary>
    public const int MaxResponseBytes = 1 << 20;

    /// <summary>session 无 poll 存活时长（60s）。对应 Go <c>streamSessionTTL</c>。</summary>
    public static readonly TimeSpan SessionTtl = TimeSpan.FromSeconds(60);

    /// <summary>session 清扫周期（20s）。对应 Go <c>streamSessionSweep</c>。</summary>
    public static readonly TimeSpan SessionSweep = TimeSpan.FromSeconds(20);

    /// <summary>仍有连接的 session 的宽限时长（10min）。对应 Go <c>streamSessionGrace</c>。</summary>
    public static readonly TimeSpan SessionGrace = TimeSpan.FromMinutes(10);

    // ---- 单连接入站队列 ----

    /// <summary>单连接入站缓冲帧数上限（256）。对应 Go <c>streamInQueueDepth</c>。</summary>
    public const int InQueueDepth = 256;

    /// <summary>单连接入站缓冲字节上限（8 MiB）。对应 Go <c>streamInQueueBytes</c>。</summary>
    public const int InQueueBytes = 8 << 20;

    // ---- 连接与会话准入 ----

    /// <summary>单 session 连接上限（256）。对应 Go <c>streamMaxConnections</c>。</summary>
    public const int MaxConnections = 256;

    /// <summary>非 close 控制帧额度（256）。对应 Go <c>streamOutControlDepth</c>。</summary>
    public const int OutControlDepth = 256;

    /// <summary>每窗口 session 上限（16）。对应 Go <c>streamMaxSessionsPerWindow</c>。</summary>
    public const int MaxSessionsPerWindow = 16;

    /// <summary>全局 session 上限（1024）。对应 Go <c>streamMaxSessions</c>。</summary>
    public const int MaxSessions = 1024;

    // ---- 全局额度 ----

    /// <summary>全局出站字节上限（256 MiB）。对应 Go <c>streamOutQueueBytesGlobal</c>。</summary>
    public const long OutQueueBytesGlobal = 256L << 20;

    /// <summary>全局出站帧数上限（8192）。对应 Go <c>streamOutQueueDepthGlobal</c>。</summary>
    public const long OutQueueDepthGlobal = 8192;

    /// <summary>全局入站字节上限（256 MiB）。对应 Go <c>streamInQueueBytesGlobal</c>。</summary>
    public const long InQueueBytesGlobal = 256L << 20;

    /// <summary>全局入站帧数上限（8192）。对应 Go <c>streamInQueueDepthGlobal</c>。</summary>
    public const long InQueueDepthGlobal = 8192;

    /// <summary>全局连接（lifecycle 槽）上限（4096）。对应 Go <c>streamMaxConnectionsGlobal</c>。</summary>
    public const long MaxConnectionsGlobal = 4096;

    /// <summary>全局 close 帧额度（4096）。对应 Go <c>streamOutCloseDepthGlobal</c>。</summary>
    public const long OutCloseDepthGlobal = 4096;

    /// <summary>全局控制帧额度（4096）。对应 Go <c>streamOutControlDepthGlobal</c>。</summary>
    public const long OutControlDepthGlobal = 4096;

    // ---- 帧与分片 ----

    /// <summary>单帧最大字节数（64 MiB）。对应 Go <c>streamMaxFrameBytes</c>。</summary>
    public const int MaxFrameBytes = 64 << 20;

    /// <summary>单次请求批量帧数上限（4096）。对应 Go <c>streamMaxChunkTotal</c>。</summary>
    public const int MaxBatchCount = 4096;

    /// <summary>单 session 未完成分片集合上限（256）。对应 Go <c>streamMaxChunkSets</c>。</summary>
    public const int MaxChunkSets = 256;

    /// <summary>分片总数上限（4096）。对应 Go <c>streamMaxChunkTotal</c>。</summary>
    public const int MaxChunkTotal = 4096;

    /// <summary>分片 id 长度上限（64）。对应 Go <c>streamMaxChunkIDLen</c>。</summary>
    public const int MaxChunkIdLen = 64;

    /// <summary>流名长度上限（256）。对应 Go <c>streamMaxNameLen</c>。</summary>
    public const int MaxNameLen = 256;

    /// <summary>session id 长度上限（64）。对应 Go <c>sessionFor</c> 的长度校验。</summary>
    public const int MaxSessionIdLen = 64;

    /// <summary>全局分片载荷字节上限（2 × <see cref="MaxFrameBytes"/>）。对应 Go <c>streamMaxChunkBytesGlobal</c>。</summary>
    public const long MaxChunkBytesGlobal = 2L * MaxFrameBytes;

    /// <summary>全局分片分片数上限（4096）。对应 Go <c>streamMaxChunkPartsGlobal</c>。</summary>
    public const long MaxChunkPartsGlobal = 4096;

    /// <summary>帧头线上字节数：connID(4) + kind(1) + length(4)。对应 Go <c>streamFrameHeaderBytes</c>。</summary>
    public const int FrameHeaderBytes = 9;
}
