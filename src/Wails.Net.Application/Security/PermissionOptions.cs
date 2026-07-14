namespace Wails.Net.Application.Security;

/// <summary>
/// 权限配置选项，从 appsettings.json 的 "Wails:Permissions" 节读取。
/// 对应 AGENTS.md §1.1.1 统一配置节命名：根节为 <c>Wails</c>。
/// </summary>
public class PermissionOptions
{
    /// <summary>
    /// 获取或设置已授权的权限标识列表。
    /// 权限标识可以是细粒度权限（如 "fs:allow-read"）或命名权限集（如 "core:default"）。
    /// </summary>
    public List<string> Permissions { get; set; } = new();

    /// <summary>
    /// 获取或设置是否启用权限检查（默认 false，向后兼容）。
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 获取或设置默认拒绝策略（默认 true）。
    /// 当 <see cref="Enabled"/> 为 true 时：
    /// - DenyByDefault=true：未授权的权限一律拒绝（最小权限原则，对应 Tauri v2 默认行为）。
    /// - DenyByDefault=false：未授权的权限默认放行（仅在显式撤销时拒绝）。
    /// </summary>
    public bool DenyByDefault { get; set; } = true;

    /// <summary>
    /// 获取或设置权限 Scope 配置。
    /// 键为权限标识（如 "fs:allow-read"），值为 Scope 配置（路径/URL 白名单）。
    /// 对应 Tauri v2 的 Scope 配置：从 appsettings.json 的 "Wails:Permissions:Scopes" 节读取。
    /// </summary>
    public Dictionary<string, ScopeConfig> Scopes { get; set; } = new();
}
