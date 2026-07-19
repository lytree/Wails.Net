using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Services;

namespace Wails.Net.Application.Plugins.BuiltIn;

/// <summary>
/// 应用更新插件，提供前端检查更新、下载和安装的命令。
/// 对应 Tauri v2 的 <c>@tauri-apps/plugin-updater</c>。
/// 通过 <see cref="ICommandContext.Services"/> 从 DI 容器获取 <see cref="UpdaterService"/> 实例。
/// </summary>
public class UpdaterPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "updater";

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。此插件无需注册额外服务。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        // 无需注册额外服务
    }

    /// <summary>
    /// 配置插件，注册更新检查、下载和安装相关命令。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        // 声明权限集
        context.Permissions.RegisterPermissionSet("updater:default", "更新器默认权限集",
            "updater:allow-check", "updater:allow-download", "updater:allow-install");
        context.Permissions.DeclarePermission("updater:allow-check", "允许检查应用更新");
        context.Permissions.DeclarePermission("updater:allow-download", "允许下载更新包");
        context.Permissions.DeclarePermission("updater:allow-install", "允许安装更新");

        // 检查更新，返回包含版本和是否可用的 JSON 字符串。
        // 服务未注册或检查失败时返回 "{}"。
        context.Commands.MapCommandAsync("updater.check", (Func<ICommandContext, Task<string>>)(async ctx =>
        {
            var service = ctx.Services.GetService<UpdaterService>();
            if (service is null)
            {
                return "{}";
            }

            try
            {
                var manifest = await service.CheckForUpdatesAsync(ctx.CancellationToken);
                return JsonSerializer.Serialize(new
                {
                    version = manifest.Version,
                    available = !string.IsNullOrEmpty(manifest.Version)
                });
            }
            catch
            {
                // UpdateURL 未配置或网络请求失败时返回空对象
                return "{}";
            }
        }));

        // 下载更新包，返回下载文件的本地路径。
        // 服务未注册、无可用下载地址或下载失败时返回空字符串。
        context.Commands.MapCommandAsync("updater.download", (Func<ICommandContext, Task<string>>)(async ctx =>
        {
            var service = ctx.Services.GetService<UpdaterService>();
            if (service is null)
            {
                return string.Empty;
            }

            try
            {
                var manifest = await service.CheckForUpdatesAsync(ctx.CancellationToken);
                if (string.IsNullOrWhiteSpace(manifest.DownloadURL))
                {
                    return string.Empty;
                }

                return await service.DownloadUpdateAsync(manifest, ctx.CancellationToken);
            }
            catch
            {
                return string.Empty;
            }
        }));

        // 安装指定路径的更新包。
        // 服务未注册或安装失败时静默忽略，避免中断命令调用链。
        context.Commands.MapCommandAsync("updater.install", (Func<ICommandContext, string, Task>)(async (ctx, archivePath) =>
        {
            var service = ctx.Services.GetService<UpdaterService>();
            if (service is null)
            {
                return;
            }

            try
            {
                await service.InstallUpdateAsync(archivePath, cancellationToken: ctx.CancellationToken);
            }
            catch
            {
                // 安装失败时静默忽略
            }
        }));

        // 检查并下载更新，返回包含版本、可用性和下载路径的 JSON 字符串。
        // 服务未注册或操作失败时返回 "{}"。
        context.Commands.MapCommandAsync("updater.checkAndDownload", (Func<ICommandContext, Task<string>>)(async ctx =>
        {
            var service = ctx.Services.GetService<UpdaterService>();
            if (service is null)
            {
                return "{}";
            }

            try
            {
                var manifest = await service.CheckForUpdatesAsync(ctx.CancellationToken);
                string? downloadPath = null;
                if (!string.IsNullOrWhiteSpace(manifest.DownloadURL))
                {
                    downloadPath = await service.DownloadUpdateAsync(manifest, ctx.CancellationToken);
                }

                return JsonSerializer.Serialize(new
                {
                    version = manifest.Version,
                    available = !string.IsNullOrEmpty(manifest.Version),
                    path = downloadPath
                });
            }
            catch
            {
                return "{}";
            }
        }));
    }
}
