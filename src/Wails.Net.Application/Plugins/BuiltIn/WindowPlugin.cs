using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Plugins.BuiltIn;

/// <summary>
/// 窗口插件，将 <see cref="WebviewWindow"/> 的所有原生操作以插件命令形式注册。
/// 借鉴 Tauri v2 的 "核心即插件" 哲学：窗口管理是核心能力，但通过插件命令路径暴露给前端。
/// 对应 Wails v3 前端的 <c>window.wails.window.*</c> API。
/// <para>
/// 命令通过 <see cref="ICommandContext.WindowId"/> 定位目标 <see cref="WebviewWindow"/> 实例，
/// 再委托到 <see cref="WebviewWindow"/> 的公共方法。
/// 支持两种前端调用形式：
/// <list type="bullet">
/// <item><c>wails.window.setTitle('标题')</c> — 走 MessageProcessor.ProcessWindow → CommandDispatcher</item>
/// <item><c>wails.call('window.setTitle', [{ title: '标题' }])</c> — 走 ProcessCallAsync → CommandDispatcher 回退</item>
/// </list>
/// </para>
/// </summary>
public class WindowPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "window";

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。此插件无需注册额外服务。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        // 无需注册额外服务，窗口实例由 Application 管理
    }

    /// <summary>
    /// 配置插件，注册所有窗口操作命令。
    /// 命令名采用 <c>window.&lt;action&gt;</c> 格式，与前端 <c>wails.window.&lt;action&gt;</c> API 一致。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        var commands = context.Commands;

        // === 权限声明（对齐 Tauri v2 window 插件权限模型） ===
        // window:default — 默认权限集，仅包含只读查询能力，能力声明中引用此集合后自动授权只读操作
        // window:allow-readonly — 允许查询窗口状态（尺寸、位置、URL、缩放、可见性、焦点等）
        // window:allow-dangerous — 允许修改窗口属性、执行 JS、注入 CSS、导航、打印、DevTools 等危险操作
        // 说明：权限校验仅在 PermissionManager.Enabled=true 时生效，默认 Enabled=false 保持向后兼容
        context.Permissions.RegisterPermissionSet("window:default", "窗口默认权限集（只读查询）",
            "window:allow-readonly");
        context.Permissions.DeclarePermission("window:allow-readonly", "允许查询窗口状态（尺寸、位置、URL、缩放、可见性、焦点、全屏、最大化、最小化）");
        context.Permissions.DeclarePermission("window:allow-dangerous", "允许修改窗口属性、执行 JS、注入 CSS、导航、打印、DevTools 等危险操作");

        // === 标题与尺寸 ===
        commands.MapCommand("window.setTitle", (Action<ICommandContext, WindowSetTitleOptions>)((ctx, opts) =>
            GetWindowOrThrow(ctx).SetTitle(opts.Title)));

        commands.MapCommand("window.setSize", (Action<ICommandContext, WindowSizeOptions>)((ctx, opts) =>
            GetWindowOrThrow(ctx).SetSize(opts.Width, opts.Height)));

        commands.MapCommand("window.setMinSize", (Action<ICommandContext, WindowSizeOptions>)((ctx, opts) =>
            GetWindowOrThrow(ctx).SetMinSize(opts.Width, opts.Height)));

        commands.MapCommand("window.setMaxSize", (Action<ICommandContext, WindowSizeOptions>)((ctx, opts) =>
            GetWindowOrThrow(ctx).SetMaxSize(opts.Width, opts.Height)));

        commands.MapCommand("window.setPosition", (Action<ICommandContext, WindowPositionOptions>)((ctx, opts) =>
            GetWindowOrThrow(ctx).SetPosition(opts.X, opts.Y)));

        // === 显示/隐藏/状态 ===
        commands.MapCommand("window.close", (Action<ICommandContext>)(ctx => GetWindowOrThrow(ctx).Close()));
        commands.MapCommand("window.minimize", (Action<ICommandContext>)(ctx => GetWindowOrThrow(ctx).Minimise()));
        commands.MapCommand("window.maximize", (Action<ICommandContext>)(ctx => GetWindowOrThrow(ctx).Maximise()));
        commands.MapCommand("window.unminimize", (Action<ICommandContext>)(ctx => GetWindowOrThrow(ctx).UnMinimise()));
        commands.MapCommand("window.unmaximize", (Action<ICommandContext>)(ctx => GetWindowOrThrow(ctx).UnMaximise()));
        commands.MapCommand("window.show", (Action<ICommandContext>)(ctx => GetWindowOrThrow(ctx).Show()));
        commands.MapCommand("window.hide", (Action<ICommandContext>)(ctx => GetWindowOrThrow(ctx).Hide()));
        commands.MapCommand("window.centre", (Action<ICommandContext>)(ctx => GetWindowOrThrow(ctx).Centre()));
        commands.MapCommand("window.restore", (Action<ICommandContext>)(ctx => GetWindowOrThrow(ctx).Restore()));
        commands.MapCommand("window.focus", (Action<ICommandContext>)(ctx => GetWindowOrThrow(ctx).Focus()));

        // === 全屏与置顶 ===
        commands.MapCommand("window.setAlwaysOnTop", (Action<ICommandContext, WindowBoolOptions>)((ctx, opts) =>
            GetWindowOrThrow(ctx).SetAlwaysOnTop(opts.OnTop)));

        commands.MapCommand("window.setFullscreen", (Action<ICommandContext, WindowFullscreenOptions>)((ctx, opts) =>
        {
            var window = GetWindowOrThrow(ctx);
            if (opts.Fullscreen)
            {
                window.Fullscreen();
            }
            else
            {
                window.UnFullscreen();
            }
        }));

        commands.MapCommand("window.unfullscreen", (Action<ICommandContext>)(ctx => GetWindowOrThrow(ctx).UnFullscreen()));

        commands.MapCommand("window.setFrameless", (Action<ICommandContext, WindowFramelessOptions>)((ctx, opts) =>
            GetWindowOrThrow(ctx).SetFrameless(opts.Frameless)));

        // === DevTools ===
        commands.MapCommand("window.openDevTools", (Action<ICommandContext>)(ctx => GetWindowOrThrow(ctx).OpenDevTools()));
        commands.MapCommand("window.closeDevTools", (Action<ICommandContext>)(ctx => GetWindowOrThrow(ctx).CloseDevTools()));

        // === 缩放 ===
        commands.MapCommand("window.setZoom", (Action<ICommandContext, WindowZoomOptions>)((ctx, opts) =>
            GetWindowOrThrow(ctx).SetZoom(opts.Zoom)));

        commands.MapCommand("window.zoomIn", (Action<ICommandContext>)(ctx => GetWindowOrThrow(ctx).ZoomIn()));
        commands.MapCommand("window.zoomOut", (Action<ICommandContext>)(ctx => GetWindowOrThrow(ctx).ZoomOut()));
        commands.MapCommand("window.zoomReset", (Action<ICommandContext>)(ctx => GetWindowOrThrow(ctx).ZoomReset()));

        // === 导航 ===
        commands.MapCommand("window.goBack", (Action<ICommandContext>)(ctx => GetWindowOrThrow(ctx).GoBack()));
        commands.MapCommand("window.goForward", (Action<ICommandContext>)(ctx => GetWindowOrThrow(ctx).GoForward()));
        commands.MapCommand("window.reload", (Action<ICommandContext>)(ctx => GetWindowOrThrow(ctx).Reload()));

        commands.MapCommand("window.setURL", (Action<ICommandContext, WindowSetUrlOptions>)((ctx, opts) =>
            GetWindowOrThrow(ctx).SetURL(opts.Url)));

        commands.MapCommand("window.setHTML", (Action<ICommandContext, WindowSetHtmlOptions>)((ctx, opts) =>
            GetWindowOrThrow(ctx).SetHTML(opts.Html)));

        // === 打印与导出 ===
        commands.MapCommand("window.print", (Action<ICommandContext>)(ctx => GetWindowOrThrow(ctx).Print()));

        commands.MapCommand("window.printToPDF", (Action<ICommandContext, WindowPrintToPdfOptions>)((ctx, opts) =>
            GetWindowOrThrow(ctx).PrintToPDF(opts.Path, opts.Options)));

        // === 执行 JS 与注入 CSS ===
        commands.MapCommand("window.execJS", (Action<ICommandContext, WindowExecJsOptions>)((ctx, opts) =>
            GetWindowOrThrow(ctx).ExecJS(opts.Js)));

        commands.MapCommand("window.injectCSS", (Action<ICommandContext, WindowInjectCssOptions>)((ctx, opts) =>
            GetWindowOrThrow(ctx).InjectCSS(opts.Css)));

        // === 透明度 ===
        commands.MapCommand("window.setOpacity", (Action<ICommandContext, WindowOpacityOptions>)((ctx, opts) =>
            GetWindowOrThrow(ctx).SetOpacity(opts.Opacity)));

        commands.MapCommand("window.getOpacity", (Func<ICommandContext, float>)(ctx =>
            GetWindowOrThrow(ctx).GetOpacity()));

        // === 可调整大小 ===
        commands.MapCommand("window.setResizable", (Action<ICommandContext, WindowResizableOptions>)((ctx, opts) =>
            GetWindowOrThrow(ctx).SetResizable(opts.Resizable)));

        // === 自定义协议 ===
        commands.MapCommand("window.registerCustomScheme", (Action<ICommandContext, WindowSchemeOptions>)((ctx, opts) =>
            GetWindowOrThrow(ctx).RegisterCustomScheme(opts.Scheme)));

        // === 任务栏 ===
        commands.MapCommand("window.setSkipTaskbar", (Action<ICommandContext, WindowSkipOptions>)((ctx, opts) =>
            GetWindowOrThrow(ctx).SetSkipTaskbar(opts.Skip)));

        commands.MapCommand("window.setIgnoreCursorEvents", (Action<ICommandContext, WindowIgnoreOptions>)((ctx, opts) =>
            GetWindowOrThrow(ctx).SetIgnoreCursorEvents(opts.Ignore)));

        commands.MapCommand("window.setBadgeCount", (Action<ICommandContext, WindowBadgeCountOptions>)((ctx, opts) =>
            GetWindowOrThrow(ctx).SetBadgeCount(opts.Count)));

        commands.MapCommand("window.setBadgeLabel", (Action<ICommandContext, WindowBadgeLabelOptions>)((ctx, opts) =>
            GetWindowOrThrow(ctx).SetBadgeLabel(opts.Label)));

        commands.MapCommand("window.setVisibleOnAllWorkspaces", (Action<ICommandContext, WindowVisibleOptions>)((ctx, opts) =>
            GetWindowOrThrow(ctx).SetVisibleOnAllWorkspaces(opts.Visible)));

        commands.MapCommand("window.setBorderColor", (Action<ICommandContext, WindowBorderColorOptions>)((ctx, opts) =>
            GetWindowOrThrow(ctx).SetBorderColor(opts.Color)));

        commands.MapCommand("window.setFileDropEnabled", (Action<ICommandContext, WindowEnabledOptions>)((ctx, opts) =>
            GetWindowOrThrow(ctx).SetFileDropEnabled(opts.Enabled)));

        // === 查询类操作（有返回值） ===
        commands.MapCommand("window.getSize", (Func<ICommandContext, WindowSizeResult>)(ctx =>
        {
            var (width, height) = GetWindowOrThrow(ctx).GetSize();
            return new WindowSizeResult { Width = width, Height = height };
        }));

        commands.MapCommand("window.getPosition", (Func<ICommandContext, WindowPositionResult>)(ctx =>
        {
            var (x, y) = GetWindowOrThrow(ctx).GetPosition();
            return new WindowPositionResult { X = x, Y = y };
        }));

        commands.MapCommand("window.getURL", (Func<ICommandContext, string>)(ctx =>
            GetWindowOrThrow(ctx).GetURL()));

        commands.MapCommand("window.getZoom", (Func<ICommandContext, float>)(ctx =>
            GetWindowOrThrow(ctx).GetZoom()));

        commands.MapCommand("window.isFullscreen", (Func<ICommandContext, bool>)(ctx =>
            GetWindowOrThrow(ctx).IsFullscreen()));

        commands.MapCommand("window.isMaximised", (Func<ICommandContext, bool>)(ctx =>
            GetWindowOrThrow(ctx).IsMaximised()));

        commands.MapCommand("window.isMinimised", (Func<ICommandContext, bool>)(ctx =>
            GetWindowOrThrow(ctx).IsMinimised()));

        commands.MapCommand("window.isVisible", (Func<ICommandContext, bool>)(ctx =>
            GetWindowOrThrow(ctx).IsVisible()));

        commands.MapCommand("window.isFocused", (Func<ICommandContext, bool>)(ctx =>
            GetWindowOrThrow(ctx).IsFocused()));
    }

    /// <summary>
    /// 从命令上下文中获取目标窗口实例。
    /// 通过 <see cref="ICommandContext.WindowId"/> 定位 <see cref="Application"/> 中的窗口。
    /// 若 WindowId 为空或窗口不存在，抛出 <see cref="InvalidOperationException"/>，
    /// 由 <see cref="CommandDispatcher"/> 捕获并返回错误响应。
    /// </summary>
    /// <param name="ctx">命令上下文。</param>
    /// <returns>目标窗口实例。</returns>
    /// <exception cref="InvalidOperationException">当 WindowId 为空或窗口未找到时抛出。</exception>
    private static WebviewWindow GetWindowOrThrow(ICommandContext ctx)
    {
        if (ctx.WindowId is not uint windowId)
        {
            throw new InvalidOperationException("窗口消息未指定目标窗口 ID");
        }

        var app = Application.Get();
        var window = app?.GetWindow(windowId);
        if (window is null)
        {
            throw new InvalidOperationException($"找不到 ID 为 {windowId} 的窗口");
        }

        return window;
    }
}

// === 窗口命令参数 Options 类 ===

/// <summary>window.setTitle 命令参数。</summary>
public sealed class WindowSetTitleOptions
{
    /// <summary>窗口标题。</summary>
    public string Title { get; set; } = string.Empty;
}

/// <summary>window.setSize / setMinSize / setMaxSize 命令参数。</summary>
public sealed class WindowSizeOptions
{
    /// <summary>窗口宽度。</summary>
    public int Width { get; set; }

    /// <summary>窗口高度。</summary>
    public int Height { get; set; }
}

/// <summary>window.setPosition 命令参数。</summary>
public sealed class WindowPositionOptions
{
    /// <summary>X 坐标。</summary>
    public int X { get; set; }

    /// <summary>Y 坐标。</summary>
    public int Y { get; set; }
}

/// <summary>window.setAlwaysOnTop 命令参数。</summary>
public sealed class WindowBoolOptions
{
    /// <summary>是否置顶。</summary>
    public bool OnTop { get; set; }
}

/// <summary>window.setFullscreen 命令参数。</summary>
public sealed class WindowFullscreenOptions
{
    /// <summary>是否进入全屏。</summary>
    public bool Fullscreen { get; set; }
}

/// <summary>window.setFrameless 命令参数。</summary>
public sealed class WindowFramelessOptions
{
    /// <summary>是否无边框。</summary>
    public bool Frameless { get; set; }
}

/// <summary>window.setZoom 命令参数。</summary>
public sealed class WindowZoomOptions
{
    /// <summary>缩放比例。</summary>
    public float Zoom { get; set; }
}

/// <summary>window.setURL 命令参数。</summary>
public sealed class WindowSetUrlOptions
{
    /// <summary>目标 URL。</summary>
    public string Url { get; set; } = string.Empty;
}

/// <summary>window.setHTML 命令参数。</summary>
public sealed class WindowSetHtmlOptions
{
    /// <summary>HTML 内容。</summary>
    public string Html { get; set; } = string.Empty;
}

/// <summary>window.printToPDF 命令参数。</summary>
public sealed class WindowPrintToPdfOptions
{
    /// <summary>输出 PDF 文件路径。</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>PDF 打印选项，可为 null。</summary>
    public PrintToPdfOptions? Options { get; set; }
}

/// <summary>window.execJS 命令参数。</summary>
public sealed class WindowExecJsOptions
{
    /// <summary>要执行的 JavaScript 代码。</summary>
    public string Js { get; set; } = string.Empty;
}

/// <summary>window.injectCSS 命令参数。</summary>
public sealed class WindowInjectCssOptions
{
    /// <summary>要注入的 CSS 代码。</summary>
    public string Css { get; set; } = string.Empty;
}

/// <summary>window.setOpacity 命令参数。</summary>
public sealed class WindowOpacityOptions
{
    /// <summary>透明度（0.0-1.0）。</summary>
    public float Opacity { get; set; }
}

/// <summary>window.setResizable 命令参数。</summary>
public sealed class WindowResizableOptions
{
    /// <summary>是否可调整大小。</summary>
    public bool Resizable { get; set; }
}

/// <summary>window.registerCustomScheme 命令参数。</summary>
public sealed class WindowSchemeOptions
{
    /// <summary>自定义协议名称。</summary>
    public string Scheme { get; set; } = string.Empty;
}

/// <summary>window.setSkipTaskbar 命令参数。</summary>
public sealed class WindowSkipOptions
{
    /// <summary>是否跳过任务栏。</summary>
    public bool Skip { get; set; }
}

/// <summary>window.setIgnoreCursorEvents 命令参数。</summary>
public sealed class WindowIgnoreOptions
{
    /// <summary>是否忽略鼠标事件。</summary>
    public bool Ignore { get; set; }
}

/// <summary>window.setBadgeCount 命令参数。</summary>
public sealed class WindowBadgeCountOptions
{
    /// <summary>角标数字。</summary>
    public int Count { get; set; }
}

/// <summary>window.setBadgeLabel 命令参数。</summary>
public sealed class WindowBadgeLabelOptions
{
    /// <summary>角标文本，可为 null。</summary>
    public string? Label { get; set; }
}

/// <summary>window.setVisibleOnAllWorkspaces 命令参数。</summary>
public sealed class WindowVisibleOptions
{
    /// <summary>是否在所有工作区可见。</summary>
    public bool Visible { get; set; }
}

/// <summary>window.setBorderColor 命令参数。</summary>
public sealed class WindowBorderColorOptions
{
    /// <summary>边框颜色，可为 null。</summary>
    public string? Color { get; set; }
}

/// <summary>window.setFileDropEnabled 命令参数。</summary>
public sealed class WindowEnabledOptions
{
    /// <summary>是否启用文件拖放。</summary>
    public bool Enabled { get; set; }
}

// === 窗口查询结果类 ===

/// <summary>window.getSize 返回结果。</summary>
public sealed class WindowSizeResult
{
    /// <summary>窗口宽度。</summary>
    public int Width { get; set; }

    /// <summary>窗口高度。</summary>
    public int Height { get; set; }
}

/// <summary>window.getPosition 返回结果。</summary>
public sealed class WindowPositionResult
{
    /// <summary>X 坐标。</summary>
    public int X { get; set; }

    /// <summary>Y 坐标。</summary>
    public int Y { get; set; }
}
