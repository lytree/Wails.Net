using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wails.Net.Application.Commands;

namespace Wails.Net.Application.Plugins.Mobile;

/// <summary>
/// 震动反馈插件，提供设备震动控制命令。
/// 对应 Tauri v2 的 <c>@tauri-apps/plugin-haptics</c>。
/// <para>
/// 命令通过 <see cref="IPlatformHaptics"/> 抽象接口委托到平台实现。
/// Server 模式 / 桌面平台 / 无设备时降级为 <see cref="NullHapticsImpl"/>（no-op）。
/// </para>
/// </summary>
public class HapticsPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "haptics";

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。
    /// 注册 <see cref="IPlatformHaptics"/> 的默认降级实现 <see cref="NullHapticsImpl"/>。
    /// 平台项目可通过替换注册提供真实实现。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IPlatformHaptics, NullHapticsImpl>();
    }

    /// <summary>
    /// 配置插件，注册震动反馈相关命令。
    /// 命令名采用 <c>haptics.&lt;action&gt;</c> 格式。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Permissions.RegisterPermissionSet("haptics:default", "震动反馈默认权限集",
            "haptics:allow-vibrate", "haptics:allow-cancel", "haptics:allow-notify");
        context.Permissions.DeclarePermission("haptics:allow-vibrate", "允许触发震动");
        context.Permissions.DeclarePermission("haptics:allow-cancel", "允许取消震动");
        context.Permissions.DeclarePermission("haptics:allow-notify", "允许触发通知震动");

        var commands = context.Commands;

        // 触发震动
        commands.MapCommand("haptics.vibrate",
            (Action<ICommandContext, HapticsVibrateOptions>)((ctx, opts) =>
                ResolveHaptics(ctx).Vibrate(opts.Duration)));

        // 取消震动
        commands.MapCommand("haptics.cancel",
            (Action<ICommandContext>)(ctx => ResolveHaptics(ctx).Cancel()));

        // 触发通知震动
        commands.MapCommand("haptics.notification",
            (Action<ICommandContext, HapticsNotificationOptions>)((ctx, opts) =>
                ResolveHaptics(ctx).Notify(opts.Type)));
    }

    /// <summary>
    /// 从命令上下文的服务容器解析 <see cref="IPlatformHaptics"/>。
    /// 若未注册则返回 <see cref="NullHapticsImpl"/> 单例，保证命令不会因缺失平台实现而抛异常。
    /// </summary>
    /// <param name="ctx">命令上下文。</param>
    /// <returns>平台震动实现实例。</returns>
    private static IPlatformHaptics ResolveHaptics(ICommandContext ctx)
    {
        return ctx.Services.GetService(typeof(IPlatformHaptics)) as IPlatformHaptics
            ?? NullHapticsImpl.Instance;
    }

    /// <summary>
    /// 空实现的震动反馈，作为 Server 模式 / 桌面平台的降级实现。
    /// 所有方法均为 no-op，不执行任何操作。
    /// </summary>
    private sealed class NullHapticsImpl : IPlatformHaptics
    {
        /// <summary>单例实例，避免重复分配。</summary>
        public static readonly NullHapticsImpl Instance = new();

        public void Vibrate(int durationMs)
        {
            // no-op：桌面/Server 模式下不支持震动
        }

        public void Cancel()
        {
            // no-op
        }

        public void Notify(NotificationType type)
        {
            // no-op
        }
    }
}

/// <summary>haptics.vibrate 命令参数。</summary>
public sealed class HapticsVibrateOptions
{
    /// <summary>震动持续时间（毫秒）。</summary>
    public int Duration { get; set; }
}

/// <summary>haptics.notification 命令参数。</summary>
public sealed class HapticsNotificationOptions
{
    /// <summary>通知类型（success/warning/error）。</summary>
    public NotificationType Type { get; set; }
}
