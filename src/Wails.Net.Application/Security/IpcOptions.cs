namespace Wails.Net.Application.Security;

/// <summary>
/// IPC 传输层安全配置选项，从 appsettings.json 的 "Wails:Ipc" 节读取。
/// 对应 Tauri v2 的 IPC 安全配置（capabilities/allowlist）与 Wails v3 的传输层安全策略。
/// <para>
/// 安全默认值遵循最小权限原则：
/// <list type="bullet">
/// <item>HTTP/WebSocket fallback 默认只监听 loopback 地址。</item>
/// <item>请求必须携带进程级随机 handshake token（由框架在装配时注入，不写入日志）。</item>
/// <item>Origin 校验默认开启：本地 WebView 源与白名单远程源允许，其余拒绝。</item>
/// <item>CORS 默认白名单回显，不再使用通配符 <c>*</c>。</item>
/// </list>
/// </para>
/// 对应优化路线图 OPT-IPC-001（新增 IpcOptions）与 OPT-IPC-002（fallback handshake token）。
/// </summary>
public sealed class IpcOptions
{
    /// <summary>
    /// 获取或设置是否允许 HTTP fallback 传输（默认 true）。
    /// 禁用后 <see cref="Wails.Net.Application.Transport.HttpTransport"/> 将不会被装配为 IPC 通道。
    /// </summary>
    public bool AllowHttpFallback { get; set; } = true;

    /// <summary>
    /// 获取或设置是否允许 WebSocket fallback 传输（默认 true）。
    /// 禁用后 <see cref="Wails.Net.Application.Transport.WebSocketTransport"/> 将不会被装配为 IPC 通道。
    /// </summary>
    public bool AllowWebSocketFallback { get; set; } = true;

    /// <summary>
    /// 获取或设置是否要求 Origin 校验（默认 true）。
    /// 开启后，非本地源且不在 <see cref="AllowedOrigins"/> 白名单中的 IPC 请求将被拒绝。
    /// </summary>
    public bool RequireOriginValidation { get; set; } = true;

    /// <summary>
    /// 获取或设置是否要求 handshake token（默认 true）。
    /// <para>
    /// 开启后，HTTP/WebSocket fallback 通道的每个请求必须携带进程级随机 token
    /// （HTTP 头 <c>x-wails-token</c>，WebSocket 使用查询参数 <c>?token=</c>）。
    /// token 由框架在装配传输层时自动生成并注入前端 runtime（<c>window._wails.ipcToken</c>），
    /// 不写入日志。裸构造传输层且未注入 token 时不强制校验，以保持测试与低层 API 兼容。
    /// </para>
    /// </summary>
    public bool RequireHandshakeToken { get; set; } = true;

    /// <summary>
    /// 获取或设置是否使用随机 loopback 端口（默认 true）。
    /// 开启后从 <see cref="PortStart"/> 起自动探测可用端口；关闭后固定使用 <see cref="PortStart"/>。
    /// </summary>
    public bool UseRandomLoopbackPort { get; set; } = true;

    /// <summary>
    /// 获取或设置端口范围起始值（默认 34115）。
    /// 对应 Wails v3 的 HTTP/WebSocket fallback 端口分配策略。
    /// </summary>
    public int PortStart { get; set; } = 34115;

    /// <summary>
    /// 获取或设置端口范围结束值（默认 34215）。
    /// 探测端口超过此值仍未找到可用端口时，回退到 <see cref="PortStart"/>。
    /// </summary>
    public int PortEnd { get; set; } = 34215;

    /// <summary>
    /// 获取或设置允许的远程 Origin 白名单。
    /// 仅当 <see cref="RequireOriginValidation"/> 为 true 时生效；
    /// 本地源（wails://、localhost、127.0.0.1）总是允许，无需在此声明。
    /// </summary>
    public List<string> AllowedOrigins { get; set; } = new();

    /// <summary>
    /// 每连接 WebSocket mailbox 容量上限（默认 256）。
    /// 超过则 <see cref="Wails.Net.Application.Transport.WebSocketBroadcaster"/> 入队阻塞形成背压。
    /// 对应 R2-3 融合审计（§3.7：有界 mailbox）。
    /// </summary>
    public int MailboxCapacity { get; set; } = 256;

    /// <summary>
    /// 每连接 WebSocket 的最大并发调用数（默认 32）。
    /// 超过则调用被拒绝（返回 RuntimeError 给前端），调用方可以重试。
    /// 控制消息（cancel 等）不受此限制，确保取消指令总能被处理。
    /// 对应 R2-3 融合审计（§3.7：call 使用受控并发任务 + 拒绝策略）。
    /// </summary>
    public int MaxConcurrentCallsPerConnection { get; set; } = 32;
}
