using Wails.Net.Application.Bindings;
using Wails.Net.Application.Events;
using Wails.Net.Application.Security;

namespace Wails.Net.Application.Transport;

/// <summary>
/// IPC 传输策略上下文（OPT-IPC-003，2026-08-15 新增）。
/// 由策略装配点（当前为 Server 模式装配，后续推广到各平台窗口）构造并传入
/// <see cref="IIpcTransportStrategy"/>，使传输层选择与安全配置（token/Origin/CORS）
/// 集中治理，避免选择逻辑散落在平台实现中。
/// </summary>
public sealed class IpcTransportContext
{
    /// <summary>消息处理器。</summary>
    public required MessageProcessor Processor { get; init; }

    /// <summary>WebSocket 事件广播器。</summary>
    public required WebSocketBroadcaster Broadcaster { get; init; }

    /// <summary>IPC 安全配置（可为 null，缺省使用传输层默认值）。</summary>
    public IpcOptions? IpcOptions { get; init; }

    /// <summary>进程级 handshake token（可为 null）。</summary>
    public string? HandshakeToken { get; init; }

    /// <summary>是否为 Server 模式（无原生 postMessage 通道）。</summary>
    public bool IsServerMode { get; init; }
}

/// <summary>
/// 统一 IPC 传输策略接口（OPT-IPC-003）。
/// <para>
/// 对应优化路线图 §5.2：根据平台能力选择首选传输，并按统一顺序构造 fallback，
/// 统一注入 <see cref="MessageProcessor"/>、<see cref="IpcOriginValidator"/>、
/// CORS 配置与 <see cref="IpcOptions"/>，输出诊断日志说明最终选中的传输路径。
/// </para>
/// </summary>
public interface IIpcTransportStrategy
{
    /// <summary>
    /// 创建首选传输层。
    /// </summary>
    /// <param name="context">传输策略上下文。</param>
    /// <returns>首选传输层；无合适首选时返回 null（交由 fallback 列表）。</returns>
    ITransport? CreatePrimaryTransport(IpcTransportContext context);

    /// <summary>
    /// 按统一顺序创建 fallback 传输层列表。
    /// </summary>
    /// <param name="context">传输策略上下文。</param>
    /// <returns>fallback 传输层列表（可为空）。</returns>
    IReadOnlyList<ITransport> CreateFallbackTransports(IpcTransportContext context);
}

/// <summary>
/// 默认 IPC 传输策略（OPT-IPC-003）。
/// <para>
/// 建议默认顺序（按平台能力修正，但必须集中在本策略中）：
/// <list type="number">
/// <item>Native IPC（桌面平台由 <see cref="NativeIpcTransport"/> 窗口级注册承载，
/// 非 <see cref="ITransport"/>，不在此列表；策略通过 <see cref="IpcTransportContext.IsServerMode"/>
/// 感知其可用性）。</item>
/// <item>WebSocket transport（Server 模式首选；桌面模式可作 fallback）。</item>
/// <item>HTTP transport（大消息 / WS 不可用时的 HTTP POST /wails/message 端点）。
/// <b>注意</b>：HTTP fallback 端点的前端 URL 注入协议尚未扩展，桌面/Server 模式下
/// 当前由 WebView 拦截（Windows）或 WS 通道（Server）承载消息路径；接入独立 HTTP
/// 端点需与前端 runtime flags 扩展一并落地（见 optimization-roadmap §5.2 后续批次）。</item>
/// <item>EventIPC fallback（由 <see cref="Application"/> 始终追加，不在此列表）。</item>
/// </list>
/// </para>
/// </summary>
public sealed class DefaultIpcTransportStrategy : IIpcTransportStrategy
{
    /// <inheritdoc />
    public ITransport? CreatePrimaryTransport(IpcTransportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Server 模式：WebSocket 为首选传输（浏览器前端通过 WS 长连接收发消息与事件）。
        if (context.IsServerMode)
        {
            var transport = new WebSocketTransport(context.Processor, context.Broadcaster);

            // OPT-IPC-001/002：注入安全配置（handshake token + Origin 白名单 + CORS 白名单回显）
            var ipcOptions = context.IpcOptions;
            transport.HandshakeToken = context.HandshakeToken;
            if (ipcOptions is not null)
            {
                transport.RequireHandshakeToken = ipcOptions.RequireHandshakeToken;
                transport.OriginValidator = CreateOriginValidator(ipcOptions);
                transport.CorsOptions = CreateCorsOptions(ipcOptions);
                transport.MaxConcurrentCallsPerConnection = ipcOptions.MaxConcurrentCallsPerConnection;
            }

            // P0 安全加固（对齐 Wails v3 v3.0.0）：Server 模式一律装配请求闸门，
            // 强制 Host 校验（DNS rebinding 防护）与同源 Origin 校验；
            // RequireOriginValidation=false 对应 Go 的 WebSocketAllowAllOrigins（仅跳过 Origin 校验）。
            transport.RequestGuard = CreateRequestGuard(ipcOptions);

            return transport;
        }

        // 桌面模式：原生 IPC 由平台窗口注册（NativeIpcTransport 非 ITransport），
        // 主传输留空，由平台 WebView 消息拦截路径承载（Win32/Linux/macOS）。
        return null;
    }

    /// <inheritdoc />
    public IReadOnlyList<ITransport> CreateFallbackTransports(IpcTransportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // 本轮仅返回 WebSocket 主传输之外的 HTTP fallback 候选。
        // 注意：HttpTransport 独立监听端口，其前端 URL 注入协议尚未扩展，
        // 接入生产 fallback 链需与前端 runtime flags 扩展一并落地（见路线图 §5.2）。
        // 为避免"装饰性装配"（创建了但前端不可达），此处暂不返回 HttpTransport，
        // 其生产装配决策记录在 optimization-roadmap.md OPT-IPC-003。
        return [];
    }

    /// <summary>
    /// 根据 <see cref="IpcOptions"/> 创建 IPC 来源校验器（OPT-IPC-002）。
    /// 白名单来自 <see cref="IpcOptions.AllowedOrigins"/>；本地源总是允许（校验器内建）。
    /// </summary>
    /// <param name="ipcOptions">IPC 安全配置。</param>
    /// <returns>校验器实例。</returns>
    private static IpcOriginValidator CreateOriginValidator(IpcOptions ipcOptions)
    {
        var whitelist = new UrlWhitelist();
        foreach (var origin in ipcOptions.AllowedOrigins)
        {
            if (!string.IsNullOrWhiteSpace(origin))
            {
                whitelist.Add(origin);
            }
        }

        return new IpcOriginValidator(whitelist);
    }

    /// <summary>
    /// 根据 <see cref="IpcOptions"/> 创建 Server 模式请求安全闸门（P0 安全加固）。
    /// 白名单来自 <see cref="IpcOptions.AllowedOrigins"/>（对应 Go 的
    /// <c>ServerOptions.WebSocketOriginPatterns</c>）；配置缺失时仍启用同源校验（安全默认）。
    /// </summary>
    /// <param name="ipcOptions">IPC 安全配置（可为 null）。</param>
    /// <returns>请求安全闸门。</returns>
    private static IpcRequestGuard CreateRequestGuard(IpcOptions? ipcOptions)
    {
        var patterns = ipcOptions?.AllowedOrigins ?? new List<string>();
        return new IpcRequestGuard(patterns)
        {
            // 与 Go 一致：AllowAllOrigins 仅跳过 Origin 校验，Host 校验仍生效。
            AllowAllOrigins = ipcOptions is not null && !ipcOptions.RequireOriginValidation,
        };
    }

    /// <summary>
    /// 根据 <see cref="IpcOptions"/> 创建 CORS 配置（OPT-IPC-002）。
    /// 白名单回显而非通配符 <c>*</c>；<see cref="IpcOptions.RequireOriginValidation"/> 为 false 时禁用。
    /// </summary>
    /// <param name="ipcOptions">IPC 安全配置。</param>
    /// <returns>CORS 配置实例。</returns>
    private static CorsOptions CreateCorsOptions(IpcOptions ipcOptions)
    {
        return new CorsOptions
        {
            Enabled = ipcOptions.RequireOriginValidation,
            AllowedOrigins = ipcOptions.AllowedOrigins.ToList(),
        };
    }
}
