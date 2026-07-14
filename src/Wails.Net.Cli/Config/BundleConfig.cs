namespace Wails.Net.Cli.Config;

/// <summary>
/// 应用打包配置。
/// 对应 Tauri v2 bundle 配置，定义各平台打包元数据、图标、资源等。
/// </summary>
public sealed class BundleConfig
{
    /// <summary>应用唯一标识（如 com.example.app）。</summary>
    public string? Identifier { get; set; }

    /// <summary>图标目录。</summary>
    public string? IconPath { get; set; }

    /// <summary>资源目录。</summary>
    public string? Resources { get; set; }

    /// <summary>版权信息。</summary>
    public string? Copyright { get; set; }

    /// <summary>应用分类（如 Productivity、Developer Tools）。</summary>
    public string? Category { get; set; }

    /// <summary>短描述。</summary>
    public string? ShortDescription { get; set; }

    /// <summary>长描述。</summary>
    public string? LongDescription { get; set; }

    /// <summary>Windows 平台打包配置。</summary>
    public WindowsBundleConfig? Windows { get; set; }

    /// <summary>Linux 平台打包配置。</summary>
    public LinuxBundleConfig? Linux { get; set; }
}

/// <summary>
/// Windows 平台打包配置。
/// </summary>
public sealed class WindowsBundleConfig
{
    /// <summary>发布者名称。</summary>
    public string? Publisher { get; set; }

    /// <summary>WebView2 安装模式：true=bootstrapper（在线），false=offline（离线）。</summary>
    public bool? WebviewInstallMode { get; set; }

    /// <summary>WiX 配置文件路径。</summary>
    public string? WixConfig { get; set; }
}

/// <summary>
/// Linux 平台打包配置。
/// </summary>
public sealed class LinuxBundleConfig
{
    /// <summary>维护者信息。</summary>
    public string? Maintainer { get; set; }

    /// <summary>deb 包依赖项。</summary>
    public string[]? DebDependencies { get; set; }

    /// <summary>rpm 包依赖项。</summary>
    public string[]? RpmDependencies { get; set; }
}
