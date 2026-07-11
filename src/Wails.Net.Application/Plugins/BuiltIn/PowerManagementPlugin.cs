using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;
using Wails.Net.Events;

namespace Wails.Net.Application.Plugins.BuiltIn;

/// <summary>
/// 电源管理插件，提供系统电源状态事件监听和唤醒锁。
/// 对应 Tauri v2 的 <c>@tauri-apps/plugin-os</c> 电源部分。
/// Windows 通过 SetThreadExecutionState 实现唤醒锁。
/// Suspend/Resume 事件由平台实现通过 Application.HandlePlatformEvent 触发。
/// </summary>
public class PowerManagementPlugin : IPlugin, IDisposable
{
    /// <summary>插件名称</summary>
    public string Name => "power-management";

    private bool _disposed;
    private bool _wakeLockHeld;

    /// <summary>
    /// 当系统即将挂起（进入睡眠/休眠）时触发。
    /// </summary>
    public event Action? Suspend;

    /// <summary>
    /// 当系统从挂起状态恢复时触发。
    /// </summary>
    public event Action? Resume;

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        // 无需注册额外服务
    }

    /// <summary>
    /// 配置插件，注册命令。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        context.Commands.MapCommand("power.requestWakeLock", (Func<bool>)(() => RequestWakeLock()));
        context.Commands.MapCommand("power.releaseWakeLock", (Func<bool>)(() => ReleaseWakeLock()));
        context.Commands.MapCommand("power.isWakeLockHeld", (Func<bool>)(() => _wakeLockHeld));
    }

    /// <summary>
    /// 请求系统保持唤醒（防止挂起）。
    /// Windows 通过 SetThreadExecutionState 实现。
    /// </summary>
    /// <returns>是否成功。</returns>
    public bool RequestWakeLock()
    {
        if (OperatingSystem.IsWindows())
        {
            return RequestWindowsWakeLock();
        }

        return false;
    }

    /// <summary>
    /// 释放保持唤醒锁。
    /// </summary>
    /// <returns>是否成功。</returns>
    public bool ReleaseWakeLock()
    {
        if (OperatingSystem.IsWindows())
        {
            return ReleaseWindowsWakeLock();
        }

        return false;
    }

    /// <summary>
    /// 触发 Suspend 事件，供平台实现调用。
    /// </summary>
    public void OnSuspend()
    {
        Suspend?.Invoke();
        Application.Get()?.HandlePlatformEvent((uint)ApplicationEventType.Suspend);
    }

    /// <summary>
    /// 触发 Resume 事件，供平台实现调用。
    /// </summary>
    public void OnResume()
    {
        Resume?.Invoke();
        Application.Get()?.HandlePlatformEvent((uint)ApplicationEventType.Resume);
    }

    /// <summary>
    /// Windows 平台：通过 SetThreadExecutionState 请求保持唤醒。
    /// ES_CONTINUOUS | ES_SYSTEM_REQUIRED = 0x80000001。
    /// </summary>
    [SupportedOSPlatform("windows")]
    private bool RequestWindowsWakeLock()
    {
        const uint EsContinuous = 0x80000000;
        const uint EsSystemRequired = 0x00000001;
        SetThreadExecutionState(EsContinuous | EsSystemRequired);
        _wakeLockHeld = true;
        return true;
    }

    /// <summary>
    /// Windows 平台：释放保持唤醒锁。
    /// ES_CONTINUOUS = 0x80000000。
    /// </summary>
    [SupportedOSPlatform("windows")]
    private bool ReleaseWindowsWakeLock()
    {
        const uint EsContinuous = 0x80000000;
        SetThreadExecutionState(EsContinuous);
        _wakeLockHeld = false;
        return true;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    [SupportedOSPlatform("windows")]
    private static extern uint SetThreadExecutionState(uint esFlags);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_wakeLockHeld)
        {
            ReleaseWakeLock();
        }

        _disposed = true;
    }
}
