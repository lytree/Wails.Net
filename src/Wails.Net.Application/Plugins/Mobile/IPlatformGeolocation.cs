namespace Wails.Net.Application.Plugins.Mobile;

/// <summary>
/// 平台地理定位抽象接口。
/// 对应 Tauri v2 的 <c>@tauri-apps/plugin-geolocation</c>。
/// <para>
/// Android 实现委托到 <c>Android.Location.LocationManager</c>；
/// 桌面 / Server 模式下为降级实现（<see cref="GeolocationPlugin.NullGeolocationImpl"/>，
/// 返回 null / 0）。
/// </para>
/// </summary>
public interface IPlatformGeolocation
{
    /// <summary>
    /// 检查地理定位可用性。
    /// </summary>
    /// <returns>
    /// 返回值约定：<c>available</c> 表示可用，<c>unavailable</c> 表示硬件存在但不可用，
    /// <c>denied</c> 表示权限被拒绝，<c>none</c> 表示无硬件支持。
    /// </returns>
    string CheckAvailability();

    /// <summary>
    /// 获取当前位置（单次定位）。
    /// </summary>
    /// <param name="options">定位选项（精度、超时等）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>位置信息，不可用时返回 null。</returns>
    Task<GeolocationPosition?> GetCurrentPositionAsync(GeolocationOptions options, CancellationToken cancellationToken);

    /// <summary>
    /// 开始持续监听位置变化。
    /// </summary>
    /// <param name="options">定位选项。</param>
    /// <param name="callback">位置变化回调。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>监听句柄 ID，用于后续 <c>ClearWatch</c> 取消监听。失败返回 0。</returns>
    Task<int> WatchPositionAsync(GeolocationOptions options, Action<GeolocationPosition> callback, CancellationToken cancellationToken);

    /// <summary>
    /// 取消位置监听。
    /// </summary>
    /// <param name="watchId">监听句柄 ID。</param>
    void ClearWatch(int watchId);
}
