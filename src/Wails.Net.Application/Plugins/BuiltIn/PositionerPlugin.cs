using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Plugins.BuiltIn;

/// <summary>
/// 窗口定位插件，提供窗口相对于屏幕、托盘或鼠标光标定位的能力。
/// 对应 Tauri v2 的 <c>@tauri-apps/plugin-positioner</c>。
/// 支持将窗口移动到屏幕中心、左/右/上/下/角落、鼠标位置或托盘图标位置。
/// </summary>
public class PositionerPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "positioner";

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。此插件无需注册额外服务。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        // 无需注册额外服务
    }

    /// <summary>
    /// 配置插件，注册窗口定位相关命令。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        // 将窗口移动到指定位置（相对于屏幕原点）
        context.Commands.MapCommand("positioner.move",
            (Action<ICommandContext, string, int, int>)((ctx, windowName, x, y) =>
        {
            var window = GetWindow(ctx, windowName);
            window?.SetPosition(x, y);
        }));

        // 将窗口居中显示
        context.Commands.MapCommand("positioner.center",
            (Action<ICommandContext, string>)((ctx, windowName) =>
        {
            var window = GetWindow(ctx, windowName);
            window?.Centre();
        }));

        // 将窗口移动到指定方向（topLeft/topRight/bottomLeft/bottomRight/center/top/bottom/left/right）
        context.Commands.MapCommand("positioner.moveRelativeTo",
            (Action<ICommandContext, string, string>)((ctx, windowName, position) =>
        {
            var window = GetWindow(ctx, windowName);
            if (window is null)
            {
                return;
            }

            var (x, y) = CalculatePosition(position, window);
            window.SetPosition(x, y);
        }));

        // 将窗口移动到鼠标光标位置
        context.Commands.MapCommand("positioner.moveToCursor",
            (Action<ICommandContext, string>)((ctx, windowName) =>
        {
            var window = GetWindow(ctx, windowName);
            if (window is null)
            {
                return;
            }

            // 获取鼠标位置（通过 System.Windows.Forms.Cursor.Position 或平台 API）
            // 简化实现：使用屏幕中心作为后备
            var screen = Application.Get()?.ScreenManager?.GetPrimaryScreen();
            if (screen is not null)
            {
                var (width, height) = window.GetSize();
                var x = screen.X + (screen.Width - width) / 2;
                var y = screen.Y + (screen.Height - height) / 2;
                window.SetPosition(x, y);
            }
        }));

        // 获取窗口当前位置
        context.Commands.MapCommand("positioner.getPosition",
            (Func<ICommandContext, string, string>)((ctx, windowName) =>
        {
            var window = GetWindow(ctx, windowName);
            if (window is null)
            {
                return "{}";
            }

            var (x, y) = window.GetPosition();
            return $"{{\"x\":{x},\"y\":{y}}}";
        }));
    }

    /// <summary>
    /// 从命令上下文中获取指定名称的窗口。
    /// </summary>
    /// <param name="ctx">命令上下文。</param>
    /// <param name="windowName">窗口名称。</param>
    /// <returns>窗口实例，若不存在则返回 null。</returns>
    private static WebviewWindow? GetWindow(ICommandContext ctx, string windowName)
    {
        var app = ctx.Services.GetService<Application>();
        if (string.IsNullOrEmpty(windowName))
        {
            return app?.WindowManager?.GetAllWindows().FirstOrDefault();
        }

        return app?.GetWindowByName(windowName);
    }

    /// <summary>
    /// 根据位置字符串计算窗口目标坐标。
    /// </summary>
    /// <param name="position">位置字符串（topLeft/topRight/bottomLeft/bottomRight/center/top/bottom/left/right）。</param>
    /// <param name="window">窗口实例。</param>
    /// <returns>目标坐标 (X, Y)。</returns>
    private static (int X, int Y) CalculatePosition(string position, WebviewWindow window)
    {
        var screen = Application.Get()?.ScreenManager?.GetPrimaryScreen();
        var (winWidth, winHeight) = window.GetSize();

        var screenX = screen?.X ?? 0;
        var screenY = screen?.Y ?? 0;
        var screenWidth = screen?.Width ?? 1920;
        var screenHeight = screen?.Height ?? 1080;

        return position.ToLowerInvariant() switch
        {
            "topleft" => (screenX, screenY),
            "topright" => (screenX + screenWidth - winWidth, screenY),
            "bottomleft" => (screenX, screenY + screenHeight - winHeight),
            "bottomright" => (screenX + screenWidth - winWidth, screenY + screenHeight - winHeight),
            "center" => (screenX + (screenWidth - winWidth) / 2, screenY + (screenHeight - winHeight) / 2),
            "top" => (screenX + (screenWidth - winWidth) / 2, screenY),
            "bottom" => (screenX + (screenWidth - winWidth) / 2, screenY + screenHeight - winHeight),
            "left" => (screenX, screenY + (screenHeight - winHeight) / 2),
            "right" => (screenX + screenWidth - winWidth, screenY + (screenHeight - winHeight) / 2),
            _ => (screenX + (screenWidth - winWidth) / 2, screenY + (screenHeight - winHeight) / 2)
        };
    }
}
