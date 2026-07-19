using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Hosting;

namespace Wails.Net.Application.Plugins;

/// <summary>
/// Builder 扩展方法，提供 UsePlugin Fluent API。
/// </summary>
public static class PluginBuilderExtensions
{
    /// <summary>
    /// 使用指定插件类型（使用无参构造函数创建实例）。
    /// 立即调用插件的 <see cref="IPlugin.ConfigureServices"/> 注册 DI 服务，
    /// 并将插件添加到构建器跟踪列表，在 <see cref="DesktopApplicationBuilder.Build"/> 时统一调用 <see cref="IPlugin.Configure"/>。
    /// </summary>
    /// <typeparam name="TPlugin">插件类型，必须有无参构造函数。</typeparam>
    /// <param name="builder">桌面应用构建器。</param>
    /// <returns>当前构建器实例，用于链式调用。</returns>
    public static DesktopApplicationBuilder UsePlugin<TPlugin>(this DesktopApplicationBuilder builder)
        where TPlugin : class, IPlugin, new()
    {
        var plugin = new TPlugin();
        return UsePlugin(builder, plugin);
    }

    /// <summary>
    /// 使用插件实例。将插件注册到 DI 容器，并确保 PluginManager 已注册。
    /// 立即调用插件的 <see cref="IPlugin.ConfigureServices"/> 注册 DI 服务，
    /// 并将插件添加到构建器跟踪列表，在 <see cref="DesktopApplicationBuilder.Build"/> 时统一调用 <see cref="IPlugin.Configure"/>。
    /// </summary>
    /// <param name="builder">桌面应用构建器。</param>
    /// <param name="plugin">插件实例。</param>
    /// <returns>当前构建器实例，用于链式调用。</returns>
    public static DesktopApplicationBuilder UsePlugin(this DesktopApplicationBuilder builder, IPlugin plugin)
    {
        // 立即调用 ConfigureServices，在 Host 构建之前注册 DI 服务
        plugin.ConfigureServices(builder.Services);

        // 添加到构建器跟踪列表，在 Build() 时调用 Configure()
        builder.AddPlugin(plugin);

        // 注册插件实例到 DI 容器（作为 IPlugin 服务）
        builder.Services.AddSingleton<IPlugin>(plugin);

        // 确保 PluginManager 已注册为单例（避免重复注册）
        EnsurePluginManagerRegistered(builder.Services);

        return builder;
    }

    /// <summary>
    /// 从程序集自动发现插件并注册到 DI 容器。
    /// </summary>
    /// <param name="builder">桌面应用构建器。</param>
    /// <param name="assembly">已弃用参数，仅用于向后兼容签名。</param>
    /// <returns>当前构建器实例。</returns>
    /// <remarks>
    /// 此方法已弃用并禁止使用。原实现依赖运行时反射扫描程序集类型（<c>Assembly.GetTypes</c>）+
    /// <c>Activator.CreateInstance</c> 创建实例，违反 AGENTS.md §3.4 "禁止使用反射" 的禁令。
    /// 请改用 <see cref="UsePlugin{TPlugin}"/> 显式注册插件类型。
    /// </remarks>
    /// <exception cref="NotSupportedException">始终抛出。原反射实现已移除。</exception>
    [Obsolete("此方法已弃用并禁止使用。原反射实现已移除（遵循 AGENTS.md §3.4）。请使用 UsePlugin<TPlugin>() 显式注册插件。")]
    public static DesktopApplicationBuilder UsePluginsFromAssembly(
        this DesktopApplicationBuilder builder,
        object? assembly = null)
    {
        throw new NotSupportedException(
            "UsePluginsFromAssembly 已弃用并禁止使用。原反射实现已移除（遵循 AGENTS.md §3.4）。" +
            "请使用 UsePlugin<TPlugin>() 显式注册插件类型。");
    }

    /// <summary>
    /// 确保 PluginManager 已注册为单例，避免重复注册。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    private static void EnsurePluginManagerRegistered(IServiceCollection services)
    {
        if (!services.Any(d => d.ServiceType == typeof(PluginManager)))
        {
            services.AddSingleton<PluginManager>();
        }
    }
}
