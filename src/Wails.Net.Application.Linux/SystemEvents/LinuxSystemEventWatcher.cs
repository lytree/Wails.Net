using System.Net.NetworkInformation;
using Gio;
using Wails.Net.Events;

namespace Wails.Net.Application.Platform;

/// <summary>
/// Linux 系统级事件监听器，负责监听网络连通性变化、主题变化等系统事件，
/// 并通过 <see cref="Application.HandlePlatformEvent"/> 转发到应用事件系统。
/// 对应 Wails v3 Go 版本中通过 D-Bus 和系统 API 监听的系统事件。
/// </summary>
/// <remarks>
/// 监听通道：
/// <list type="bullet">
/// <item><see cref="NetworkChange.NetworkAvailabilityChanged"/>：.NET 内置跨平台网络事件（间接走 netlink）。</item>
/// <item><see cref="Settings.Changed"/> 信号：通过 GirCore 监听 GSettings D-Bus 信号，
/// 捕获 GNOME 桌面主题/强调色变化。对应 Wails v3 Go 版本 <c>application_linux_dbus.go</c>
/// 中通过 D-Bus 监听 <c>org.gnome.desktop.interface</c> 信号。</item>
/// </list>
/// </remarks>
internal sealed class LinuxSystemEventWatcher : IDisposable
{
    /// <summary>
    /// gsettings schema：org.gnome.desktop.interface，包含主题与强调色设置。
    /// </summary>
    private const string GnomeInterfaceSchema = "org.gnome.desktop.interface";

    /// <summary>
    /// 用于线程安全控制标志。
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// 是否已注册事件监听。
    /// </summary>
    private bool _registered;

    /// <summary>
    /// GNOME 桌面设置 GSettings 实例，用于监听主题变化信号。
    /// </summary>
    private Settings? _gnomeSettings;

    /// <summary>
    /// 开始监听系统事件。
    /// 注册 <see cref="NetworkChange.NetworkAvailabilityChanged"/> 事件，
    /// 并通过 GSettings D-Bus 监听 GNOME 主题变化。
    /// </summary>
    public void Start()
    {
        if (_registered)
        {
            return;
        }

        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        TryStartGnomeSettingsListener();
        _registered = true;
    }

    /// <summary>
    /// 尝试通过 GSettings D-Bus 监听 GNOME 桌面主题变化。
    /// 对应 Wails v3 Go 版本中通过 D-Bus 监听 <c>gsettings changed</c> 信号。
    /// </summary>
    private void TryStartGnomeSettingsListener()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        try
        {
            _gnomeSettings = Settings.New(GnomeInterfaceSchema);
            if (_gnomeSettings is null)
            {
                return;
            }

            _gnomeSettings.OnChanged += OnGnomeSettingsChanged;
        }
        catch
        {
            // GSettings 不可用（非 GNOME 桌面或 schema 未安装）时静默忽略
            _gnomeSettings = null;
        }
    }

    /// <summary>
    /// GSettings 信号回调：键值变化时根据 key 名称映射到对应应用事件。
    /// </summary>
    /// <param name="sender">事件发送者。</param>
    /// <param name="e">GSettings 变化事件参数，包含变更的键名。</param>
    private static void OnGnomeSettingsChanged(Settings sender, Settings.ChangedSignalArgs e)
    {
        try
        {
            var key = e.Key;
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            // 主题相关 key 变化时触发 ThemeChanged 事件
            if (key is "color-scheme" or "gtk-theme")
            {
                Application.Get()?.Events.Emit(KnownEvents.ThemeChanged, null, null);
            }
        }
        catch
        {
            // 信号处理中的异常不应中断 D-Bus 监听
        }
    }

    /// <summary>
    /// 停止监听系统事件。
    /// </summary>
    public void Stop()
    {
        if (!_registered)
        {
            return;
        }

        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;

        if (_gnomeSettings is not null)
        {
            try
            {
                _gnomeSettings.OnChanged -= OnGnomeSettingsChanged;
            }
            catch
            {
                // 注销信号失败时忽略
            }

            _gnomeSettings.Dispose();
            _gnomeSettings = null;
        }

        _registered = false;
    }

    /// <summary>
    /// 网络连通性变化回调。
    /// 将网络可用性变化映射为 <see cref="KnownEvents.NetworkChanged"/> 事件，
    /// 并携带当前网络可用状态数据。
    /// </summary>
    /// <param name="sender">事件发送者。</param>
    /// <param name="e">网络可用性变化事件参数。</param>
    private static void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        Application.Get()?.HandlePlatformEvent(
            (uint)ApplicationEventType.NetworkChanged, e.IsAvailable);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
    }
}
