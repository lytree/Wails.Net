namespace Wails.Net.Application.Security;

/// <summary>
/// 权限检查模式（OPT-SEC-001）。
/// <para>
/// 对应优化路线图「权限 strict 模式迁移」：以兼容迁移方式向 Tauri v2 最小权限姿态靠拢，
/// 而不是一次性破坏现有应用。
/// </para>
/// <list type="bullet">
/// <item><see cref="Compatibility"/>：兼容模式。旧应用迁移期使用，未授权命令默认放行。</item>
/// <item><see cref="Strict"/>：严格模式。未授权命令一律拒绝，推荐生产模式（最小权限原则）。</item>
/// </list>
/// </summary>
public enum PermissionMode
{
    /// <summary>
    /// 兼容模式（默认）。
    /// 对应 <see cref="PermissionOptions.Enabled"/>=false 的旧行为：未授权命令默认放行。
    /// 仅建议开发/迁移期使用；启动时会输出一次 warning 提示启用 Strict 模式。
    /// </summary>
    Compatibility,

    /// <summary>
    /// 严格模式。
    /// 未授权命令一律拒绝（<see cref="PermissionOptions.DenyByDefault"/> 生效），
    /// 权限必须通过 capabilities 文件或配置显式授权。推荐生产模式。
    /// 即便 <see cref="PermissionOptions.Enabled"/> 未显式设为 true，本模式也强制启用权限检查。
    /// </summary>
    Strict,
}
