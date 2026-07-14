namespace Wails.Net.AssetServer.Security;

/// <summary>
/// Isolation Pattern 选项。
/// 对应 Tauri v2 的 Isolation 模式：将敏感 JS 代码隔离在独立 iframe 中执行，
/// 通过 sandbox 属性限制其能力，避免污染主上下文。
/// </summary>
public class IsolationOptions
{
    /// <summary>
    /// 获取或设置是否启用 Isolation Pattern。
    /// 默认值为 false。
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 获取或设置 Isolation 资源所在目录名（相对于资源根路径）。
    /// 默认值为 "isolation"。
    /// </summary>
    public string IsolationDir { get; set; } = "isolation";

    /// <summary>
    /// 获取或设置 iframe 加载的 URL。
    /// 默认值为 "/isolation/index.html"。
    /// </summary>
    public string IsolationSrc { get; set; } = "/isolation/index.html";

    /// <summary>
    /// 获取或设置 iframe 的 sandbox 属性值。
    /// 默认值为 "allow-scripts"（允许脚本执行但不允许同源访问）。
    /// </summary>
    public string Sandbox { get; set; } = "allow-scripts";

    /// <summary>
    /// 获取或设置 iframe 的 name 属性值。
    /// 用于在主页面中通过 window.frames[name] 访问 iframe。
    /// 默认值为 "isolation-frame"。
    /// </summary>
    public string FrameName { get; set; } = "isolation-frame";
}
