using Microsoft.Extensions.DependencyInjection;

namespace Wails.Net.Application.Plugins;

/// <summary>
/// 插件接口。
/// 借鉴 Tauri v2 的插件设计，提供 ASP.NET Core 风格的服务注册和配置。
/// </summary>
/// <remarks>
/// 生命周期对应关系：
/// <list type="table">
/// <item><term>ConfigureServices</term><description>注册 DI 服务（对应 ASP.NET Core 的 ConfigureServices）</description></item>
/// <item><term>Configure</term><description>配置命令和事件（对应 Tauri v2 的 init/extend_api）</description></item>
/// <item><term>StartupAsync</term><description>应用启动后调用（对应 Wails v3 的 Startup、Tauri v2 的 setup）</description></item>
/// <item><term>ShutdownAsync</term><description>应用关闭时调用（对应 Wails v3 的 Shutdown、Tauri v2 的 on_drop）</description></item>
/// </list>
/// </remarks>
public interface IPlugin
{
    /// <summary>插件名称</summary>
    string Name { get; }

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。
    /// 在 Host 构建前调用，对应 ASP.NET Core 的 <c>ConfigureServices</c>。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    void ConfigureServices(IServiceCollection services);

    /// <summary>
    /// 配置插件（注册命令、事件等）。
    /// 在 Host 构建前调用，对应 Tauri v2 的 <c>init</c>。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    void Configure(IPluginContext context);

    /// <summary>
    /// 应用启动后调用，用于初始化运行时资源（打开连接、启动后台任务等）。
    /// 在 <see cref="Application.Run"/> 中、OnAfterStart 回调之后、平台主循环之前调用。
    /// 对应 Wails v3 Go 版本的 <c>Startup()</c> 方法和 Tauri v2 的 <c>setup()</c> 钩子。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步启动操作的任务。</returns>
    Task StartupAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>
    /// 应用关闭时调用，用于释放资源（关闭连接、停止后台任务、刷新缓存等）。
    /// 在 <see cref="Application.Shutdown"/> 中、关闭任务执行之后、服务逆序关闭之前调用。
    /// 对应 Wails v3 Go 版本的 <c>Shutdown()</c> 方法和 Tauri v2 的 <c>on_drop</c> 钩子。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步关闭操作的任务。</returns>
    Task ShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
