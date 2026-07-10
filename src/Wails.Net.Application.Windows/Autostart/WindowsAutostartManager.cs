using Microsoft.Win32;
using Wails.Net.Application.Managers;

namespace Wails.Net.Application.Managers;

/// <summary>
/// Windows 自启动管理器实现，通过注册表 HKCU\Software\Microsoft\Windows\CurrentVersion\Run 管理开机自启动。
/// 对应 Go 版 autostart_windows.go。
/// </summary>
public sealed class WindowsAutostartManager : IAutostartManager
{
    /// <summary>
    /// 自启动注册表键路径：HKCU\Software\Microsoft\Windows\CurrentVersion\Run。
    /// </summary>
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// 应用名称，用作注册表值名称。
    /// </summary>
    private readonly string _appName;

    /// <summary>
    /// 构造 WindowsAutostartManager 实例。
    /// </summary>
    /// <param name="appName">应用名称，用作注册表值名称。</param>
    public WindowsAutostartManager(string appName)
    {
        _appName = appName;
    }

    /// <inheritdoc />
    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(_appName) is not null;
        }
        catch (System.Security.SecurityException)
        {
            return false;
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
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.SetValue(_appName, Environment.ProcessPath ?? string.Empty);
        }
        catch (System.Security.SecurityException)
        {
            // 注册表访问失败时静默忽略
        }
        catch (System.UnauthorizedAccessException)
        {
            // 注册表访问失败时静默忽略
        }
        catch (System.IO.IOException)
        {
            // 注册表访问失败时静默忽略
        }
    }

    /// <inheritdoc />
    public void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(_appName, throwOnMissingValue: false);
        }
        catch (System.Security.SecurityException)
        {
            // 注册表访问失败时静默忽略
        }
        catch (System.UnauthorizedAccessException)
        {
            // 注册表访问失败时静默忽略
        }
        catch (System.IO.IOException)
        {
            // 注册表访问失败时静默忽略
        }
    }
}
