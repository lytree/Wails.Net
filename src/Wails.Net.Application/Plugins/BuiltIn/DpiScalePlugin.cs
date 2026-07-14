using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Plugins.BuiltIn;

/// <summary>
/// DPI 缩放插件，提供屏幕缩放因子与 WebView 缩放控制命令。
/// 对应 Tauri v2 的 <c>plugin-dpi-scale</c>，允许前端查询和调整内容缩放比例。
/// <para>
/// 命令通过 <see cref="ICommandContext.WindowId"/> 定位目标 <see cref="WebviewWindow"/> 实例，
/// 再委托到 <see cref="WebviewWindow"/> 的 <c>GetZoomLevel</c> / <c>SetZoomLevel</c> 方法。
/// </para>
/// </summary>
public class DpiScalePlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "dpi-scale";

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。此插件无需注册额外服务。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        // 无需注册额外服务
    }

    /// <summary>
    /// 配置插件，注册 DPI 缩放相关命令。
    /// 命令名采用 <c>dpi-scale.&lt;action&gt;</c> 格式。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // 声明权限集
        context.Permissions.RegisterPermissionSet("dpi-scale:default", "DPI 缩放默认权限集",
            "dpi-scale:allow-get", "dpi-scale:allow-set");
        context.Permissions.DeclarePermission("dpi-scale:allow-get", "允许查询缩放因子");
        context.Permissions.DeclarePermission("dpi-scale:allow-set", "允许设置缩放因子");

        var commands = context.Commands;

        // 查询当前缩放因子
        commands.MapCommand("dpi-scale.getScaleFactor", (Func<ICommandContext, float>)(ctx =>
            GetWindowOrThrow(ctx).GetZoomLevel()));

        // 设置缩放因子
        commands.MapCommand("dpi-scale.setZoomFactor", (Action<ICommandContext, DpiScaleZoomOptions>)((ctx, opts) =>
            GetWindowOrThrow(ctx).SetZoomLevel(opts.Zoom)));

        // 重置缩放为默认值 1.0
        commands.MapCommand("dpi-scale.reset", (Action<ICommandContext>)(ctx =>
            GetWindowOrThrow(ctx).SetZoomLevel(1.0f)));
    }

    /// <summary>
    /// 从命令上下文中获取目标窗口实例。
    /// 复用 <see cref="WindowPlugin"/> 的窗口定位模式：通过 <see cref="ICommandContext.WindowId"/> 查找。
    /// </summary>
    /// <param name="ctx">命令上下文。</param>
    /// <returns>目标窗口实例。</returns>
    /// <exception cref="InvalidOperationException">当未指定窗口 ID 或窗口不存在时抛出。</exception>
    private static WebviewWindow GetWindowOrThrow(ICommandContext ctx)
    {
        if (ctx.WindowId is not uint windowId)
        {
            throw new InvalidOperationException("DPI 缩放命令未指定目标窗口 ID");
        }

        var app = Application.Get();
        var window = app?.GetWindow(windowId);
        if (window is null)
        {
            throw new InvalidOperationException($"未找到 ID 为 {windowId} 的窗口");
        }

        return window;
    }
}

/// <summary>dpi-scale.setZoomFactor 命令参数。</summary>
public sealed class DpiScaleZoomOptions
{
    /// <summary>缩放因子（1.0 为 100%）。</summary>
    public float Zoom { get; set; }
}
