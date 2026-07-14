using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wails.Net.Application.Commands;

namespace Wails.Net.Application.Plugins.Mobile;

/// <summary>
/// 条码扫描插件，提供相机条码扫描命令。
/// 对应 Tauri v2 的 <c>@tauri-apps/plugin-barcode-scanner</c>。
/// <para>
/// 命令通过 <see cref="IPlatformBarcodeScanner"/> 抽象接口委托到平台实现。
/// Server 模式 / 桌面平台 / 无设备时降级为 <see cref="NullBarcodeScannerImpl"/>（返回空字符串）。
/// </para>
/// </summary>
public class BarcodeScannerPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "barcode-scanner";

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。
    /// 注册 <see cref="IPlatformBarcodeScanner"/> 的默认降级实现 <see cref="NullBarcodeScannerImpl"/>。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IPlatformBarcodeScanner, NullBarcodeScannerImpl>();
    }

    /// <summary>
    /// 配置插件，注册条码扫描相关命令。
    /// 命令名采用 <c>barcode-scanner.&lt;action&gt;</c> 格式。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Permissions.RegisterPermissionSet("barcode-scanner:default", "条码扫描默认权限集",
            "barcode-scanner:allow-scan", "barcode-scanner:allow-cancel");
        context.Permissions.DeclarePermission("barcode-scanner:allow-scan", "允许启动条码扫描");
        context.Permissions.DeclarePermission("barcode-scanner:allow-cancel", "允许取消条码扫描");

        var commands = context.Commands;

        // 启动扫描，返回扫描结果
        commands.MapCommand("barcode-scanner.scan",
            (Func<ICommandContext, Task<string>>)(ctx => ResolveScanner(ctx).ScanAsync()));

        // 取消扫描
        commands.MapCommand("barcode-scanner.cancel",
            (Action<ICommandContext>)(ctx => ResolveScanner(ctx).Cancel()));
    }

    /// <summary>
    /// 从命令上下文的服务容器解析 <see cref="IPlatformBarcodeScanner"/>。
    /// </summary>
    /// <param name="ctx">命令上下文。</param>
    /// <returns>平台条码扫描实现实例。</returns>
    private static IPlatformBarcodeScanner ResolveScanner(ICommandContext ctx)
    {
        return ctx.Services.GetService(typeof(IPlatformBarcodeScanner)) as IPlatformBarcodeScanner
            ?? NullBarcodeScannerImpl.Instance;
    }

    /// <summary>
    /// 空实现的条码扫描器，作为 Server 模式 / 桌面平台的降级实现。
    /// <see cref="ScanAsync"/> 返回空字符串，<see cref="Cancel"/> 为 no-op。
    /// </summary>
    private sealed class NullBarcodeScannerImpl : IPlatformBarcodeScanner
    {
        /// <summary>单例实例。</summary>
        public static readonly NullBarcodeScannerImpl Instance = new();

        public Task<string> ScanAsync()
        {
            // 降级：返回空字符串表示无扫描结果
            return Task.FromResult(string.Empty);
        }

        public void Cancel()
        {
            // no-op
        }
    }
}
