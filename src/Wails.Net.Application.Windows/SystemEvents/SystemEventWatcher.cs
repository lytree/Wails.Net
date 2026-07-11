using System.Net.NetworkInformation;
using Microsoft.Win32;
using Wails.Net.Events;

namespace Wails.Net.Application.Platform;

/// <summary>
/// Windows 系统级事件监听器，负责监听电源模式变化、网络连通性变化等系统事件，
/// 并通过 <see cref="Application.HandlePlatformEvent"/> 转发到应用事件系统。
/// 对应 Wails v3 Go 版本中通过 Win32 消息和系统 API 监听的系统事件。
/// </summary>
internal sealed class SystemEventWatcher : IDisposable
{
    /// <summary>
    /// 用于线程安全控制标志。
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// 是否已注册事件监听。
    /// </summary>
    private bool _registered;

    /// <summary>
    /// 开始监听系统事件。
    /// 注册 <see cref="SystemEvents.PowerModeChanged"/> 和
    /// <see cref="NetworkChange.NetworkAvailabilityChanged"/> 事件。
    /// </summary>
    public void Start()
    {
        if (_registered)
        {
            return;
        }

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        _registered = true;
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

        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        _registered = false;
    }

    /// <summary>
    /// 电源模式变化回调。
    /// 将 <see cref="PowerModes.Suspend"/> 映射为 <see cref="KnownEvents.Suspend"/>，
    /// 将 <see cref="PowerModes.Resume"/> 映射为 <see cref="KnownEvents.Resume"/>。
    /// </summary>
    /// <param name="sender">事件发送者。</param>
    /// <param name="e">电源模式变化事件参数。</param>
    private static void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        var app = Application.Get();
        if (app is null)
        {
            return;
        }

        switch (e.Mode)
        {
            case PowerModes.Suspend:
                app.HandlePlatformEvent((uint)ApplicationEventType.Suspend);
                break;

            case PowerModes.Resume:
                app.HandlePlatformEvent((uint)ApplicationEventType.Resume);
                app.HandlePlatformEvent((uint)ApplicationEventType.BatteryChanged);
                break;

            case PowerModes.StatusChange:
                app.HandlePlatformEvent((uint)ApplicationEventType.BatteryChanged);
                break;
        }
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
