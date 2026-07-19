using Android.Hardware.Biometrics;
using Android.OS;
using Wails.Net.Application.Plugins.Mobile;
using BuildVersionCodes = Android.OS.BuildVersionCodes;

namespace Wails.Net.Application.Android.Mobile;

/// <summary>
/// Android 平台生物识别实现。
/// 对应 Tauri v2 <c>@tauri-apps/plugin-biometric</c> 的 Android 后端。
/// 通过 <c>Android.Hardware.Biometrics.BiometricManager</c>（API 29+）检查可用性，
/// 通过 <c>BiometricPrompt</c>（API 28+）发起认证。
/// <para>
/// 最低 API Level 24（ADR-0004）。API &lt; 30 时 <c>BiometricManager.CanAuthenticate</c> 不可用，返回 <c>none</c>；
/// 非 Android 环境（单元测试）下 Context 为 null，返回 <c>none</c> / false。
/// </para>
/// <para>
/// 注意：完整 <c>BiometricPrompt</c> 认证流程需要 <c>FragmentActivity</c> 与 <c>Executor</c>，
/// 本实现通过注入的 <c>Func&lt;CancellationToken, Task&lt;bool&gt;&gt;</c> 委托解耦 UI 层，
/// 由 <c>AndroidPlatformApp</c> 在 Activity 可用时提供实际实现。
/// </para>
/// </summary>
public sealed class AndroidBiometric : IPlatformBiometric
{
    /// <summary>
    /// 认证委托，由平台层注入。为 null 时 <c>AuthenticateAsync</c> 返回 false。
    /// 实际实现通过 <c>BiometricPrompt</c> + <c>FragmentActivity</c> 完成认证。
    /// </summary>
    private readonly Func<CancellationToken, Task<bool>>? _authenticateImpl;

    /// <summary>
    /// 构造 <see cref="AndroidBiometric"/> 实例，使用默认（无 UI 委托）模式。
    /// 可用性检查依赖 <c>BiometricManager</c>；认证返回 false（需注入委托才能完成认证）。
    /// </summary>
    public AndroidBiometric() : this(authenticateImpl: null)
    {
    }

    /// <summary>
    /// 构造 <see cref="AndroidBiometric"/> 实例，注入认证委托。
    /// </summary>
    /// <param name="authenticateImpl">
    /// 认证委托，接收 <see cref="CancellationToken"/>，返回认证是否成功。
    /// 由 <c>AndroidPlatformApp</c> 提供 <c>BiometricPrompt</c> 实际实现；
    /// 为 null 时 <c>AuthenticateAsync</c> 返回 false。
    /// </param>
    public AndroidBiometric(Func<CancellationToken, Task<bool>>? authenticateImpl)
    {
        _authenticateImpl = authenticateImpl;
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

        // BiometricManager.CanAuthenticate(int) 在 API 30+ 可用
        // 使用 OperatingSystem.IsAndroidVersionAtLeast 进行平台守卫（CA1416 识别此模式）
        if (!OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            return "none";
        }

        try
        {
            // 通过 BiometricService 系统服务获取 BiometricManager 实例
            // .NET Android 绑定未提供 BiometricManager.Get(Context) 静态方法，
            // 使用 Context.GetSystemService + BiometricService 常量获取
            var manager = context.GetSystemService(global::Android.Content.Context.BiometricService)
                as BiometricManager;
            if (manager is null)
            {
                return "none";
            }

            // Authenticators.BIOMETRIC_WEAK = 0x00FF（允许弱生物识别：指纹/面容/虹膜）
            // 使用整数值避免绑定 API 差异（Authenticators 嵌套类在某些 .NET Android 版本不暴露）
            const int AuthenticatorsBiometricWeak = 0x00FF;
            var canAuthenticate = manager.CanAuthenticate(AuthenticatorsBiometricWeak);

            // BiometricCode 枚举值（Android.Hardware.Biometrics.BiometricCode）：
            // 转为 int 后用常量值匹配，避免枚举成员命名差异（不同 .NET Android 版本可能用 ErrorHwUnavailable / ErrorHardwareUnavailable）
            // Success = 0
            // ErrorHwUnavailable = 1
            // ErrorNoneEnrolled (ErrorNoBiometrics) = 11
            // ErrorNoHardware (ErrorHwNotPresent) = 12
            return ((int)canAuthenticate) switch
            {
                0 => "available",       // BiometricCode.Success
                1 => "unavailable",     // BiometricCode.ErrorHwUnavailable
                11 => "unavailable",    // BiometricCode.ErrorNoneEnrolled
                12 => "none",           // BiometricCode.ErrorNoHardware
                _ => "none",
            };
        }
        catch (Java.Lang.NoSuchMethodException)
        {
            // 旧 API Level 不支持 BiometricManager
            return "none";
        }
        catch (Java.Lang.Exception)
        {
            return "none";
        }
    }

    /// <inheritdoc />
    public Task<bool> AuthenticateAsync(string reason)
    {
        if (_authenticateImpl is null)
        {
            // 无注入委托时返回 false（需要 Activity + BiometricPrompt 才能完成认证）
            return Task.FromResult(false);
        }

        return _authenticateImpl(CancellationToken.None);
    }
}
