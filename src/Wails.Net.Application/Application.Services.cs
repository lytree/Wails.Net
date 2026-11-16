using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Coordinators;
using Wails.Net.Application.Events;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Invocation;
using Wails.Net.Application.Managers;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Screens;
using Wails.Net.Application.Services;
using Wails.Net.Application.Streams;
using Wails.Net.Application.Transport;
using Wails.Net.Application.Windows;
using Wails.Net.Events;

namespace Wails.Net.Application;

/// <summary>
/// Application 的服务注册分部（RH-HOST-02 文件级拆分）。
/// 承载 RegisterService / RegisterBindings / HandleStream（Streams 注册入口）。
/// public API 与拆分前完全一致，仅按关注点拆分文件。
/// </summary>
public partial class Application
{
    /// <summary>
    /// 为命名流注册 handler（Streams 双向字节流）。
    /// 前端按该名称连接，handler 每个连接运行一次；返回即关闭连接。
    /// 对应 Wails v3 Go 版本 <c>App.HandleStream</c>。
    /// </summary>
    /// <param name="name">流名（非空，长度 ≤ 256）。</param>
    /// <param name="handler">连接处理委托。</param>
    public void HandleStream(string name, StreamHandler handler) => Streams.HandleStream(name, handler);

    /// <summary>
    /// 注册服务。同时将服务的公共方法注册到绑定管理器。
    /// </summary>
    /// <param name="service">要注册的服务实例。</param>
    public virtual void RegisterService(object service)
    {
        RegisterService(service, ServiceOptions.Default);
    }

    /// <summary>
    /// 注册服务并附带选项。同时将服务的公共方法注册到绑定管理器。
    /// <para>
    /// 对应 Wails v3 Go 版本 <c>NewServiceWithOptions</c> 函数。
    /// 当 <paramref name="options"/>.<see cref="ServiceOptions.Route"/> 非空且
    /// <paramref name="service"/> 实现 <see cref="Wails.Net.AssetServer.IHttpServiceHandler"/> 时，
    /// 自动将服务挂载到 <see cref="AssetServer"/> 此前缀下。
    /// </para>
    /// <para>
    /// 若 <see cref="AssetServer"/> 尚未注入，路由信息将暂存到待挂载列表，
    /// 待 AssetServer 注入时（通过 setter）自动应用。
    /// </para>
    /// </summary>
    /// <param name="service">要注册的服务实例。</param>
    /// <param name="options">服务选项；为 null 时使用 <see cref="ServiceOptions.Default"/>。</param>
    /// <remarks>
    /// 对应 Wails v3 Go 版本 <c>application.go</c> 中 RegisterService 在 Run 之后报错并丢弃的逻辑。
    /// 本方法采用更友好的策略：若在 <see cref="Run"/> 之后调用，记录警告并丢弃（不抛异常），
    /// 以兼容运行时动态注册场景但提醒开发者服务不会触发 <see cref="IServiceStartup"/>。
    /// </remarks>
    public virtual void RegisterService(object service, ServiceOptions? options)
    {
        ArgumentNullException.ThrowIfNull(service);

        if (_isRunning)
        {
            // 对应 Wails v3 Go 版本 RegisterService 在 Run 之后的行为：
            // 服务在 Run 之后注册不会触发 ServiceStartup，将导致服务未初始化。
            // 此处记录警告并丢弃，避免破坏运行时动态注册场景的兼容性。
            _logger?.LogWarning(
                "在应用运行后注册服务被丢弃：类型={ServiceType}。服务不会触发 IServiceStartup，请改为在 Run 之前注册。",
                service.GetType().FullName);
            return;
        }

        var opts = options ?? ServiceOptions.Default;

        _serviceRegistry.Register(service, opts);
        Bindings.RegisterInstance(service);

        // 若服务实现 IHttpServiceHandler 且配置了 Route，挂载到 AssetServer
        if (service is Wails.Net.AssetServer.IHttpServiceHandler handler &&
            !string.IsNullOrWhiteSpace(opts.Route))
        {
            if (_assetServer is not null)
            {
                _assetServer.MountServiceRoute(opts.Route!, handler);
            }
            else
            {
                // AssetServer 尚未注入，暂存待后续 setter 触发时应用
                _serviceRoutes.Add(opts.Route!, handler);
            }
        }
    }

    /// <summary>
    /// 从 DI 容器获取指定类型的服务实例，并注册到绑定管理器。
    /// 对应 ASP.NET Core 风格的 DI 集成，避免双重注册（DI + RegisterService）。
    ///
    /// 使用示例：
    /// <code>
    /// builder.Services.AddSingleton&lt;GreetingService&gt;();
    /// // ...
    /// var app = builder.Build().Application;
    /// app.RegisterBindings&lt;GreetingService&gt;();  // 从 DI 获取实例
    /// </code>
    /// </summary>
    /// <typeparam name="T">服务类型，必须已注册到 DI 容器。</typeparam>
    /// <returns>从 DI 容器获取的服务实例。</returns>
    /// <exception cref="InvalidOperationException">
    /// 当 <see cref="InitializeFromServiceProvider"/> 未被调用或 DI 容器中未注册 <typeparamref name="T"/> 时抛出。
    /// </exception>
    public virtual T RegisterBindings<T>() where T : class
    {
        if (_serviceProvider is null)
        {
            throw new InvalidOperationException(
                "Application 尚未从 DI 容器初始化。请先调用 DesktopApplicationBuilder.Build()。");
        }

        var service = _serviceProvider.GetRequiredService<T>();
        Bindings.RegisterInstance(service);
        return service;
    }

}
