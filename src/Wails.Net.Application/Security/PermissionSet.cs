namespace Wails.Net.Application.Security;

/// <summary>
/// 权限集，命名权限组合（如 "core:default"、"window:default"）。
/// 对应 Tauri v2 的 PermissionSet 概念：插件可声明默认权限集，
/// 能力声明（<see cref="Capability"/>）中可引用权限集标识，授权时自动展开为集内所有权限。
/// </summary>
public sealed class PermissionSet
{
    /// <summary>
    /// 获取或设置权限集标识符（如 "core:default"、"fs:default"）。
    /// 约定格式：&lt;plugin&gt;:&lt;setName&gt;
    /// </summary>
    public string Identifier { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置权限集描述。
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置此权限集包含的权限标识列表。
    /// 权限标识约定格式：&lt;plugin&gt;:&lt;allow|deny&gt;-&lt;action&gt;（如 "fs:allow-read"）。
    /// </summary>
    public List<string> Permissions { get; set; } = new();

    /// <summary>
    /// 使用指定标识符构造权限集。
    /// </summary>
    /// <param name="identifier">权限集标识符。</param>
    public PermissionSet(string identifier) => Identifier = identifier;

    /// <summary>
    /// 无参构造，用于配置绑定和反序列化。
    /// </summary>
    public PermissionSet() { }
}
