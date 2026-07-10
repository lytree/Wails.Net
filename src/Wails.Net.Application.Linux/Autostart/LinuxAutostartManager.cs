using Wails.Net.Application.Managers;

namespace Wails.Net.Application.Managers;

/// <summary>
/// Linux 自启动管理器实现，通过 XDG autostart .desktop 文件管理开机自启动。
/// 对应 Go 版 autostart_linux.go。
/// </summary>
public sealed class LinuxAutostartManager : IAutostartManager
{
    /// <summary>
    /// 应用名称，用作 .desktop 文件名。
    /// </summary>
    private readonly string _appName;

    /// <summary>
    /// 构造 LinuxAutostartManager 实例。
    /// </summary>
    /// <param name="appName">应用名称，用作 .desktop 文件名。</param>
    public LinuxAutostartManager(string appName)
    {
        _appName = appName;
    }

    /// <summary>
    /// 获取 autostart 目录路径。
    /// 优先使用 XDG_CONFIG_HOME 环境变量，否则回退到 ~/.config/autostart。
    /// </summary>
    /// <returns>autostart 目录路径。</returns>
    private string GetAutostartDirectory()
    {
        var xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var configDir = !string.IsNullOrEmpty(xdgConfigHome)
            ? xdgConfigHome
            : $"{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}/.config";
        return System.IO.Path.Combine(configDir, "autostart");
    }

    /// <summary>
    /// 获取 .desktop 文件完整路径。
    /// </summary>
    /// <returns>.desktop 文件路径。</returns>
    private string GetDesktopFilePath()
    {
        return System.IO.Path.Combine(GetAutostartDirectory(), $"{_appName}.desktop");
    }

    /// <inheritdoc />
    public bool IsEnabled()
    {
        try
        {
            return System.IO.File.Exists(GetDesktopFilePath());
        }
        catch (System.UnauthorizedAccessException)
        {
            return false;
        }
        catch (System.IO.IOException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public void Enable()
    {
        try
        {
            var dir = GetAutostartDirectory();
            // 如果 autostart 目录不存在则创建。
            if (!System.IO.Directory.Exists(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
            }

            var filePath = GetDesktopFilePath();
            var processPath = Environment.ProcessPath ?? string.Empty;

            // 写入标准 XDG autostart .desktop 文件内容。
            var content = $"""
                [Desktop Entry]
                Type=Application
                Name={_appName}
                Exec={processPath}
                Terminal=false
                X-GNOME-Autostart-enabled=true
                """;

            System.IO.File.WriteAllText(filePath, content);
        }
        catch (System.UnauthorizedAccessException)
        {
            // 文件系统访问失败时静默忽略
        }
        catch (System.IO.IOException)
        {
            // 文件系统访问失败时静默忽略
        }
    }

    /// <inheritdoc />
    public void Disable()
    {
        try
        {
            var filePath = GetDesktopFilePath();
            if (System.IO.File.Exists(filePath))
            {
                System.IO.File.Delete(filePath);
            }
        }
        catch (System.UnauthorizedAccessException)
        {
            // 文件系统访问失败时静默忽略
        }
        catch (System.IO.IOException)
        {
            // 文件系统访问失败时静默忽略
        }
    }
}
