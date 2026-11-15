using Wails.Net.Application.Options;
using Wails.Net.Application.Hosting;

namespace Wails.Net.Application.Windows;

/// <summary>
/// 配置驱动的窗口初始化器（RH-HOST-02）。
/// <para>
/// 承载 <c>DesktopApplicationBuilder.Build</c> 传入的窗口配置，并在 <see cref="Wails.Net.Application.Application.Run"/>
/// 的 UI 线程上按配置创建窗口。窗口不在 Build（主线程）创建，避免 Win32 窗口线程亲和性死锁。
/// </para>
/// <para>
/// 创建动作通过委托注入（<see cref="CreateAll"/> 的 <c>create</c> 参数），以保留
/// <see cref="Wails.Net.Application.Application.CreateWebviewWindow"/> 的虚方法可扩展性。
/// </para>
/// </summary>
internal sealed class ConfiguredWindowFactory
{
    private List<WindowConfig>? _configuredWindows;
    private DesktopHostOptions.WindowOptions? _defaultWindow;

    /// <summary>
    /// 已配置的窗口数量（诊断用）；无配置时为 0。
    /// </summary>
    public int ConfiguredWindowCount => _configuredWindows?.Count ?? 0;

    /// <summary>
    /// 是否存在任何配置（多窗口列表或默认窗口）。
    /// </summary>
    public bool HasConfiguration => _configuredWindows is { Count: > 0 } || _defaultWindow is not null;

    /// <summary>
    /// 设置窗口配置，由 <c>DesktopApplicationBuilder.Build</c> 调用。
    /// </summary>
    /// <param name="configuredWindows">多窗口配置列表，可为 null。</param>
    /// <param name="defaultWindow">默认窗口配置，可为 null。</param>
    public void SetConfigs(
        List<WindowConfig>? configuredWindows,
        DesktopHostOptions.WindowOptions? defaultWindow)
    {
        _configuredWindows = configuredWindows;
        _defaultWindow = defaultWindow;
    }

    /// <summary>
    /// 按配置创建所有窗口。仅在 <see cref="Wails.Net.Application.Application.Run"/> 的 UI 线程上调用，
    /// 且调用方需保证未设置 <c>OnAfterStart</c>（否则用户回调自行创建窗口，避免双重窗口）。
    /// </summary>
    /// <param name="create">实际窗口创建委托（通常为 <c>Application.CreateWebviewWindow</c>）。</param>
    /// <returns>创建的窗口数量。</returns>
    public int CreateAll(Func<WebviewWindowOptions, WebviewWindow> create)
    {
        ArgumentNullException.ThrowIfNull(create);

        if (_configuredWindows is { Count: > 0 } windows)
        {
            foreach (var cfg in windows)
            {
                create(new WebviewWindowOptions
                {
                    Name = cfg.Name ?? string.Empty,
                    Title = cfg.Title ?? "Wails.Net",
                    Width = cfg.Width,
                    Height = cfg.Height,
                    Resizable = cfg.Resizable,
                    Centered = cfg.Centered,
                    Fullscreen = cfg.Fullscreen,
                    Frameless = cfg.Frameless,
                    AlwaysOnTop = cfg.AlwaysOnTop,
                    URL = cfg.Url ?? string.Empty,
                });
            }

            return windows.Count;
        }

        if (_defaultWindow is not null)
        {
            var window = _defaultWindow;
            create(new WebviewWindowOptions
            {
                Title = window.Title,
                Width = window.Width,
                Height = window.Height,
                Frameless = window.Frameless,
            });

            return 1;
        }

        return 0;
    }
}
