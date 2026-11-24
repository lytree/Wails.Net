#if WINDOWS
using System.Runtime.InteropServices;
using Microsoft.Win32;
#endif

namespace Wails.Net.Demo.ResDownloader.Services;

/// <summary>
/// 系统代理（WinINet）开关：把系统代理指向本机 MITM 代理，并在退出时还原。
/// <para>
/// 对应 res-downloader 的“一键设置系统代理”，避免用户手动到网络设置里填
/// <c>127.0.0.1:8899</c>。Windows 通过注册表 + WinINet 刷新实现；其他平台暂不接管。
/// </para>
/// </summary>
public sealed class SystemProxyManager
{
    private readonly object _gate = new();

#if WINDOWS
    // 以下备份字段只在 Windows 分支使用，非 Windows 目标下由 #if 裁掉，避免 CS0169
    private bool _backedUp;
    private int _backupEnabled;
    private string? _backupServer;
    private string? _backupOverride;
#endif

    /// <summary>当前平台是否支持自动设置系统代理。</summary>
    public bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>
    /// 读取系统代理是否已指向本机代理。
    /// </summary>
    /// <param name="port">
    /// 本机代理端口。给定端口时要求 <c>ProxyServer</c> 精确等于 <c>127.0.0.1:port</c>，
    /// 避免把用户本来就有的其它 127.0.0.1 代理（如其它工具占用 127.0.0.1:14562）误判为本应用设置。
    /// </param>
    /// <returns>已指向本机代理返回 <c>true</c>。</returns>
    public bool IsEnabled(int? port = null)
    {
#if WINDOWS
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", writable: false);
            if (key is null)
            {
                return false;
            }

            var enabled = Convert.ToInt32(key.GetValue("ProxyEnable", 0), System.Globalization.CultureInfo.InvariantCulture) != 0;
            var server = key.GetValue("ProxyServer") as string;
            if (!enabled || string.IsNullOrEmpty(server))
            {
                return false;
            }

            return port is > 0
                ? string.Equals(server, $"127.0.0.1:{port}", StringComparison.OrdinalIgnoreCase)
                : server.Contains("127.0.0.1", StringComparison.Ordinal);
        }
        catch (Exception)
        {
            return false;
        }
#else
        _ = port;
        return false;
#endif
    }

    /// <summary>
    /// 启用系统代理，指向 <c>127.0.0.1:port</c>。
    /// </summary>
    /// <param name="port">本机代理端口。</param>
    /// <returns>设置成功返回 <c>true</c>。</returns>
    public bool Enable(int port)
    {
#if WINDOWS
        try
        {
            lock (_gate)
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", writable: true);
                if (key is null)
                {
                    return false;
                }

                if (!_backedUp)
                {
                    var currentServer = key.GetValue("ProxyServer") as string;
                    // 当前系统代理若已经是本应用的地址（上一次未还原、或进程重启），
                    // 不能把它当作「用户原有配置」备份，否则 Disable 会把死代理再写回去。
                    var alreadyOurs = string.Equals(
                        currentServer, $"127.0.0.1:{port}", StringComparison.OrdinalIgnoreCase);

                    _backupEnabled = alreadyOurs
                        ? 0
                        : Convert.ToInt32(
                            key.GetValue("ProxyEnable", 0), System.Globalization.CultureInfo.InvariantCulture);
                    _backupServer = alreadyOurs ? null : currentServer;
                    _backupOverride = alreadyOurs ? null : key.GetValue("ProxyOverride") as string;
                    _backedUp = true;
                }

                key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                key.SetValue("ProxyServer", $"127.0.0.1:{port}", RegistryValueKind.String);
                // 本机地址不走代理，否则 WebView 自身加载本地资源会成环
                key.SetValue("ProxyOverride", "<local>", RegistryValueKind.String);
            }

            NotifyProxyChanged();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
#else
        _ = port;
        return false;
#endif
    }

    /// <summary>
    /// 关闭系统代理并还原启用代理前的配置。
    /// </summary>
    /// <returns>操作成功返回 <c>true</c>。</returns>
    public bool Disable()
    {
#if WINDOWS
        try
        {
            lock (_gate)
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", writable: true);
                if (key is null)
                {
                    return false;
                }

                key.SetValue("ProxyEnable", _backedUp ? _backupEnabled : 0, RegistryValueKind.DWord);

                if (_backedUp && !string.IsNullOrEmpty(_backupServer))
                {
                    key.SetValue("ProxyServer", _backupServer, RegistryValueKind.String);
                }
                else
                {
                    // 没有可还原的配置：清掉本应用写入的地址，避免留下指向已关闭代理的死配置
                    key.DeleteValue("ProxyServer", throwOnMissingValue: false);
                }

                if (_backedUp && !string.IsNullOrEmpty(_backupOverride))
                {
                    key.SetValue("ProxyOverride", _backupOverride, RegistryValueKind.String);
                }
                else if (_backedUp)
                {
                    // 启用前没有 ProxyOverride：清掉本应用写入的「<local>」，保持环境原样
                    key.DeleteValue("ProxyOverride", throwOnMissingValue: false);
                }

                _backedUp = false;
                _backupServer = null;
                _backupOverride = null;
            }

            NotifyProxyChanged();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
#else
        return false;
#endif
    }

#if WINDOWS
    private const int InternetOptionRefresh = 37;
    private const int InternetOptionSettingsChanged = 39;

    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

    private static void NotifyProxyChanged()
    {
        // 只改注册表不会立即生效，必须通知 WinINet 重新加载代理配置
        _ = InternetSetOption(IntPtr.Zero, InternetOptionSettingsChanged, IntPtr.Zero, 0);
        _ = InternetSetOption(IntPtr.Zero, InternetOptionRefresh, IntPtr.Zero, 0);
    }
#endif
}
