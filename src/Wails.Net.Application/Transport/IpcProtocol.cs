namespace Wails.Net.Application.Transport;

/// <summary>
/// 前后端 IPC 协议常量（RH-IPC-04，2026-11-15）。
/// <para>
/// 这些常量此前散落在已删除的 <c>HttpTransport</c> 上（<c>MessageEndpoint</c> /
/// <c>HandshakeTokenHeader</c> / <c>MaxRuntimeBodyBytes</c>）。独立 HTTP 监听传输被移除后，
/// 仍被 <see cref="WebSocketTransport"/>（token 头回退）、
/// <see cref="Wails.Net.Application.Webview.WebviewRequestPipeline"/>（运行时消息端点与
/// 请求体上限）复用，故收敛到本类，避免"删掉宿主常量后各处重新硬编码字符串"。
/// </para>
/// <para>
/// 前端对应实现：<c>packages/wails-net-runtime/src/internal/transport.ts</c>
/// （<c>/wails/message</c> 端点、<c>x-wails-token</c> 头）。
/// </para>
/// </summary>
public static class IpcProtocol
{
    /// <summary>
    /// 前端运行时消息端点路径（HTTP/scheme <c>POST /wails/message</c>）。
    /// <para>
    /// 桌面模式下该路径由平台 WebView 请求拦截（<c>WebResourceRequested</c> /
    /// <c>wails://</c> scheme / <c>ShouldInterceptRequest</c>）承载，
    /// Server 模式下由 <see cref="WebSocketTransport"/> 的长连接承载，两者均不经过独立 HTTP 监听。
    /// </para>
    /// </summary>
    public const string MessageEndpoint = "/wails/message";

    /// <summary>
    /// handshake token 请求头名称（OPT-IPC-002）。
    /// 与前端 <c>@wails-net/runtime</c> 的 <c>x-wails-token</c> 头保持一致；
    /// WebSocket 升级请求无法自定义请求头，改用查询参数
    /// <see cref="WebSocketTransport.HandshakeTokenQuery"/>，本头仅作回退读取。
    /// </summary>
    public const string HandshakeTokenHeader = "x-wails-token";

    /// <summary>
    /// 单次 IPC 消息请求体的最大字节数（64 MiB）。
    /// 对应 Wails v3 Go 版本 transport_http.go 中的
    /// <c>maxRuntimeBodyBytes = maxAssembledBytes = 64 * 1024 * 1024</c>。
    /// 超过此上限的请求由 <see cref="Wails.Net.Application.Webview.WebviewRequestPipeline"/>
    /// 拒绝并返回 HTTP 413。
    /// </summary>
    public const long MaxRuntimeBodyBytes = 64 * 1024 * 1024;

    /// <summary>
    /// 请求体超限时返回的状态码（HTTP 413 Payload Too Large）。
    /// </summary>
    public const int PayloadTooLargeStatusCode = 413;
}
