using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Wails.Net.Application.Commands;

namespace Wails.Net.Application.Plugins.BuiltIn;

/// <summary>
/// 文件关联插件，提供文件扩展名注册和关联事件处理。
/// 对应 Tauri v2 的文件关联功能和 Wails v3 的 FileAssociations。
/// Windows 通过注册表注册文件扩展名关联，Linux 通过 .desktop 文件 MIME 类型注册。
/// </summary>
public class FileAssociationPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "file-association";

    /// <summary>
    /// 已注册的文件扩展名列表（如 ".txt"、".myapp"）。
    /// </summary>
    private readonly List<string> _extensions = new();

    /// <summary>
    /// 应用名称，用于注册表 ProgID 和 .desktop 文件。
    /// </summary>
    private readonly string _appName;

    /// <summary>
    /// 应用可执行文件路径。
    /// </summary>
    private readonly string? _appPath;

    /// <summary>
    /// 初始化文件关联插件实例。
    /// </summary>
    /// <param name="extensions">要注册的文件扩展名列表（如 ".myapp"）。</param>
    /// <param name="appName">应用名称，用于注册表 ProgID。</param>
    public FileAssociationPlugin(string[] extensions, string? appName = null)
    {
        foreach (var ext in extensions)
        {
            var normalized = NormalizeExtension(ext);
            if (!string.IsNullOrEmpty(normalized))
            {
                _extensions.Add(normalized);
            }
        }

        _appName = appName ?? AppDomain.CurrentDomain.FriendlyName;
        _appPath = Environment.ProcessPath;
    }

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。此插件无需注册额外服务。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        // 无需注册额外服务
    }

    /// <summary>
    /// 配置插件，注册文件关联命令并注册文件扩展名。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        // 注册当前已配置的文件扩展名。
        foreach (var ext in _extensions)
        {
            RegisterExtension(ext);
        }

        // 处理启动时传入的文件参数。
        ProcessCommandLineArgs();

        context.Commands.MapCommand("fileassociation.register",
            (Action<string>)(ext => RegisterExtension(NormalizeExtension(ext) ?? string.Empty)));
        context.Commands.MapCommand("fileassociation.unregister",
            (Action<string>)(ext => UnregisterExtension(NormalizeExtension(ext) ?? string.Empty)));
        context.Commands.MapCommand("fileassociation.getRegistered",
            (Func<string[]>)(() => _extensions.ToArray()));
    }

    /// <summary>
    /// 注册指定的文件扩展名到操作系统。
    /// Windows 通过注册表，Linux 通过 .desktop 文件 MIME 类型。
    /// </summary>
    /// <param name="extension">文件扩展名（如 ".myapp"）。</param>
    public void RegisterExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return;
        }

        extension = NormalizeExtension(extension) ?? extension;
        if (!_extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            _extensions.Add(extension);
        }

        if (OperatingSystem.IsWindows())
        {
            RegisterWindowsExtension(extension);
        }
        else if (OperatingSystem.IsLinux())
        {
            RegisterLinuxExtension(extension);
        }
    }

    /// <summary>
    /// 注销指定的文件扩展名。
    /// </summary>
    /// <param name="extension">文件扩展名。</param>
    public void UnregisterExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return;
        }

        extension = NormalizeExtension(extension) ?? extension;
        _extensions.RemoveAll(e => string.Equals(e, extension, StringComparison.OrdinalIgnoreCase));

        if (OperatingSystem.IsWindows())
        {
            UnregisterWindowsExtension(extension);
        }
        else if (OperatingSystem.IsLinux())
        {
            UnregisterLinuxExtension(extension);
        }
    }

    /// <summary>
    /// 处理启动时传入的文件路径参数，分发文件打开事件。
    /// </summary>
    private static void ProcessCommandLineArgs()
    {
        var args = Environment.GetCommandLineArgs();
        foreach (var arg in args.Skip(1))
        {
            // 排除以 - 或 / 开头的选项参数
            if (arg.StartsWith('-') || arg.StartsWith('/'))
            {
                continue;
            }

            // 检查是否为已注册扩展名的文件
            var ext = Path.GetExtension(arg);
            if (!string.IsNullOrEmpty(ext) && File.Exists(arg))
            {
                Application.Get()?.Events.Emit("file:opened", arg, null);
                return;
            }
        }
    }

    /// <summary>
    /// 规范化文件扩展名，确保以点号开头。
    /// </summary>
    /// <param name="extension">原始扩展名（如 "myapp" 或 ".myapp"）。</param>
    /// <returns>规范化的扩展名（如 ".myapp"），无效时返回 null。</returns>
    private static string? NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        extension = extension.Trim();
        if (!extension.StartsWith(".", StringComparison.Ordinal))
        {
            extension = "." + extension;
        }

        return extension.ToLowerInvariant();
    }

    /// <summary>
    /// Windows 平台注册文件扩展名到注册表。
    /// 写入 HKCU\Software\Classes\.ext 和 HKCU\Software\Classes\AppName\shell\open\command。
    /// </summary>
    [SupportedOSPlatform("windows")]
    private void RegisterWindowsExtension(string extension)
    {
        var progId = $"{_appName}.File";
        var extKeyPath = $@"Software\Classes\{extension}";
        using var extKey = Registry.CurrentUser.CreateSubKey(extKeyPath);
        if (extKey is null)
        {
            return;
        }

        extKey.SetValue(string.Empty, progId);

        // 注册 ProgID
        var progIdKeyPath = $@"Software\Classes\{progId}";
        using var progIdKey = Registry.CurrentUser.CreateSubKey(progIdKeyPath);
        if (progIdKey is null)
        {
            return;
        }

        progIdKey.SetValue(string.Empty, $"{_appName} File");

        using var iconKey = progIdKey.CreateSubKey("DefaultIcon");
        iconKey?.SetValue(string.Empty, _appPath ?? string.Empty);

        using var commandKey = progIdKey.CreateSubKey(@"shell\open\command");
        commandKey?.SetValue(string.Empty, $"\"{_appPath}\" \"%1\"");
    }

    /// <summary>
    /// Windows 平台注销文件扩展名。
    /// </summary>
    [SupportedOSPlatform("windows")]
    private void UnregisterWindowsExtension(string extension)
    {
        var extKeyPath = $@"Software\Classes\{extension}";
        Registry.CurrentUser.DeleteSubKeyTree(extKeyPath, throwOnMissingSubKey: false);
    }

    /// <summary>
    /// Linux 平台通过 .desktop 文件注册文件扩展名 MIME 类型。
    /// </summary>
    private void RegisterLinuxExtension(string extension)
    {
        var desktopPath = GetDesktopFilePath();
        var mimeType = GetMimeType(extension);

        var content = $""""
            [Desktop Entry]
            Type=Application
            Name={_appName}
            Exec={_appPath} %f
            Terminal=false
            MimeType={mimeType};
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
    /// Linux 平台注销文件扩展名。
    /// </summary>
    private void UnregisterLinuxExtension(string extension)
    {
        var desktopPath = GetDesktopFilePath();
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
    private string GetDesktopFilePath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".local", "share", "applications", $"{_appName}-file-handler.desktop");
    }

    /// <summary>
    /// 根据文件扩展名推断 MIME 类型。
    /// </summary>
    private static string GetMimeType(string extension)
    {
        // 去掉前导点号
        var ext = extension.TrimStart('.');
        return $"application/x-{ext}";
    }
}
