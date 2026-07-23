namespace Wails.Net.Application.Security;

/// <summary>
/// IPC 消息来源信息结构，对应 Wails v3 Go 版本 <c>OriginInfo</c>。
/// <para>
/// 在 WebView 拦截 message 事件时填充，使后端能够区分主框架与 iframe、原始 Origin 与顶层 Origin。
/// </para>
/// <para>
/// 字段含义：
/// <list type="bullet">
/// <item><see cref="Origin"/>：当前发送消息的 frame 的 Origin。</item>
/// <item><see cref="TopOrigin"/>：顶层 frame 的 Origin。当 iframe 嵌套时，
/// 顶层与子 frame 不同；主框架消息时与 <see cref="Origin"/> 相同。</item>
/// <item><see cref="IsMainFrame"/>：是否为主框架消息（<c>window.top === window</c>）。</item>
/// </list>
/// </para>
/// </summary>
public readonly record struct OriginInfo
{
    /// <summary>
    /// 当前发送消息的 frame 的 Origin，可为空（如本地资源）。
    /// </summary>
    public string? Origin { get; init; }

    /// <summary>
    /// 顶层 frame 的 Origin，可为空。
    /// </summary>
    public string? TopOrigin { get; init; }

    /// <summary>
    /// 是否为主框架消息。
    /// </summary>
    public bool IsMainFrame { get; init; }

    /// <summary>
    /// 默认来源信息（视为本地、主框架）。
    /// </summary>
    public static OriginInfo Default => new()
    {
        Origin = null,
        TopOrigin = null,
        IsMainFrame = true,
    };
}
