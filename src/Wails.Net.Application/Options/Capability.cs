namespace Wails.Net.Application.Options;

/// <summary>
/// 权限能力声明，定义应用所需的权限及作用窗口范围。
/// 对应 Tauri v2 的 Capabilities 配置模型。
/// </summary>
public sealed class Capability
{
    /// <summary>
    /// 获取或设置能力标识符。
    /// </summary>
    public string Identifier { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置能力描述。
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置此能力包含的权限标识列表。
    /// </summary>
    public List<string> Permissions { get; set; } = new();

    /// <summary>
    /// 获取或设置应用此能力的窗口名称列表。
    /// 空列表表示应用于所有窗口。
    /// </summary>
    public List<string> Windows { get; set; } = new();
}
