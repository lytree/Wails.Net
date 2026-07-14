namespace Wails.Net.Application.Plugins.Mobile;

/// <summary>
/// 平台生物识别抽象接口。
/// 对应 Tauri v2 的 <c>@tauri-apps/plugin-biometric</c>。
/// <para>
/// Android 实现委托到 <c>AndroidX.Biometric</c> API；
/// 桌面 / Server 模式下为降级实现（<see cref="BiometricPlugin.NullBiometricImpl"/>，返回 none / false）。
/// </para>
/// </summary>
public interface IPlatformBiometric
{
    /// <summary>
    /// 检查生物识别可用性。
    /// </summary>
    /// <returns>
    /// 返回值约定：<c>available</c> 表示可用，<c>unavailable</c> 表示硬件存在但不可用，<c>none</c> 表示无硬件支持。
    /// </returns>
    string CheckAvailability();

    /// <summary>
    /// 发起生物识别认证。
    /// </summary>
    /// <param name="reason">展示给用户的认证理由文本。</param>
    /// <returns>认证成功返回 true，失败或取消返回 false。</returns>
    Task<bool> AuthenticateAsync(string reason);
}
