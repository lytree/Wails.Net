using Wails.Net.Application.Plugins;
using WailsApplication = Wails.Net.Application.Application;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;

namespace Wails.Net.Plugins.Autostart;

/// <summary>
/// 开机自启动插件，提供启用、禁用、查询开机自启动的能力。
/// 对应 Tauri v2 的 <c>@tauri-apps/plugin-autostart</c>。
/// Windows：通过注册表 HKCU\Software\Microsoft\Windows\CurrentVersion\Run 实现。
/// Linux：通过 ~/.config/autostart/{appName}.desktop 文件实现。
/// macOS：通过 ~/Library/LaunchAgents/{label}.plist（LaunchAgent）+ launchctl 实现，
/// 与 <c>Wails.Net.Application.MacOS.MacOSAutostartManager</c> 行为一致；
/// 其余平台（Android 等）无自启动概念，统一返回 <c>false</c>。
/// </summary>
public class AutostartPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "autostart";

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。此插件无需注册额外服务。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        // 无需注册额外服务
    }

    /// <summary>
    /// 配置插件，注册开机自启动相关命令。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // R3-B 权限声明：所有 autostart 命令显式声明 capability。
        context.Permissions.RegisterPermissionSet("autostart:default", "autostart 默认权限集",
            "autostart:allow-default");
        context.Permissions.DeclarePermission("autostart:allow-default", "允许调用 autostart 插件命令");

        // 声明权限集
        context.Permissions.RegisterPermissionSet("autostart:default", "开机自启默认权限集",
            "autostart:allow-enable", "autostart:allow-disable");
        context.Permissions.DeclarePermission("autostart:allow-enable", "允许启用开机自启");
        context.Permissions.DeclarePermission("autostart:allow-disable", "允许禁用开机自启");

        context.Commands.MapCommand("autostart.enable", (Func<ICommandContext, bool>)(ctx =>
            EnableAutostart(GetAppName())), CommandSecurity.Require("autostart:allow-default"));

        context.Commands.MapCommand("autostart.disable", (Func<ICommandContext, bool>)(ctx =>
            DisableAutostart(GetAppName())), CommandSecurity.Require("autostart:allow-default"));

        context.Commands.MapCommand("autostart.isEnabled", (Func<ICommandContext, bool>)(ctx =>
            IsAutostartEnabled(GetAppName())), CommandSecurity.Require("autostart:allow-default"));
    }

    /// <summary>
    /// 获取应用名称，用于注册表键名或 .desktop 文件名。
    /// </summary>
    /// <returns>应用名称。</returns>
    public static string GetAppName()
    {
        var exePath = ResolveExecutablePath();
        return Path.GetFileNameWithoutExtension(exePath);
    }

    /// <summary>
    /// 解析当前进程可执行文件路径，用于注册表值或 .desktop 文件名。
    /// <para>
    /// 优先使用 <see cref="Environment.ProcessPath"/>（可靠且不抛异常），
    /// 其次尝试 <see cref="Process.MainModule"/>（某些环境如 CI 沙箱下其 getter 可能抛出
    /// <see cref="System.ComponentModel.Win32Exception"/>，需捕获），
    /// 最后回退到 <see cref="AppDomain.CurrentDomain.FriendlyName"/>。
    /// </para>
    /// </summary>
    /// <returns>可执行文件路径或程序友好名称，永不为 null/空。</returns>
    private static string ResolveExecutablePath()
    {
        var path = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(path))
        {
            return path;
        }

        try
        {
            path = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(path))
            {
                return path;
            }
        }
        catch (Exception)
        {
            // 某些沙箱/CI 环境下访问 MainModule 会抛异常，忽略并继续回退。
        }

        return AppDomain.CurrentDomain.FriendlyName;
    }

    /// <summary>
    /// 启用开机自启动。
    /// Windows：向注册表 HKCU\Software\Microsoft\Windows\CurrentVersion\Run 写入值。
    /// Linux：在 ~/.config/autostart/ 目录下创建 .desktop 文件。
    /// macOS：写入 ~/Library/LaunchAgents/{label}.plist 并 best-effort 执行 launchctl bootstrap。
    /// </summary>
    /// <param name="appName">应用名称。</param>
    /// <returns>成功返回 true。</returns>
    public static bool EnableAutostart(string appName)
    {
        if (OperatingSystem.IsWindows())
        {
            return EnableAutostartWindows(appName);
        }

        if (OperatingSystem.IsLinux())
        {
            return EnableAutostartLinux(appName);
        }

        if (OperatingSystem.IsMacOS())
        {
            return EnableAutostartMacOS(appName);
        }

        return false;
    }

    /// <summary>
    /// 禁用开机自启动。
    /// Windows：从注册表中删除对应值。
    /// Linux：删除 .desktop 文件。
    /// macOS：best-effort 执行 launchctl bootout 并删除 plist。
    /// </summary>
    /// <param name="appName">应用名称。</param>
    /// <returns>成功返回 true。</returns>
    public static bool DisableAutostart(string appName)
    {
        if (OperatingSystem.IsWindows())
        {
            return DisableAutostartWindows(appName);
        }

        if (OperatingSystem.IsLinux())
        {
            return DisableAutostartLinux(appName);
        }

        if (OperatingSystem.IsMacOS())
        {
            return DisableAutostartMacOS(appName);
        }

        return false;
    }

    /// <summary>
    /// 检查开机自启动是否已启用。
    /// </summary>
    /// <param name="appName">应用名称。</param>
    /// <returns>已启用返回 true。</returns>
    public static bool IsAutostartEnabled(string appName)
    {
        if (OperatingSystem.IsWindows())
        {
            return IsAutostartEnabledWindows(appName);
        }

        if (OperatingSystem.IsLinux())
        {
            return IsAutostartEnabledLinux(appName);
        }

        if (OperatingSystem.IsMacOS())
        {
            return IsAutostartEnabledMacOS(appName);
        }

        return false;
    }

    /// <summary>
    /// Windows 平台：通过注册表启用开机自启动。
    /// </summary>
    /// <param name="appName">应用名称（作为注册表值名称）。</param>
    /// <returns>成功返回 true。</returns>
    [SupportedOSPlatform("windows")]
    private static bool EnableAutostartWindows(string appName)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key is null)
            {
                return false;
            }

            // 优先使用不抛异常的 Environment.ProcessPath；MainModule 在沙箱环境可能抛异常。
            var exePath = ResolveExecutablePath();
            key.SetValue(appName, $"\"{exePath}\"");
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Windows 平台：通过注册表禁用开机自启动。
    /// </summary>
    /// <param name="appName">应用名称。</param>
    /// <returns>成功返回 true。</returns>
    [SupportedOSPlatform("windows")]
    private static bool DisableAutostartWindows(string appName)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key is null)
            {
                return true; // 键不存在视为已禁用
            }

            if (key.GetValue(appName) is not null)
            {
                key.DeleteValue(appName, throwOnMissingValue: false);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Windows 平台：检查注册表中是否已启用开机自启动。
    /// </summary>
    /// <param name="appName">应用名称。</param>
    /// <returns>已启用返回 true。</returns>
    [SupportedOSPlatform("windows")]
    private static bool IsAutostartEnabledWindows(string appName)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: false);
            return key?.GetValue(appName) is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Linux 平台：通过创建 .desktop 文件启用开机自启动。
    /// </summary>
    /// <param name="appName">应用名称。</param>
    /// <returns>成功返回 true。</returns>
    private static bool EnableAutostartLinux(string appName)
    {
        try
        {
            var autostartDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "autostart");

            Directory.CreateDirectory(autostartDir);

            var desktopPath = Path.Combine(autostartDir, $"{appName}.desktop");
            var exePath = Environment.ProcessPath ?? "/usr/bin/" + appName;

            var content = $"""
                [Desktop Entry]
                Type=Application
                Name={appName}
                Exec={exePath}
                Terminal=false
                X-GNOME-Autostart-enabled=true
                """;

            File.WriteAllText(desktopPath, content);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Linux 平台：删除 .desktop 文件禁用开机自启动。
    /// </summary>
    /// <param name="appName">应用名称。</param>
    /// <returns>成功返回 true。</returns>
    private static bool DisableAutostartLinux(string appName)
    {
        try
        {
            var desktopPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "autostart", $"{appName}.desktop");

            if (File.Exists(desktopPath))
            {
                File.Delete(desktopPath);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Linux 平台：检查 .desktop 文件是否存在。
    /// </summary>
    /// <param name="appName">应用名称。</param>
    /// <returns>已启用返回 true。</returns>
    private static bool IsAutostartEnabledLinux(string appName)
    {
        try
        {
            var desktopPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "autostart", $"{appName}.desktop");

            return File.Exists(desktopPath);
        }
        catch
        {
            return false;
        }
    }

    // -------------------------------------------------------------------------
    // macOS：LaunchAgent plist 方案
    // 对应 Wails v3 Go 版本 autostart_darwin.go，行为与
    // Wails.Net.Application.MacOS.MacOSAutostartManager 一致（该类型位于平台程序集，
    // 插件不引用平台程序集，故在此按同一契约独立实现）。
    // -------------------------------------------------------------------------

    /// <summary>
    /// macOS 平台：启用开机自启动（写入 LaunchAgent plist 并尝试立即加载）。
    /// </summary>
    /// <param name="appName">应用名称（归一化为 plist 标签）。</param>
    /// <returns>成功返回 true。</returns>
    [SupportedOSPlatform("macos")]
    private static bool EnableAutostartMacOS(string appName)
    {
        try
        {
            var exePath = ResolveExecutablePath();
            if (string.IsNullOrEmpty(exePath))
            {
                return false;
            }

            var launchAgentsDir = GetMacOSLaunchAgentsDirectory();
            Directory.CreateDirectory(launchAgentsDir);

            var plistPath = GetMacOSPlistPath(appName);
            File.WriteAllText(
                plistPath,
                BuildMacOSPlist(GetMacOSLabel(appName), exePath),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            // 立即加载到当前 GUI 会话，失败不影响下次登录自启。
            RunLaunchctlMacOS("bootstrap", plistPath);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// macOS 平台：禁用开机自启动（卸载 LaunchAgent 并删除 plist）。
    /// plist 不存在时视为已禁用，返回 true（幂等）。
    /// </summary>
    /// <param name="appName">应用名称。</param>
    /// <returns>成功返回 true。</returns>
    [SupportedOSPlatform("macos")]
    private static bool DisableAutostartMacOS(string appName)
    {
        try
        {
            var plistPath = GetMacOSPlistPath(appName);
            RunLaunchctlMacOS("bootout", plistPath);

            if (File.Exists(plistPath))
            {
                File.Delete(plistPath);
            }

            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// macOS 平台：检查 LaunchAgent plist 是否存在且指向当前可执行文件。
    /// </summary>
    /// <param name="appName">应用名称。</param>
    /// <returns>已启用返回 true。</returns>
    [SupportedOSPlatform("macos")]
    private static bool IsAutostartEnabledMacOS(string appName)
    {
        try
        {
            var plistPath = GetMacOSPlistPath(appName);
            if (!File.Exists(plistPath))
            {
                return false;
            }

            // 校验 plist 指向当前可执行文件且带 RunAtLoad，避免误判同名其他应用的条目。
            var content = File.ReadAllText(plistPath);
            var exePath = ResolveExecutablePath();
            return !string.IsNullOrEmpty(exePath)
                && content.Contains(exePath, StringComparison.Ordinal)
                && content.Contains("RunAtLoad", StringComparison.Ordinal);
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// 获取 macOS LaunchAgents 目录（~/Library/LaunchAgents）。
    /// </summary>
    /// <returns>目录路径。</returns>
    private static string GetMacOSLaunchAgentsDirectory()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "LaunchAgents");

    /// <summary>
    /// 获取 LaunchAgent 标签：<c>wails.autostart.&lt;slug&gt;</c>（仅保留字母数字）。
    /// </summary>
    /// <param name="appName">应用名称。</param>
    /// <returns>标签。</returns>
    private static string GetMacOSLabel(string appName)
    {
        var slug = new string(appName.Where(char.IsLetterOrDigit).ToArray());
        return string.IsNullOrEmpty(slug) ? "wails.autostart.app" : $"wails.autostart.{slug}";
    }

    /// <summary>
    /// 获取 LaunchAgent plist 完整路径。
    /// </summary>
    /// <param name="appName">应用名称。</param>
    /// <returns>plist 路径。</returns>
    private static string GetMacOSPlistPath(string appName)
        => Path.Combine(GetMacOSLaunchAgentsDirectory(), $"{GetMacOSLabel(appName)}.plist");

    /// <summary>
    /// 构造 LaunchAgent plist（RunAtLoad=true、KeepAlive=false）。
    /// </summary>
    /// <param name="label">LaunchAgent 标签。</param>
    /// <param name="exePath">可执行文件路径。</param>
    /// <returns>plist XML 字符串。</returns>
    private static string BuildMacOSPlist(string label, string exePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">");
        sb.AppendLine("<plist version=\"1.0\">");
        sb.AppendLine("  <dict>");
        sb.AppendLine("    <key>Label</key>");
        sb.AppendLine($"    <string>{EscapeXml(label)}</string>");
        sb.AppendLine("    <key>ProgramArguments</key>");
        sb.AppendLine("    <array>");
        sb.AppendLine($"      <string>{EscapeXml(exePath)}</string>");
        sb.AppendLine("    </array>");
        sb.AppendLine("    <key>RunAtLoad</key>");
        sb.AppendLine("    <true/>");
        sb.AppendLine("    <key>KeepAlive</key>");
        sb.AppendLine("    <false/>");
        sb.AppendLine("  </dict>");
        sb.AppendLine("</plist>");
        return sb.ToString();
    }

    /// <summary>
    /// 转义 plist XML 中的保留字符。
    /// </summary>
    /// <param name="value">原始字符串。</param>
    /// <returns>转义后字符串。</returns>
    private static string EscapeXml(string value)
        => value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");

    /// <summary>
    /// 执行 launchctl 子命令（bootstrap / bootout），尽力而为。
    /// </summary>
    /// <param name="subcommand">子命令。</param>
    /// <param name="plistPath">plist 路径。</param>
    [SupportedOSPlatform("macos")]
    private static void RunLaunchctlMacOS(string subcommand, string plistPath)
    {
        try
        {
            var uid = Environment.GetEnvironmentVariable("UID");
            var target = string.IsNullOrEmpty(uid) ? "gui/501" : $"gui/{uid}";
            var psi = new ProcessStartInfo
            {
                FileName = "/bin/launchctl",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            psi.ArgumentList.Add(subcommand);
            psi.ArgumentList.Add(target);
            psi.ArgumentList.Add(plistPath);

            using var proc = Process.Start(psi);
            proc?.WaitForExit(2000);
        }
        catch (Exception)
        {
            // launchctl 调用失败静默忽略（plist 仍会在下次登录时生效）。
        }
    }
}
