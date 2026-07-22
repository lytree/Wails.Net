namespace Wails.Net.Application.SystemEnvironment;

/// <summary>
/// 应用运行环境信息。
/// 对应 Wails v3 Go 版本 environment.go 中的 EnvironmentInfo 结构。
/// </summary>
public class EnvironmentInfo
{
    /// <summary>
    /// 操作系统名称（如 "windows"、"linux"、"android"）。
    /// 对应 Wails v3 Go 版本 <c>EnvironmentInfo.OS</c>（runtime.GOOS）。
    /// </summary>
    public string OS { get; set; } = string.Empty;

    /// <summary>
    /// 系统架构（如 "x64"、"arm64"）。
    /// 对应 Wails v3 Go 版本 <c>EnvironmentInfo.Arch</c>（runtime.GOARCH）。
    /// </summary>
    public string Arch { get; set; } = string.Empty;

    /// <summary>
    /// 是否启用调试模式。
    /// 对应 Wails v3 Go 版本 <c>EnvironmentInfo.Debug</c>。
    /// </summary>
    public bool Debug { get; set; }

    /// <summary>
    /// 操作系统详细信息（版本号、产品名等）。
    /// 对应 Wails v3 Go 版本 <c>EnvironmentInfo.OSInfo</c>（operatingsystem.OS）。
    /// </summary>
    public OperatingSystemInfo? OSInfo { get; set; }

    /// <summary>
    /// 平台特定信息字典（如 Linux 的 focusFollowsMouse 等）。
    /// 对应 Wails v3 Go 版本 <c>EnvironmentInfo.PlatformInfo</c>。
    /// </summary>
    public Dictionary<string, object?> PlatformInfo { get; set; } = new();

    /// <summary>
    /// 返回环境信息的字符串表示。
    /// </summary>
    public override string ToString()
    {
        return $"{OS}/{Arch} (Debug={Debug})";
    }
}

/// <summary>
/// 操作系统详细信息。
/// 对应 Wails v3 Go 版本 internal/operatingsystem.OS 结构。
/// </summary>
public class OperatingSystemInfo
{
    /// <summary>
    /// 操作系统产品名称（如 "Windows 11"、"Ubuntu 22.04 LTS"）。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 操作系统版本号（如 "10.0.26200"、"22.04"）。
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// 操作系统内部版本号。
    /// </summary>
    public string Build { get; set; } = string.Empty;

    /// <summary>
    /// 硬件标识（厂商/型号），可为空。
    /// </summary>
    public string? Hardware { get; set; }

    /// <summary>
    /// 返回操作系统信息的字符串表示。
    /// </summary>
    public override string ToString()
    {
        return $"{Name} {Version} (Build {Build})";
    }
}
