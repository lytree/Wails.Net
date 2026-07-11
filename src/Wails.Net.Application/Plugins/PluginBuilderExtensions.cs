using System.Reflection;
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
    /// </summary>
    /// <param name="builder">桌面应用构建器。</param>
    /// <param name="plugin">插件实例。</param>
    /// <returns>当前构建器实例，用于链式调用。</returns>
    public static DesktopApplicationBuilder UsePlugin(this DesktopApplicationBuilder builder, IPlugin plugin)
    {
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
    /// <param name="assembly">要扫描的程序集，为 null 时使用入口程序集。</param>
    /// <returns>当前构建器实例，用于链式调用。</returns>
    public static DesktopApplicationBuilder UsePluginsFromAssembly(
        this DesktopApplicationBuilder builder,
        Assembly? assembly = null)
    {
        assembly ??= Assembly.GetEntryAssembly();
        if (assembly is null)
        {
            return builder;
        }

        var pluginTypes = assembly.GetTypes()
            .Where(t => typeof(IPlugin).IsAssignableFrom(t)
                && !t.IsAbstract
                && !t.IsInterface
                && t.GetConstructor(Type.EmptyTypes) is not null);

        foreach (var type in pluginTypes)
        {
            var plugin = (IPlugin)Activator.CreateInstance(type)!;
            builder.Services.AddSingleton<IPlugin>(plugin);
        }

        // 确保 PluginManager 已注册为单例
        EnsurePluginManagerRegistered(builder.Services);

        return builder;
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
