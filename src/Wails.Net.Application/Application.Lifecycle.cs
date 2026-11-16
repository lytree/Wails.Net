using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Coordinators;
using Wails.Net.Application.Events;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Invocation;
using Wails.Net.Application.Managers;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Screens;
using Wails.Net.Application.Services;
using Wails.Net.Application.Streams;
using Wails.Net.Application.Transport;
using Wails.Net.Application.Windows;
using Wails.Net.Events;

namespace Wails.Net.Application;

/// <summary>
/// Application 的生命周期信号分部（RH-HOST-02 文件级拆分）。
/// 承载 PlatformStarted 就绪信号（TCS）、<see cref="Hosting.ApplicationLifecycleState"/> 推进
/// 与等待 / 失败标记 API。public API 与拆分前完全一致，仅按关注点拆分文件。
/// </summary>
public partial class Application
{
    /// <summary>
    /// 平台启动就绪信号（OPT-RUNTIME-002，2026-08-15 新增）。
    /// <see cref="Run"/> 在进入平台主循环前 SetResult，<see cref="WaitUntilPlatformStartedAsync"/>
    /// 与 <see cref="Hosting.DesktopHostedService.StartAsync"/> 等待该信号。
    /// 平台主循环抛异常时 SetException，使等待方快速失败。
    /// </summary>
    private readonly TaskCompletionSource _platformStartedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// 当前生命周期状态（OPT-RUNTIME-002）。
    /// 由 <see cref="Run"/> / <see cref="Shutdown"/> 推进，供诊断与测试等待。
    /// </summary>
    public Hosting.ApplicationLifecycleState LifecycleState { get; private set; } = Hosting.ApplicationLifecycleState.Created;

    /// <summary>
    /// 等待平台启动就绪（OPT-RUNTIME-002，2026-08-15 新增）。
    /// <para>
    /// 等待 <see cref="LifecycleState"/> 至少到达 <see cref="Hosting.ApplicationLifecycleState.PlatformStarted"/>
    /// （<see cref="Run"/> 已完成服务/资源服务器/传输层启动，即将进入平台主循环）。
    /// 供 <see cref="Hosting.DesktopHostedService.StartAsync"/>、测试与初始化逻辑等待明确就绪阶段，
    /// 避免依赖 sleep 或误用 Host lifecycle 返回语义。
    /// </para>
    /// </summary>
    /// <param name="timeout">等待超时；null 表示无限等待。默认 30 秒。</param>
    /// <returns>就绪返回 true；超时返回 false；平台主循环异常会抛出该异常。</returns>
    public async Task<bool> WaitUntilPlatformStartedAsync(TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(30);

        // 已就绪则立即返回
        if (LifecycleState is Hosting.ApplicationLifecycleState.PlatformStarted or
            Hosting.ApplicationLifecycleState.Running)
        {
            return true;
        }

        // 未运行的应用（Run 从未被调用）快速返回
        if (!_isRunning && LifecycleState is Hosting.ApplicationLifecycleState.Created)
        {
            return false;
        }

        try
        {
            await _platformStartedTcs.Task.WaitAsync(timeout.Value);
            return true;
        }
        catch (TimeoutException)
        {
            _logger?.LogWarning("等待平台启动就绪超时（{Timeout}），继续启动流程", timeout.Value);
            return false;
        }
    }

    /// <summary>
    /// 标记平台启动失败（OPT-RUNTIME-002）。
    /// 由 <see cref="Hosting.DesktopHostedService"/> 在 <see cref="Run"/> 抛出异常时调用，
    /// 使 <see cref="WaitUntilPlatformStartedAsync"/> 的等待快速失败（而非等待超时）。
    /// </summary>
    /// <param name="exception">启动异常。</param>
    internal void MarkPlatformStartFailed(Exception exception)
    {
        _platformStartedTcs.TrySetException(exception);
    }

}
