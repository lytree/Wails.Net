using System.Diagnostics;
using System.Runtime.InteropServices;
using Wails.Net.Application.SystemEnvironment;
using Wails.Net.Application.Managers;

namespace Wails.Net.Application.Managers;

/// <summary>
/// Windows 环境信息管理器实现，提供操作系统名称、架构、主目录和数据目录。
/// 对应 Go 版 environment_windows.go。
/// </summary>
public sealed class WindowsEnvironmentManager : IEnvironmentManager
{
    /// <inheritdoc />
    public string GetOS()
    {
        return "windows";
    }

    /// <inheritdoc />
    public string GetArch()
    {
        // 根据 CPU 架构返回 Go 风格的架构字符串。
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "amd64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "386",
            Architecture.Arm => "arm",
            _ => "unknown"
        };
    }

    /// <inheritdoc />
    public string GetHomeDir()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    /// <inheritdoc />
    public string GetDataDir()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    }

    /// <inheritdoc />
    public EnvironmentInfo Info()
    {
        var osInfo = BuildOSInfo();
        return new EnvironmentInfo
        {
            OS = GetOS(),
            Arch = GetArch(),
            Debug = System.Diagnostics.Debugger.IsAttached,
            OSInfo = osInfo,
            PlatformInfo = new Dictionary<string, object?>
            {
                ["version"] = osInfo.Version,
                ["build"] = osInfo.Build,
            },
        };
    }

    /// <inheritdoc />
    public bool IsDarkMode()
    {
        // 查询注册表 HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme
        // 0 表示深色模式，1 表示浅色模式。
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int value)
            {
                return value == 0;
            }
        }
        catch
        {
            // 注册表查询失败时回退到浅色模式。
        }

        return false;
    }

    /// <inheritdoc />
    public string GetAccentColor()
    {
        // 查询注册表 HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM\AccentColor
        // 值为 0xAARRGGBB 格式的 DWORD。
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is int value)
            {
                // DWORD 存储为 0xAARRGGBB，提取 RGB 分量（忽略 Alpha）。
                var r = (value >> 16) & 0xFF;
                var g = (value >> 8) & 0xFF;
                var b = value & 0xFF;
                return $"rgb({r},{g},{b})";
            }
        }
        catch
        {
            // 注册表查询失败时回退到默认蓝色。
        }

        return "rgb(0,122,255)";
    }

    /// <inheritdoc />
    public void OpenFileManager(string path, bool selectFile)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        // selectFile 为 true 时使用 /select 参数选中指定文件。
        // 否则直接打开目录。
        var args = selectFile ? $"/select,\"{path}\"" : $"\"{path}\"";
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", args)
            {
                UseShellExecute = true,
            });
        }
        catch
        {
            // 启动 explorer 失败时静默忽略。
        }
    }

    /// <summary>
    /// 构建操作系统详细信息。
    /// 通过注册表查询 Windows 产品名和版本号。
    /// </summary>
    /// <returns>操作系统信息实例。</returns>
    private static OperatingSystemInfo BuildOSInfo()
    {
        var info = new OperatingSystemInfo
        {
            Name = "Windows",
            Version = Environment.OSVersion.Version.ToString(),
            Build = Environment.OSVersion.Version.Build.ToString(),
        };

        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key?.GetValue("ProductName") is string productName)
            {
                info.Name = productName;
            }

            if (key?.GetValue("DisplayVersion") is string displayVersion)
            {
                info.Version = displayVersion;
            }
            else if (key?.GetValue("CurrentVersion") is string currentVersion
                && key.GetValue("CurrentBuild") is string currentBuild)
            {
                info.Version = $"{currentVersion}.{currentBuild}";
            }

            if (key?.GetValue("CurrentBuild") is string build)
            {
                info.Build = build;
            }
        }
        catch
        {
            // 注册表查询失败时保留默认值。
        }

        return info;
    }
}
