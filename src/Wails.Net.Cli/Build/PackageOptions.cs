namespace Wails.Net.Cli.Build;

/// <summary>
/// 打包格式。
/// 对应 Wails v3 Go 版本 cmd/wails3/package.go 中的格式枚举。
/// </summary>
public enum PackageFormat
{
    /// <summary>ZIP 压缩包</summary>
    Zip,

    /// <summary>tar.gz 压缩包</summary>
    TarGz,

    /// <summary>NSIS 安装程序（Windows）</summary>
    Nsis,

    /// <summary>AppImage（Linux）</summary>
    AppImage,

    /// <summary>Debian 包（.deb），对应 Tauri v2 bundle.linux.debian。</summary>
    Deb,

    /// <summary>RPM 包（.rpm），对应 Tauri v2 bundle.linux.rpm。</summary>
    Rpm
}

/// <summary>
/// 打包选项。
/// 对应 Wails v3 Go 版本 cmd/wails3/package.go 中的打包配置。
/// </summary>
public sealed class PackageOptions
{
    /// <summary>打包格式</summary>
    public PackageFormat Format { get; set; } = OperatingSystem.IsWindows() ? PackageFormat.Zip : PackageFormat.TarGz;

    /// <summary>输出目录</summary>
    public string OutputDirectory { get; set; } = "bin/packages";

    /// <summary>应用名称</summary>
    public string AppName { get; set; } = "WailsApp";

    /// <summary>版本号</summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>发布者名称（用于安装程序元数据）。</summary>
    /// <remarks>默认 null，由 BundleConfig 或 Packager 兜底为 "Wails.Net"。</remarks>
    public string? Publisher { get; set; }

    /// <summary>是否包含调试符号</summary>
    public bool IncludeSymbols { get; set; } = false;

    /// <summary>是否生成校验和文件</summary>
    public bool GenerateChecksum { get; set; } = true;

    /// <summary>应用唯一标识（如 com.example.app）。</summary>
    public string? BundleIdentifier { get; set; }

    /// <summary>图标目录。</summary>
    public string? IconPath { get; set; }

    /// <summary>资源目录。</summary>
    public string? Resources { get; set; }

    /// <summary>版权信息。</summary>
    public string? Copyright { get; set; }

    /// <summary>应用分类。</summary>
    public string? Category { get; set; }

    /// <summary>短描述。</summary>
    public string? ShortDescription { get; set; }

    /// <summary>长描述。</summary>
    public string? LongDescription { get; set; }
}
