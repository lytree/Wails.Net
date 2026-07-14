namespace Wails.Net.Application.Hosting;

/// <summary>
/// 应用配置节（对应 appsettings.json 中 "Wails":"App" 节）。
/// 对应 Tauri v2 的 app/windows/security 配置结构。
/// </summary>
public sealed class HostingAppConfig
{
    /// <summary>多窗口配置列表。第一个窗口作为主窗口。</summary>
    public List<WindowConfig> Windows { get; set; } = new();

    /// <summary>安全配置。</summary>
    public SecurityConfig Security { get; set; } = new();
}

/// <summary>
/// 窗口配置（对应 appsettings.json 中 "Wails":"App":"Windows" 数组元素）。
/// 字段语义对齐 <see cref="Wails.Net.Application.Options.WebviewWindowOptions"/>。
/// </summary>
public sealed class WindowConfig
{
    /// <summary>窗口名称（唯一标识）。</summary>
    public string? Name { get; set; }

    /// <summary>窗口标题。</summary>
    public string? Title { get; set; }

    /// <summary>窗口宽度（像素），默认 1280。</summary>
    public int Width { get; set; } = 1280;

    /// <summary>窗口高度（像素），默认 720。</summary>
    public int Height { get; set; } = 720;

    /// <summary>是否可调整大小，默认 true。</summary>
    public bool Resizable { get; set; } = true;

    /// <summary>是否居中显示。</summary>
    public bool Centered { get; set; }

    /// <summary>是否全屏显示。</summary>
    public bool Fullscreen { get; set; }

    /// <summary>是否无边框。</summary>
    public bool Frameless { get; set; }

    /// <summary>是否始终置顶。</summary>
    public bool AlwaysOnTop { get; set; }

    /// <summary>窗口初始加载的 URL。</summary>
    public string? Url { get; set; }
}

/// <summary>
/// 安全配置（对应 appsettings.json 中 "Wails":"App":"Security" 节）。
/// 对应 Tauri v2 的 security 配置。
/// </summary>
public sealed class SecurityConfig
{
    /// <summary>Content-Security-Policy 头。</summary>
    public string? Csp { get; set; }

    /// <summary>
    /// 能力文件目录（默认 capabilities/，相对 AppContext.BaseDirectory）。
    /// 对应 Tauri v2 的 capabilities 目录。
    /// </summary>
    public string? CapabilitiesDir { get; set; }

    /// <summary>是否启用更新签名验证（覆盖 UpdaterConfig.VerifySignature）。</summary>
    public bool? VerifySignature { get; set; }

    /// <summary>信任的 minisign 公钥（覆盖 UpdaterConfig.TrustedPublicKey）。</summary>
    public string? TrustedPublicKey { get; set; }
}
