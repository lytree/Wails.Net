using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Plugins;
using Wails.Net.Plugins.Updater.Services;

namespace Wails.Net.Plugins.Updater;

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
        ArgumentNullException.ThrowIfNull(context);

        // R3-B 权限声明：所有 updater 命令显式声明 capability。
        context.Permissions.RegisterPermissionSet("updater:default", "updater 默认权限集",
            "updater:allow-default");
        context.Permissions.DeclarePermission("updater:allow-default", "允许调用 updater 插件命令");

        // 声明权限集
        context.Permissions.RegisterPermissionSet("updater:default", "更新器默认权限集",
            "updater:allow-check", "updater:allow-download", "updater:allow-install");
        context.Permissions.DeclarePermission("updater:allow-check", "允许检查应用更新");
        context.Permissions.DeclarePermission("updater:allow-download", "允许下载更新包");
        context.Permissions.DeclarePermission("updater:allow-install", "允许安装更新");

        // 检查更新，返回包含版本和是否可用的 JSON 字符串。
        // 服务未注册或检查失败时抛出异常，由调度器包装为结构化 CallError（§2.5 卫生项：不再返回 "{}" 吞错）。
        context.Commands.MapCommandAsync("updater.check", (Func<ICommandContext, Task<string>>)(async ctx =>
        {
            var service = ctx.Services.GetService<UpdaterService>();
            if (service is null)
            {
                throw new InvalidOperationException("更新器服务未注册");
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
            catch (OperationCanceledException)
            {
                // 取消异常直接重抛，由 MessageProcessor 统一处理为 "调用已被取消"
                throw;
            }
            catch (Exception ex)
            {
                // UpdateURL 未配置或网络请求失败时，抛出异常而非返回空对象
                throw new InvalidOperationException($"更新检查失败: {ex.Message}", ex);
            }
        }), CommandSecurity.Require("updater:allow-default"));

        // 下载更新包，返回下载文件的本地路径。
        // 服务未注册、无可用下载地址或下载失败时抛出异常，由调度器包装为结构化 CallError（§2.5 卫生项：不再返回空字符串吞错）。
        context.Commands.MapCommandAsync("updater.download", (Func<ICommandContext, Task<string>>)(async ctx =>
        {
            var service = ctx.Services.GetService<UpdaterService>();
            if (service is null)
            {
                throw new InvalidOperationException("更新器服务未注册");
            }

            try
            {
                var manifest = await service.CheckForUpdatesAsync(ctx.CancellationToken);
                if (string.IsNullOrWhiteSpace(manifest.DownloadURL))
                {
                    throw new InvalidOperationException("更新清单未提供下载地址");
                }

                return await service.DownloadUpdateAsync(manifest, ctx.CancellationToken);
            }
            catch (OperationCanceledException)
            {
                // 取消异常直接重抛，由 MessageProcessor 统一处理为 "调用已被取消"
                throw;
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"更新下载失败: {ex.Message}", ex);
            }
        }), CommandSecurity.Require("updater:allow-default"));

        // 安装指定路径的更新包。
        // 服务未注册或安装失败时抛出异常，由调度器包装为结构化 CallError（§2.5 卫生项：不再静默忽略吞错）。
        context.Commands.MapCommandAsync("updater.install", (Func<ICommandContext, string, Task>)(async (ctx, archivePath) =>
        {
            var service = ctx.Services.GetService<UpdaterService>();
            if (service is null)
            {
                throw new InvalidOperationException("更新器服务未注册");
            }

            try
            {
                await service.InstallUpdateAsync(archivePath, cancellationToken: ctx.CancellationToken);
            }
            catch (OperationCanceledException)
            {
                // 取消异常直接重抛，由 MessageProcessor 统一处理为 "调用已被取消"
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"更新安装失败: {ex.Message}", ex);
            }
        }), CommandSecurity.Require("updater:allow-default"));

        // 检查并下载更新，返回包含版本、可用性和下载路径的 JSON 字符串。
        // 服务未注册或操作失败时抛出异常，由调度器包装为结构化 CallError（§2.5 卫生项：不再返回 "{}" 吞错）。
        context.Commands.MapCommandAsync("updater.checkAndDownload", (Func<ICommandContext, Task<string>>)(async ctx =>
        {
            var service = ctx.Services.GetService<UpdaterService>();
            if (service is null)
            {
                throw new InvalidOperationException("更新器服务未注册");
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
            catch (OperationCanceledException)
            {
                // 取消异常直接重抛，由 MessageProcessor 统一处理为 "调用已被取消"
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"检查并下载更新失败: {ex.Message}", ex);
            }
        }), CommandSecurity.Require("updater:allow-default"));
    }
}
