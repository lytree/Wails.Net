namespace Wails.Net.Application.Platform;

/// <summary>
/// 平台能力探测结构，对应 Wails v3 Go 版本 <c>internal/capabilities/capabilities.go</c>。
/// <para>
/// 用于在前端查询后端平台的原生能力，前端 JS runtime 可通过此能力做特性开关。
/// 与 <see cref="Security.Capability"/>（Tauri v2 风格 ACL 权限）不同，本结构仅描述
/// 平台原生支持的功能，不涉及授权决策。
/// </para>
/// <para>
/// 各字段含义：
/// <list type="bullet">
/// <item><see cref="HasNativeDrag"/>：是否支持原生拖放（如 OLE Drag-Drop），不支持时回退到 WebView 模拟拖放。</item>
/// <item><see cref="GtkVersion"/>：Linux GTK 主版本号（4 或 3），非 Linux 平台为 0。</item>
/// <item><see cref="WebKitVersion"/>：WebKit 引擎版本号字符串，无法获取时为空。</item>
/// </list>
/// </para>
/// </summary>
public readonly record struct PlatformCapabilities
{
    /// <summary>
    /// 是否支持原生拖放。
    /// </summary>
    public bool HasNativeDrag { get; init; }

    /// <summary>
    /// GTK 主版本号（Linux）。
    /// </summary>
    public int GtkVersion { get; init; }

    /// <summary>
    /// WebKit 引擎版本号字符串。
    /// </summary>
    public string WebKitVersion { get; init; }

    /// <summary>
    /// 默认能力（Server 模式 / 无平台）。
    /// </summary>
    public static PlatformCapabilities Default => new()
    {
        HasNativeDrag = false,
        GtkVersion = 0,
        WebKitVersion = string.Empty,
    };
}
