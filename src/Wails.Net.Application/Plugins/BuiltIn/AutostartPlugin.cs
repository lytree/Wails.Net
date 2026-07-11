using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;

namespace Wails.Net.Application.Plugins.BuiltIn;

/// <summary>
/// 开机自启动插件，提供启用、禁用、查询开机自启动的能力。
/// 对应 Tauri v2 的 <c>@tauri-apps/plugin-autostart</c>。
/// Windows：通过注册表 HKCU\Software\Microsoft\Windows\CurrentVersion\Run 实现。
/// Linux：通过 ~/.config/autostart/{appName}.desktop 文件实现。
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
        context.Commands.MapCommand("autostart.enable", (Func<ICommandContext, bool>)(ctx =>
            EnableAutostart(GetAppName())));

        context.Commands.MapCommand("autostart.disable", (Func<ICommandContext, bool>)(ctx =>
            DisableAutostart(GetAppName())));

        context.Commands.MapCommand("autostart.isEnabled", (Func<ICommandContext, bool>)(ctx =>
            IsAutostartEnabled(GetAppName())));
    }

    /// <summary>
    /// 获取应用名称，用于注册表键名或 .desktop 文件名。
    /// </summary>
    /// <returns>应用名称。</returns>
    internal static string GetAppName()
    {
        var asm = Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrEmpty(asm))
        {
            return Path.GetFileNameWithoutExtension(asm);
        }

        return AppDomain.CurrentDomain.FriendlyName;
    }

    /// <summary>
    /// 启用开机自启动。
    /// Windows：向注册表 HKCU\Software\Microsoft\Windows\CurrentVersion\Run 写入值。
    /// Linux：在 ~/.config/autostart/ 目录下创建 .desktop 文件。
    /// </summary>
    /// <param name="appName">应用名称。</param>
    /// <returns>成功返回 true。</returns>
    internal static bool EnableAutostart(string appName)
    {
        if (OperatingSystem.IsWindows())
        {
            return EnableAutostartWindows(appName);
        }

        if (OperatingSystem.IsLinux())
        {
            return EnableAutostartLinux(appName);
        }

        return false;
    }

    /// <summary>
    /// 禁用开机自启动。
    /// Windows：从注册表中删除对应值。
    /// Linux：删除 .desktop 文件。
    /// </summary>
    /// <param name="appName">应用名称。</param>
    /// <returns>成功返回 true。</returns>
    internal static bool DisableAutostart(string appName)
    {
        if (OperatingSystem.IsWindows())
        {
            return DisableAutostartWindows(appName);
        }

        if (OperatingSystem.IsLinux())
        {
            return DisableAutostartLinux(appName);
        }

        return false;
    }

    /// <summary>
    /// 检查开机自启动是否已启用。
    /// </summary>
    /// <param name="appName">应用名称。</param>
    /// <returns>已启用返回 true。</returns>
    internal static bool IsAutostartEnabled(string appName)
    {
        if (OperatingSystem.IsWindows())
        {
            return IsAutostartEnabledWindows(appName);
        }

        if (OperatingSystem.IsLinux())
        {
            return IsAutostartEnabledLinux(appName);
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

            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath))
            {
                exePath = Environment.ProcessPath;
            }

            if (string.IsNullOrEmpty(exePath))
            {
                return false;
            }

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
}
