using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;

namespace Wails.Net.Application.Plugins.BuiltIn;

/// <summary>
/// 剪贴板插件，提供剪贴板读写命令。
/// 通过 <see cref="ICommandContext.Services"/> 从 DI 容器获取 <see cref="Application"/> 实例，
/// 再访问其 <see cref="Application.ClipboardManager"/> 属性。
/// </summary>
public class ClipboardPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "clipboard";

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。此插件无需注册额外服务。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        // 无需注册额外服务
    }

    /// <summary>
    /// 配置插件，注册剪贴板相关命令。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        // 声明权限集
        context.Permissions.RegisterPermissionSet("clipboard:default", "剪贴板默认权限集",
            "clipboard:allow-read", "clipboard:allow-write");
        context.Permissions.DeclarePermission("clipboard:allow-read", "允许读取剪贴板内容");
        context.Permissions.DeclarePermission("clipboard:allow-write", "允许写入剪贴板内容");

        context.Commands.MapCommand("clipboard.getText", (Func<ICommandContext, string>)(ctx =>
        {
            var app = ctx.Services.GetService<Application>();
            return app?.ClipboardManager?.GetText() ?? string.Empty;
        }));

        context.Commands.MapCommand("clipboard.setText", (Action<ICommandContext, string>)((ctx, text) =>
        {
            var app = ctx.Services.GetService<Application>();
            app?.ClipboardManager?.SetText(text);
        }));

        context.Commands.MapCommand("clipboard.getHTML", (Func<ICommandContext, string>)(ctx =>
            ctx.Services.GetService<Application>()?.ClipboardManager?.GetHTML() ?? string.Empty));

        context.Commands.MapCommand("clipboard.setHTML", (Action<ICommandContext, string, string>)((ctx, html, fallbackText) =>
        {
            var app = ctx.Services.GetService<Application>();
            app?.ClipboardManager?.SetHTML(html, fallbackText);
        }));

        context.Commands.MapCommand("clipboard.getImage", (Func<ICommandContext, byte[]?>)(ctx =>
            ctx.Services.GetService<Application>()?.ClipboardManager?.GetImage()));

        context.Commands.MapCommand("clipboard.setImage", (Action<ICommandContext, byte[]>)((ctx, imageData) =>
        {
            var app = ctx.Services.GetService<Application>();
            app?.ClipboardManager?.SetImage(imageData);
        }));

        context.Commands.MapCommand("clipboard.clear", (Action<ICommandContext>)(ctx =>
        {
            var app = ctx.Services.GetService<Application>();
            app?.ClipboardManager?.Clear();
        }));
    }
}
