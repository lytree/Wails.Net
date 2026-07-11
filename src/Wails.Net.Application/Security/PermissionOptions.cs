namespace Wails.Net.Application.Security;

/// <summary>
/// 权限配置选项，从 appsettings.json 的 "Desktop:Permissions" 节读取。
/// </summary>
public class PermissionOptions
{
    /// <summary>已授权的能力列表</summary>
    public List<string> Permissions { get; set; } = new();

    /// <summary>是否启用权限检查（默认 false，向后兼容）</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>默认拒绝策略：未声明的能力是否拒绝</summary>
    public bool DenyByDefault { get; set; } = true;
}
