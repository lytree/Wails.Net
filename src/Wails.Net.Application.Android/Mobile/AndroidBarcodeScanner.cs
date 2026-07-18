using Android.Content;
using Wails.Net.Application.Plugins.Mobile;
using Uri = Android.Net.Uri;

namespace Wails.Net.Application.Android.Mobile;

/// <summary>
/// Android 平台条码扫描实现。
/// 对应 Tauri v2 <c>@tauri-apps/plugin-barcode-scanner</c> 的 Android 后端。
/// 通过隐式 <c>Intent.ActionGetContent</c> / 第三方扫描应用 Intent 启动系统扫描 Activity。
/// <para>
/// 完整实现需在 <c>Activity.OnActivityResult</c> 中接收扫描结果，
/// 本实现通过注入的扫描委托解耦 Activity 生命周期，由 <c>AndroidPlatformApp</c> 提供实际实现。
/// </para>
/// <para>
/// 非 Android 环境（单元测试）下委托为 null，<c>ScanAsync</c> 返回空字符串。
/// </para>
/// </summary>
public sealed class AndroidBarcodeScanner : IPlatformBarcodeScanner
{
    /// <summary>
    /// 扫描委托，由平台层注入。为 null 时返回空字符串。
    /// 实际实现通过 <c>Activity.StartActivityForResult</c> 启动扫描应用并在
    /// <c>OnActivityResult</c> 中通过 <c>TaskCompletionSource&lt;string&gt;</c> 完成回调。
    /// </summary>
    private readonly Func<CancellationToken, Task<string>>? _scanImpl;

    /// <summary>
    /// 取消委托，由平台层注入。为 null 时为 no-op。
    /// </summary>
    private readonly Action? _cancelImpl;

    /// <summary>
    /// 构造 <see cref="AndroidBarcodeScanner"/> 实例，使用默认（无委托）模式。
    /// </summary>
    public AndroidBarcodeScanner() : this(scanImpl: null, cancelImpl: null)
    {
    }

    /// <summary>
    /// 构造 <see cref="AndroidBarcodeScanner"/> 实例，注入扫描委托。
    /// </summary>
    /// <param name="scanImpl">扫描委托，由 <c>AndroidPlatformApp</c> 提供实际实现。</param>
    /// <param name="cancelImpl">取消委托。</param>
    public AndroidBarcodeScanner(
        Func<CancellationToken, Task<string>>? scanImpl,
        Action? cancelImpl)
    {
        _scanImpl = scanImpl;
        _cancelImpl = cancelImpl;
    }

    /// <inheritdoc />
    public Task<string> ScanAsync()
    {
        if (_scanImpl is null)
        {
            // 无注入委托时返回空字符串（需要 Activity 才能启动扫描 Intent）
            return Task.FromResult(string.Empty);
        }

        return _scanImpl(CancellationToken.None);
    }

    /// <inheritdoc />
    public void Cancel()
    {
        _cancelImpl?.Invoke();
    }

    /// <summary>
    /// 创建启动第三方扫描应用（如 Google Lens / ZXing）的 Intent。
    /// 供 <c>AndroidPlatformApp</c> 在注入扫描委托时使用。
    /// </summary>
    /// <param name="prompt">扫描提示文本，可为 null。</param>
    /// <returns>配置好的 <see cref="Intent"/>；非 Android 环境返回 null。</returns>
    public static Intent? CreateScanIntent(string? prompt)
    {
        var context = global::Android.App.Application.Context;
        if (context is null)
        {
            return null;
        }

        // 优先尝试 Google Lens / Google Play Services 扫描 Intent
        // 回退到通用 GET_CONTENT + image/* 让用户选择扫描应用
        var intent = new Intent(Intent.ActionGetContent);
        intent.SetType("image/*");
        intent.AddCategory(Intent.CategoryOpenable);

        if (!string.IsNullOrEmpty(prompt))
        {
            intent.PutExtra(Intent.ExtraTitle, prompt);
        }

        intent.AddFlags(ActivityFlags.NewTask);
        return intent;
    }
}
