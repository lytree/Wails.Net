namespace Wails.Net.Application.Security;

/// <summary>
/// Scope 配置，从 appsettings.json 的 "Wails:Permissions:Scopes" 节读取。
/// 每个 ScopeConfig 关联一个权限标识，定义允许的路径列表和 URL 模式列表。
/// </summary>
public sealed class ScopeConfig
{
    /// <summary>
    /// 获取或设置允许的文件系统路径列表。
    /// 用于构建 <see cref="FileSystemScope"/>，路径前缀匹配。
    /// </summary>
    public List<string> Paths { get; set; } = new();

    /// <summary>
    /// 获取或设置允许的 URL 模式列表（支持 * 通配符）。
    /// 用于构建 <see cref="UrlScope"/>。
    /// </summary>
    public List<string> Urls { get; set; } = new();
}
