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
    /// 获取或设置权限检查模式（默认 <see cref="PermissionMode.Compatibility"/>，向后兼容）。
    /// <para>
    /// OPT-SEC-001（权限 strict 模式迁移）：
    /// <list type="bullet">
    /// <item><see cref="PermissionMode.Compatibility"/>：旧应用迁移期行为，
    /// 未授权命令默认放行（等价于旧版 <see cref="Enabled"/>=false 语义）。</item>
    /// <item><see cref="PermissionMode.Strict"/>：最小权限姿态，
    /// 未授权命令一律拒绝；推荐生产模式。即使 <see cref="Enabled"/> 未显式置 true 也强制启用检查。</item>
    /// </list>
    /// </para>
    /// </summary>
    public PermissionMode Mode { get; set; } = PermissionMode.Compatibility;

    /// <summary>
    /// 获取或设置是否启用权限检查（默认 false，向后兼容）。
    /// 当 <see cref="Mode"/> 为 <see cref="PermissionMode.Strict"/> 时，权限检查始终启用。
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

    /// <summary>
    /// 获取或设置 Capability 文件目录路径（相对 ContentRoot / AppContext.BaseDirectory）。
    /// 默认 "capabilities"，对应 Tauri v2 的 capabilities/ 目录约定。
    /// 仅在 <see cref="Enabled"/> 为 true 时触发自动加载；设为 null 或空字符串禁用自动加载。
    /// </summary>
    public string? CapabilitiesDirectory { get; set; } = "capabilities";
}
