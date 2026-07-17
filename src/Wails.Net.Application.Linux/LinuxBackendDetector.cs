using Gdk;

namespace Wails.Net.Application.Platform;

/// <summary>
/// Linux 显示后端类型（X11 / Wayland / Broadway 等）。
/// 对应 GTK4 GDK_BACKEND 环境变量支持的取值。
/// </summary>
internal enum LinuxDisplayBackend
{
    /// <summary>
    /// 未知或尚未检测。
    /// </summary>
    Unknown,

    /// <summary>
    /// X11 后端（传统 X Window System）。
    /// 支持 wmctrl、EWMH 协议、窗口位置控制等完整功能。
    /// </summary>
    X11,

    /// <summary>
    /// Wayland 后端。
    /// 窗口位置由 compositor 控制，应用无法直接设置；
    /// 不支持 wmctrl；置顶/最小化等需通过 GTK4 API 或 layer-shell 协议。
    /// </summary>
    Wayland,

    /// <summary>
    /// Broadway 后端（HTML5 远程显示）。
    /// 极少用于桌面应用，行为与 X11 类似但功能受限。
    /// </summary>
    Broadway,
}

/// <summary>
/// Linux 显示后端检测器。
/// 通过 GDK_DISPLAY 环境变量和 Gdk.Display 类型名判断当前后端，
/// 提供能力查询以便在不同后端下采用兼容策略。
/// 对应 Wails v3 Go 版本在 Linux 平台对 X11/Wayland 的差异化处理。
/// </summary>
internal static class LinuxBackendDetector
{
    /// <summary>
    /// 缓存的后端类型，避免重复反射查询。
    /// 默认 Unknown，首次调用 <see cref="Detect"/> 后被赋值。
    /// </summary>
    private static LinuxDisplayBackend _cachedBackend = LinuxDisplayBackend.Unknown;

    /// <summary>
    /// 检测当前 GDK 显示后端类型。
    /// 优先级：
    /// 1. <c>GDK_BACKEND</c> 环境变量（用户显式指定）；
    /// 2. <see cref="Display.GetDefault"/> 返回对象的类型名（GdkX11Display / GdkWaylandDisplay 等）；
    /// 3. <c>WAYLAND_DISPLAY</c> / <c>DISPLAY</c> 环境变量启发式判断。
    /// </summary>
    /// <returns>当前后端类型；无法确定时返回 <see cref="LinuxDisplayBackend.Unknown"/>。</returns>
    public static LinuxDisplayBackend Detect()
    {
        if (_cachedBackend != LinuxDisplayBackend.Unknown)
        {
            return _cachedBackend;
        }

        // 1. 优先尊重用户通过 GDK_BACKEND 显式指定的后端。
        var explicitBackend = Environment.GetEnvironmentVariable("GDK_BACKEND");
        if (!string.IsNullOrEmpty(explicitBackend))
        {
            _cachedBackend = explicitBackend.ToLowerInvariant() switch
            {
                "x11" => LinuxDisplayBackend.X11,
                "wayland" => LinuxDisplayBackend.Wayland,
                "broadway" => LinuxDisplayBackend.Broadway,
                _ => LinuxDisplayBackend.Unknown,
            };

            if (_cachedBackend != LinuxDisplayBackend.Unknown)
            {
                return _cachedBackend;
            }
        }

        // 2. 通过 Gdk.Display 的运行时类型名精确判断（GTK 初始化后可用）。
        // GirCore 0.8.0 为不同后端生成不同的托管类：
        //   GdkX11.X11Display → X11；GdkWayland.WaylandDisplay → Wayland；GdkBroadway.BroadwayDisplay → Broadway。
        try
        {
            var display = Display.GetDefault();
            if (display is not null)
            {
                var typeName = display.GetType().Name ?? string.Empty;
                var lower = typeName.ToLowerInvariant();
                if (lower.Contains("x11"))
                {
                    _cachedBackend = LinuxDisplayBackend.X11;
                    return _cachedBackend;
                }

                if (lower.Contains("wayland"))
                {
                    _cachedBackend = LinuxDisplayBackend.Wayland;
                    return _cachedBackend;
                }

                if (lower.Contains("broadway"))
                {
                    _cachedBackend = LinuxDisplayBackend.Broadway;
                    return _cachedBackend;
                }
            }
        }
        catch
        {
            // GTK 尚未初始化或 Display 不可用，回退到环境变量判断。
        }

        // 3. 启发式：WAYLAND_DISPLAY 存在则倾向 Wayland；否则若 DISPLAY 存在则倾向 X11。
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            _cachedBackend = LinuxDisplayBackend.Wayland;
        }
        else if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            _cachedBackend = LinuxDisplayBackend.X11;
        }

        return _cachedBackend;
    }

    /// <summary>
    /// 判断当前后端是否为 X11。
    /// </summary>
    /// <returns>当前为 X11 后端时返回 true。</returns>
    public static bool IsX11() => Detect() == LinuxDisplayBackend.X11;

    /// <summary>
    /// 判断当前后端是否为 Wayland。
    /// </summary>
    /// <returns>当前为 Wayland 后端时返回 true。</returns>
    public static bool IsWayland() => Detect() == LinuxDisplayBackend.Wayland;

    /// <summary>
    /// 当前后端是否支持通过 wmctrl 等 X11 工具控制窗口位置/置顶。
    /// 仅 X11 后端支持；Wayland 下 wmctrl 无效。
    /// </summary>
    /// <returns>支持 wmctrl 时返回 true。</returns>
    public static bool SupportsWindowPositionControl() => IsX11();

    /// <summary>
    /// 当前后端是否支持 EWMH _NET_WM_STATE_ABOVE（窗口置顶）。
    /// X11 通过 wmctrl 支持；Wayland 下需 compositor 专有协议（未通用支持）。
    /// </summary>
    /// <returns>支持置顶时返回 true。</returns>
    public static bool SupportsAlwaysOnTop() => IsX11();

    /// <summary>
    /// 重置缓存的后端类型，仅用于单元测试。
    /// 在测试不同环境变量组合前调用，确保 <see cref="Detect"/> 重新执行检测逻辑。
    /// </summary>
    internal static void ResetForTesting()
    {
        _cachedBackend = LinuxDisplayBackend.Unknown;
    }
}
