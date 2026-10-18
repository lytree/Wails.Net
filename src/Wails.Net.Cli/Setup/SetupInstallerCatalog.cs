using System.Runtime.InteropServices;

namespace Wails.Net.Cli.Setup;

/// <summary>
/// setup 向导的已批准安装命令目录。
/// 对应 Wails v3 Go 版本 <c>internal/setupwizard</c> 的依赖安装表（<c>wizard.go:1208-1336</c>
/// 白名单约束下的命令来源）。
/// <para>
/// 命令一律以常量 token 数组（<c>string[]</c>）形式声明，绝不接受用户提供的任意字符串
/// 拼接执行；执行前统一经 <see cref="SafeInstallerCommand"/> 校验。若执行开关被启用，
/// 实际 argv 由 <see cref="SafeInstallCommand.ToArgv"/> 组装，不经过任何 shell 解析。
/// </para>
/// </summary>
internal static class SetupInstallerCatalog
{
    /// <summary>Windows 平台已批准的安装命令（winget，免特权提升）。</summary>
    private static readonly Dictionary<string, string[]> WindowsCommands = new(StringComparer.Ordinal)
    {
        ["Node.js"] = ["winget", "install", "OpenJS.NodeJS.LTS"],
        ["WebView2 Runtime"] = ["winget", "install", "Microsoft.EdgeWebView2Runtime"],
        ["NSIS"] = ["winget", "install", "NSIS.NSIS"],
    };

    /// <summary>Linux 平台已批准的安装命令（apt，经 sudo 特权提升）。</summary>
    private static readonly Dictionary<string, string[]> LinuxCommands = new(StringComparer.Ordinal)
    {
        ["GTK4"] = ["sudo", "apt", "install", "libgtk-4-dev"],
        ["WebKitGTK-6.0"] = ["sudo", "apt", "install", "libwebkitgtk-6.0-dev"],
        ["Linux 共享库"] = ["sudo", "apt", "install", "libgtk-4-1", "libwebkitgtk-6.0-4"],
        ["D-Bus"] = ["sudo", "apt", "install", "dbus"],
    };

    /// <summary>当前平台无适配目录时使用的空表。</summary>
    private static readonly Dictionary<string, string[]> EmptyCommands = new(StringComparer.Ordinal);

    /// <summary>
    /// 当前平台支持的依赖名集合（供 <c>--install</c> 提示与测试枚举）。
    /// </summary>
    internal static IEnumerable<string> SupportedDependencies => CurrentCommands.Keys;

    /// <summary>
    /// 当前平台对应的命令表。
    /// </summary>
    private static Dictionary<string, string[]> CurrentCommands
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return WindowsCommands;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return LinuxCommands;
            }

            return EmptyCommands;
        }
    }

    /// <summary>
    /// 解析依赖名对应的已批准包管理器命令（当前平台）。
    /// </summary>
    /// <param name="dependencyName">依赖名（与 <c>DoctorCommand.RunDiagnostics</c> 的检查项名称一致）。</param>
    /// <param name="commandParts">解析成功时输出命令 token 数组；失败时为空数组。</param>
    /// <returns>存在已批准命令时返回 true。</returns>
    internal static bool TryResolve(string dependencyName, out string[] commandParts)
    {
        if (CurrentCommands.TryGetValue(dependencyName, out var parts))
        {
            commandParts = parts;
            return true;
        }

        commandParts = [];
        return false;
    }
}
