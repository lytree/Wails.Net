using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wails.Net.Application.Commands;

namespace Wails.Net.Application.Plugins.Mobile;

/// <summary>
/// 地理定位插件，提供当前位置获取和持续位置监听命令。
/// 对应 Tauri v2 的 <c>@tauri-apps/plugin-geolocation</c>。
/// <para>
/// 命令通过 <see cref="IPlatformGeolocation"/> 抽象接口委托到平台实现。
/// Server 模式 / 桌面平台 / 无硬件时降级为 <see cref="NullGeolocationImpl"/>（CheckAvailability 返回 none，定位返回 null）。
/// </para>
/// </summary>
public class GeolocationPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "geolocation";

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。
    /// 注册 <see cref="IPlatformGeolocation"/> 的默认降级实现 <see cref="NullGeolocationImpl"/>。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IPlatformGeolocation, NullGeolocationImpl>();
    }

    /// <summary>
    /// 配置插件，注册地理定位相关命令。
    /// 命令名采用 <c>geolocation.&lt;action&gt;</c> 格式。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Permissions.RegisterPermissionSet("geolocation:default", "地理定位默认权限集",
            "geolocation:allow-check-availability", "geolocation:allow-get-position", "geolocation:allow-watch-position");
        context.Permissions.DeclarePermission("geolocation:allow-check-availability", "允许检查地理定位可用性");
        context.Permissions.DeclarePermission("geolocation:allow-get-position", "允许获取当前位置");
        context.Permissions.DeclarePermission("geolocation:allow-watch-position", "允许持续监听位置变化");

        var commands = context.Commands;

        // 检查地理定位可用性
        commands.MapCommand("geolocation.checkAvailability",
            (Func<ICommandContext, string>)(ctx => ResolveGeolocation(ctx).CheckAvailability()));

        // 获取当前位置（单次定位）
        // CancellationToken 由 ICommandContext 提供，不作为 JSON 业务参数暴露给前端（遵循 AGENTS.md §3.4.6）
        commands.MapCommand("geolocation.getCurrentPosition",
            (Func<ICommandContext, GeolocationOptions?, Task<GeolocationPosition?>>)((ctx, opts) =>
                ResolveGeolocation(ctx).GetCurrentPositionAsync(opts ?? new GeolocationOptions(), ctx.CancellationToken)));

        // 开始持续监听位置变化
        commands.MapCommand("geolocation.watchPosition",
            (Func<ICommandContext, GeolocationOptions?, Task<WatchPositionResult>>)(async (ctx, opts) =>
            {
                var impl = ResolveGeolocation(ctx);
                // watchPosition 的回调通过事件系统通知前端，此处仅启动监听
                var watchId = await impl.WatchPositionAsync(opts ?? new GeolocationOptions(),
                    position => OnPositionUpdate(ctx, position), ctx.CancellationToken);
                return new WatchPositionResult { WatchId = watchId };
            }));

        // 取消位置监听
        commands.MapCommand("geolocation.clearWatch",
            (Action<ICommandContext, WatchPositionResult>)((ctx, result) =>
                ResolveGeolocation(ctx).ClearWatch(result.WatchId)));
    }

    /// <summary>
    /// 位置更新回调，通过事件系统通知前端。
    /// </summary>
    /// <param name="ctx">命令上下文。</param>
    /// <param name="position">位置信息。</param>
    private static void OnPositionUpdate(ICommandContext ctx, GeolocationPosition position)
    {
        // 通过事件总线广播位置更新事件
        // 前端可通过 EventProcessor.On("geolocation:position", ...) 订阅
        // 此处仅占位，实际事件发射由 Application.EventProcessor 在插件启动后注入
        _ = ctx;
        _ = position;
    }

    /// <summary>
    /// 从命令上下文的服务容器解析 <see cref="IPlatformGeolocation"/>。
    /// </summary>
    /// <param name="ctx">命令上下文。</param>
    /// <returns>平台地理定位实现实例。</returns>
    private static IPlatformGeolocation ResolveGeolocation(ICommandContext ctx)
    {
        return ctx.Services.GetService(typeof(IPlatformGeolocation)) as IPlatformGeolocation
            ?? NullGeolocationImpl.Instance;
    }

    /// <summary>
    /// 空实现的地理定位器，作为 Server 模式 / 桌面平台的降级实现。
    /// <see cref="CheckAvailability"/> 返回 <c>none</c>，定位返回 null / 0。
    /// </summary>
    private sealed class NullGeolocationImpl : IPlatformGeolocation
    {
        /// <summary>单例实例。</summary>
        public static readonly NullGeolocationImpl Instance = new();

        public string CheckAvailability()
        {
            // 降级：无地理定位硬件支持
            return "none";
        }

        public Task<GeolocationPosition?> GetCurrentPositionAsync(GeolocationOptions options, CancellationToken cancellationToken)
        {
            // 降级：无法获取位置
            return Task.FromResult<GeolocationPosition?>(null);
        }

        public Task<int> WatchPositionAsync(GeolocationOptions options, Action<GeolocationPosition> callback, CancellationToken cancellationToken)
        {
            // 降级：无法监听位置
            return Task.FromResult(0);
        }

        public void ClearWatch(int watchId)
        {
            // 降级：无操作
        }
    }
}
