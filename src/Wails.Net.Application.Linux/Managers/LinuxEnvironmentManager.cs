using System.Runtime.InteropServices;
using Wails.Net.Application.Managers;

namespace Wails.Net.Application.Managers;

/// <summary>
/// Linux 环境信息管理器实现，提供操作系统名称、架构、主目录和数据目录。
/// 对应 Go 版 environment_linux.go。
/// </summary>
public sealed class LinuxEnvironmentManager : IEnvironmentManager
{
    /// <inheritdoc />
    public string GetOS()
    {
        return "linux";
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
        // 优先使用 .NET 提供的用户主目录路径。
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
        {
            return home;
        }

        // 回退到 /home/{用户名} 路径。
        return $"/home/{Environment.UserName}";
    }

    /// <inheritdoc />
    public string GetDataDir()
    {
        // 优先使用 XDG_DATA_HOME 环境变量（XDG 基本目录规范）。
        var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrEmpty(xdgDataHome))
        {
            return xdgDataHome;
        }

        // 回退到 ~/.local/share 路径（XDG 默认值）。
        return $"{GetHomeDir()}/.local/share";
    }
}
