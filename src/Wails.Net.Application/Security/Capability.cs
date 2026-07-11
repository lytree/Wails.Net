namespace Wails.Net.Application.Security;

/// <summary>
/// 能力声明，描述命令所需的能力。
/// 借鉴 Tauri v2 的 Capability 设计。
/// </summary>
public sealed class Capability
{
    /// <summary>能力标识（如 "filesystem.read"）</summary>
    public string Id { get; init; }

    /// <summary>描述</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>所属插件</summary>
    public string Plugin { get; init; } = string.Empty;

    /// <summary>
    /// 构造能力声明。
    /// </summary>
    /// <param name="id">能力标识。</param>
    public Capability(string id) => Id = id;
}
