using Android.Content.PM;
using Wails.Net.Application.Plugins.Mobile;

namespace Wails.Net.Application.Android.Mobile;

/// <summary>
/// Android 平台运行时权限实现。
/// 对应 Tauri v2 <c>@tauri-apps/plugin-permissions</c> 的 Android 后端。
/// 通过 <c>Context.CheckSelfPermission</c> 检查权限状态（API 23+），
/// 通过 <c>Activity.RequestPermissions</c> 发起权限请求（由注入委托完成 UI 流程）。
/// <para>
/// 完整的权限请求回调需在 <c>Activity.OnRequestPermissionsResult</c> 中接收结果，
/// 本实现通过注入的权限请求委托解耦 Activity 生命周期，由 <c>AndroidPlatformApp</c> 提供实际实现。
/// </para>
/// <para>
/// 非 Android 环境（单元测试 / Server 模式）下委托为 null，<c>CheckAsync</c> 返回 <c>granted</c>，
/// <c>RequestAsync</c> 降级为逐个检查权限状态。
/// </para>
/// </summary>
public sealed class AndroidPermissions : IPlatformPermissions
{
    /// <summary>
    /// 权限请求委托，由平台层注入。为 null 时 <c>RequestAsync</c> 降级为逐个权限检查。
    /// 实际实现通过 <c>Activity.RequestPermissions</c> 发起请求，
    /// 在 <c>OnRequestPermissionsResult</c> 中通过 <c>TaskCompletionSource&lt;PermissionRequestResult[]&gt;</c> 完成回调。
    /// </summary>
    private readonly Func<string[], CancellationToken, Task<PermissionRequestResult[]>>? _requestImpl;

    /// <summary>
    /// 构造 <see cref="AndroidPermissions"/> 实例，使用默认（无委托）模式。
    /// </summary>
    public AndroidPermissions() : this(requestImpl: null)
    {
    }

    /// <summary>
    /// 构造 <see cref="AndroidPermissions"/> 实例，注入权限请求委托。
    /// </summary>
    /// <param name="requestImpl">权限请求委托，由 <c>AndroidPlatformApp</c> 提供实际实现。</param>
    public AndroidPermissions(
        Func<string[], CancellationToken, Task<PermissionRequestResult[]>>? requestImpl)
    {
        _requestImpl = requestImpl;
    }

    /// <inheritdoc />
    public Task<string> CheckAsync(string permission)
    {
        var context = global::Android.App.Application.Context;
        if (context is null)
        {
            // 非 Android 环境（单元测试）：默认已授权
            return Task.FromResult("granted");
        }

        if (string.IsNullOrEmpty(permission))
        {
            return Task.FromResult("unknown");
        }

        try
        {
            // Context.CheckSelfPermission(string) 在 API 23+ 可用，最低 API 24 满足要求
            // 使用 OperatingSystem.IsAndroidVersionAtLeast 进行平台守卫（CA1416 识别此模式）
            if (!OperatingSystem.IsAndroidVersionAtLeast(23))
            {
                // API < 23 在安装时授予所有权限，视为已授权
                return Task.FromResult("granted");
            }

#pragma warning disable CA1416 // 平台守卫已在上方完成，CA1416 分析器对 context.CheckSelfPermission 仍可能误报
            var result = context.CheckSelfPermission(permission);
#pragma warning restore CA1416
            return Task.FromResult(result == Permission.Granted ? "granted" : "prompt");
        }
        catch (Java.Lang.Exception)
        {
            return Task.FromResult("unknown");
        }
        catch (System.Exception)
        {
            // 某些权限标识符可能无效，降级为 unknown
            return Task.FromResult("unknown");
        }
    }

    /// <inheritdoc />
    public Task<PermissionRequestResult[]> RequestAsync(string[] permissions, CancellationToken cancellationToken)
    {
        if (_requestImpl is null)
        {
            // 无注入委托时：检查每个权限的当前状态，已授权的返回 granted，未授权的返回 prompt
            // 对应 Android 在无 Activity 场景下无法发起权限请求对话框的降级行为
            return CheckAllPermissions(permissions, cancellationToken);
        }

        return _requestImpl(permissions, cancellationToken);
    }

    /// <summary>
    /// 降级实现：通过 <see cref="CheckAsync"/> 逐个检查权限状态，不发起系统对话框。
    /// 用于未注入 Activity 委托的场景（单元测试 / Server 模式）。
    /// </summary>
    /// <param name="permissions">权限标识符数组。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>每个权限的检查结果数组。</returns>
    private async Task<PermissionRequestResult[]> CheckAllPermissions(string[] permissions, CancellationToken cancellationToken)
    {
        var results = new PermissionRequestResult[permissions.Length];
        for (var i = 0; i < permissions.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = await CheckAsync(permissions[i]).ConfigureAwait(false);
            results[i] = new PermissionRequestResult
            {
                Permission = permissions[i],
                State = state,
            };
        }

        return results;
    }
}
