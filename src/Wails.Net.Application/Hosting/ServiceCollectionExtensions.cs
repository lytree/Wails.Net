using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Events;
using Wails.Net.Application.Managers;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Services;

namespace Wails.Net.Application.Hosting;

/// <summary>
/// 服务集合扩展方法，注册 Wails.Net 核心服务和管理器到 DI 容器。
/// 对应 AGENTS.md §1.1.1 技术选型：DI 统一使用 <c>Microsoft.Extensions.DependencyInjection</c>。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 添加 Wails.Net 核心管理器（EventProcessor、BindingManager、WindowManager、DialogManager、ScreenManager）。
    /// 管理器注册为单例生命周期。
    /// 平台依赖的管理器通过工厂方法创建，若 <see cref="IPlatformApp"/> 未注册则传入 null（Server 模式）。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>当前服务集合，用于链式调用。</returns>
    public static IServiceCollection AddWailsManagers(this IServiceCollection services)
    {
        // 核心管理器（无平台依赖，可独立创建）
        services.AddSingleton<EventProcessor>();
        services.AddSingleton<BindingManager>();

        // 平台依赖的管理器，通过工厂方法获取 IPlatformApp（可能为 null，Server 模式下）
        services.AddSingleton<WindowManager>(sp => new WindowManager(sp.GetService<IPlatformApp>()));
        services.AddSingleton<DialogManager>(sp => new DialogManager(sp.GetService<IPlatformApp>()));
        services.AddSingleton<ScreenManager>(sp => new ScreenManager(sp.GetService<IPlatformApp>()));

        // 注册管理器接口映射，使依赖接口的服务也能解析
        services.AddSingleton<IWindowManager>(sp => sp.GetRequiredService<WindowManager>());
        services.AddSingleton<IDialogManager>(sp => sp.GetRequiredService<DialogManager>());
        services.AddSingleton<IScreenManager>(sp => sp.GetRequiredService<ScreenManager>());

        return services;
    }

    /// <summary>
    /// 添加 Wails.Net 内置服务（FileServer、KvStore、Log、Notification、Sqlite、Updater）。
    /// 服务注册为单例生命周期，均使用无参构造函数创建默认实例。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>当前服务集合，用于链式调用。</returns>
    public static IServiceCollection AddWailsServices(this IServiceCollection services)
    {
        services.AddSingleton<FileServerService>();
        services.AddSingleton<KvStoreService>();
        services.AddSingleton<LogService>();
        services.AddSingleton<NotificationService>();
        services.AddSingleton<SqliteService>();
        services.AddSingleton<UpdaterService>();

        return services;
    }

    /// <summary>
    /// 添加所有 Wails.Net 核心服务和管理器。
    /// 等效于依次调用 <see cref="AddWailsManagers"/> 和 <see cref="AddWailsServices"/>。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>当前服务集合，用于链式调用。</returns>
    public static IServiceCollection AddWailsCore(this IServiceCollection services)
    {
        return services
            .AddWailsManagers()
            .AddWailsServices();
    }

    /// <summary>
    /// 添加 Wails.Net 全部服务到 DI 容器，一站式入口。
    /// 等效于 <see cref="AddWailsCore"/>，并提供未来扩展点（默认日志提供程序、
    /// 生命周期钩子、配置默认值等）。
    ///
    /// 对应 ASP.NET Core 的 <c>AddMvc</c> / <c>AddRazorPages</c> 风格入口，
    /// 是用户在 <c>Program.cs</c> 中首选的注册方法。
    ///
    /// 使用示例：
    /// <code>
    /// var builder = DesktopApplicationBuilder.CreateBuilder(args);
    /// builder.Services.AddWails();
    /// </code>
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>当前服务集合，用于链式调用。</returns>
    public static IServiceCollection AddWails(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddWailsCore();
    }
}

