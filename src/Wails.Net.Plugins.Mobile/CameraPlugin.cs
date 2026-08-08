using Wails.Net.Application.Plugins;
using Wails.Net.Application.Plugins.Mobile;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wails.Net.Application.Commands;

namespace Wails.Net.Plugins.Mobile;

/// <summary>
/// 相机插件，提供拍照与可用性检查命令。
/// 对应 Tauri v2 的 <c>@tauri-apps/plugin-camera</c>。
/// <para>
/// 命令通过 <see cref="IPlatformCamera"/> 抽象接口委托到平台实现。
/// Server 模式 / 桌面平台 / 无相机硬件时降级为 <see cref="NullCameraImpl"/>（CheckAvailability 返回 none，Capture 返回空数组）。
/// </para>
/// </summary>
public class CameraPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "camera";

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。
    /// 注册 <see cref="IPlatformCamera"/> 的默认降级实现 <see cref="NullCameraImpl"/>。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IPlatformCamera, NullCameraImpl>();
    }

    /// <summary>
    /// 配置插件，注册相机相关命令。
    /// 命令名采用 <c>camera.&lt;action&gt;</c> 格式，对齐 Tauri v2 命名约定。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Permissions.RegisterPermissionSet("camera:default", "相机默认权限集",
            "camera:allow-check-availability", "camera:allow-capture", "camera:allow-cancel");
        context.Permissions.DeclarePermission("camera:allow-check-availability", "允许检查相机可用性");
        context.Permissions.DeclarePermission("camera:allow-capture", "允许启动相机拍照");
        context.Permissions.DeclarePermission("camera:allow-cancel", "允许取消相机拍照");

        var commands = context.Commands;

        // 检查相机可用性
        commands.MapCommand("camera.checkAvailability",
            (Func<ICommandContext, string>)(ctx => ResolveCamera(ctx).CheckAvailability()));

        // 启动拍照，返回 JPEG 字节数据（CancellationToken 由 ICommandContext 提供，不作为 JSON 参数暴露给前端）
        commands.MapCommand("camera.capture",
            (Func<ICommandContext, Task<CameraCaptureResult>>)(ctx =>
                CaptureAsync(ctx, ctx.CancellationToken)));

        // 取消正在进行的拍照
        commands.MapCommand("camera.cancel",
            (Action<ICommandContext>)(ctx => ResolveCamera(ctx).Cancel()));
    }

    /// <summary>
    /// 拍照并封装结果为 <see cref="CameraCaptureResult"/>。
    /// 平台实现返回空数组视为失败（用户取消或硬件不可用）。
    /// </summary>
    /// <param name="ctx">命令上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>拍照结果。</returns>
    private static async Task<CameraCaptureResult> CaptureAsync(ICommandContext ctx, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await ResolveCamera(ctx).CaptureAsync(cancellationToken).ConfigureAwait(false);
            if (bytes is null || bytes.Length == 0)
            {
                return new CameraCaptureResult { Success = false, Error = "无图像数据（用户取消或硬件不可用）" };
            }

            return new CameraCaptureResult
            {
                Success = true,
                Base64Data = Convert.ToBase64String(bytes),
            };
        }
        catch (OperationCanceledException)
        {
            return new CameraCaptureResult { Success = false, Error = "拍照已被取消" };
        }
        catch (Exception ex)
        {
            return new CameraCaptureResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// 从命令上下文的服务容器解析 <see cref="IPlatformCamera"/>。
    /// </summary>
    /// <param name="ctx">命令上下文。</param>
    /// <returns>平台相机实现实例。</returns>
    private static IPlatformCamera ResolveCamera(ICommandContext ctx)
    {
        return ctx.Services.GetService(typeof(IPlatformCamera)) as IPlatformCamera
            ?? NullCameraImpl.Instance;
    }

    /// <summary>
    /// 空实现的相机，作为 Server 模式 / 桌面平台的降级实现。
    /// <see cref="CheckAvailability"/> 返回 <c>none</c>，<see cref="CaptureAsync"/> 返回空数组。
    /// </summary>
    private sealed class NullCameraImpl : IPlatformCamera
    {
        /// <summary>单例实例。</summary>
        public static readonly NullCameraImpl Instance = new();

        public string CheckAvailability()
        {
            // 降级：无相机硬件支持
            return "none";
        }

        public Task<byte[]> CaptureAsync(CancellationToken cancellationToken)
        {
            // 降级：返回空数组表示无图像数据
            return Task.FromResult(Array.Empty<byte>());
        }

        public void Cancel()
        {
            // no-op
        }
    }
}
