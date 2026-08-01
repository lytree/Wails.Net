namespace Wails.Net.Application.Plugins.Mobile;

/// <summary>
/// 地理位置（经纬度、海拔、精度、速度、方向等）。
/// 对应 Tauri v2 <c>@tauri-apps/plugin-geolocation</c> 的 <c>Position</c> 结构。
/// </summary>
public sealed class GeolocationPosition
{
    /// <summary>坐标信息</summary>
    public GeolocationCoords Coords { get; set; } = new();

    /// <summary>时间戳（Unix 毫秒），表示位置获取的时间</summary>
    public long Timestamp { get; set; }
}

/// <summary>
/// 地理坐标信息。
/// 对应 Tauri v2 geolocation 插件的 <c>Coords</c> 结构，字段命名对齐 W3C Geolocation API。
/// </summary>
public sealed class GeolocationCoords
{
    /// <summary>纬度（十进制度，-90 ~ 90）</summary>
    public double Latitude { get; set; }

    /// <summary>经度（十进制度，-180 ~ 180）</summary>
    public double Longitude { get; set; }

    /// <summary>精度（米），表示纬度/经度的误差半径</summary>
    public double Accuracy { get; set; }

    /// <summary>海拔高度（米，相对于海平面），不可用时为 null</summary>
    public double? Altitude { get; set; }

    /// <summary>海拔精度（米），不可用时为 null</summary>
    public double? AltitudeAccuracy { get; set; }

    /// <summary>移动方向（度，相对于正北顺时针，0 ~ 360），不可用时为 null</summary>
    public double? Heading { get; set; }

    /// <summary>移动速度（米/秒），不可用时为 null</summary>
    public double? Speed { get; set; }
}

/// <summary>
/// 获取位置的选项参数。
/// 对应 Tauri v2 geolocation 插件的 <c>Options</c> 结构。
/// </summary>
public sealed class GeolocationOptions
{
    /// <summary>是否启用高精度定位（如 GPS），默认 false</summary>
    public bool EnableHighAccuracy { get; set; }

    /// <summary>超时时间（毫秒），超时后返回错误，默认 10000</summary>
    public int Timeout { get; set; } = 10000;

    /// <summary>可接受的缓存位置最大年龄（毫秒），默认 0（不使用缓存）</summary>
    public int MaximumAge { get; set; } = 0;
}

/// <summary>
/// watchPosition 返回的监听句柄标识。
/// </summary>
public sealed class WatchPositionResult
{
    /// <summary>监听句柄 ID，用于后续 clearWatch 取消监听</summary>
    public int WatchId { get; set; }
}
