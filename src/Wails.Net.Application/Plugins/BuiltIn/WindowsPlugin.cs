using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Plugins.BuiltIn;

/// <summary>
/// 窗口管理器插件，将窗口查询操作以插件命令形式注册。
/// 借鉴 Tauri v2 的 "核心即插件" 哲学：窗口管理是核心能力，
/// 但通过插件命令路径暴露给前端。
/// 对应 Wails v3 前端的 <c>window.wails.windows.*</c> API。
/// <para>
/// 与 <see cref="WindowPlugin"/> 区别：
/// <list type="bullet">
/// <item><see cref="WindowPlugin"/> 操作单个窗口（window.setTitle 等），通过 ICommandContext.WindowId 定位</item>
/// <item><see cref="WindowsPlugin"/> 查询窗口列表（windows.getAll 等），不依赖 WindowId</item>
/// </list>
/// </para>
/// </summary>
public class WindowsPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "windows";

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。此插件无需注册额外服务。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        // 无需注册额外服务，窗口列表通过 Application.Windows 获取
    }

    /// <summary>
    /// 配置插件，注册所有窗口查询命令。
    /// 命令名采用 <c>windows.&lt;action&gt;</c> 格式，与前端 <c>wails.windows.&lt;action&gt;</c> API 一致。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        var commands = context.Commands;

        // === 窗口查询 ===
        commands.MapCommand("windows.getCurrent", (Func<ICommandContext, WindowInfo?>)(ctx =>
        {
            var app = GetAppOrThrow(ctx);
            if (!ctx.WindowId.HasValue)
            {
                return null;
            }

            var window = app.GetWindow(ctx.WindowId.Value);
            return window is null ? null : ToWindowInfo(window);
        }));

        commands.MapCommand("windows.getAll", (Func<ICommandContext, WindowInfo[]>)(ctx =>
        {
            var app = GetAppOrThrow(ctx);
            return app.Windows.Select(ToWindowInfo).ToArray();
        }));

        commands.MapCommand("windows.getByName", (Func<ICommandContext, WindowsByNameOptions, WindowInfo?>)((ctx, opts) =>
        {
            var app = GetAppOrThrow(ctx);
            var window = app.Windows.FirstOrDefault(w => string.Equals(w.Name, opts.Name, StringComparison.OrdinalIgnoreCase));
            return window is null ? null : ToWindowInfo(window);
        }));

        commands.MapCommand("windows.getById", (Func<ICommandContext, WindowsByIdOptions, WindowInfo?>)((ctx, opts) =>
        {
            var app = GetAppOrThrow(ctx);
            var window = app.GetWindow((uint)opts.Id);
            return window is null ? null : ToWindowInfo(window);
        }));

        // === 窗口事件广播 ===
        commands.MapCommand("windows.emit", (Action<ICommandContext, WindowsEmitOptions>)((ctx, opts) =>
        {
            var app = GetAppOrThrow(ctx);

            // 向指定窗口或所有窗口广播事件
            if (opts.TargetWindowId.HasValue)
            {
                var window = app.GetWindow((uint)opts.TargetWindowId.Value);
                if (window is not null)
                {
                    app.Events.Emit($"wails:window:{opts.Name}", opts.Data);
                }
            }
            else
            {
                // 广播到所有窗口
                app.Events.Emit($"wails:window:{opts.Name}", opts.Data);
            }
        }));
    }

    /// <summary>
    /// 从命令上下文中获取全局 <see cref="Application"/> 实例。
    /// </summary>
    /// <param name="ctx">命令上下文。</param>
    /// <returns>全局应用实例。</returns>
    /// <exception cref="InvalidOperationException">当应用未初始化时抛出。</exception>
    private static Application GetAppOrThrow(ICommandContext ctx)
    {
        var app = Application.Get();
        if (app is null)
        {
            throw new InvalidOperationException("应用未初始化，无法执行窗口查询命令");
        }

        return app;
    }

    /// <summary>
    /// 将 <see cref="WebviewWindow"/> 转换为前端可序列化的 <see cref="WindowInfo"/>。
    /// </summary>
    /// <param name="window">窗口实例。</param>
    /// <returns>窗口信息 DTO。</returns>
    private static WindowInfo ToWindowInfo(WebviewWindow window)
    {
        return new WindowInfo
        {
            Id = window.ID,
            Name = window.Name
        };
    }
}

/// <summary>
/// 窗口信息 DTO，用于前端窗口查询命令的返回值。
/// </summary>
public sealed class WindowInfo
{
    /// <summary>窗口 ID。</summary>
    public uint Id { get; set; }

    /// <summary>窗口名称。</summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>windows.getByName 命令参数。</summary>
public sealed class WindowsByNameOptions
{
    /// <summary>窗口名称。</summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>windows.getById 命令参数。</summary>
public sealed class WindowsByIdOptions
{
    /// <summary>窗口 ID。</summary>
    public long Id { get; set; }
}

/// <summary>windows.emit 命令参数。</summary>
public sealed class WindowsEmitOptions
{
    /// <summary>事件名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>事件数据，可为 null。</summary>
    public object? Data { get; set; }

    /// <summary>目标窗口 ID，为 null 时广播到所有窗口。</summary>
    public long? TargetWindowId { get; set; }
}
