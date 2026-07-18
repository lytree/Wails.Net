using Android.Content;
using Android.OS;
using Wails.Net.Application.Plugins.Mobile;

namespace Wails.Net.Application.Android.Mobile;

/// <summary>
/// Android 平台震动反馈实现。
/// 对应 Wails v3 Go 版本 messageprocessor_android.go 中的 <c>androidHapticsVibrate</c>。
/// 通过 <c>Android.OS.Vibrator</c> 系统服务触发设备震动。
/// <para>
/// 非 Android 环境（单元测试 / Server 模式）下 <c>Vibrator</c> 为 null，所有方法降级为 no-op。
/// </para>
/// <para>
/// API 兼容性：
/// <list type="bullet">
///   <item>API 31+：使用 <c>VibratorManager</c> 获取 <c>Vibrator</c>（<c>Context.VibratorService</c> 已过时）。</item>
///   <item>API 26-30：使用 <c>Context.VibratorService</c> + <c>VibrationEffect.CreateOneShot</c>。</item>
///   <item>API &lt; 26：回退到 <c>Vibrator.Vibrate(long)</c>（已过时但可用）。</item>
/// </list>
/// 平台守卫使用 <c>System.OperatingSystem.IsAndroidVersionAtLeast(int)</c>，
/// CA1416 分析器识别此模式后不会报告平台兼容性警告。
/// </para>
/// </summary>
public sealed class AndroidHaptics : IPlatformHaptics
{
    /// <summary>
    /// Android Vibrator 系统服务实例，延迟获取。
    /// 非 Android 环境下为 null（<c>Application.Context</c> 返回 null）。
    /// </summary>
    private readonly Vibrator? _vibrator;

    /// <summary>
    /// 构造 <see cref="AndroidHaptics"/> 实例，从全局 Context 获取 Vibrator 系统服务。
    /// 对应 Go 版本通过 JNI 调用 <c>Vibrator.vibrate(durationMs)</c>。
    /// </summary>
    public AndroidHaptics()
    {
        var context = global::Android.App.Application.Context;
        if (context is null)
        {
            // 非 Android 环境（单元测试）：保持 _vibrator 为 null，方法降级为 no-op
            return;
        }

        _vibrator = ResolveVibrator(context);
    }

    /// <summary>
    /// 根据当前 API Level 解析 Vibrator 实例。
    /// API 31+ 优先使用 VibratorManager（Context.VibratorService 在 API 31 已过时）；
    /// 低版本回退到 Context.VibratorService。
    /// </summary>
    /// <param name="context">Android Context。</param>
    /// <returns>Vibrator 实例；不可用时返回 null。</returns>
    private static Vibrator? ResolveVibrator(global::Android.Content.Context context)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            // API 31+：通过 VibratorManager 获取 Vibrator
            var vibratorManager = context.GetSystemService(Context.VibratorManagerService)
                as VibratorManager;
            return vibratorManager?.DefaultVibrator;
        }

        // API 21-30：通过 Context.VibratorService 获取 Vibrator
        // 最低 API 24 已满足 API 21 要求，OperatingSystem 守卫用于 CA1416 分析器识别
        if (OperatingSystem.IsAndroidVersionAtLeast(21))
        {
#pragma warning disable CA1422 // Context.VibratorService 在 API 31 过时，此处为低版本回退路径
            return context.GetSystemService(Context.VibratorService) as Vibrator;
#pragma warning restore CA1422
        }

        return null;
    }

    /// <inheritdoc />
    public void Vibrate(int durationMs)
    {
        if (_vibrator is null)
        {
            return;
        }

        // durationMs <= 0 时跳过（避免 Android API 抛 IllegalArgumentException）
        if (durationMs <= 0)
        {
            return;
        }

        // VibrationEffect.CreateOneShot 在 API 26+ 可用
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            try
            {
                var effect = VibrationEffect.CreateOneShot(durationMs, VibrationEffect.DefaultAmplitude);
                _vibrator.Vibrate(effect);
            }
            catch (Java.Lang.SecurityException)
            {
                // 缺少 VIBRATE 权限时静默忽略（对应 Go 版本忽略权限错误）
            }
            return;
        }

        // API 21-25 回退：Vibrator.Vibrate(long) 在 API 21+ 可用，API 26 过时但可用
        // 最低 API 24 已满足 API 21 要求，OperatingSystem 守卫用于 CA1416 分析器识别
        if (OperatingSystem.IsAndroidVersionAtLeast(21))
        {
#pragma warning disable CA1422 // Vibrator.Vibrate(long) 在 API 26 过时，此处为低版本回退路径
            try
            {
                _vibrator.Vibrate(durationMs);
            }
            catch (Java.Lang.SecurityException)
            {
                // 缺少 VIBRATE 权限时静默忽略
            }
#pragma warning restore CA1422
        }
    }

    /// <inheritdoc />
    public void Cancel()
    {
        _vibrator?.Cancel();
    }

    /// <inheritdoc />
    public void Notify(NotificationType type)
    {
        // 对应 Wails v3：通知震动按类型使用不同时长
        // success=short, warning=medium, error=long
        var duration = type switch
        {
            NotificationType.Success => 50,
            NotificationType.Warning => 150,
            NotificationType.Error => 300,
            _ => 100,
        };

        Vibrate(duration);
    }
}
