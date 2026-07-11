using System.CommandLine;
using System.Reflection;
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
    /// 优先读取 <see cref="AssemblyInformationalVersionAttribute"/>，回退到 <see cref="AssemblyVersionAttribute"/>，再回退到默认值。
    /// </summary>
    /// <returns>版本字符串。</returns>
    internal static string GetCliVersion()
    {
        var asm = typeof(VersionCommand).Assembly;

        var informational = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (informational is not null && !string.IsNullOrEmpty(informational.InformationalVersion))
        {
            return informational.InformationalVersion.Split('+')[0];
        }

        var assemblyVersion = asm.GetName().Version;
        if (assemblyVersion is not null)
        {
            return assemblyVersion.ToString();
        }

        return "0.0.0";
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
