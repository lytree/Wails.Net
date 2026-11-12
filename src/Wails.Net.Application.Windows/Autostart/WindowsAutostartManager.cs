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
            // 必须用 CreateSubKey 而非 OpenSubKey：全新用户配置文件（CI runner、首次登录的
            // Windows 用户）上 HKCU\...\Run 可能尚不存在，OpenSubKey(writable: true) 此时
            // 返回 null，SetValue 会被静默跳过，导致"启用自启动"实际不生效。
            // CreateSubKey 在键缺失时创建，存在时直接打开，两种情况都返回可写句柄。
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
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
