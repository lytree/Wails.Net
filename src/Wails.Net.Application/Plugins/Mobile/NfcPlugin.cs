using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wails.Net.Application.Commands;

namespace Wails.Net.Application.Plugins.Mobile;

/// <summary>
/// NFC 插件，提供 NFC 标签读写命令。
/// 对应 Tauri v2 的 <c>@tauri-apps/plugin-nfc</c>。
/// <para>
/// 命令通过 <see cref="IPlatformNfc"/> 抽象接口委托到平台实现。
/// Server 模式 / 桌面平台 / 无设备时降级为 <see cref="NullNfcImpl"/>（Read 返回空字符串，Write 为 no-op）。
/// </para>
/// </summary>
public class NfcPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "nfc";

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。
    /// 注册 <see cref="IPlatformNfc"/> 的默认降级实现 <see cref="NullNfcImpl"/>。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IPlatformNfc, NullNfcImpl>();
    }

    /// <summary>
    /// 配置插件，注册 NFC 相关命令。
    /// 命令名采用 <c>nfc.&lt;action&gt;</c> 格式。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Permissions.RegisterPermissionSet("nfc:default", "NFC 默认权限集",
            "nfc:allow-read", "nfc:allow-write", "nfc:allow-cancel");
        context.Permissions.DeclarePermission("nfc:allow-read", "允许读取 NFC 标签");
        context.Permissions.DeclarePermission("nfc:allow-write", "允许写入 NFC 标签");
        context.Permissions.DeclarePermission("nfc:allow-cancel", "允许取消 NFC 操作");

        var commands = context.Commands;

        // 读取 NFC 标签
        commands.MapCommand("nfc.read",
            (Func<ICommandContext, Task<string>>)(ctx => ResolveNfc(ctx).ReadAsync()));

        // 写入 NFC 标签
        commands.MapCommand("nfc.write",
            (Func<ICommandContext, NfcWriteOptions, Task>)((ctx, opts) => ResolveNfc(ctx).WriteAsync(opts.Data)));

        // 取消 NFC 操作
        commands.MapCommand("nfc.cancel",
            (Action<ICommandContext>)(ctx => ResolveNfc(ctx).Cancel()));
    }

    /// <summary>
    /// 从命令上下文的服务容器解析 <see cref="IPlatformNfc"/>。
    /// </summary>
    /// <param name="ctx">命令上下文。</param>
    /// <returns>平台 NFC 实现实例。</returns>
    private static IPlatformNfc ResolveNfc(ICommandContext ctx)
    {
        return ctx.Services.GetService(typeof(IPlatformNfc)) as IPlatformNfc
            ?? NullNfcImpl.Instance;
    }

    /// <summary>
    /// 空实现的 NFC，作为 Server 模式 / 桌面平台的降级实现。
    /// <see cref="ReadAsync"/> 返回空字符串，<see cref="WriteAsync"/> 和 <see cref="Cancel"/> 为 no-op。
    /// </summary>
    private sealed class NullNfcImpl : IPlatformNfc
    {
        /// <summary>单例实例。</summary>
        public static readonly NullNfcImpl Instance = new();

        public Task<string> ReadAsync()
        {
            return Task.FromResult(string.Empty);
        }

        public Task WriteAsync(string data)
        {
            return Task.CompletedTask;
        }

        public void Cancel()
        {
            // no-op
        }
    }
}

/// <summary>nfc.write 命令参数。</summary>
public sealed class NfcWriteOptions
{
    /// <summary>要写入 NFC 标签的字符串数据。</summary>
    public string Data { get; set; } = string.Empty;
}
