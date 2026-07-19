using System.CommandLine;
using System.Runtime.InteropServices;

namespace Wails.Net.Cli.Commands;

/// <summary>
/// version 命令：显示 Wails.Net CLI 版本信息。
/// 对应 Tauri v2 的 <c>tauri version</c> 命令。
/// 输出 CLI 版本、.NET 运行时版本、操作系统信息。
/// </summary>
internal sealed class VersionCommand : CliCommandBase
{
    /// <summary>
    /// 创建 version 命令实例。
    /// </summary>
    /// <returns>配置好的命令。</returns>
    public static Command Create()
    {
        var command = new Command("version", "显示 Wails.Net CLI 版本信息");
        command.Action = AsyncAction.Create(() =>
        {
            var cmd = new VersionCommand();
            return Task.FromResult(cmd.Execute());
        });
        return command;
    }

    /// <summary>
    /// 执行 version 命令，输出版本信息。
    /// </summary>
    /// <returns>退出码：始终返回 0。</returns>
    private int Execute()
    {
        var cliVersion = GetCliVersion();
        var runtimeVersion = Environment.Version.ToString();
        var osInfo = GetOsDescription();

        Info("Wails.Net CLI");
        Info("=============");
        Info($"版本:        v{cliVersion}");
        Info($".NET 运行时: v{runtimeVersion}");
        Info($"操作系统:    {osInfo}");
        Info($"架构:        {RuntimeInformation.OSArchitecture}");
        Info($"机器名:      {Environment.MachineName}");

        return 0;
    }

    /// <summary>
    /// 获取 CLI 版本字符串。
    /// 使用 MSBuild 在编译期生成的 <see cref="VersionConstants"/> 静态类（零反射）。
    /// <c>VersionConstants.InformationalVersion</c> 由 csproj 的
    /// <c>&lt;InformationalVersion&gt;</c> 属性派生（见 <c>Directory.Build.props</c>），
    /// 通过 GenerateVersionConstants MSBuild Target 在 CoreCompile 之前写入
    /// <c>VersionConstants.g.cs</c> 文件，运行时直接读取常量字段。
    /// </summary>
    /// <returns>版本字符串（不含 SourceLink 追加的 commit hash 后缀）。</returns>
    internal static string GetCliVersion()
    {
        // VersionConstants 由 MSBuild 在编译期生成，运行时直接读取常量，零反射。
        var informational = VersionConstants.InformationalVersion;
        if (!string.IsNullOrEmpty(informational))
        {
            // 剥离 SourceLink 注入的 commit hash 后缀（如 "0.1.0-alpha.1+abc123" → "0.1.0-alpha.1"）
            return informational.Split('+')[0];
        }

        // 兜底：使用 Version（4 段数字格式）
        var version = VersionConstants.Version;
        return !string.IsNullOrEmpty(version) ? version : "0.0.0";
    }

    /// <summary>
    /// 获取操作系统描述字符串。
    /// </summary>
    /// <returns>操作系统名称和版本。</returns>
    internal static string GetOsDescription()
    {
        var os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "Windows"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                ? "Linux"
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                    ? "macOS"
                    : "未知";

        return $"{os} {Environment.OSVersion.Version}";
    }
}
