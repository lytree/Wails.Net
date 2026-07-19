using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Plugins.BuiltIn;

/// <summary>
/// Cookie 管理插件，提供浏览器 Cookie 的读写管理。
/// 对应 Tauri v2 的 Cookie 管理功能。
/// 由于项目尚无 Cookie 管理器抽象，此插件为简化实现：
/// 在内存中维护 Cookie 状态，并通过在 WebView 中执行 JavaScript 与浏览器同步。
/// </summary>
public class CookiePlugin : IPlugin
{
    /// <summary>
    /// 内存中的 Cookie 存储，按名称映射到值。
    /// 使用并发字典保证线程安全。
    /// </summary>
    private static readonly ConcurrentDictionary<string, string> _cookies = new();

    /// <summary>插件名称</summary>
    public string Name => "cookie";

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。此插件无需注册额外服务。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        // 无需注册额外服务
    }

    /// <summary>
    /// 配置插件，注册 Cookie 相关命令。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        context.Commands.MapCommandAsync("cookie.get", (Func<ICommandContext, string, Task<string>>)(async (ctx, url) =>
        {
            await Task.CompletedTask;
            // 从内存存储中返回当前所有 Cookie，序列化为 JSON 对象。
            // url 参数保留用于未来按 URL 过滤的扩展，当前实现忽略。
            var dict = new Dictionary<string, string>(_cookies);
            return JsonSerializer.Serialize(dict);
        }));

        context.Commands.MapCommandAsync("cookie.set", (Func<ICommandContext, string, string, Task<bool>>)(async (ctx, name, value) =>
        {
            var window = GetFirstWindow(ctx);
            if (window is null)
            {
                return false;
            }

            // 内存中保存
            _cookies[name] = value;
            // 同步到 WebView：通过设置 document.cookie 写入浏览器。
            window.ExecJS($"document.cookie = '{name}={value}; path=/'");
            await Task.CompletedTask;
            return true;
        }));

        context.Commands.MapCommandAsync("cookie.delete", (Func<ICommandContext, string, Task<bool>>)(async (ctx, name) =>
        {
            var window = GetFirstWindow(ctx);
            if (window is null)
            {
                return false;
            }

            // 从内存中移除
            _cookies.TryRemove(name, out _);
            // 同步到 WebView：通过设置过期时间为 Unix 纪元删除浏览器中的 Cookie。
            window.ExecJS($"document.cookie = '{name}=; expires=Thu, 01 Jan 1970 00:00:00 UTC; path=/'");
            await Task.CompletedTask;
            return true;
        }));

        context.Commands.MapCommandAsync("cookie.clear", (Func<ICommandContext, Task<bool>>)(async (ctx) =>
        {
            var window = GetFirstWindow(ctx);
            if (window is null)
            {
                return false;
            }

            // 清空内存存储
            _cookies.Clear();
            // 通过执行 JS 清除浏览器中所有 Cookie。
            window.ExecJS("""
                document.cookie.split(';').forEach(function(c) {
                    var eq = c.indexOf('=');
                    var name = eq > -1 ? c.substr(0, eq).trim() : c.trim();
                    document.cookie = name + '=;expires=Thu, 01 Jan 1970 00:00:00 UTC;path=/';
                });
            """);
            await Task.CompletedTask;
            return true;
        }));
    }

    /// <summary>
    /// 从命令上下文中获取第一个可用窗口。
    /// </summary>
    /// <param name="ctx">命令上下文。</param>
    /// <returns>第一个窗口实例，若不存在则返回 null。</returns>
    private static WebviewWindow? GetFirstWindow(ICommandContext ctx)
    {
        var app = ctx.Services.GetService<Application>();
        return app?.WindowManager?.GetAllWindows().FirstOrDefault();
    }
}
