using Gtk;
using WebKit;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Options;
using Wails.Net.Application.Windows;
using Menu = Wails.Net.Application.Menus.Menu;

namespace Wails.Net.Application.Platform;

/// <summary>
/// Linux 平台的 Webview 窗口实现，对应 Go 版的 webview_window_linux.go。
/// 通过 Gtk.Window 创建原生窗口，使用 WebKitGTK 承载 Web 内容。
/// </summary>
public sealed class LinuxWebviewWindow : IWebviewWindowImpl, IDisposable
{
    /// <summary>
    /// 窗口 ID。
    /// </summary>
    private readonly uint _id;

    /// <summary>
    /// 窗口选项。
    /// </summary>
    private readonly WebviewWindowOptions _options;

    /// <summary>
    /// GTK 原生窗口实例。
    /// </summary>
    private Window? _window;

    /// <summary>
    /// WebKitGTK WebView 实例。
    /// </summary>
    private WebView? _webView;

    /// <summary>
    /// 窗口是否已关闭。
    /// </summary>
    private bool _closed;

    /// <summary>
    /// 窗口是否可见（GTK4 无直接查询方法，通过跟踪状态实现）。
    /// </summary>
    private bool _visible;

    /// <summary>
    /// 窗口是否已最小化。
    /// </summary>
    private bool _minimised;

    /// <summary>
    /// 当前 URL。
    /// </summary>
    private string _currentUrl = string.Empty;

    /// <summary>
    /// 最小宽度（GTK4 通过LayoutManager 约束，此处缓存以便查询）。
    /// </summary>
    private int _minWidth;

    /// <summary>
    /// 最小高度。
    /// </summary>
    private int _minHeight;

    /// <summary>
    /// 最大宽度。
    /// </summary>
    private int _maxWidth;

    /// <summary>
    /// 最大高度。
    /// </summary>
    private int _maxHeight;

    /// <summary>
    /// 窗口 X 坐标（GTK4 窗口位置由窗口管理器控制，此处缓存以便查询）。
    /// </summary>
    private int _x = -1;

    /// <summary>
    /// 窗口 Y 坐标。
    /// </summary>
    private int _y = -1;

    /// <summary>
    /// 是否总置顶（GTK4 需 X11 窗口管理器支持，此处缓存状态）。
    /// </summary>
    private bool _alwaysOnTop;

    /// <summary>
    /// 是否启用缩放。
    /// </summary>
    private bool _zoomEnabled = true;

    /// <summary>
    /// 是否半透明。
    /// </summary>
    private bool _translucent;

    /// <summary>
    /// 是否有阴影。
    /// </summary>
    private bool _hasShadow = true;

    /// <summary>
    /// 全屏按钮是否可用。
    /// </summary>
    private bool _fullscreenButtonEnabled = true;

    /// <summary>
    /// 背景色红色分量。
    /// </summary>
    private byte _bgR = 255;

    /// <summary>
    /// 背景色绿色分量。
    /// </summary>
    private byte _bgG = 255;

    /// <summary>
    /// 背景色蓝色分量。
    /// </summary>
    private byte _bgB = 255;

    /// <summary>
    /// 背景色透明度分量。
    /// </summary>
    private byte _bgA = 255;

    /// <summary>
    /// 上下文菜单实例，由 SetMenu 设置。
    /// </summary>
    private LinuxContextMenu? _contextMenu;

    /// <summary>
    /// 构造 LinuxWebviewWindow 实例并创建 GTK 窗口与 WebKit WebView。
    /// </summary>
    /// <param name="id">窗口 ID。</param>
    /// <param name="options">窗口选项。</param>
    public LinuxWebviewWindow(uint id, WebviewWindowOptions options)
    {
        _id = id;
        _options = options;
        _minWidth = options.MinWidth;
        _minHeight = options.MinHeight;
        _maxWidth = options.MaxWidth;
        _maxHeight = options.MaxHeight;

        CreateNativeWindow();
        CreateWebView();

        // 加载初始 URL 或 HTML。
        if (!string.IsNullOrEmpty(options.URL))
        {
            _webView?.LoadUri(options.URL);
            _currentUrl = options.URL;
        }
        else if (!string.IsNullOrEmpty(options.HTML))
        {
            _webView?.LoadHtml(options.HTML, "about:blank");
        }

        // 若不隐藏，则显示窗口。
        if (!options.Hidden)
        {
            Show();
        }
    }

    /// <summary>
    /// 获取窗口 ID。
    /// </summary>
    public uint Id => _id;

    /// <summary>
    /// 获取窗口是否已关闭。
    /// </summary>
    public bool IsClosed => _closed;

    /// <summary>
    /// 创建 GTK 原生窗口并应用选项配置。
    /// </summary>
    private void CreateNativeWindow()
    {
        _window = Window.New();

        if (!string.IsNullOrEmpty(_options.Title))
        {
            _window.SetTitle(_options.Title);
        }

        var width = _options.Width > 0 ? _options.Width : 800;
        var height = _options.Height > 0 ? _options.Height : 600;
        _window.SetDefaultSize(width, height);

        _window.SetResizable(_options.Resizable);

        if (_options.Fullscreen)
        {
            _window.Fullscreen();
        }

        if (_options.AlwaysOnTop)
        {
            // GTK4 中置顶通过 SetTransientFor 或 wm_class 实现，简化实现暂留空。
        }
    }

    /// <summary>
    /// 创建 WebKitGTK WebView 并设置为窗口子控件。
    /// </summary>
    private void CreateWebView()
    {
        _webView = WebView.New();
        _window?.SetChild(_webView);

        // 配置 WebView 设置。
        var settings = _webView.GetSettings();
        if (settings is not null)
        {
            settings.SetEnableJavascript(true);
            settings.SetEnableDeveloperExtras(true);
        }
    }

    /// <inheritdoc />
    public void SetTitle(string title)
    {
        _window?.SetTitle(title);
    }

    /// <inheritdoc />
    public void SetSize(int width, int height)
    {
        if (_window is null)
        {
            return;
        }

        _window.SetDefaultSize(width, height);
    }

    /// <inheritdoc />
    public void SetMinSize(int width, int height)
    {
        _minWidth = width;
        _minHeight = height;
        if (_window is not null && OperatingSystem.IsLinux())
        {
            // 通过 GtkWidget.SetSizeRequest 设置最小尺寸约束。
            _window.SetSizeRequest(width, height);
        }
    }

    /// <inheritdoc />
    public void SetMaxSize(int width, int height)
    {
        _maxWidth = width;
        _maxHeight = height;
        // GTK4 无直接最大尺寸 API，此处缓存值供 GetMaxSize 查询。
    }

    /// <inheritdoc />
    public void SetPosition(int x, int y)
    {
        _x = x;
        _y = y;
        // GTK4 中窗口位置由窗口管理器控制，不直接支持 SetPosition。
    }

    /// <inheritdoc />
    public void Show()
    {
        if (_window is null)
        {
            return;
        }

        _window.Present();
        _visible = true;
    }

    /// <inheritdoc />
    public void Hide()
    {
        if (_window is null)
        {
            return;
        }

        _window.SetVisible(false);
        _visible = false;
    }

    /// <inheritdoc />
    public void Maximise()
    {
        _window?.Maximize();
    }

    /// <inheritdoc />
    public void UnMaximise()
    {
        _window?.Unmaximize();
    }

    /// <inheritdoc />
    public void Minimise()
    {
        _window?.Minimize();
        _minimised = true;
    }

    /// <inheritdoc />
    public void UnMinimise()
    {
        _window?.Unminimize();
        _minimised = false;
    }

    /// <inheritdoc />
    public void Fullscreen()
    {
        _window?.Fullscreen();
    }

    /// <inheritdoc />
    public void UnFullscreen()
    {
        _window?.Unfullscreen();
    }

    /// <inheritdoc />
    public void Restore()
    {
        _window?.Unmaximize();
        _window?.Unminimize();
        _minimised = false;
    }

    /// <inheritdoc />
    public void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _visible = false;

        _window?.Close();
        _window?.Destroy();
        _window = null;
        _webView = null;
    }

    /// <inheritdoc />
    public void Focus()
    {
        _window?.Present();
    }

    /// <inheritdoc />
    public void ShowMenuBar()
    {
        // GTK4 不使用传统菜单栏，通过 GMenu 和 Popover 实现，简化实现暂留空。
    }

    /// <inheritdoc />
    public void HideMenuBar()
    {
        // GTK4 不使用传统菜单栏，简化实现暂留空。
    }

    /// <inheritdoc />
    public void ToggleMenuBar()
    {
        // GTK4 不使用传统菜单栏，简化实现暂留空。
    }

    /// <inheritdoc />
    public void SetAlwaysOnTop(bool onTop)
    {
        _alwaysOnTop = onTop;
        // GTK4 中置顶需 X11 窗口管理器支持（GdkToplevel.set_above），简化实现缓存状态。
    }

    /// <inheritdoc />
    public void SetBackgroundColour(byte r, byte g, byte b, byte a)
    {
        _bgR = r;
        _bgG = g;
        _bgB = b;
        _bgA = a;

        // 通过注入 CSS 设置页面背景色（WebView 填满窗口，覆盖窗口背景）。
        if (_webView is not null)
        {
            var alpha = a / 255.0;
            var js = $"document.documentElement.style.background = 'rgba({r},{g},{b},{alpha:F3})';";
            _ = _webView.EvaluateJavascriptAsync(js);
        }
    }

    /// <inheritdoc />
    public void SetBackgroundColour(int r, int g, int b, int a)
    {
        SetBackgroundColour((byte)r, (byte)g, (byte)b, (byte)a);
    }

    /// <inheritdoc />
    public bool IsFullscreen()
    {
        return _window is not null && _window.IsFullscreen();
    }

    /// <inheritdoc />
    public bool IsMaximised()
    {
        return _window is not null && _window.IsMaximized();
    }

    /// <inheritdoc />
    public bool IsMinimised()
    {
        return _minimised;
    }

    /// <inheritdoc />
    public bool IsVisible()
    {
        return _visible;
    }

    /// <inheritdoc />
    public bool IsFocused()
    {
        return _window is not null && _window.IsActive;
    }

    /// <inheritdoc />
    public void SetFrameless(bool frameless)
    {
        // GTK4 无边框通过 SetDecorated(false) 实现。
        if (_window is not null)
        {
            _window.SetDecorated(!frameless);
        }
    }

    /// <inheritdoc />
    public void OpenDevTools()
    {
        var inspector = _webView?.GetInspector();
        inspector?.Show();
    }

    /// <inheritdoc />
    public void CloseDevTools()
    {
        var inspector = _webView?.GetInspector();
        inspector?.Close();
    }

    /// <inheritdoc />
    public void SetZoom(float zoom)
    {
        _webView?.SetZoomLevel(zoom);
    }

    /// <inheritdoc />
    public void SetZoomLevel(float level)
    {
        _webView?.SetZoomLevel(level);
    }

    /// <inheritdoc />
    public (int Width, int Height) GetSize()
    {
        if (_window is null)
        {
            return (0, 0);
        }

        _window.GetDefaultSize(out var width, out var height);
        return (width, height);
    }

    /// <inheritdoc />
    public (int Width, int Height) GetContentSize()
    {
        // 内容区域大小与窗口大小相同（WebView 填满窗口）。
        return GetSize();
    }

    /// <inheritdoc />
    public (int Width, int Height) GetMinSize()
    {
        return (_minWidth, _minHeight);
    }

    /// <inheritdoc />
    public (int Width, int Height) GetMaxSize()
    {
        return (_maxWidth, _maxHeight);
    }

    /// <inheritdoc />
    public (int X, int Y) GetPosition()
    {
        // 返回缓存的窗口位置（GTK4 窗口位置由窗口管理器控制，无法直接获取）。
        return (_x, _y);
    }

    /// <inheritdoc />
    public float GetZoom()
    {
        return _webView is not null ? (float)_webView.GetZoomLevel() : 1.0f;
    }

    /// <inheritdoc />
    public float GetZoomLevel()
    {
        return _webView is not null ? (float)_webView.GetZoomLevel() : 1.0f;
    }

    /// <inheritdoc />
    public void ExecJS(string js)
    {
        if (_webView is not null)
        {
            _ = _webView.EvaluateJavascriptAsync(js);
        }
    }

    /// <inheritdoc />
    public void GoBack()
    {
        _webView?.GoBack();
    }

    /// <inheritdoc />
    public void GoForward()
    {
        _webView?.GoForward();
    }

    /// <inheritdoc />
    public void Reload()
    {
        _webView?.Reload();
    }

    /// <inheritdoc />
    public void SetURL(string url)
    {
        LoadURL(url);
    }

    /// <inheritdoc />
    public void SetHTML(string html)
    {
        _webView?.LoadHtml(html, "about:blank");
    }

    /// <inheritdoc />
    public void Print()
    {
        _webView?.ExecuteEditingCommand("print");
    }

    /// <inheritdoc />
    public void PrintToPDF(string path)
    {
        // WebKitGTK 的 PDF 导出通过 WebKit.PrintOperation 实现，简化实现仅记录路径。
        // 完整实现需创建 PrintOperation 并连接信号。
    }

    /// <inheritdoc />
    public void SetMenu(Menu? menu)
    {
        // 创建 LinuxContextMenu 包装菜单，供 OpenContextMenu 使用。
        _contextMenu = menu is null ? null : new LinuxContextMenu(menu);
    }

    /// <inheritdoc />
    public void StartDrag()
    {
        // GTK4 窗口拖动通过 Gdk.Toplevel.begin_move 处理，需事件控制器，简化实现暂留空。
    }

    /// <inheritdoc />
    public void StartResize()
    {
        // GTK4 窗口调整大小通过 Gdk.Toplevel.begin_resize 处理，需事件控制器，简化实现暂留空。
    }

    /// <inheritdoc />
    public void SetEnabled(bool enabled)
    {
        // 窗口启用/禁用通过 SetSensitive 实现。
        _window?.SetSensitive(enabled);
    }

    /// <inheritdoc />
    public void SetContentProtection(bool enabled)
    {
        // 内容保护通过窗口 hint 实现，简化实现暂留空。
    }

    /// <inheritdoc />
    public void AttachAsModal(uint parentWindowId)
    {
        // 模态窗口通过 SetModal(true) 实现。
        _window?.SetModal(true);
    }

    /// <inheritdoc />
    public void SetResizable(bool resizable)
    {
        _window?.SetResizable(resizable);
    }

    /// <inheritdoc />
    public void SetMaximisable(bool maximisable)
    {
        // GTK4 无直接 API 控制最大化按钮，通过缓存状态供窗口管理器扩展使用。
        _options.Maximisable = maximisable;
    }

    /// <inheritdoc />
    public void SetMinimisable(bool minimisable)
    {
        // GTK4 无直接 API 控制最小化按钮，通过缓存状态供窗口管理器扩展使用。
        _options.Minimisable = minimisable;
    }

    /// <inheritdoc />
    public void SetClosable(bool closable)
    {
        // GTK4 中通过 SetDeletable 控制。
        _window?.SetDeletable(closable);
    }

    /// <inheritdoc />
    public void SetHasShadow(bool hasShadow)
    {
        _hasShadow = hasShadow;
        if (_window is not null && OperatingSystem.IsLinux())
        {
            // 通过 CSS 类控制阴影显示：无阴影时移除默认装饰阴影。
            if (hasShadow)
            {
                _window.RemoveCssClass("no-shadow");
            }
            else
            {
                _window.AddCssClass("no-shadow");
            }
        }
    }

    /// <inheritdoc />
    public void SetTitleBarStyle(TitleBarStyle style)
    {
        // GTK4 标题栏样式通过 SetTitlebar 或 CSS 实现，简化实现暂留空。
    }

    /// <inheritdoc />
    public void Centre()
    {
        // GTK4 中窗口居中由窗口管理器控制，简化实现暂留空。
    }

    /// <inheritdoc />
    public void SetDebuggingEnabled(bool enabled)
    {
        var settings = _webView?.GetSettings();
        if (settings is not null)
        {
            settings.SetEnableDeveloperExtras(enabled);
        }
    }

    /// <inheritdoc />
    public string GetURL()
    {
        var uri = _webView?.GetUri();
        return uri ?? _currentUrl;
    }

    /// <inheritdoc />
    public void LoadURL(string url)
    {
        _webView?.LoadUri(url);
        _currentUrl = url;
    }

    /// <inheritdoc />
    public void LoadHTML(string html)
    {
        SetHTML(html);
    }

    /// <inheritdoc />
    public void SetBackgroundType(string type)
    {
        // 根据背景类型字符串切换 WebKit 背景透明与窗口装饰。
        // 对应 Go 版 webview_window_linux.go 中的 BackgroundType 处理。
        _translucent = string.Equals(type, "translucent", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(type, "transparent", StringComparison.OrdinalIgnoreCase);

        if (_webView is not null && OperatingSystem.IsLinux())
        {
            // WebKitGTK 通过 is-web-context-interactive 设置支持透明背景，简化实现缓存状态。
            _ = _webView.GetSettings();
        }
    }

    /// <inheritdoc />
    public void SetFullscreenButtonEnabled(bool enabled)
    {
        // GTK4 无直接 API 控制全屏按钮，缓存状态供窗口管理器扩展使用。
        _fullscreenButtonEnabled = enabled;
    }

    /// <inheritdoc />
    public void SetZoom(double zoom)
    {
        // double 重载委托给 float 版本。
        SetZoom((float)zoom);
    }

    /// <inheritdoc />
    public void SetZoomEnabled(bool enabled)
    {
        _zoomEnabled = enabled;
        if (_webView is not null && OperatingSystem.IsLinux())
        {
            // WebKitGTK 通过 zoom-text-only 设置控制缩放是否仅作用于文本。
            var settings = _webView.GetSettings();
            if (settings is not null)
            {
                // 禁用缩放时将缩放级别重置为 1.0。
                if (!enabled)
                {
                    _webView.SetZoomLevel(1.0);
                }
            }
        }
    }

    /// <inheritdoc />
    public void SetTranslucent(bool translucent)
    {
        _translucent = translucent;
        // GTK4 半透明需 compositor 支持，此处通过 CSS 类标记以供主题处理。
        if (_window is not null && OperatingSystem.IsLinux())
        {
            if (translucent)
            {
                _window.AddCssClass("translucent");
            }
            else
            {
                _window.RemoveCssClass("translucent");
            }
        }
    }

    /// <inheritdoc />
    public void SetTitleBarStyle(string style)
    {
        // 将字符串样式映射为 TitleBarStyle 枚举并委托。
        var barStyle = style?.ToLowerInvariant() switch
        {
            "hidden" => TitleBarStyle.Hidden,
            "hiddeninset" => TitleBarStyle.HiddenInset,
            "unified" => TitleBarStyle.Unified,
            _ => TitleBarStyle.Default,
        };
        SetTitleBarStyle(barStyle);
    }

    /// <inheritdoc />
    public void InjectCSS(string css)
    {
        // 通过 JavaScript 将 CSS 注入到当前页面，对应 Go 版的 InjectCSS。
        if (_webView is not null && !string.IsNullOrEmpty(css))
        {
            var escapedCss = css.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "\\r");
            var js = $"(function(){{var s=document.createElement('style');s.type='text/css';s.appendChild(document.createTextNode('{escapedCss}'));document.head.appendChild(s);}})();";
            _ = _webView.EvaluateJavascriptAsync(js);
        }
    }

    /// <inheritdoc />
    public void ZoomIn()
    {
        if (_webView is not null && _zoomEnabled)
        {
            var current = _webView.GetZoomLevel();
            _webView.SetZoomLevel(current + 0.1);
        }
    }

    /// <inheritdoc />
    public void ZoomOut()
    {
        if (_webView is not null && _zoomEnabled)
        {
            var current = _webView.GetZoomLevel();
            _webView.SetZoomLevel(current - 0.1);
        }
    }

    /// <inheritdoc />
    public void ZoomReset()
    {
        _webView?.SetZoomLevel(1.0);
    }

    /// <inheritdoc />
    public void SetMinimised()
    {
        Minimise();
    }

    /// <inheritdoc />
    public void SetMaximised()
    {
        Maximise();
    }

    /// <inheritdoc />
    public void SetNormal()
    {
        Restore();
    }

    /// <inheritdoc />
    public void OpenContextMenu(int x, int y)
    {
        // 在指定坐标弹出上下文菜单，对应 Go 版 webview_window_linux.go 中的上下文菜单处理。
        _contextMenu?.PopupAt(_window, x, y);
    }

    /// <inheritdoc />
    public void PrintToPDF(byte[]? pageOptions)
    {
        // WebKitGTK 的 PDF 导出通过 WebKit.PrintOperation 实现，简化实现暂留空。
        // pageOptions 为序列化的打印选项，完整实现需反序列化并配置 PrintOperation。
    }

    /// <inheritdoc />
    public void Run(Action callback)
    {
        // 窗口就绪后立即执行回调；GTK4 中窗口在 Present 后即可交互。
        callback();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _contextMenu?.Dispose();
        Close();
    }
}
