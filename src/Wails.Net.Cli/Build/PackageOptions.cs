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
    AppImage
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

    /// <summary>是否包含调试符号</summary>
    public bool IncludeSymbols { get; set; } = false;

    /// <summary>是否生成校验和文件</summary>
    public bool GenerateChecksum { get; set; } = true;
}
