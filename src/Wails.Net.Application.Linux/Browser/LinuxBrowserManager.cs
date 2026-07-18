using System.Diagnostics;
using System.Runtime.Versioning;
using Wails.Net.Application.Browser;
using Wails.Net.Application.Managers;

namespace Wails.Net.Application.Browser;

/// <summary>
/// Linux 浏览器管理器实现。
/// 对应 Wails v3 Go 版本 internal/browser 包 Linux 实现。
/// 通过 <c>xdg-open</c> 命令打开 URL，符合 XDG Desktop Entry Specification。
/// GNOME 环境优先回退到 <c>gnome-open</c>，KDE 环境回退到 <c>kde-open</c>，
/// 最后回退到 <c>x-www-browser</c> symlink。
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxBrowserManager : IBrowserManager
{
    /// <summary>
    /// xdg-open 命令查找顺序：依次尝试每个命令，找到第一个可执行者使用。
    /// 与 Go 版本 internal/browser/browser_linux.go 的查找逻辑保持一致。
    /// </summary>
    private static readonly string[] OpenCommands =
    [
        "xdg-open",
        "gio open",
        "gnome-open",
        "kde-open",
        "x-www-browser",
    ];

    /// <inheritdoc />
    public void OpenURL(string url) => TryOpenWithXdg(url);

    /// <inheritdoc />
    public void OpenURLInDefaultBrowser(string url) => TryOpenWithXdg(url);

    /// <summary>
    /// 依次尝试预定义的 open 命令，找到第一个成功启动者即返回。
    /// 所有命令失败时静默忽略（与 Go 版本一致）。
    /// </summary>
    /// <param name="url">待验证并打开的 URL。</param>
    private static void TryOpenWithXdg(string url)
    {
        if (!BrowserUrlValidator.TryValidate(url, out var sanitized))
        {
            return;
        }

        foreach (var cmd in OpenCommands)
        {
            if (TryLaunch(cmd, sanitized))
            {
                return;
            }
        }
    }

    /// <summary>
    /// 启动指定命令打开 URL。
    /// 命令以空格分隔成可执行名与参数，例如 <c>gio open</c> 拆分为 <c>gio</c> + <c>open</c>。
    /// </summary>
    /// <param name="command">命令字符串。</param>
    /// <param name="url">已验证的 URL。</param>
    /// <returns>是否成功启动进程。</returns>
    private static bool TryLaunch(string command, string url)
    {
        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = parts[0],
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            for (var i = 1; i < parts.Length; i++)
            {
                psi.ArgumentList.Add(parts[i]);
            }
            psi.ArgumentList.Add(url);

            using var proc = Process.Start(psi);
            // 启动后立即返回，不等待退出
            return proc is not null;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
