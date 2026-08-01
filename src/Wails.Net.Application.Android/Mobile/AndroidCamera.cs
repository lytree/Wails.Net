using Android.Content;
using Android.Content.PM;
using Android.Provider;
using Wails.Net.Application.Plugins.Mobile;

namespace Wails.Net.Application.Android.Mobile;

/// <summary>
/// Android 平台相机实现。
/// 对应 Tauri v2 <c>@tauri-apps/plugin-camera</c> 的 Android 后端。
/// 通过 <c>PackageManager.HasSystemFeature(FEATURE_CAMERA_ANY)</c> 检查相机硬件，
/// 通过 <c>MediaStore.ACTION_IMAGE_CAPTURE</c> Intent 启动系统相机应用拍照。
/// <para>
/// 完整的拍照回调需在 <c>Activity.OnActivityResult</c> 中接收图像数据，
/// 本实现通过注入的拍照委托解耦 Activity 生命周期，由 <c>AndroidPlatformApp</c> 提供实际实现。
/// </para>
/// <para>
/// 非 Android 环境（单元测试 / Server 模式）下委托为 null，<c>CheckAvailability</c> 返回 <c>none</c>，
/// <c>CaptureAsync</c> 返回空字节数组。
/// </para>
/// </summary>
public sealed class AndroidCamera : IPlatformCamera
{
    /// <summary>
    /// 拍照委托，由平台层注入。为 null 时返回空字节数组。
    /// 实际实现通过 <c>MediaStore.ACTION_IMAGE_CAPTURE</c> Intent 启动系统相机应用，
    /// 在 <c>OnActivityResult</c> 中通过 <c>TaskCompletionSource&lt;byte[]&gt;</c> 完成回调。
    /// </summary>
    private readonly Func<CancellationToken, Task<byte[]>>? _captureImpl;

    /// <summary>
    /// 取消委托，由平台层注入。为 null 时为 no-op。
    /// </summary>
    private readonly Action? _cancelImpl;

    /// <summary>
    /// 构造 <see cref="AndroidCamera"/> 实例，使用默认（无委托）模式。
    /// </summary>
    public AndroidCamera() : this(captureImpl: null, cancelImpl: null)
    {
    }

    /// <summary>
    /// 构造 <see cref="AndroidCamera"/> 实例，注入拍照委托。
    /// </summary>
    /// <param name="captureImpl">拍照委托，由 <c>AndroidPlatformApp</c> 提供实际实现。</param>
    /// <param name="cancelImpl">取消委托。</param>
    public AndroidCamera(
        Func<CancellationToken, Task<byte[]>>? captureImpl,
        Action? cancelImpl)
    {
        _captureImpl = captureImpl;
        _cancelImpl = cancelImpl;
    }

    /// <inheritdoc />
    public string CheckAvailability()
    {
        var context = global::Android.App.Application.Context;
        if (context is null)
        {
            // 非 Android 环境（单元测试）
            return "none";
        }

        try
        {
            // FEATURE_CAMERA_ANY 在 API 17+ 可用，最低 API 24 满足要求
            // 使用 OperatingSystem.IsAndroidVersionAtLeast 进行平台守卫（CA1416 识别此模式）
            if (!OperatingSystem.IsAndroidVersionAtLeast(17))
            {
                return "none";
            }

            var hasCamera = context.PackageManager?.HasSystemFeature(PackageManager.FeatureCameraAny) ?? false;
            return hasCamera ? "available" : "none";
        }
        catch (Java.Lang.Exception)
        {
            return "none";
        }
    }

    /// <inheritdoc />
    public Task<byte[]> CaptureAsync(CancellationToken cancellationToken)
    {
        if (_captureImpl is null)
        {
            // 无注入委托时返回空数组（需要 Activity 才能启动相机 Intent）
            return Task.FromResult(Array.Empty<byte>());
        }

        return _captureImpl(cancellationToken);
    }

    /// <inheritdoc />
    public void Cancel()
    {
        _cancelImpl?.Invoke();
    }

    /// <summary>
    /// 创建启动系统相机应用的 <c>MediaStore.ACTION_IMAGE_CAPTURE</c> Intent。
    /// 供 <c>AndroidPlatformApp</c> 在注入拍照委托时使用。
    /// </summary>
    /// <returns>配置好的 <see cref="Intent"/>；非 Android 环境返回 null。</returns>
    public static Intent? CreateCaptureIntent()
    {
        var context = global::Android.App.Application.Context;
        if (context is null)
        {
            return null;
        }

        var intent = new Intent(MediaStore.ActionImageCapture);
        intent.AddFlags(ActivityFlags.NewTask);
        return intent;
    }
}
