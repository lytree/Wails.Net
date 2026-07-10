using System.Runtime.InteropServices;
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
}
