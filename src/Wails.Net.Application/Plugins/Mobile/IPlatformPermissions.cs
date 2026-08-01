namespace Wails.Net.Application.Plugins.Mobile;

/// <summary>
/// 平台运行时权限抽象接口。
/// 对应 Tauri v2 的 <c>@tauri-apps/plugin-permissions</c>，封装 Android 运行时权限请求。
/// <para>
/// Android 实现委托到 <c>ActivityCompat.RequestPermissions</c> + <c>OnRequestPermissionsResult</c> 回调；
/// 桌面 / Server 模式下为降级实现（<see cref="PermissionsPlugin.NullPermissionsImpl"/>，所有请求返回 <c>granted</c>）。
/// </para>
/// </summary>
public interface IPlatformPermissions
{
    /// <summary>
    /// 检查指定权限的当前授权状态（不发起请求）。
    /// </summary>
    /// <param name="permission">平台权限标识符（如 <c>android.permission.CAMERA</c>）。</param>
    /// <returns>权限状态字符串：<c>granted</c> / <c>denied</c> / <c>prompt</c> / <c>unknown</c>。</returns>
    Task<string> CheckAsync(string permission);

    /// <summary>
    /// 异步请求一组权限的授权。
    /// </summary>
    /// <param name="permissions">平台权限标识符数组。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>每个权限的请求结果数组（与 <paramref name="permissions"/> 顺序对应）。</returns>
    Task<PermissionRequestResult[]> RequestAsync(string[] permissions, CancellationToken cancellationToken);
}

/// <summary>
/// 权限请求结果。
/// 对应 Tauri v2 <c>@tauri-apps/plugin-permissions</c> 的返回结构。
/// </summary>
public sealed class PermissionRequestResult
{
    /// <summary>权限标识符（如 <c>android.permission.CAMERA</c>）。</summary>
    public string Permission { get; set; } = string.Empty;

    /// <summary>
    /// 授权状态：<c>granted</c> / <c>denied</c> / <c>prompt</c> / <c>unknown</c>。
    /// </summary>
    public string State { get; set; } = "unknown";
}
