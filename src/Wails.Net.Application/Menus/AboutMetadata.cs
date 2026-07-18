namespace Wails.Net.Application.Menus;

/// <summary>
/// 关于对话框的元数据。对应 Tauri v2 AboutMetadata 结构。
/// 用于 <see cref="MenuRole.About"/> 角色弹出关于对话框时显示的信息。
/// </summary>
public sealed class AboutMetadata
{
    /// <summary>
    /// 应用名称。
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 应用完整版本号（如 1.0.0-beta.1）。
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// 短版本号（如 1.0）。
    /// </summary>
    public string? ShortVersion { get; set; }

    /// <summary>
    /// 作者信息（多作者用换行或分号分隔）。
    /// </summary>
    public string? Authors { get; set; }

    /// <summary>
    /// 版权信息。
    /// </summary>
    public string? Copyright { get; set; }

    /// <summary>
    /// 许可证信息。
    /// </summary>
    public string? License { get; set; }

    /// <summary>
    /// 网站 URL。
    /// </summary>
    public string? Website { get; set; }

    /// <summary>
    /// 网站链接的显示文本（默认为 Website URL 本身）。
    /// </summary>
    public string? WebsiteLabel { get; set; }

    /// <summary>
    /// 附加评论或描述。
    /// </summary>
    public string? Comments { get; set; }
}
