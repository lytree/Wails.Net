namespace Wails.Net.Application.Plugins.Mobile;

/// <summary>
/// 平台震动反馈抽象接口。
/// 对应 Tauri v2 的 <c>@tauri-apps/plugin-haptics</c>。
/// <para>
/// Android 实现委托到 <c>Android.OS.Vibrator</c> API；
/// 桌面 / Server 模式下为 no-op（默认 <see cref="HapticsPlugin.NullHapticsImpl"/>）。
/// </para>
/// </summary>
public interface IPlatformHaptics
{
    /// <summary>
    /// 触发设备震动。
    /// </summary>
    /// <param name="durationMs">震动持续时间（毫秒）。</param>
    void Vibrate(int durationMs);

    /// <summary>
    /// 取消当前正在进行的震动。
    /// </summary>
    void Cancel();

    /// <summary>
    /// 触发通知类型震动（不同类型对应不同震动模式）。
    /// </summary>
    /// <param name="type">通知类型。</param>
    void Notify(NotificationType type);
}

/// <summary>
/// 震动通知类型。
/// 对应 Tauri v2 haptics 插件的 notification 枚举。
/// </summary>
public enum NotificationType
{
    /// <summary>成功</summary>
    Success,

    /// <summary>警告</summary>
    Warning,

    /// <summary>错误</summary>
    Error
}
