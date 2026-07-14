using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wails.Net.Application.Commands;

namespace Wails.Net.Application.Plugins.Mobile;

/// <summary>
/// 生物识别插件，提供指纹/面容认证命令。
/// 对应 Tauri v2 的 <c>@tauri-apps/plugin-biometric</c>。
/// <para>
/// 命令通过 <see cref="IPlatformBiometric"/> 抽象接口委托到平台实现。
/// Server 模式 / 桌面平台 / 无硬件时降级为 <see cref="NullBiometricImpl"/>（CheckAvailability 返回 none，Authenticate 返回 false）。
/// </para>
/// </summary>
public class BiometricPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "biometric";

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。
    /// 注册 <see cref="IPlatformBiometric"/> 的默认降级实现 <see cref="NullBiometricImpl"/>。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IPlatformBiometric, NullBiometricImpl>();
    }

    /// <summary>
    /// 配置插件，注册生物识别相关命令。
    /// 命令名采用 <c>biometric.&lt;action&gt;</c> 格式。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Permissions.RegisterPermissionSet("biometric:default", "生物识别默认权限集",
            "biometric:allow-check-availability", "biometric:allow-authenticate");
        context.Permissions.DeclarePermission("biometric:allow-check-availability", "允许检查生物识别可用性");
        context.Permissions.DeclarePermission("biometric:allow-authenticate", "允许发起生物识别认证");

        var commands = context.Commands;

        // 检查生物识别可用性
        commands.MapCommand("biometric.checkAvailability",
            (Func<ICommandContext, string>)(ctx => ResolveBiometric(ctx).CheckAvailability()));

        // 发起生物识别认证
        commands.MapCommand("biometric.authenticate",
            (Func<ICommandContext, BiometricAuthOptions, Task<bool>>)((ctx, opts) =>
                ResolveBiometric(ctx).AuthenticateAsync(opts.Reason)));
    }

    /// <summary>
    /// 从命令上下文的服务容器解析 <see cref="IPlatformBiometric"/>。
    /// </summary>
    /// <param name="ctx">命令上下文。</param>
    /// <returns>平台生物识别实现实例。</returns>
    private static IPlatformBiometric ResolveBiometric(ICommandContext ctx)
    {
        return ctx.Services.GetService(typeof(IPlatformBiometric)) as IPlatformBiometric
            ?? NullBiometricImpl.Instance;
    }

    /// <summary>
    /// 空实现的生物识别器，作为 Server 模式 / 桌面平台的降级实现。
    /// <see cref="CheckAvailability"/> 返回 <c>none</c>，<see cref="AuthenticateAsync"/> 返回 false。
    /// </summary>
    private sealed class NullBiometricImpl : IPlatformBiometric
    {
        /// <summary>单例实例。</summary>
        public static readonly NullBiometricImpl Instance = new();

        public string CheckAvailability()
        {
            // 降级：无生物识别硬件支持
            return "none";
        }

        public Task<bool> AuthenticateAsync(string reason)
        {
            // 降级：认证失败
            return Task.FromResult(false);
        }
    }
}

/// <summary>biometric.authenticate 命令参数。</summary>
public sealed class BiometricAuthOptions
{
    /// <summary>展示给用户的认证理由文本。</summary>
    public string Reason { get; set; } = string.Empty;
}
