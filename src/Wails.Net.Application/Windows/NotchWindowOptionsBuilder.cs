using Wails.Net.Application.Options;

namespace Wails.Net.Application.Windows;

/// <summary>
/// notch 窗口配置到标准窗口选项的映射（RH-HOST-02 抽出）。
/// <para>
/// 对应 Wails v3 Go 版本 <c>notch_window.go</c> 的 <c>webviewOptionsForNotchWindow</c>：
/// 补齐原生形状边距、透明无边框、NSPanel 与非激活浮动面板等固定外观字段。
/// 纯函数，无状态，便于单测与跨平台编译。
/// </para>
/// </summary>
internal static class NotchWindowOptionsBuilder
{
    /// <summary>
    /// 将 notch 窗口配置映射为标准窗口选项。
    /// </summary>
    /// <param name="options">notch 窗口配置。</param>
    /// <returns>标准窗口选项（同时回写 <paramref name="options"/> 的 WindowOptions 字段）。</returns>
    public static WebviewWindowOptions Build(NotchWindowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // 默认值与 Wails v3 Go 版本 notch_window.go 保持一致。
        var width = options.Width > 0 ? options.Width : 660;
        var height = options.Height > 0 ? options.Height : 92;
        var animationSpeed = options.AnimationSpeed > TimeSpan.Zero
            ? options.AnimationSpeed
            : TimeSpan.FromMilliseconds(420);

        // 与 Go webviewOptionsForNotchWindow 一一对应：
        // 原生形状边距 30pt（水平）/ 12pt（底部）。
        const int horizontalInset = 30;
        const int bottomInset = 12;

        var windowOptions = options.WindowOptions;
        windowOptions.Width = width + (2 * horizontalInset);
        windowOptions.Height = height + bottomInset;
        windowOptions.Frameless = true;
        windowOptions.Resizable = false;
        windowOptions.Minimisable = false;
        windowOptions.Maximisable = false;
        windowOptions.MinWidth = 0;
        windowOptions.MinHeight = 0;
        windowOptions.MaxWidth = 0;
        windowOptions.MaxHeight = 0;
        windowOptions.Hidden = false;
        windowOptions.X = 0;
        windowOptions.Y = 0;
        windowOptions.BackgroundType = "transparent";
        windowOptions.R = 0;
        windowOptions.G = 0;
        windowOptions.B = 0;
        windowOptions.A = 0;

        var mac = windowOptions.Mac ??= new WebviewWindowMacOptions();
        mac.Backdrop = 1; // MacBackdropTransparent
        mac.DisableShadow = true;
        mac.CornerType = 1; // MacWindowCornerTypeSquare
        mac.WindowClass = 1; // MacWindowClassPanel
        mac.WindowLevel = 6; // MacWindowLevelPopUpMenu（本项目序号映射）
        mac.PanelPreferences = new MacPanelPreferences
        {
            NonActivating = true,
            FloatingPanel = true,
        };
        // CanJoinAllSpaces | FullScreenAuxiliary | Stationary | IgnoresCycle
        mac.CollectionBehavior = 1 | 256 | 16 | 64;
        mac.Notch = new NotchWindowInfo
        {
            ContentWidth = width,
            ContentHeight = height,
            Animated = options.Animated,
            AnimationSpeedMilliseconds = (int)Math.Max(0, animationSpeed.TotalMilliseconds),
            ScreenId = options.Screen?.Id,
        };

        return windowOptions;
    }
}
