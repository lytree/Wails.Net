using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Plugins.Keychain;

namespace Wails.Net.Plugins.Keychain;

/// <summary>
/// 钥匙串插件 DI 扩展方法。
/// </summary>
public static class KeychainExtensions
{
    /// <summary>
    /// 为应用启用 Keychain 插件并注入平台钥匙串实现。
    /// </summary>
    /// <typeparam name="TKeychain">平台钥匙串实现类型，必须实现 <see cref="IPlatformKeychain"/> 且有无参构造。</typeparam>
    /// <param name="services">DI 服务集合。</param>
    /// <returns>DI 服务集合，以支持链式调用。</returns>
    public static IServiceCollection AddKeychain<TKeychain>(this IServiceCollection services)
        where TKeychain : class, IPlatformKeychain, new()
    {
        // 注册平台钥匙串为单例
        services.AddSingleton<IPlatformKeychain, TKeychain>();

        // 注册 KeychainPlugin 单例，并确保 Configure 时能拿到 IPlatformKeychain 实例
        services.AddSingleton<KeychainPlugin>(sp =>
        {
            var plugin = new KeychainPlugin();
            var keychain = sp.GetService<IPlatformKeychain>();
            plugin.Keychain = keychain;
            return plugin;
        });

        return services;
    }

    /// <summary>
    /// 为应用启用 Keychain 插件（使用指定实例作为平台钥匙串）。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    /// <param name="keychain">平台钥匙串实例。</param>
    /// <returns>DI 服务集合，以支持链式调用。</returns>
    public static IServiceCollection AddKeychain(this IServiceCollection services, IPlatformKeychain keychain)
    {
        ArgumentNullException.ThrowIfNull(keychain);
        services.AddSingleton(keychain);
        services.AddSingleton<KeychainPlugin>(sp =>
        {
            var plugin = new KeychainPlugin();
            plugin.Keychain = sp.GetService<IPlatformKeychain>();
            return plugin;
        });
        return services;
    }
}
