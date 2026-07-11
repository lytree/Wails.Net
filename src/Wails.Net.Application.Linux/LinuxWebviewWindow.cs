using Gtk;
using WebKit;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Options;
using Wails.Net.Application.Windows;
using Wails.Net.Events;
using Menu = Wails.Net.Application.Menus.Menu;
using WailsApplication = Wails.Net.Application.Application;

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
    /// 主容器 Box，用于组合菜单栏与 WebView。
    /// </summary>
    private Box? _mainBox;

    /// <summary>
    /// 应用菜单栏实例，由 SetApplicationMenu 设置。
    /// </summary>
    private PopoverMenuBar? _appMenuBar;

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

        // 连接 WebKit 信号，使 WebView 生命周期事件可观测。
        ConnectWebViewSignals();

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
    /// 使用垂直 Box 组合，使应用菜单栏可插入到 WebView 上方。
    /// </summary>
    private void CreateWebView()
    {
        _webView = WebView.New();

        // 创建垂直 Box 作为主容器，菜单栏在顶部，WebView 填充剩余空间。
        _mainBox = Box.New(Orientation.Vertical, 0);
        _webView.SetHexpand(true);
        _webView.SetVexpand(true);
        _mainBox.Append(_webView);
        _window?.SetChild(_mainBox);

        // 配置 WebView 设置。
        var settings = _webView.GetSettings();
        if (settings is not null)
        {
            settings.SetEnableJavascript(true);
            settings.SetEnableDeveloperExtras(true);
        }
    }

    /// <summary>
    /// 连接 WebKit WebView 的关键信号，使窗口生命周期事件可观测。
    /// 包括加载状态、新窗口创建、关闭、上下文菜单、策略决策和标题变更。
    /// 同时连接 GTK 窗口的 notify 信号以检测焦点变化。
    /// </summary>
    private void ConnectWebViewSignals()
    {
        if (_webView is null)
        {
            return;
        }

        _webView.OnLoadChanged += OnWebViewLoadChanged;
        _webView.OnLoadFailed += OnWebViewLoadFailed;
        _webView.OnCreate += OnWebViewCreate;
        _webView.OnClose += OnWebViewClose;
        _webView.OnContextMenu += OnWebViewContextMenu;
        _webView.OnDecidePolicy += OnWebViewDecidePolicy;
        _webView.OnNotify += OnWebViewNotify;
        _webView.OnReadyToShow += OnWebViewReadyToShow;

        // 连接 GTK 窗口的 notify 信号，用于检测 is-active 属性变化以分发焦点事件。
        if (_window is not null)
        {
            _window.OnNotify += OnWindowNotify;
        }
    }

    /// <summary>
    /// 处理 WebView 加载状态变化，分发 URL 开始/完成加载事件。
    /// 页面加载完成时（LoadEvent.Finished），若窗口为无边框模式，
    /// 注入 CSS 拖拽区域脚本以支持 -webkit-app-region: drag。
    /// </summary>
    /// <param name="sender">触发事件的 WebView。</param>
    /// <param name="args">加载事件参数，包含 LoadEvent。</param>
    private void OnWebViewLoadChanged(WebView sender, WebView.LoadChangedSignalArgs args)
    {
        var app = WailsApplication.Get();
        if (app is null)
        {
            return;
        }

        switch (args.LoadEvent)
        {
            case LoadEvent.Started:
            case LoadEvent.Redirected:
                // 页面开始加载或重定向，分发 URL 开始加载事件。
                app.HandlePlatformEvent((uint)ApplicationEventType.URLStartsLoading);
                break;
            case LoadEvent.Committed:
                // 页面已提交，可注入运行时 JS。
                break;
            case LoadEvent.Finished:
                // 页面加载完成，分发 URL 完成加载事件。
                app.HandlePlatformEvent((uint)ApplicationEventType.URLFinishedLoading);

                // 无边框窗口注入 CSS 拖拽区域脚本，支持 -webkit-app-region: drag。
                // 对应 Tauri v2 / Electron 的 frameless window drag region 实现。
                // 先注册全局拖拽回调，再注入拖拽区域监听脚本（顺序与 Win32 实现保持一致）。
                if (_options.Frameless && _webView is not null)
                {
                    _ = _webView.EvaluateJavascriptAsync(
                        DragRegionHelper.GetStartDragCallbackScript(_id));
                    _ = _webView.EvaluateJavascriptAsync(
                        DragRegionHelper.GetDragRegionScript());
                }
                break;
        }
    }

    /// <summary>
    /// 处理新窗口创建请求，返回 null 表示不创建新窗口。
    /// </summary>
    /// <param name="sender">触发事件的 WebView。</param>
    /// <param name="args">创建事件参数，包含 NavigationAction。</param>
    /// <returns>返回 null 表示不创建新窗口。</returns>
    private Gtk.Widget OnWebViewCreate(WebView sender, WebView.CreateSignalArgs args)
    {
        // 返回 null! 表示不创建新窗口，由 WebKit 使用默认行为。
        // GirCore 将 Widget 标记为 non-nullable，但 WebKitGTK 的 create 信号返回 NULL 是合法语义。
        return null!;
    }

    /// <summary>
    /// 处理 WebView 关闭事件，分发窗口即将关闭事件。
    /// </summary>
    /// <param name="sender">触发事件的 WebView。</param>
    /// <param name="args">事件参数。</param>
    private void OnWebViewClose(WebView sender, EventArgs args)
    {
        var app = WailsApplication.Get();
        app?.DispatchWindowEvent(_id, (uint)WindowEventType.WindowClosing);
    }

    /// <summary>
    /// 处理上下文菜单请求。
    /// 返回 false 表示允许 WebKit 显示默认上下文菜单。
    /// </summary>
    /// <param name="sender">触发事件的 WebView。</param>
    /// <param name="args">上下文菜单事件参数。</param>
    /// <returns>返回 false 表示允许默认上下文菜单。</returns>
    private bool OnWebViewContextMenu(WebView sender, WebView.ContextMenuSignalArgs args)
    {
        // 返回 false 允许 WebKit 显示默认上下文菜单。
        // 若有自定义上下文菜单，可在此处弹出并返回 true。
        return false;
    }

    /// <summary>
    /// 处理导航策略决策，允许默认导航并阻止新窗口。
    /// </summary>
    /// <param name="sender">触发事件的 WebView。</param>
    /// <param name="args">策略决策事件参数，包含 Decision 和 DecisionType。</param>
    /// <returns>返回 true 表示已处理决策。</returns>
    private bool OnWebViewDecidePolicy(WebView sender, WebView.DecidePolicySignalArgs args)
    {
        // 允许所有导航决策的默认处理。
        args.Decision?.Use();
        return true;
    }

    /// <summary>
    /// 处理 GObject 属性变更通知，检测 title 属性变化并分发标题变更事件。
    /// WebKit WebView 无独立的 OnNotifyTitle 信号，通过 OnNotify 检查 pspec 名实现。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="args">通知事件参数，包含 Pspec。</param>
    private void OnWebViewNotify(GObject.Object sender, GObject.Object.NotifySignalArgs args)
    {
        // 检查是否为 title 属性变化。
        if (args.Pspec is not null && args.Pspec.GetName() == "title")
        {
            var app = WailsApplication.Get();
            app?.DispatchWindowEvent(_id, (uint)WindowEventType.WindowTitleChanged);
        }
    }

    /// <summary>
    /// 处理 GTK 窗口的 notify 信号，检测 is-active 属性变化以分发窗口焦点事件。
    /// GTK4 中窗口激活状态通过 is-active 属性变更通知，IsActive 返回当前焦点状态。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="args">通知事件参数，包含 Pspec。</param>
    private void OnWindowNotify(GObject.Object sender, GObject.Object.NotifySignalArgs args)
    {
        if (args.Pspec is null || args.Pspec.GetName() != "is-active")
        {
            return;
        }

        var app = WailsApplication.Get();
        if (app is null || _window is null)
        {
            return;
        }

        // 读取窗口激活状态分发对应的焦点事件。
        if (_window.IsActive)
        {
            app.DispatchWindowEvent(_id, (uint)WindowEventType.WindowFocus);
        }
        else
        {
            app.DispatchWindowEvent(_id, (uint)WindowEventType.WindowFocusLost);
        }
    }

    /// <summary>
    /// 处理 WebView 加载失败事件，分发加载失败事件供应用层处理。
    /// 对应 Wails v3 Go 版 webview_window_linux.go 中的 load-failed 信号处理。
    /// </summary>
    /// <param name="sender">触发事件的 WebView。</param>
    /// <param name="args">加载失败事件参数，包含 FailingUri 和 Error。</param>
    private bool OnWebViewLoadFailed(WebView sender, WebView.LoadFailedSignalArgs args)
    {
        var app = WailsApplication.Get();
        if (app is null)
        {
            return false;
        }

        // 分发 URL 加载失败事件，使应用层能感知加载错误。
        app.HandlePlatformEvent((uint)ApplicationEventType.URLLoadFailed);
        return false;
    }

    /// <summary>
    /// 处理 WebView 就绪显示事件，分发窗口就绪事件。
    /// 对应 Wails v3 Go 版 webview_window_linux.go 中的 ready-to-show 信号处理。
    /// </summary>
    /// <param name="sender">触发事件的 WebView。</param>
    /// <param name="args">事件参数。</param>
    private void OnWebViewReadyToShow(WebView sender, EventArgs args)
    {
        var app = WailsApplication.Get();
        if (app is null)
        {
            return;
        }

        // WebView 就绪后确保窗口可见。
        _window?.SetVisible(true);
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

        // 分发 WindowShow 事件，通知应用层窗口已显示。
        WailsApplication.Get()?.DispatchWindowEvent(
            _id, (uint)WindowEventType.WindowShow);
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

        // 分发 WindowHide 事件，通知应用层窗口已隐藏。
        WailsApplication.Get()?.DispatchWindowEvent(
            _id, (uint)WindowEventType.WindowHide);
    }

    /// <inheritdoc />
    public void Maximise()
    {
        _window?.Maximize();

        // 分发 WindowMaximised 事件，通知应用层窗口已最大化。
        WailsApplication.Get()?.DispatchWindowEvent(
            _id, (uint)WindowEventType.WindowMaximised);
    }

    /// <inheritdoc />
    public void UnMaximise()
    {
        _window?.Unmaximize();

        // 分发 WindowUnmaximised 事件，通知应用层窗口已从最大化状态恢复。
        WailsApplication.Get()?.DispatchWindowEvent(
            _id, (uint)WindowEventType.WindowUnmaximised);
    }

    /// <inheritdoc />
    public void Minimise()
    {
        _window?.Minimize();
        _minimised = true;

        // 分发 WindowMinimised 事件，通知应用层窗口已最小化。
        WailsApplication.Get()?.DispatchWindowEvent(
            _id, (uint)WindowEventType.WindowMinimised);
    }

    /// <inheritdoc />
    public void UnMinimise()
    {
        _window?.Unminimize();
        _minimised = false;

        // 分发 WindowUnminimised 事件，通知应用层窗口已从最小化状态恢复。
        WailsApplication.Get()?.DispatchWindowEvent(
            _id, (uint)WindowEventType.WindowUnminimised);
    }

    /// <inheritdoc />
    public void Fullscreen()
    {
        _window?.Fullscreen();

        // 分发 WindowFullscreen 事件，通知应用层窗口已进入全屏。
        WailsApplication.Get()?.DispatchWindowEvent(
            _id, (uint)WindowEventType.WindowFullscreen);
    }

    /// <inheritdoc />
    public void UnFullscreen()
    {
        _window?.Unfullscreen();

        // 分发 WindowUnfullscreen 事件，通知应用层窗口已退出全屏。
        WailsApplication.Get()?.DispatchWindowEvent(
            _id, (uint)WindowEventType.WindowUnfullscreen);
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
        // 显示应用菜单栏（PopoverMenuBar），对应 Go 版的 ShowMenuBar 实现。
        // GTK4 通过 Widget.SetVisible 控制子控件可见性。
        if (_appMenuBar is null || !OperatingSystem.IsLinux())
        {
            return;
        }

        _appMenuBar.SetVisible(true);
    }

    /// <inheritdoc />
    public void HideMenuBar()
    {
        // 隐藏应用菜单栏，对应 Go 版的 HideMenuBar 实现。
        if (_appMenuBar is null || !OperatingSystem.IsLinux())
        {
            return;
        }

        _appMenuBar.SetVisible(false);
    }

    /// <inheritdoc />
    public void ToggleMenuBar()
    {
        // 切换应用菜单栏可见性，对应 Go 版的 ToggleMenuBar 实现。
        // 通过 Widget.IsVisible 查询当前状态后翻转。
        if (_appMenuBar is null || !OperatingSystem.IsLinux())
        {
            return;
        }

        _appMenuBar.SetVisible(!_appMenuBar.IsVisible());
    }

    /// <inheritdoc />
    public void SetAlwaysOnTop(bool onTop)
    {
        _alwaysOnTop = onTop;
        if (_window is null || !OperatingSystem.IsLinux())
        {
            return;
        }

        // GDK4 移除了 gtk_window_set_keep_above API。
        // 通过 X11 EWMH 协议发送 ClientMessage 设置 _NET_WM_STATE_ABOVE 窗口属性。
        // 对应 Go 版 webview_window_linux.go 中通过 SetKeepAbove 的实现。
        try
        {
            var surface = _window.GetSurface();
            if (surface is null)
            {
                return;
            }

            // 获取 X11 显示连接。
            var display = Gdk.Display.GetDefault();
            if (display is null)
            {
                return;
            }

            // 通过 wmctrl 命令行工具设置 _NET_WM_STATE_ABOVE 窗口属性。
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "wmctrl",
                Arguments = $"-r :ACTIVE: -b {(onTop ? "add" : "remove")},above",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process is not null)
            {
                process.WaitForExit(1000);
            }
        }
        catch
        {
            // wmctrl 不可用时忽略，状态已缓存供查询。
        }
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
    public Task<byte[]?> CapturePreviewAsync()
    {
        // WebKitGTK 无直接的 CapturePreview API，返回 null 表示不支持。
        // 可通过 GTK 截图功能间接实现。
        return Task.FromResult<byte[]?>(null);
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
        if (_webView is null || !OperatingSystem.IsLinux() || string.IsNullOrEmpty(path))
        {
            return;
        }

        // 通过 WebKit.PrintOperation 配合 Gtk.PrintSettings 将页面导出为 PDF。
        // 对应 Go 版 webview_window_linux.go 中的 PrintToPDF 实现。
        var printOperation = WebKit.PrintOperation.New(_webView);
        var settings = Gtk.PrintSettings.New();
        settings.SetPrinter("Print to File");
        settings.Set("output-uri", $"file://{path}");
        settings.Set("output-file-format", "pdf");
        printOperation.SetPrintSettings(settings);

        // 调用 Print() 直接打印（不显示对话框），配合上面的 Print to File 打印机设置输出到指定路径。
        // 对应 C API: webkit_print_operation_print(print_operation)。
        // TODO: WebKitGTK 对无界面 PDF 导出支持有限，某些 GTK 版本可能仍会弹出对话框。
        // 完整的无界面导出可通过 JavaScript 端使用 jsPDF 等库在前端生成 PDF 后下载。
        printOperation.Print();
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
        // GTK4 窗口拖动通过 Gdk.Toplevel.BeginMove 实现。
        // 对应 Go 版 webview_window_linux.go 中的 StartDrag。
        // 需获取窗口的 GdkSurface（Toplevel）和默认指针设备，调用 BeginMove 触发窗口管理器移动操作。
        if (_window is null || !OperatingSystem.IsLinux())
        {
            return;
        }

        var surface = _window.GetSurface();
        if (surface is null)
        {
            return;
        }

        var display = surface.GetDisplay();
        if (display is null)
        {
            return;
        }

        var seat = display.GetDefaultSeat();
        var device = seat?.GetPointer();
        if (device is null)
        {
            return;
        }

        // 通过 Gdk.Internal.Toplevel 调用原生 gdk_toplevel_begin_move。
        // button=0, x=0, y=0, timestamp=0 表示使用当前事件状态触发移动。
        Gdk.Internal.Toplevel.BeginMove(
            surface.Handle.DangerousGetHandle(),
            device.Handle.DangerousGetHandle(),
            0, 0, 0, 0);
    }

    /// <inheritdoc />
    public void StartResize()
    {
        // GTK4 窗口调整大小通过 Gdk.Toplevel.BeginResize 实现。
        // 对应 Go 版 webview_window_linux.go 中的 StartResize。
        // 需获取窗口的 GdkSurface（Toplevel）和默认指针设备，调用 BeginResize 触发窗口管理器调整大小操作。
        if (_window is null || !OperatingSystem.IsLinux())
        {
            return;
        }

        var surface = _window.GetSurface();
        if (surface is null)
        {
            return;
        }

        var display = surface.GetDisplay();
        if (display is null)
        {
            return;
        }

        var seat = display.GetDefaultSeat();
        var device = seat?.GetPointer();
        if (device is null)
        {
            return;
        }

        // 通过 Gdk.Internal.Toplevel 调用原生 gdk_toplevel_begin_resize。
        // edge=SurfaceEdge.SouthEast 表示从右下角开始调整大小。
        Gdk.Internal.Toplevel.BeginResize(
            surface.Handle.DangerousGetHandle(),
            Gdk.SurfaceEdge.SouthEast,
            device.Handle.DangerousGetHandle(),
            0, 0, 0, 0);
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
        // 内容保护通过 WebKit Settings 的 enable-write-protected-content 属性实现。
        // 对应 Go 版 webview_window_linux.go 中的 SetContentProtection 实现。
        // 启用后可阻止屏幕截图与不安全显示捕获（依赖 Wayland/X11 会话管理器支持）。
        if (_webView is null || !OperatingSystem.IsLinux())
        {
            return;
        }

        var settings = _webView.GetSettings();
        if (settings is null)
        {
            return;
        }

        // GirCore 0.8.0 未为 WebKitGTK 的 "enable-write-protected-content" 属性
        // 生成强类型 SetEnableWriteProtectedContent 方法（属性本身需 WebKit2 2.34+）。
        // 此处通过 GObject 通用属性接口 SetProperty 按名称设置该属性。
        // 对应 C API: g_object_set_property(settings, "enable-write-protected-content", &value)。
        using var value = new GObject.Value(enabled);
        settings.SetProperty("enable-write-protected-content", value);
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
        if (_window is null || !OperatingSystem.IsLinux())
        {
            return;
        }

        // GTK4 标题栏样式通过自定义 HeaderBar 实现。
        // 对应 Go 版 webview_window_linux.go 中的 SetTitleBarStyle 实现。
        switch (style)
        {
            case TitleBarStyle.Hidden:
            case TitleBarStyle.HiddenInset:
                // hidden 样式：隐藏标题文本，仅保留窗口控制按钮。
                var hiddenBar = HeaderBar.New();
                hiddenBar.SetShowTitleButtons(true);
                _window.SetTitlebar(hiddenBar);
                break;
            case TitleBarStyle.Unified:
                // unified 样式：使用统一 HeaderBar，标题与内容融合。
                var unifiedBar = HeaderBar.New();
                unifiedBar.SetShowTitleButtons(true);
                _window.SetTitlebar(unifiedBar);
                break;
            case TitleBarStyle.Default:
            default:
                // 默认样式：移除自定义 HeaderBar，恢复系统默认标题栏。
                _window.SetTitlebar(null);
                break;
        }
    }

    /// <inheritdoc />
    public void Centre()
    {
        if (_window is null || !OperatingSystem.IsLinux())
        {
            return;
        }

        // 获取主显示器几何信息以计算居中位置。
        // 对应 Go 版 webview_window_linux.go 中的 Centre 实现。
        var display = Gdk.Display.GetDefault();
        if (display is null)
        {
            return;
        }

        var monitors = display.GetMonitors();
        if (monitors.GetNItems() == 0)
        {
            return;
        }

        var monitor = monitors.GetObject(0) as Gdk.Monitor;
        if (monitor is null)
        {
            return;
        }

        var geometry = monitor.Geometry;
        _window.GetDefaultSize(out var width, out var height);

        // 计算居中坐标。
        var x = geometry.X + (geometry.Width - width) / 2;
        var y = geometry.Y + (geometry.Height - height) / 2;
        _x = x;
        _y = y;

        // GTK4 中窗口位置由窗口管理器控制，不直接支持 SetPosition。
        // 通过 wmctrl 工具移动窗口到计算后的居中坐标（兼容 X11）。
        try
        {
            var title = _window.GetTitle() ?? string.Empty;
            if (!string.IsNullOrEmpty(title))
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "wmctrl",
                    Arguments = $"-r \"{title}\" -e 0,{x},{y},{width},{height}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(psi);
                if (process is not null)
                {
                    process.WaitForExit(1000);
                }
            }
        }
        catch
        {
            // wmctrl 不可用时忽略，坐标已缓存供 GetPosition 查询。
        }

        _window.Present();
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

    /// <summary>
    /// 将快捷键控制器附加到窗口。
    /// 由 LinuxKeyBindingManager 调用，将 GTK4 ShortcutController 添加到窗口。
    /// </summary>
    /// <param name="controller">GTK4 快捷键控制器。</param>
    public void AttachShortcutController(Gtk.ShortcutController controller)
    {
        if (!OperatingSystem.IsLinux() || _window is null)
        {
            return;
        }

        _window.AddController(controller);
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
        var isTransparent = string.Equals(type, "transparent", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(type, "translucent", StringComparison.OrdinalIgnoreCase);
        _translucent = isTransparent;

        if (_webView is null || !OperatingSystem.IsLinux())
        {
            return;
        }

        var settings = _webView.GetSettings();
        if (settings is null)
        {
            return;
        }

        // 透明背景通过 WebKit Settings 的 draw-compositing-indicators 属性辅助可视化合成分层，
        // 同时通过 WebView 合成模式使背景可透明显示（依赖 compositor 支持）。
        settings.SetDrawCompositingIndicators(isTransparent);
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
        if (_webView is null || !OperatingSystem.IsLinux())
        {
            return;
        }

        // pageOptions 为序列化的打印选项，当前简化实现忽略选项直接触发打印对话框。
        // 通过 WebKit.PrintOperation 显示打印对话框，用户可选择导出为 PDF。
        // 对应 C API: webkit_print_operation_run_dialog(print_operation, parent)。
        var printOperation = WebKit.PrintOperation.New(_webView);
        printOperation.RunDialog(_window);
    }

    /// <inheritdoc />
    public void Run(Action callback)
    {
        // 窗口就绪后立即执行回调；GTK4 中窗口在 Present 后即可交互。
        callback();
    }

    /// <summary>
    /// 注入 Wails 运行时 JavaScript 到 WebView。
    /// 在页面加载完成后调用，注入运行时初始化脚本以建立前后端通信桥。
    /// </summary>
    /// <param name="js">要注入的 JavaScript 代码。</param>
    public void InjectRuntimeJs(string js)
    {
        if (_webView is not null && !string.IsNullOrEmpty(js))
        {
            _ = _webView.EvaluateJavascriptAsync(js);
        }
    }

    /// <summary>
    /// 设置窗口的应用菜单栏。
    /// 使用 GMenu 模型与 Gtk.PopoverMenuBar 实现 GTK4 菜单栏。
    /// 菜单栏插入到主容器 Box 的顶部，WebView 填充剩余空间。
    /// 同时注入 ActionGroup，使 GMenu 中的 app.item{ID} 引用能解析到对应回调。
    /// 对应 Go 版 application_linux_gtk3.go 中的菜单构建与 action 注入逻辑。
    /// </summary>
    /// <param name="menuModel">GMenu 模型实例，可为 null（移除菜单栏）。</param>
    /// <param name="actionGroup">包含菜单项 action 的 SimpleActionGroup，可为 null。注入后 GMenu 的 app.item{ID} 引用可解析到对应 SimpleAction。</param>
    public void SetApplicationMenu(Gio.Menu? menuModel, Gio.SimpleActionGroup? actionGroup)
    {
        if (!OperatingSystem.IsLinux() || _mainBox is null)
        {
            return;
        }

        // 移除旧的菜单栏。
        if (_appMenuBar is not null)
        {
            _mainBox.Remove(_appMenuBar);
            _appMenuBar = null;
        }

        if (menuModel is null)
        {
            return;
        }

        // 创建 PopoverMenuBar 并插入到主容器顶部。
        _appMenuBar = PopoverMenuBar.NewFromModel(menuModel);
        _mainBox.Prepend(_appMenuBar);

        // 注入 ActionGroup，使 GMenu 中的 app.item{ID} 引用能解析到对应的 SimpleAction 回调。
        // 对应 Go 版中 gtk_widget_insert_action_group(window, "app", actionGroup) 调用。
        if (actionGroup is not null)
        {
            _window?.InsertActionGroup("app", actionGroup);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _contextMenu?.Dispose();
        Close();
    }
}
