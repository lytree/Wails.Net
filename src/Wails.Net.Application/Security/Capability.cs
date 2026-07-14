namespace Wails.Net.Application.Security;

/// <summary>
/// 能力声明，定义应用所需的权限及作用窗口范围。
/// 对应 Tauri v2 的 Capability 模型：一个能力包含多个权限标识，可限定到特定窗口。
/// 同时作为运行时能力声明（供 <see cref="PermissionManager"/> 校验）和配置层模型（从 appsettings.json 加载）。
/// </summary>
public sealed class Capability
{
    /// <summary>
    /// 获取或设置能力标识符（如 "main-capability"）。
    /// 对应 Tauri v2 Capability 的 identifier 字段。
    /// </summary>
    public string Identifier { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置能力描述。
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置此能力包含的权限标识列表（如 "fs:allow-read"、"window:default"）。
    /// 权限标识可指向 <see cref="PermissionSet"/>（以 :default 等后缀标识的命名集合）。
    /// </summary>
    public List<string> Permissions { get; set; } = new();

    /// <summary>
    /// 获取或设置应用此能力的窗口名称列表。
    /// 空列表表示应用于所有窗口。
    /// 对应 Tauri v2 的窗口级能力隔离。
    /// </summary>
    public List<string> Windows { get; set; } = new();

    /// <summary>
    /// 获取或设置所属插件名称（运行时由插件声明时填充）。
    /// </summary>
    public string Plugin { get; set; } = string.Empty;

    /// <summary>
    /// 使用指定标识符构造能力声明。
    /// </summary>
    /// <param name="identifier">能力标识符。</param>
    public Capability(string identifier) => Identifier = identifier;

    /// <summary>
    /// 无参构造，用于配置绑定和反序列化。
    /// </summary>
    public Capability() { }
}
