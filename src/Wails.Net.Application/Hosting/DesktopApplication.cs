using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Manifests;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Security;

namespace Wails.Net.Application.Hosting;

/// <summary>
/// 桌面应用实现，封装 Generic Host。
/// 对应 ASP.NET Core 风格的 Generic Host 模式，将现有 <see cref="Application"/> 适配为托管应用。
/// </summary>
public sealed class DesktopApplication : IDesktopApplication
{
    private readonly IHost _host;
    private readonly Application _application;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>应用名称</summary>
    public string Name { get; }

    /// <summary>DI 服务容器</summary>
    public IServiceProvider Services => _host.Services;

    /// <summary>配置</summary>
    public IConfiguration Configuration => _host.Services.GetRequiredService<IConfiguration>();

    /// <summary>日志工厂</summary>
    public ILoggerFactory LoggerFactory => _host.Services.GetRequiredService<ILoggerFactory>();

    /// <summary>
    /// Host 应用生命周期，用于注册 Started/Stopping/Stopped 回调。
    /// 对应 ASP.NET Core 的 <c>IHostApplicationLifetime</c>。
    /// </summary>
    public IHostApplicationLifetime Lifetime => _host.Services.GetRequiredService<IHostApplicationLifetime>();

    /// <summary>底层 Application 实例（兼容层）</summary>
    public Application Application => _application;

    /// <summary>
    /// 内部构造函数，由 <see cref="DesktopApplicationBuilder.Build"/> 调用。
    /// </summary>
    /// <param name="host">已构建的 Generic Host 实例。</param>
    /// <param name="application">兼容层 Application 实例。</param>
    /// <param name="name">应用名称。</param>
    internal DesktopApplication(IHost host, Application application, string name)
    {
        _host = host;
        _application = application;
        Name = name;
    }

    /// <summary>
    /// 启动应用，阻塞直到应用停止。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>退出码，0 表示正常退出，1 表示发生异常。</returns>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
        try
        {
            await _host.RunAsync(linkedCts.Token).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception ex)
        {
            var logger = _host.Services.GetService<ILogger<DesktopApplication>>();
            logger?.LogError(ex, "应用运行时发生未处理异常");
            return 1;
        }
    }

    /// <summary>
    /// 请求停止应用，取消内部令牌并触发 Application 关闭流程。
    /// </summary>
    public void Stop()
    {
        _cts.Cancel();
        _application.Shutdown();
    }

    /// <summary>
    /// 释放资源，停止应用并释放 Host。
    /// </summary>
    /// <returns>表示异步释放操作的任务。</returns>
    public async ValueTask DisposeAsync()
    {
        _cts.Dispose();
        await ((IAsyncDisposable)_host).DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 生成命令清单（P2-1 收尾：应用侧导出 commands.manifest.json）。
    /// <para>
    /// 从 DI 容器解析 <see cref="CommandRegistry"/>（由 <see cref="DesktopApplicationBuilder.Build"/>
    /// 注册为单例，已包含全部插件注册的命令），经 <see cref="ManifestGenerator"/> 生成
    /// 机器可读命令清单，供 <c>wails generate --commands-manifest</c> 生成前端
    /// TypeScript wrapper（commands.ts），以清单为唯一来源消除前后端 API 漂移。
    /// </para>
    /// </summary>
    /// <returns>命令清单。</returns>
    /// <exception cref="InvalidOperationException">DI 容器中未注册 <see cref="CommandRegistry"/> 时抛出
    /// （正常通过 <see cref="DesktopApplicationBuilder.Build"/> 构建时不会发生）。</exception>
    public CommandsManifest GenerateCommandsManifest()
    {
        var registry = Services.GetRequiredService<CommandRegistry>();
        return ManifestGenerator.GenerateCommandsManifest(registry);
    }

    /// <summary>
    /// 将命令清单写入磁盘（P2-1 收尾）。
    /// 写入 <see cref="ManifestGenerator.ManifestFileName"/>（commands.manifest.json）。
    /// </summary>
    /// <param name="directory">输出目录；不存在时自动创建。</param>
    /// <returns>生成的清单文件完整路径。</returns>
    public string WriteCommandsManifestToDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        Directory.CreateDirectory(directory);
        var manifest = GenerateCommandsManifest();
        var filePath = Path.Combine(directory, ManifestGenerator.ManifestFileName);
        File.WriteAllText(filePath, ManifestGenerator.GenerateCommandsManifestJson(manifest));
        return filePath;
    }

    /// <summary>
    /// 生成插件清单（C-1 审计项：plugins.manifest.json）。
    /// <para>
    /// 从 DI 容器解析 <see cref="PluginManager"/>（已注册全部 <see cref="UsePlugin{T}"/>
    /// 插件实例），经 <see cref="ManifestGenerator"/> 生成机器可读插件清单，
    /// 供 CLI <c>plugin inspect</c>、插件文档命令表与前端 API 一致性校验消费，
    /// 消除"插件包有哪些、注册了哪些命令"的人工核对成本。
    /// </para>
    /// </summary>
    /// <returns>插件清单。</returns>
    /// <exception cref="InvalidOperationException">DI 容器中未注册 <see cref="PluginManager"/> 时抛出
    /// （正常通过 <see cref="DesktopApplicationBuilder.Build"/> 构建时不会发生）。</exception>
    public PluginsManifest GeneratePluginsManifest()
    {
        var pluginManager = Services.GetRequiredService<PluginManager>();
        // 命令与权限集的归属由插件注册作用域记录，这里把注册结果一并传入，
        // 使清单输出每个插件实际注册的 Commands / PermissionSets（SEC-H-003 收尾）。
        var commandRegistry = Services.GetService<CommandRegistry>();
        var permissionSets = Services.GetService<PermissionManager>()?.GetPermissionSets();
        return ManifestGenerator.GeneratePluginsManifest(
            pluginManager.Plugins,
            commandRegistry,
            permissionSets);
    }

    /// <summary>
    /// 将插件清单写入磁盘（C-1 审计项）。
    /// 写入 <c>plugins.manifest.json</c>（<see cref="ManifestGenerator.PluginsManifestFileName"/>）。
    /// </summary>
    /// <param name="directory">输出目录；不存在时自动创建。</param>
    /// <returns>生成的清单文件完整路径。</returns>
    public string WritePluginsManifestToDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        Directory.CreateDirectory(directory);
        var manifest = GeneratePluginsManifest();
        var filePath = Path.Combine(directory, ManifestGenerator.PluginsManifestFileName);
        File.WriteAllText(filePath, ManifestGenerator.GeneratePluginsManifestJson(manifest));
        return filePath;
    }
}
