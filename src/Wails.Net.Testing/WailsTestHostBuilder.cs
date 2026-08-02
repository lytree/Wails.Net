using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Managers;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Plugins;
using Wails.Net.Testing.Platform;
using Wails.Net.Testing.Recording;

namespace Wails.Net.Testing;

/// <summary>
/// <see cref="WailsTestHost"/> 的 Fluent 构建器，封装 <see cref="DesktopApplicationBuilder"/>。
/// <para>
/// 设计要点（避免全局副作用，契合 CI 并行安全）：
/// <list type="bullet">
/// <item>不修改 <c>WAILS_PLATFORM</c> 等进程级环境变量，而是通过 DI 直接注入 <see cref="MockPlatformApp"/> 作为 <c>IPlatformApp</c>；</item>
/// <item>每个宿主持有独立的 <see cref="CallRecorder"/>，跨平台与剪贴板共享，避免多宿主调用记录互相污染；</item>
/// <item>构建后无需调用 <see cref="Application.Run"/> 即可通过 <see cref="WailsTestHost.InvokeAsync{T}"/> 驱动完整 IPC 管线。</item>
/// </list>
/// </para>
/// </summary>
public sealed class WailsTestHostBuilder
{
    private readonly DesktopApplicationBuilder _inner;
    private ApplicationOptions? _options;
    private bool _clipboardEnabled = true;

    /// <summary>
    /// 基于已有的 <see cref="DesktopApplicationBuilder"/> 创建测试宿主构建器。
    /// </summary>
    /// <param name="builder">底层桌面应用构建器。</param>
    public static WailsTestHostBuilder Create(DesktopApplicationBuilder builder)
        => new(builder);

    /// <summary>
    /// 使用默认选项创建测试宿主构建器（自动注入 Mock 平台）。
    /// </summary>
    /// <param name="appName">应用名称，用于平台标识与断言。</param>
    public static WailsTestHostBuilder Create(string appName = "Wails.Net.Test")
        => new(DesktopApplicationBuilder.CreateBuilder())
        {
            _options = new ApplicationOptions { Name = appName }
        };

    private WailsTestHostBuilder(DesktopApplicationBuilder inner)
    {
        _inner = inner;
    }

    /// <summary>
    /// 配置应用选项（<see cref="ApplicationOptions"/>）。
    /// </summary>
    /// <param name="configure">配置回调。</param>
    /// <returns>当前构建器实例。</returns>
    public WailsTestHostBuilder ConfigureOptions(Action<ApplicationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _options ??= new ApplicationOptions { Name = "Wails.Net.Test" };
        configure(_options);
        return this;
    }

    /// <summary>
    /// 配置 DI 服务集合。
    /// </summary>
    /// <param name="configure">配置回调。</param>
    /// <returns>当前构建器实例。</returns>
    public WailsTestHostBuilder ConfigureServices(Action<IServiceCollection> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_inner.Services);
        return this;
    }

    /// <summary>
    /// 注册一个插件类型（由 DI 解析，需有无参构造函数或已注册依赖）。
    /// 插件在 <see cref="Build"/> 时经 <see cref="DesktopApplicationBuilder"/> 初始化（调用其 <c>Configure</c> 注册命令）。
    /// </summary>
    /// <typeparam name="TPlugin">插件类型，必须有无参构造函数。</typeparam>
    /// <returns>当前构建器实例。</returns>
    public WailsTestHostBuilder UsePlugin<TPlugin>() where TPlugin : class, IPlugin, new()
    {
        _inner.UsePlugin<TPlugin>();
        return this;
    }

    /// <summary>
    /// 注册一个插件实例。
    /// </summary>
    /// <param name="plugin">插件实例。</param>
    /// <returns>当前构建器实例。</returns>
    public WailsTestHostBuilder UsePlugin(IPlugin plugin)
    {
        _inner.UsePlugin(plugin);
        return this;
    }

    /// <summary>
    /// 是否将 Mock 剪贴板接线为 <c>IClipboardManager</c> 注入到 <see cref="Application.ClipboardManager"/>。
    /// 默认 true；设为 false 时 <see cref="WailsTestHost.Clipboard"/> 为 null（剪贴板相关契约不验证）。
    /// </summary>
    /// <param name="enable">是否启用剪贴板接线。</param>
    /// <returns>当前构建器实例。</returns>
    public WailsTestHostBuilder EnableClipboard(bool enable = true)
    {
        _clipboardEnabled = enable;
        return this;
    }

    /// <summary>
    /// 构建测试宿主。
    /// <para>
    /// 流程：覆盖 <see cref="ApplicationOptions"/>（若已配置）→ 通过 DI 注入
    /// <see cref="MockPlatformApp"/> 为 <c>IPlatformApp</c> →（可选）注入 Mock 剪贴板 →
    /// 调用 <see cref="DesktopApplicationBuilder.Build"/> 完成完整 DI 初始化
    /// （平台应用注入、<see cref="Application"/> 从 DI 初始化、<see cref="CommandDispatcher"/> 注入）。
    /// </para>
    /// </summary>
    /// <returns>已构建、可直接驱动 IPC 的 <see cref="WailsTestHost"/>。</returns>
    public WailsTestHost Build()
    {
        if (_options is not null)
        {
            // 覆盖 DesktopApplicationBuilder 默认的 ApplicationOptions 工厂注册，
            // 使应用名称等选项可由测试控制。
            _inner.Services.AddSingleton(_options);
        }

        var recorder = new CallRecorder();

        // 通过 DI 直接注入 Mock 平台应用，避免修改全局 WAILS_PLATFORM 环境变量。
        _inner.Services.AddSingleton<IPlatformApp>(sp =>
            new MockPlatformApp(sp.GetRequiredService<ApplicationOptions>(), recorder));

        MockClipboard? clipboard = null;
        if (_clipboardEnabled)
        {
            clipboard = new MockClipboard(recorder);
            _inner.Services.AddSingleton<IClipboardManager>(clipboard);
        }

        var desktopApp = _inner.Build();
        var mockPlatform = desktopApp.Application.PlatformApp as MockPlatformApp
            ?? throw new InvalidOperationException(
                "未能从构建结果获取 MockPlatformApp，Mock 平台未生效。请确认通过 WailsTestHostBuilder 构建。");

        return new WailsTestHost(desktopApp, mockPlatform, clipboard, recorder);
    }
}
