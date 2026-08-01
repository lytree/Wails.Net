using Android.Content;
using Android.Locations;
using Android.OS;
using Wails.Net.Application.Plugins.Mobile;

namespace Wails.Net.Application.Android.Mobile;

/// <summary>
/// Android 平台地理定位实现。
/// 对应 Tauri v2 <c>@tauri-apps/plugin-geolocation</c> 的 Android 后端。
/// 通过 <c>Android.Locations.LocationManager</c> 获取 GPS / 网络定位。
/// <para>
/// 最低 API Level 24（ADR-0004）。非 Android 环境（单元测试）下 Context 为 null，
/// CheckAvailability 返回 <c>none</c>，定位返回 null / 0。
/// </para>
/// <para>
/// 注意：完整定位功能需要 <c>ACCESS_FINE_LOCATION</c> / <c>ACCESS_COARSE_LOCATION</c> 权限。
/// 权限由应用清单声明并通过 <c>ActivityCompat.RequestPermissions</c> 请求。
/// </para>
/// </summary>
public sealed class AndroidGeolocation : IPlatformGeolocation
{
    /// <summary>
    /// 位置监听器字典，键为 watchId，值为 ILocationListener。
    /// 用于 ClearWatch 时移除监听器。
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, ILocationListener> _watchers = new();

    /// <summary>下一个监听句柄 ID（线程安全自增）</summary>
    private static int s_nextWatchId = 1;

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
            var locationManager = (LocationManager?)context.GetSystemService(Context.LocationService);
            if (locationManager is null)
            {
                return "none";
            }

            // 检查 GPS 和网络定位提供者是否可用
            var gpsAvailable = locationManager.IsProviderEnabled(LocationManager.GpsProvider);
            var networkAvailable = locationManager.IsProviderEnabled(LocationManager.NetworkProvider);

            if (gpsAvailable || networkAvailable)
            {
                return "available";
            }

            // 提供者存在但未启用
            return "unavailable";
        }
        catch (Java.Lang.SecurityException)
        {
            // 权限被拒绝
            return "denied";
        }
        catch (Java.Lang.Exception)
        {
            return "none";
        }
    }

    /// <inheritdoc />
    public Task<GeolocationPosition?> GetCurrentPositionAsync(GeolocationOptions options, CancellationToken cancellationToken)
    {
        var context = global::Android.App.Application.Context;
        if (context is null)
        {
            return Task.FromResult<GeolocationPosition?>(null);
        }

        try
        {
            var locationManager = (LocationManager?)context.GetSystemService(Context.LocationService);
            if (locationManager is null)
            {
                return Task.FromResult<GeolocationPosition?>(null);
            }

            // 选择定位提供者：高精度优先 GPS，否则网络定位
            var provider = options.EnableHighAccuracy
                ? LocationManager.GpsProvider
                : LocationManager.NetworkProvider;

            // 若首选提供者不可用，尝试另一个
            if (!locationManager.IsProviderEnabled(provider))
            {
                provider = provider == LocationManager.GpsProvider
                    ? LocationManager.NetworkProvider
                    : LocationManager.GpsProvider;
            }

            if (!locationManager.IsProviderEnabled(provider))
            {
                return Task.FromResult<GeolocationPosition?>(null);
            }

            // 获取最后一次已知位置（快速返回，不等待 GPS 锁定）
            var location = locationManager.GetLastKnownLocation(provider);
            if (location is null)
            {
                return Task.FromResult<GeolocationPosition?>(null);
            }

            return Task.FromResult<GeolocationPosition?>(ToGeolocationPosition(location));
        }
        catch (Java.Lang.SecurityException)
        {
            return Task.FromResult<GeolocationPosition?>(null);
        }
        catch (Java.Lang.Exception)
        {
            return Task.FromResult<GeolocationPosition?>(null);
        }
    }

    /// <inheritdoc />
    public Task<int> WatchPositionAsync(GeolocationOptions options, Action<GeolocationPosition> callback, CancellationToken cancellationToken)
    {
        var context = global::Android.App.Application.Context;
        if (context is null)
        {
            return Task.FromResult(0);
        }

        try
        {
            var locationManager = (LocationManager?)context.GetSystemService(Context.LocationService);
            if (locationManager is null)
            {
                return Task.FromResult(0);
            }

            var provider = options.EnableHighAccuracy
                ? LocationManager.GpsProvider
                : LocationManager.NetworkProvider;

            if (!locationManager.IsProviderEnabled(provider))
            {
                provider = provider == LocationManager.GpsProvider
                    ? LocationManager.NetworkProvider
                    : LocationManager.GpsProvider;
            }

            if (!locationManager.IsProviderEnabled(provider))
            {
                return Task.FromResult(0);
            }

            var watchId = System.Threading.Interlocked.Increment(ref s_nextWatchId);
            var listener = new PositionListener(callback);
            _watchers[watchId] = listener;

            // 最小时间间隔（毫秒）和最小距离（米）
            var minTimeMs = Math.Max(options.MaximumAge, 1000L);
            var minDistanceM = 0f;

            locationManager.RequestLocationUpdates(provider, minTimeMs, minDistanceM, listener);

            return Task.FromResult(watchId);
        }
        catch (Java.Lang.SecurityException)
        {
            return Task.FromResult(0);
        }
        catch (Java.Lang.Exception)
        {
            return Task.FromResult(0);
        }
    }

    /// <inheritdoc />
    public void ClearWatch(int watchId)
    {
        if (watchId <= 0)
        {
            return;
        }

        if (_watchers.TryRemove(watchId, out var listener))
        {
            try
            {
                var context = global::Android.App.Application.Context;
                var locationManager = (LocationManager?)context?.GetSystemService(Context.LocationService);
                locationManager?.RemoveUpdates(listener);
            }
            catch (Java.Lang.Exception)
            {
                // 移除监听器失败时静默忽略
            }
        }
    }

    /// <summary>
    /// 将 Android <see cref="Location"/> 转换为 <see cref="GeolocationPosition"/>。
    /// </summary>
    /// <param name="location">Android 位置信息。</param>
    /// <returns>Wails.Net 地理位置信息。</returns>
    private static GeolocationPosition ToGeolocationPosition(Location location)
    {
        return new GeolocationPosition
        {
            Coords = new GeolocationCoords
            {
                Latitude = location.Latitude,
                Longitude = location.Longitude,
                Accuracy = location.HasAccuracy ? location.Accuracy : 0,
                Altitude = location.HasAltitude ? location.Altitude : null,
                // AltitudeAccuracy 在 Android Location API 中不可用
                AltitudeAccuracy = null,
                Heading = location.HasBearing ? (double?)location.Bearing : null,
                Speed = location.HasSpeed ? (double?)location.Speed : null,
            },
            // Android Location.Time 返回 Unix 毫秒时间戳（API 1+）
            Timestamp = location.Time,
        };
    }

    /// <summary>
    /// 位置监听器，将 Android 位置更新回调转换为 <see cref="GeolocationPosition"/>。
    /// </summary>
    private sealed class PositionListener : Java.Lang.Object, ILocationListener
    {
        private readonly Action<GeolocationPosition> _callback;

        public PositionListener(Action<GeolocationPosition> callback)
        {
            _callback = callback;
        }

        public void OnLocationChanged(Location? location)
        {
            if (location is null)
            {
                return;
            }
            _callback(ToGeolocationPosition(location));
        }

        public void OnProviderDisabled(string? provider)
        {
            // 提供者被禁用时无需操作
        }

        public void OnProviderEnabled(string? provider)
        {
            // 提供者被启用时无需操作
        }

        public void OnStatusChanged(string? provider, [global::Android.Runtime.GeneratedEnum] Availability status, Bundle? extras)
        {
            // 状态变化时无需操作（API 29+ 弃用，但仍需实现接口）
        }
    }
}
