using Android.Views;
using Android.Webkit;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Windows;
using Menu = Wails.Net.Application.Menus.Menu;
using WailsApplication = Wails.Net.Application.Application;

namespace Wails.Net.Application.Android;

/// <summary>
/// Android 平台的 Webview 窗口实现。
/// 对应 ADR-0002 §2：基于 <c>Android.Webkit.WebView</c>，通过 .NET Android SDK 工作负载直接调用。
/// 大部分桌面专属方法（最大化、最小化、标题栏等）在 Android 上无意义，使用 no-op 实现。
/// </summary>
public sealed class AndroidWebviewWindow : IWebviewWindowImpl
{
    /// <summary>
    /// 窗口 ID。
    /// </summary>
    private readonly uint _id;

    /// <summary>
    /// 窗口配置选项。
    /// </summary>
    private readonly WebviewWindowOptions _options;

    /// <summary>
    /// 所属的 Android 平台应用实例，用于主线程分发。
    /// </summary>
    private readonly AndroidPlatformApp _app;

    /// <summary>
    /// 静态资源服务器引用，用于 <see cref="WailsWebViewClient"/> 拦截请求。
    /// </summary>
    private readonly Wails.Net.AssetServer.AssetServer? _assetServer;

    /// <summary>
    /// Android WebView 原生实例，延迟创建。
    /// </summary>
    private WebView? _webView;

    /// <summary>
    /// 自定义 WebViewClient，用于资源拦截。
    /// </summary>
    private WailsWebViewClient? _webViewClient;

    /// <summary>
    /// IPC 桥的 WebMessageListener 句柄，用于接收前端 postMessage 消息。
    /// </summary>
    private WailsWebMessageListener? _messageListener;

    /// <summary>
    /// 窗口是否可见（Android WebView 无直接查询方法，通过跟踪状态实现）。
    /// </summary>
    private bool _visible;

    /// <summary>
    /// 是否处于全屏模式。
    /// </summary>
    private bool _fullscreen;

    /// <summary>
    /// 当前加载的 URL。
    /// </summary>
    private string _currentUrl;

    /// <summary>
    /// 窗口宽度（缓存以便查询，Android WebView 不直接管理窗口大小）。
    /// </summary>
    private int _width;

    /// <summary>
    /// 窗口高度。
    /// </summary>
    private int _height;

    /// <summary>
    /// 最小宽度。
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
    /// 窗口 X 坐标（Android 窗口位置由系统管理，此处缓存以便查询）。
    /// </summary>
    private int _x;

    /// <summary>
    /// 窗口 Y 坐标。
    /// </summary>
    private int _y;

    /// <summary>
    /// 当前缩放比例（1.0 为 100%）。
    /// </summary>
    private float _zoom = 1.0f;

    /// <summary>
    /// 当前缩放级别。
    /// </summary>
    private float _zoomLevel;

    /// <summary>
    /// 构造 AndroidWebviewWindow 实例。
    /// </summary>
    /// <param name="id">窗口 ID。</param>
    /// <param name="options">窗口配置选项。</param>
    /// <param name="app">所属的 Android 平台应用实例。</param>
    public AndroidWebviewWindow(uint id, WebviewWindowOptions options, AndroidPlatformApp app)
        : this(id, options, app, assetServer: null)
    {
    }

    /// <summary>
    /// 构造 AndroidWebviewWindow 实例，注入 AssetServer 用于资源拦截。
    /// </summary>
    /// <param name="id">窗口 ID。</param>
    /// <param name="options">窗口配置选项。</param>
    /// <param name="app">所属的 Android 平台应用实例。</param>
    /// <param name="assetServer">静态资源服务器引用，可为 null。</param>
    public AndroidWebviewWindow(uint id, WebviewWindowOptions options, AndroidPlatformApp app, Wails.Net.AssetServer.AssetServer? assetServer)
    {
        _id = id;
        _options = options;
        _app = app;
        _assetServer = assetServer;
        _width = options.Width;
        _height = options.Height;
        _minWidth = options.MinWidth;
        _minHeight = options.MinHeight;
        _maxWidth = options.MaxWidth;
        _maxHeight = options.MaxHeight;
        _x = options.X;
        _y = options.Y;
        _currentUrl = options.URL ?? string.Empty;
    }

    /// <inheritdoc />
    public void SetTitle(string title)
    {
        // Android 无窗口标题概念（标题由 Activity/ActionBar 管理），no-op。
    }

    /// <inheritdoc />
    public void SetSize(int width, int height)
    {
        _width = width;
        _height = height;
    }

    /// <inheritdoc />
    public void SetMinSize(int width, int height)
    {
        _minWidth = width;
        _minHeight = height;
    }

    /// <inheritdoc />
    public void SetMaxSize(int width, int height)
    {
        _maxWidth = width;
        _maxHeight = height;
    }

    /// <inheritdoc />
    public void SetPosition(int x, int y)
    {
        // Android 窗口位置由系统窗口管理器控制，此处仅缓存。
        _x = x;
        _y = y;
    }

    /// <inheritdoc />
    public void Show()
    {
        if (_webView is not null)
        {
            _webView.Visibility = ViewStates.Visible;
            _visible = true;
            return;
        }

        _app.DispatchOnMainThread(CreateWebView);
    }

    /// <summary>
    /// 在主线程创建并初始化 WebView 实例。
    /// 对应 ADR-0002 §5：通过 Android.Webkit.WebView 承载 Web 内容。
    /// 注入 <see cref="WailsWebViewClient"/> 实现资源拦截，
    /// 注入 <see cref="WailsWebMessageListener"/> 实现 IPC 桥接。
    /// 当 Activity 引用可用时，通过 <c>Activity.SetContentView</c> 将 WebView 附加到视图层级，
    /// 否则回退到 <c>Application.Context</c>（测试场景，WebView 不可见但逻辑可验证）。
    /// </summary>
    private void CreateWebView()
    {
        var activity = _app.GetActivity();
        if (activity is null)
        {
            // 测试场景回退：无 Activity 时使用 Application.Context（WebView 不可见但可测试逻辑）
            var appContext = global::Android.App.Application.Context;
            if (appContext is null)
            {
                return;
            }

            var testWebView = new WebView(appContext);
            ConfigureWebView(testWebView);
            _webView = testWebView;
            _visible = true;
            LoadContent(testWebView);
            return;
        }

        // 生产路径：用 Activity 创建 WebView 并附加到视图层级
        var webView = new WebView(activity);
        ConfigureWebView(webView);

        // 附加到 Activity 视图：SetContentView 使 WebView 填满整个 Activity
        activity.SetContentView(webView);

        _webView = webView;
        _visible = true;
        LoadContent(webView);
    }

    /// <summary>
    /// 配置 WebView 设置（JS 启用、调试、WebViewClient、IPC 桥接）。
    /// 提取为独立方法，避免生产路径与测试路径重复配置。
    /// </summary>
    /// <param name="webView">要配置的 WebView 实例。</param>
    private void ConfigureWebView(WebView webView)
    {
        // 配置 WebView 设置
        var settings = webView.Settings;
        settings.JavaScriptEnabled = true;
        settings.DomStorageEnabled = true;
        settings.AllowFileAccess = true;
        settings.AllowContentAccess = true;

        // 开发模式下启用 WebView 调试（通过 chrome://inspect 远程调试）
        WebView.SetWebContentsDebuggingEnabled(true);

        // 设置自定义 WebViewClient 用于资源拦截
        // P0-4：传递窗口名称以支持 per-window CSP 注入
        _webViewClient = new WailsWebViewClient(_assetServer, _options.Name);
        webView.SetWebViewClient(_webViewClient);

        // 设置 WebChromeClient，转发 console.log / alert / 等到 Android Logcat
        // 没有它，前端 JS 的 console.log 不会出现在 logcat，调试困难
        webView.SetWebChromeClient(new WailsWebChromeClient());

        // 注册 IPC 桥接对象，通过 AddJavascriptInterface 注入到 JS 全局。
        // 对应 ADR-0002 §5：前端通过 window.WailsBridge.postMessage(json) 调用后端。
        // 注：.NET Android 工作负载的 Android.Webkit.WebView 不直接提供 AddWebMessageListener
        // （该方法属于 AndroidX WebKit 扩展），改用 AddJavascriptInterface 是标准做法。
        _messageListener = new WailsWebMessageListener(_id);
        webView.AddJavascriptInterface(_messageListener, "WailsBridge");
    }

    /// <summary>
    /// 加载 URL 或 HTML 内容到 WebView。
    /// 提取为独立方法，避免生产路径与测试路径重复加载逻辑。
    /// </summary>
    /// <param name="webView">要加载内容的 WebView 实例。</param>
    private void LoadContent(WebView webView)
    {
        if (!string.IsNullOrEmpty(_currentUrl))
        {
            webView.LoadUrl(_currentUrl);
        }
        else if (_options.HTML is not null)
        {
            webView.LoadDataWithBaseURL(null, _options.HTML, "text/html", "utf-8", null);
        }
    }

    /// <inheritdoc />
    public void Hide()
    {
        if (_webView is not null)
        {
            _webView.Visibility = ViewStates.Invisible;
        }

        _visible = false;
    }

    /// <inheritdoc />
    public void Maximise()
    {
        // Android 无窗口最大化概念（应用全屏由 Activity 管理），no-op。
    }

    /// <inheritdoc />
    public void UnMaximise()
    {
        // Android 无窗口最大化概念，no-op。
    }

    /// <inheritdoc />
    public void Minimise()
    {
        // Android 无窗口最小化概念（Home 键由系统管理），no-op。
    }

    /// <inheritdoc />
    public void UnMinimise()
    {
        // Android 无窗口最小化概念，no-op。
    }

    /// <inheritdoc />
    public void Fullscreen()
    {
        _fullscreen = true;
        // 完整实现需通过 Activity.Window 设置全屏 Flag：
        //   activity.Window.AddFlags(WindowManagerFlags.Fullscreen);
        //   activity.Window.ClearFlags(WindowManagerFlags.ForceNotFullscreen);
    }

    /// <inheritdoc />
    public void UnFullscreen()
    {
        _fullscreen = false;
        // 完整实现需清除全屏 Flag。
    }

    /// <inheritdoc />
    public void Restore()
    {
        // Android 无窗口恢复概念，no-op。
    }

    /// <inheritdoc />
    public void Close()
    {
        if (_webView is not null)
        {
            _app.DispatchOnMainThread(() =>
            {
                _webView.Destroy();
                _webView = null;
            });
        }

        _visible = false;
    }

    /// <inheritdoc />
    public void Focus()
    {
        _webView?.RequestFocus();
    }

    /// <inheritdoc />
    public void ShowMenuBar()
    {
        // Android 无菜单栏概念，no-op。
    }

    /// <inheritdoc />
    public void HideMenuBar()
    {
        // Android 无菜单栏概念，no-op。
    }

    /// <inheritdoc />
    public void ToggleMenuBar()
    {
        // Android 无菜单栏概念，no-op。
    }

    /// <inheritdoc />
    public void SetAlwaysOnTop(bool onTop)
    {
        // Android 无窗口置顶概念（系统级窗口权限 required），no-op。
    }

    /// <inheritdoc />
    public void SetBackgroundColour(byte r, byte g, byte b, byte a)
    {
        if (_webView is not null)
        {
            _webView.SetBackgroundColor(new global::Android.Graphics.Color(r, g, b, a));
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
        return _fullscreen;
    }

    /// <inheritdoc />
    public bool IsMaximised()
    {
        // Android 无窗口最大化概念，始终返回 false。
        return false;
    }

    /// <inheritdoc />
    public bool IsMinimised()
    {
        // Android 无窗口最小化概念，始终返回 false。
        return false;
    }

    /// <inheritdoc />
    public bool IsVisible()
    {
        return _visible;
    }

    /// <inheritdoc />
    public bool IsFocused()
    {
        return _webView?.HasFocus == true;
    }

    /// <inheritdoc />
    public void SetFrameless(bool frameless)
    {
        // Android 无窗口边框概念，no-op。
    }

    /// <inheritdoc />
    public void OpenDevTools()
    {
        // Android WebView 无内置开发者工具，通过 chrome://inspect 远程调试，no-op。
    }

    /// <inheritdoc />
    public void CloseDevTools()
    {
        // Android WebView 无内置开发者工具，no-op。
    }

    /// <inheritdoc />
    public void SetZoom(float zoom)
    {
        _zoom = zoom;
        // Android WebView 无直接 SetZoom 方法，通过 CSS zoom 样式实现缩放。
        _webView?.EvaluateJavascript($"document.body.style.zoom = '{zoom}'", null);
    }

    /// <inheritdoc />
    public void SetZoomLevel(float level)
    {
        _zoomLevel = level;
        // Android WebView 无 zoom level 概念（使用 SetZoom 代替）。
    }

    /// <inheritdoc />
    public (int Width, int Height) GetSize()
    {
        return (_width, _height);
    }

    /// <inheritdoc />
    public (int Width, int Height) GetContentSize()
    {
        // Android WebView 内容区域与窗口大小一致。
        return (_width, _height);
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
        return (_x, _y);
    }

    /// <inheritdoc />
    public float GetZoom()
    {
        return _zoom;
    }

    /// <inheritdoc />
    public float GetZoomLevel()
    {
        return _zoomLevel;
    }

    /// <inheritdoc />
    public void ExecJS(string js)
    {
        _webView?.EvaluateJavascript(js, null);
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
        _currentUrl = url;
        _webView?.LoadUrl(url);
    }

    /// <inheritdoc />
    public void SetHTML(string html)
    {
        _currentUrl = string.Empty;
        _webView?.LoadDataWithBaseURL(null, html, "text/html", "utf-8", null);
    }

    /// <inheritdoc />
    public void Print()
    {
        // Android WebView 打印需通过 PrintManager + PrintDocumentAdapter，骨架实现 no-op。
        // 完整实现：
        //   var adapter = _webView?.CreatePrintDocumentAdapter("document");
        //   var printManager = context.GetSystemService(Context.PrintService) as PrintManager;
        //   printManager?.Print("document", adapter, null);
    }

    /// <inheritdoc />
    public void PrintToPDF(string path)
    {
        // Android WebView 不原生支持 PDF 导出，需通过 PrintManager 间接实现。
        throw new NotSupportedException("Android WebView 不支持直接导出 PDF，请使用 PrintManager 间接打印。");
    }

    /// <inheritdoc />
    public void SetMenu(Menu? menu)
    {
        // Android 无窗口菜单概念（使用 ActionBar/Toolbar 代替），no-op。
    }

    /// <inheritdoc />
    public void StartDrag()
    {
        // Android 无窗口拖动概念，no-op。
    }

    /// <inheritdoc />
    public void StartResize()
    {
        // Android 无窗口手动调整大小概念，no-op。
    }

    /// <inheritdoc />
    public void SetEnabled(bool enabled)
    {
        _webView?.Enabled = enabled;
    }

    /// <inheritdoc />
    public void SetContentProtection(bool enabled)
    {
        // Android 无内容保护概念（FLAG_SECURE 为 Activity 级别），no-op。
    }

    /// <inheritdoc />
    public void AttachAsModal(uint parentWindowId)
    {
        // Android 模态窗口需通过 Dialog 实现，骨架实现 no-op。
    }

    /// <inheritdoc />
    public void SetResizable(bool resizable)
    {
        // Android 窗口大小由系统管理，no-op。
    }

    /// <inheritdoc />
    public void SetMaximisable(bool maximisable)
    {
        // Android 无窗口最大化概念，no-op。
    }

    /// <inheritdoc />
    public void SetMinimisable(bool minimisable)
    {
        // Android 无窗口最小化概念，no-op。
    }

    /// <inheritdoc />
    public void SetClosable(bool closable)
    {
        // Android 窗口关闭由 Activity 生命周期管理，no-op。
    }

    /// <inheritdoc />
    public void SetHasShadow(bool hasShadow)
    {
        // Android 无窗口阴影概念，no-op。
    }

    /// <inheritdoc />
    public void SetTitleBarStyle(TitleBarStyle style)
    {
        // Android 无标题栏样式概念（由 Activity 主题控制），no-op。
    }

    /// <inheritdoc />
    public void Centre()
    {
        // Android 窗口位置由系统管理，no-op。
    }

    /// <inheritdoc />
    public void SetDebuggingEnabled(bool enabled)
    {
        WebView.SetWebContentsDebuggingEnabled(enabled);
    }

    /// <inheritdoc />
    public string GetURL()
    {
        return _webView?.Url ?? _currentUrl;
    }

    /// <inheritdoc />
    public void LoadURL(string url)
    {
        _currentUrl = url;
        _webView?.LoadUrl(url);
    }

    /// <inheritdoc />
    public void LoadHTML(string html)
    {
        _currentUrl = string.Empty;
        _webView?.LoadDataWithBaseURL(null, html, "text/html", "utf-8", null);
    }
}
