using System.Diagnostics;
using System.Runtime.Versioning;
using Wails.Net.Application.Browser;
using Wails.Net.Application.Managers;

namespace Wails.Net.Application.Browser;

/// <summary>
/// Windows 浏览器管理器实现。
/// 对应 Wails v3 Go 版本 internal/browser 包 Windows 实现。
/// 通过 <see cref="Process.Start(ProcessStartInfo)"/> 配合 <c>UseShellExecute=true</c> 调用 ShellExecuteW，
/// 以系统默认浏览器打开 URL。<see cref="OpenURL"/> 与 <see cref="OpenURLInDefaultBrowser"/> 行为一致：
/// Windows 平台上 ShellExecute 始终使用默认浏览器，无独立"应用内浏览器"概念。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsBrowserManager : IBrowserManager
{
    /// <inheritdoc />
    public void OpenURL(string url)
    {
        if (!BrowserUrlValidator.TryValidate(url, out var sanitized))
        {
            return;
        }

        LaunchShellExecute(sanitized);
    }

    /// <inheritdoc />
    public void OpenURLInDefaultBrowser(string url)
    {
        if (!BrowserUrlValidator.TryValidate(url, out var sanitized))
        {
            return;
        }

        LaunchShellExecute(sanitized);
    }

    /// <summary>
    /// 通过 ShellExecuteW 以默认浏览器打开 URL。
    /// 使用 <see cref="ProcessStartInfo.UseShellExecute"/> = true 触发 Shell 行为，
    /// 等价于 Go 版本调用 <c>exec.Command("cmd", "/c", "start", url)</c>。
    /// </summary>
    /// <param name="sanitizedUrl">已验证的 URL。</param>
    private static void LaunchShellExecute(string sanitizedUrl)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = sanitizedUrl,
                UseShellExecute = true,
            };
            using var proc = Process.Start(psi);
            // 不 await 退出，浏览器启动后立即返回
        }
        catch (InvalidOperationException)
        {
            // UseShellExecute 在某些 shell 关联缺失时抛出，静默忽略
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // ShellExecuteW 失败（如无默认浏览器关联），静默忽略
        }
        catch (PlatformNotSupportedException)
        {
            // 在非 Windows 平台调用时（理论不应发生），静默忽略
        }
    }
}
