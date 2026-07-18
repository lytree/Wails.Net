using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wails.Net.Application.Logging;
using Wails.Net.Application.Managers;
using Wails.Net.Application.Services;

namespace Wails.Net.Application.Hosting;

/// <summary>
/// <see cref="DesktopApplicationBuilder"/> 的浏览器 console 日志转发扩展（P1-3）。
/// <para>
/// 为 <see cref="DesktopApplicationBuilder"/> 提供 <see cref="UseBrowserConsoleLogForwarder"/>
/// 扩展，自动装配 <see cref="BrowserConsoleLogForwarder"/> 单例，将后端日志（包括
/// <c>ILogger&lt;T&gt;</c> 写入和前端 <c>wails.Log.*</c> 写入）通过 <c>ExecJS</c>
/// 注入到所有已注册窗口的前端 <c>console.log/info/warn/error</c>。
/// </para>
/// <para>
/// 典型用法：
/// <code>
/// var builder = DesktopApplicationBuilder.CreateBuilder(args);
/// builder.UseAutoPlatform()
///        .UseBrowserConsoleLogForwarder();
/// </code>
/// </para>
/// </summary>
public static class BrowserConsoleLogForwarderExtensions
{
    /// <summary>
    /// 启用浏览器 console 日志转发（opt-in）。
    /// <para>
    /// 注册 <see cref="BrowserConsoleLogForwarder"/> 为单例，并注册一个
    /// <see cref="IHostedService"/> 初始化器在应用启动时构造实例（触发 LogHandler 注册）。
    /// </para>
    /// <para>
    /// 调用后，所有 <c>ILogger&lt;T&gt;</c> 写入（已被 <see cref="LogServiceLoggerProvider"/>
    /// 桥接到 <see cref="LogService"/>）将通过 <see cref="BrowserConsoleLogForwarder"/>
    /// 自动转发到所有已注册窗口的前端 console。
    /// </para>
    /// </summary>
    /// <param name="builder">桌面应用构建器。</param>
    /// <returns>当前构建器实例，用于链式调用。</returns>
    public static DesktopApplicationBuilder UseBrowserConsoleLogForwarder(this DesktopApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // 幂等：若已注册则跳过
        if (builder.Services.Any(d => d.ServiceType == typeof(BrowserConsoleLogForwarder)))
        {
            return builder;
        }

        builder.Services.AddSingleton(sp =>
        {
            var logService = sp.GetRequiredService<LogService>();
            var windowManager = sp.GetRequiredService<WindowManager>();
            var diagnosticLogger = sp.GetService<ILogger<BrowserConsoleLogForwarder>>();
            return new BrowserConsoleLogForwarder(logService, windowManager, diagnosticLogger);
        });

        // 注册为 IHostedService 起始器，确保在 Application 启动时构造实例以注册 LogHandler。
        // 使用 hosted service 适配器避免直接依赖 Application 生命周期。
        // 注意：AddHostedService 要求 T 实现 IHostedService，使用 Hosting 命名空间的接口。
        builder.Services.AddHostedService<BrowserConsoleLogForwarderInitializer>();

        return builder;
    }

    /// <summary>
    /// 在 Host 启动时构造 <see cref="BrowserConsoleLogForwarder"/> 单例以注册 LogHandler。
    /// </summary>
    private sealed class BrowserConsoleLogForwarderInitializer : IHostedService
    {
        private readonly BrowserConsoleLogForwarder _forwarder;

        public BrowserConsoleLogForwarderInitializer(BrowserConsoleLogForwarder forwarder)
        {
            _forwarder = forwarder;
        }

        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken)
        {
            // 构造函数已注册 LogHandler，此处仅确保实例被解析。
            _ = _forwarder;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken)
        {
            _forwarder.Dispose();
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 启用浏览器 console 日志接收器（P1-3-4，opt-in）。
    /// <para>
    /// 注册 <see cref="BrowserConsoleLogReceiver"/> 为单例，并注册一个
    /// <see cref="IHostedService"/> 初始化器在应用启动时调用 <see cref="BrowserConsoleLogReceiver.Start"/>
    /// 订阅所有窗口的 console 消息事件。
    /// </para>
    /// <para>
    /// 调用后，前端 JavaScript 调用 <c>console.log/info/warn/error/debug</c> 时，
    /// 消息会通过 <see cref="BrowserConsoleLogReceiver"/> 写入后端 <see cref="LogService"/>。
    /// 与 <see cref="UseBrowserConsoleLogForwarder"/> 配合使用可实现双向桥接，
    /// 通过 <c>source=browser</c> 字段标记防止回环。
    /// </para>
    /// </summary>
    /// <param name="builder">桌面应用构建器。</param>
    /// <returns>当前构建器实例，用于链式调用。</returns>
    public static DesktopApplicationBuilder UseBrowserConsoleLogReceiver(this DesktopApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // 幂等：若已注册则跳过
        if (builder.Services.Any(d => d.ServiceType == typeof(BrowserConsoleLogReceiver)))
        {
            return builder;
        }

        builder.Services.AddSingleton(sp =>
        {
            var logService = sp.GetRequiredService<LogService>();
            var windowManager = sp.GetRequiredService<WindowManager>();
            var diagnosticLogger = sp.GetService<ILogger<BrowserConsoleLogReceiver>>();
            return new BrowserConsoleLogReceiver(logService, windowManager, diagnosticLogger);
        });

        // 注册为 IHostedService 起始器，确保在 Application 启动时调用 Start。
        builder.Services.AddHostedService<BrowserConsoleLogReceiverInitializer>();

        return builder;
    }

    /// <summary>
    /// 在 Host 启动时调用 <see cref="BrowserConsoleLogReceiver.Start"/> 订阅所有窗口的 console 事件。
    /// </summary>
    private sealed class BrowserConsoleLogReceiverInitializer : IHostedService
    {
        private readonly BrowserConsoleLogReceiver _receiver;

        public BrowserConsoleLogReceiverInitializer(BrowserConsoleLogReceiver receiver)
        {
            _receiver = receiver;
        }

        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _receiver.Start();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken)
        {
            _receiver.Dispose();
            return Task.CompletedTask;
        }
    }
}

