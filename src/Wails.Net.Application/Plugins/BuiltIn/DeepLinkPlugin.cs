using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Wails.Net.Application.Commands;
using Wails.Net.Events;

namespace Wails.Net.Application.Plugins.BuiltIn;

/// <summary>
/// 深度链接插件，提供自定义 URL Scheme 注册和深度链接事件处理。
/// 对应 Tauri v2 的 <c>@tauri-apps/plugin-deep-link</c>。
/// Windows 通过注册表注册 URL Scheme，Linux 通过 .desktop 文件注册。
/// </summary>
public class DeepLinkPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "deep-link";

    private readonly List<string> _schemes = new();
    private string? _appName;
    private string? _appPath;

    /// <summary>
    /// 初始化深度链接插件实例。
    /// </summary>
    /// <param name="schemes">要注册的 URL Scheme 列表（如 "myapp"）。</param>
    /// <param name="appName">应用名称，用于 Linux .desktop 文件。</param>
    public DeepLinkPlugin(string[] schemes, string? appName = null)
    {
        foreach (var scheme in schemes)
        {
            if (!string.IsNullOrWhiteSpace(scheme))
            {
                _schemes.Add(scheme);
            }
        }

        _appName = appName ?? AppDomain.CurrentDomain.FriendlyName;
        _appPath = Environment.ProcessPath;
    }

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        // 无需注册额外服务
    }

    /// <summary>
    /// 配置插件，注册深度链接命令并注册 URL Scheme。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        // 注册当前已配置的 schemes。
        foreach (var scheme in _schemes)
        {
            RegisterScheme(scheme);
        }

        // 处理启动时传入的 URL 参数。
        ProcessCommandLineArgs();

        context.Commands.MapCommand("deeplink.register", (Action<string>)(scheme => RegisterScheme(scheme)));
        context.Commands.MapCommand("deeplink.unregister", (Action<string>)(scheme => UnregisterScheme(scheme)));
        context.Commands.MapCommand("deeplink.getCurrent", (Func<string?>)(() => GetCurrentUrl()));
    }

    /// <summary>
    /// 注册指定的 URL Scheme 到操作系统。
    /// Windows 通过注册表，Linux 通过 .desktop 文件。
    /// </summary>
    /// <param name="scheme">URL Scheme（如 "myapp"）。</param>
    public void RegisterScheme(string scheme)
    {
        if (string.IsNullOrWhiteSpace(scheme))
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            RegisterWindowsScheme(scheme);
        }
        else if (OperatingSystem.IsLinux())
        {
            RegisterLinuxScheme(scheme);
        }
    }

    /// <summary>
    /// 注销指定的 URL Scheme。
    /// </summary>
    /// <param name="scheme">URL Scheme。</param>
    public void UnregisterScheme(string scheme)
    {
        if (string.IsNullOrWhiteSpace(scheme))
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            UnregisterWindowsScheme(scheme);
        }
        else if (OperatingSystem.IsLinux())
        {
            UnregisterLinuxScheme(scheme);
        }
    }

    /// <summary>
    /// 获取当前启动时传入的深度链接 URL。
    /// </summary>
    /// <returns>URL 字符串，若无则返回 null。</returns>
    public string? GetCurrentUrl()
    {
        var args = Environment.GetCommandLineArgs();
        foreach (var arg in args.Skip(1))
        {
            if (Uri.TryCreate(arg, UriKind.Absolute, out _))
            {
                return arg;
            }
        }

        return null;
    }

    /// <summary>
    /// 处理命令行参数中的 URL，分发深度链接事件。
    /// </summary>
    private void ProcessCommandLineArgs()
    {
        var args = Environment.GetCommandLineArgs();
        foreach (var arg in args.Skip(1))
        {
            if (Uri.TryCreate(arg, UriKind.Absolute, out var uri) &&
                _schemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase))
            {
                EmitDeepLinkEvent(arg);
                return;
            }
        }
    }

    /// <summary>
    /// 分发深度链接事件到应用事件处理器。
    /// </summary>
    /// <param name="url">收到的 URL。</param>
    private static void EmitDeepLinkEvent(string url)
    {
        Application.Get()?.HandlePlatformEvent((uint)ApplicationEventType.DeepLinkReceived, url);
    }

    /// <summary>
    /// Windows 平台注册 URL Scheme 到注册表。
    /// </summary>
    [SupportedOSPlatform("windows")]
    private void RegisterWindowsScheme(string scheme)
    {
        var keyPath = $@"Software\Classes\{scheme}";
        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        if (key is null)
        {
            return;
        }

        key.SetValue(string.Empty, $"URL:{scheme} Protocol");
        key.SetValue("URL Protocol", string.Empty);

        using var iconKey = key.CreateSubKey("DefaultIcon");
        iconKey?.SetValue(string.Empty, _appPath ?? string.Empty);

        using var commandKey = key.CreateSubKey(@"shell\open\command");
        commandKey?.SetValue(string.Empty, $"\"{_appPath}\" \"%1\"");
    }

    /// <summary>
    /// Windows 平台注销 URL Scheme。
    /// </summary>
    [SupportedOSPlatform("windows")]
    private void UnregisterWindowsScheme(string scheme)
    {
        var keyPath = $@"Software\Classes\{scheme}";
        Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
    }

    /// <summary>
    /// Linux 平台通过 .desktop 文件注册 URL Scheme。
    /// </summary>
    private void RegisterLinuxScheme(string scheme)
    {
        var desktopPath = GetDesktopFilePath(scheme);
        var content = $""""
            [Desktop Entry]
            Type=Application
            Name={_appName}
            Exec={_appPath} %u
            Terminal=false
            MimeType=x-scheme-handler/{scheme};
            NoDisplay=true
            """";

        try
        {
            var dir = Path.GetDirectoryName(desktopPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(desktopPath, content);

            // 刷新 MIME 数据库。
            var psi = new ProcessStartInfo
            {
                FileName = "update-desktop-database",
                Arguments = Path.GetDirectoryName(desktopPath) ?? string.Empty,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            process?.WaitForExit();
        }
        catch
        {
            // 非关键错误，忽略。
        }
    }

    /// <summary>
    /// Linux 平台注销 URL Scheme。
    /// </summary>
    private void UnregisterLinuxScheme(string scheme)
    {
        var desktopPath = GetDesktopFilePath(scheme);
        if (File.Exists(desktopPath))
        {
            try
            {
                File.Delete(desktopPath);
            }
            catch
            {
                // 忽略删除错误。
            }
        }
    }

    /// <summary>
    /// 获取 Linux .desktop 文件路径。
    /// </summary>
    private static string GetDesktopFilePath(string scheme)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".local", "share", "applications", $"{scheme}-handler.desktop");
    }
}
