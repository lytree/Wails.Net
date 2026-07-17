using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Wails.Net.Application.Options;
using Wails.Net.Application.Windows;
using Wails.Net.Events;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;
using Menu = Wails.Net.Application.Menus.Menu;

namespace Wails.Net.Application.Platform;

/// <summary>
/// Win32 平台的 Webview 窗口实现，对应 Go 版的 webview_window_windows.go。
/// 通过 Win32 CreateWindowEx 创建原生窗口，使用 WebView2 承载 Web 内容。
/// WebView2 初始化为异步操作，在消息循环中完成后即可正常使用。
/// </summary>
public sealed class Win32WebviewWindow : IWebviewWindowImpl, IDisposable
{
    /// <summary>
    /// 窗口类名称，用于 RegisterClassEx 注册。
    /// </summary>
    internal const string WindowClassName = "WailsNetWebviewWindow";

    /// <summary>
    /// WM_DESTROY 消息常量（0x0002），窗口销毁时收到。
    /// </summary>
    private const uint WmDestroy = 0x0002;

    /// <summary>
    /// WM_CLOSE 消息常量（0x0010），窗口关闭时收到。
    /// </summary>
    private const uint WmClose = 0x0010;

    /// <summary>
    /// WM_SIZE 消息常量（0x0005），窗口大小改变时收到。
    /// </summary>
    private const uint WmSize = 0x0005;

    /// <summary>
    /// WM_COMMAND 消息常量（0x0111），菜单命令或加速键触发时收到。
    /// </summary>
    private const uint WmCommand = 0x0111;

    /// <summary>
    /// WM_SYSCOMMAND 消息常量（0x0112），系统菜单命令触发时收到。
    /// </summary>
    private const uint WmSysCommand = 0x0112;

    /// <summary>
    /// WM_GETMINMAXINFO 消息常量（0x0024），窗口大小约束查询时收到。
    /// </summary>
    private const uint WmGetMinMaxInfo = 0x0024;

    /// <summary>
    /// WM_DPICHANGED 消息常量（0x02E0），DPI 变化时收到。
    /// </summary>
    private const uint WmDpiChanged = 0x02E0;

    /// <summary>
    /// WM_HOTKEY 消息常量（0x0312），全局热键触发时收到。
    /// </summary>
    private const uint WmHotkey = 0x0312;

    /// <summary>
    /// WM_DROPFILES 消息常量（0x0233），文件拖放时收到。
    /// </summary>
    private const uint WmDropFiles = 0x0233;

    /// <summary>
    /// WM_SETTINGCHANGE 消息常量（0x001A），系统设置（如主题）变化时收到。
    /// </summary>
    private const uint WmSettingChange = 0x001A;

    /// <summary>
    /// WM_MOVE 消息常量（0x0003），窗口移动时收到。
    /// </summary>
    private const uint WmMove = 0x0003;

    /// <summary>
    /// WM_NCLBUTTONDOWN 消息常量（0x00A1），非客户区左键按下。
    /// </summary>
    private const uint WmNclButtonDown = 0x00A1;

    /// <summary>
    /// WM_SETICON 消息常量（0x0080），设置窗口图标。
    /// </summary>
    private const uint WmSetIcon = 0x0080;

    /// <summary>
    /// WM_ACTIVATE 消息常量（0x0006），窗口激活或失活时收到。
    /// </summary>
    private const uint WmActivate = 0x0006;

    /// <summary>
    /// WM_DISPLAYCHANGE 消息常量（0x007E），显示器配置变化时收到。
    /// wParam 为每像素位数，lParam 低字为新水平分辨率，高字为新垂直分辨率。
    /// </summary>
    private const uint WmDisplayChange = 0x007E;

    /// <summary>
    /// WM_CLIPBOARDUPDATE 消息常量（0x031D），剪贴板内容变化时收到。
    /// 需通过 AddClipboardFormatListener 注册窗口后才能接收。
    /// </summary>
    private const uint WmClipboardUpdate = 0x031D;

    /// <summary>
    /// WM_KEYDOWN 消息常量（0x0100），键盘按键按下时收到。
    /// </summary>
    private const uint WmKeyDown = 0x0100;

    /// <summary>
    /// WM_CONTEXTMENU 消息常量（0x007B），窗口收到右键菜单请求时触发。
    /// </summary>
    private const uint WmContextMenu = 0x007B;

    /// <summary>
    /// VK_F12 虚拟键码（0x7B），用于打开 DevTools。
    /// </summary>
    private const uint VkF12 = 0x7B;

    /// <summary>
    /// SC_CLOSE 系统命令 ID（0xF060）。
    /// </summary>
    private const uint ScClose = 0xF060;

    /// <summary>
    /// HTCAPTION 命中测试值（2），表示标题栏区域，用于拖动。
    /// </summary>
    private const nint Htcaption = 2;

    /// <summary>
    /// HTBOTTOMRIGHT 命中测试值（17），表示右下角，用于调整大小。
    /// </summary>
    private const nint HtBottomRight = 17;

    /// <summary>
    /// GWL_STYLE 索引（-16），用于 GetWindowLongPtrW/SetWindowLongPtrW 获取/设置窗口样式。
    /// </summary>
    private const int GwlStyle = -16;

    /// <summary>
    /// GWL_EXSTYLE 索引（-20），用于 GetWindowLongPtrW/SetWindowLongPtrW 获取/设置扩展窗口样式。
    /// </summary>
    private const int GwlExStyle = -20;

    /// <summary>
    /// LWA_ALPHA 标志（0x00000002），用于 SetLayeredWindowAttributes 按透明度设置。
    /// </summary>
    private const uint LwaAlpha = 0x00000002;

    /// <summary>
    /// HWND_MESSAGE 常量（-3），用于创建消息-only 窗口。
    /// </summary>
    private static readonly IntPtr HwndMessage = new(-3);

    /// <summary>
    /// Win32 CW_USEDEFAULT 常量，用于 CreateWindowEx 使用默认位置/尺寸。
    /// CsWin32 未生成此常量，此处手动定义为 ((int)0x80000000)。
    /// </summary>
    private const int CW_USEDEFAULT = unchecked((int)0x80000000);

    /// <summary>
    /// 全局窗口类注册锁，确保只注册一次。
    /// </summary>
    private static readonly object _classLock = new();

    /// <summary>
    /// 全局窗口过程委托引用，防止 GC 回收导致回调失效。
    /// </summary>
    private static WNDPROC? _wndProc;

    /// <summary>
    /// 全局窗口实例表，按 HWND 句柄索引，用于窗口过程分发。
    /// </summary>
    private static readonly Dictionary<IntPtr, Win32WebviewWindow> _instancesByHwnd = new();

    /// <summary>
    /// JSON 序列化选项，用于 IPC 响应序列化。
    /// </summary>
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// 全局实例表锁，保护 _instancesByHwnd 字典。
    /// </summary>
    private static readonly object _instancesLock = new();

    /// <summary>
    /// 窗口 ID。
    /// </summary>
    private readonly uint _id;

    /// <summary>
    /// 窗口选项。
    /// </summary>
    private readonly WebviewWindowOptions _options;

    /// <summary>
    /// Win32 窗口句柄。
    /// </summary>
    private HWND _hwnd;

    /// <summary>
    /// WebView2 控制器，初始化完成后可用。
    /// </summary>
    private CoreWebView2Controller? _controller;

    /// <summary>
    /// WebView2 核心 Webview 实例，初始化完成后可用。
    /// </summary>
    private CoreWebView2? _webview;

    /// <summary>
    /// WebView2 异步初始化任务源，用于等待初始化完成。
    /// </summary>
    private readonly TaskCompletionSource<bool> _initTcs = new();

    /// <summary>
    /// 窗口是否已关闭。
    /// </summary>
    private bool _closed;

    /// <summary>
    /// 当前 URL。
    /// </summary>
    private string _currentUrl = string.Empty;

    /// <summary>
    /// 最小宽度，用于 WM_GETMINMAXINFO 约束。
    /// </summary>
    private int _minWidth;

    /// <summary>
    /// 最小高度，用于 WM_GETMINMAXINFO 约束。
    /// </summary>
    private int _minHeight;

    /// <summary>
    /// 最大宽度，用于 WM_GETMINMAXINFO 约束（0 表示不限制）。
    /// </summary>
    private int _maxWidth;

    /// <summary>
    /// 最大高度，用于 WM_GETMINMAXINFO 约束（0 表示不限制）。
    /// </summary>
    private int _maxHeight;

    /// <summary>
    /// 是否处于全屏模式。
    /// </summary>
    private bool _isFullscreen;

    /// <summary>
    /// 全屏前保存的窗口样式，用于退出全屏时恢复。
    /// </summary>
    private WINDOW_STYLE _savedStyle;

    /// <summary>
    /// 全屏前保存的窗口矩形，用于退出全屏时恢复。
    /// </summary>
    private RECT _savedRect;

    /// <summary>
    /// 窗口级菜单句柄（区别于应用级菜单），由 SetMenu 设置。
    /// </summary>
    private HMENU _windowMenu = default;

    /// <summary>
    /// 上下文菜单句柄，由 SetMenu 设置用于弹出式显示。
    /// </summary>
    private HMENU _contextMenu;

    /// <summary>
    /// 标记运行时 JS 是否已注入，避免重复注入。
    /// </summary>
    private bool _runtimeInjected;

    /// <summary>
    /// 窗口是否曾处于最小化状态，用于 SIZE_RESTORED 时判断是否应触发 WindowUnminimised 事件。
    /// </summary>
    private bool _wasMinimised;

    /// <summary>
    /// 窗口是否曾处于最大化状态，用于 SIZE_RESTORED 时判断是否应触发 WindowUnmaximised 事件。
    /// </summary>
    private bool _wasMaximised;

    /// <summary>
    /// 构造 Win32WebviewWindow 实例并创建原生窗口。
    /// </summary>
    /// <param name="id">窗口 ID。</param>
    /// <param name="options">窗口选项。</param>
    public Win32WebviewWindow(uint id, WebviewWindowOptions options)
    {
        _id = id;
        _options = options;

        EnsureWindowClassRegistered();
        CreateNativeWindow();

        // 注册实例到全局表，供窗口过程查找。
        lock (_instancesLock)
        {
            _instancesByHwnd[(IntPtr)_hwnd] = this;
        }

        // 应用初始窗口选项。
        ApplyInitialOptions();

        // 启动 WebView2 异步初始化，不阻塞构造函数。
        _ = InitializeWebViewAsync();
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
    /// 获取 Win32 窗口句柄。
    /// </summary>
    internal HWND Hwnd => _hwnd;

    /// <summary>
    /// 等待 WebView2 初始化完成。
    /// </summary>
    /// <returns>表示初始化完成的任务。</returns>
    public Task WaitForInitializationAsync() => _initTcs.Task;

    /// <summary>
    /// 注册窗口类（全局只注册一次）。
    /// </summary>
    internal static void EnsureWindowClassRegistered()
    {
        if (_wndProc is not null)
        {
            return;
        }

        lock (_classLock)
        {
            if (_wndProc is not null)
            {
                return;
            }

            _wndProc = StaticWindowProc;
            unsafe
            {
                fixed (char* className = WindowClassName)
                {
                    var wcx = new WNDCLASSEXW
                    {
                        cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                        lpfnWndProc = _wndProc,
                        hInstance = default,
                        hCursor = default,
                        hbrBackground = default,
                        lpszClassName = className,
                    };
                    PInvoke.RegisterClassEx(in wcx);
                }
            }
        }
    }

    /// <summary>
    /// 创建 Win32 原生窗口。
    /// </summary>
    private void CreateNativeWindow()
    {
        var style = WINDOW_STYLE.WS_OVERLAPPEDWINDOW;

        var x = _options.X < 0 ? CW_USEDEFAULT : _options.X;
        var y = _options.Y < 0 ? CW_USEDEFAULT : _options.Y;
        var width = _options.Width <= 0 ? CW_USEDEFAULT : _options.Width;
        var height = _options.Height <= 0 ? CW_USEDEFAULT : _options.Height;

        unsafe
        {
            _hwnd = PInvoke.CreateWindowEx(
                dwExStyle: 0,
                lpClassName: WindowClassName,
                lpWindowName: _options.Title,
                dwStyle: style,
                X: x,
                Y: y,
                nWidth: width,
                nHeight: height,
                hWndParent: default,
                hMenu: null,
                hInstance: null,
                lpParam: null);
        }

        if (_hwnd.IsNull)
        {
            throw new InvalidOperationException($"创建 Win32 窗口失败，窗口 ID: {_id}，错误码: {Marshal.GetLastWin32Error()}");
        }

        // 启用文件拖放接收，使窗口能够收到 WM_DROPFILES 消息。
        // 对应 Wails v3 Go 版本中的 DragAcceptFiles 调用。
        PInvoke.DragAcceptFiles(_hwnd, true);

        // 注册剪贴板格式监听器，使窗口能够收到 WM_CLIPBOARDUPDATE 消息。
        // 对应 Wails v3 的 ClipboardChanged 事件监听。
        PInvoke.AddClipboardFormatListener(_hwnd);
    }

    /// <summary>
    /// 应用初始窗口选项（从 WebviewWindowOptions 读取）。
    /// </summary>
    private void ApplyInitialOptions()
    {
        if (_options.MinWidth > 0 || _options.MinHeight > 0)
        {
            SetMinSize(_options.MinWidth, _options.MinHeight);
        }

        if (_options.MaxWidth > 0 || _options.MaxHeight > 0)
        {
            SetMaxSize(_options.MaxWidth, _options.MaxHeight);
        }

        if (_options.Frameless)
        {
            SetFrameless(true);
        }

        if (_options.AlwaysOnTop)
        {
            SetAlwaysOnTop(true);
        }

        if (_options.Minimised)
        {
            SetMinimised();
        }
        else if (_options.Maximised)
        {
            SetMaximised();
        }

        if (_options.Fullscreen)
        {
            Fullscreen();
        }

        if (!_options.Resizable)
        {
            SetResizable(false);
        }

        if (!_options.Maximisable)
        {
            SetMaximisable(false);
        }

        if (!_options.Minimisable)
        {
            SetMinimisable(false);
        }

        if (!_options.Closable)
        {
            SetClosable(false);
        }

        if (_options.Centered)
        {
            Centre();
        }

        if (_options.Zoom != 1.0)
        {
            SetZoom(_options.Zoom);
        }

        if (!_options.ZoomEnabled)
        {
            SetZoomEnabled(false);
        }
    }

    /// <summary>
    /// 异步初始化 WebView2 控制器和 Webview 实例。
    /// </summary>
    private async Task InitializeWebViewAsync()
    {
        try
        {
            var hwndPtr = (IntPtr)_hwnd;
            var environment = await CoreWebView2Environment.CreateAsync().ConfigureAwait(true);
            var controller = await environment.CreateCoreWebView2ControllerAsync(hwndPtr).ConfigureAwait(true);
            _controller = controller;
            _webview = controller.CoreWebView2;

            // 设置 WebView2 边界为窗口客户区大小。
            UpdateBounds();

            // 设置初始缩放。
            if (_options.Zoom != 1.0)
            {
                _controller.ZoomFactor = _options.Zoom;
            }

            // 设置调试模式。
            _webview.Settings.AreDevToolsEnabled = _options.ShowDevmodeEnabled;

            // 注册 WebResourceRequested 过滤器，拦截 wails.localhost 的所有请求。
            // 对应 Wails v3 Go 版本 webview_window_windows.go 中的 AddWebResourceRequestedFilter 调用。
            _webview.AddWebResourceRequestedFilter(
                "http://wails.localhost/*", CoreWebView2WebResourceContext.All);
            _webview.AddWebResourceRequestedFilter(
                "https://wails.localhost/*", CoreWebView2WebResourceContext.All);
            _webview.WebResourceRequested += OnWebResourceRequested;

            // 注册 WebMessageReceived 事件，接收前端 postMessage 消息。
            // 对应 Wails v3 Go 版本中的 WebMessageReceived 事件处理。
            _webview.WebMessageReceived += OnWebMessageReceived;

            // 注册 NavigationCompleted 事件，导航完成后注入拖拽区域等辅助脚本。
            // 对应 Wails v3 Go 版本中的 NavigationCompleted 事件处理。
            _webview.NavigationCompleted += OnNavigationCompleted;

            // 注入 Wails 运行时 JS（必须在导航之前！）
            // 使用 AddScriptToExecuteOnDocumentCreatedAsync 注册的脚本会在页面脚本执行前运行，
            // 确保 window.wails 在页面脚本中可用。
            // 若放在导航之后，页面脚本已执行完，wails 会是 undefined。
            InjectRuntimeJs();

            // 设置 URL（若有）。
            if (!string.IsNullOrEmpty(_options.URL))
            {
                _webview.Navigate(_options.URL);
                _currentUrl = _options.URL;
            }
            else if (!string.IsNullOrEmpty(_options.HTML))
            {
                _webview.NavigateToString(_options.HTML);
            }
            else
            {
                // 未设置 URL 或 HTML 时，若 Application 已配置 AssetServer，
                // 自动导航到 http://wails.localhost/ 加载静态资源（仿 Wails v3）。
                // 避免使用 file:// 协议导致的权限问题。
                var app = Application.Get();
                if (app?.AssetServer is not null)
                {
                    const string wailsUrl = "http://wails.localhost/";
                    _webview.Navigate(wailsUrl);
                    _currentUrl = wailsUrl;
                }
            }

            // 注入 JS（若有）。
            if (!string.IsNullOrEmpty(_options.JS))
            {
                _ = _webview.ExecuteScriptAsync(_options.JS);
            }

            // 注入 CSS（若有）。
            if (!string.IsNullOrEmpty(_options.CSS))
            {
                InjectCSS(_options.CSS);
            }

            // 设置背景色（若有）。
            if (_options.BackgroundColour is { } bg)
            {
                _controller.DefaultBackgroundColor = Color.FromArgb(bg.A, bg.R, bg.G, bg.B);
            }

            _initTcs.TrySetResult(true);
        }
        catch (Exception ex)
        {
            _initTcs.TrySetException(ex);
        }
    }

    /// <summary>
    /// 更新 WebView2 控制器边界以匹配窗口客户区。
    /// </summary>
    private void UpdateBounds()
    {
        if (_controller is null || _hwnd.IsNull)
        {
            return;
        }

        PInvoke.GetClientRect(_hwnd, out var rect);
        _controller.Bounds = new Rectangle(0, 0, rect.Width, rect.Height);
    }

    /// <summary>
    /// 处理 WebView2 WebResourceRequested 事件。
    /// 对应 Wails v3 Go 版本 webview_window_windows.go 中的 onWebResourceRequested。
    /// 拦截 http(s)://wails.localhost/* 的请求：
    /// - /wails/call、/wails/event、/wails/drag：转发到 MessageProcessor 进行 IPC 消息处理。
    /// - 其他路径：由 AssetServer 提供静态资源服务。
    /// 重要：必须使用同步方法（非 async/await），因为 Win32 消息循环无 SynchronizationContext，
    /// await 后的 continuation 会在线程池线程运行，而 WebView2 要求 CoreWebView2 成员
    /// 只能在 UI 线程访问（否则抛出 InvalidOperationException）。
    /// </summary>
    /// <param name="sender">事件发送者。</param>
    /// <param name="args">WebResourceRequested 事件参数。</param>
    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            var uri = new Uri(args.Request.Uri);
            var path = uri.AbsolutePath;
            var method = args.Request.Method;
            var app = Application.Get();

            // IPC 消息端点：拦截所有 POST /wails/* 请求
            // 运行时 API（window/dialog/clipboard 等）通过 _wailsInvoke 发送到 /wails/<type> 端点，
            // 全部由 MessageProcessor 统一分发处理。
            if (method.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
                path.StartsWith("/wails/", StringComparison.OrdinalIgnoreCase))
            {
                // 同步读取请求体，避免 await 导致后续在线程池线程执行
                string body = string.Empty;
                if (args.Request.Content is { } contentStream)
                {
                    using var reader = new StreamReader(contentStream);
                    body = reader.ReadToEnd();
                }

                // 同步调用后端处理消息并获取响应。
                // HandleMessageFromFrontend 主要是 CPU 密集型（反射调用），
                // 阻塞 UI 线程的时间通常很短，可接受。
                // 传递当前窗口 ID，使窗口操作消息能分发到对应的 WebviewWindow 实例。
                string responseJson = "{\"result\":null,\"error\":null}";
                if (app is not null)
                {
                    var response = app.HandleMessageFromFrontend(body, _id).GetAwaiter().GetResult();
                    if (response is not null)
                    {
                        responseJson = JsonSerializer.Serialize(response, _jsonOptions);
                    }
                }

                var responseBytes = System.Text.Encoding.UTF8.GetBytes(responseJson);
                var responseStream = new MemoryStream(responseBytes);
                args.Response = _webview?.Environment.CreateWebResourceResponse(
                    responseStream, 200, "OK", "Content-Type: application/json\r\n");
                return;
            }

            // 静态资源端点：转发给 AssetServer
            var assetServer = app?.AssetServer;
            if (assetServer is not null)
            {
                var assetPath = path.TrimStart('/');
                if (string.IsNullOrEmpty(assetPath))
                {
                    assetPath = "index.html";
                }

                // 同步读取资源，避免 await 导致后续在线程池线程执行
                var content = assetServer.ServeAsync(assetPath).GetAwaiter().GetResult();
                if (content is not null && content.Length > 0)
                {
                    // 使用 assetPath（已规范化为 index.html 等）而非原始 path（可能是 "/"）
                    // 计算 MIME 类型，否则根路径请求会得到 application/octet-stream，
                    // 导致 WebView2 把 HTML 当作下载文件而非网页渲染（白屏 + 下载按钮）。
                    var mimeType = assetServer.GetMimeType(assetPath);
                    var ms = new MemoryStream(content);
                    args.Response = _webview?.Environment.CreateWebResourceResponse(
                        ms, 200, "OK", $"Content-Type: {mimeType}\r\n");
                    return;
                }

                // SPA 路由回退：当资源不存在时，回退到 index.html。
                // 适用于 Vue/React/Angular 等前端框架的客户端路由。
                if (!string.IsNullOrEmpty(assetPath) &&
                    !assetPath.Equals("index.html", StringComparison.OrdinalIgnoreCase) &&
                    !Path.HasExtension(assetPath))
                {
                    var fallbackContent = assetServer.ServeAsync("index.html").GetAwaiter().GetResult();
                    if (fallbackContent is not null && fallbackContent.Length > 0)
                    {
                        var ms = new MemoryStream(fallbackContent);
                        args.Response = _webview?.Environment.CreateWebResourceResponse(
                            ms, 200, "OK", "Content-Type: text/html\r\n");
                        return;
                    }
                }
            }

            // 无匹配资源：返回 404
            args.Response = _webview?.Environment.CreateWebResourceResponse(
                null, 404, "Not Found", string.Empty);
        }
        catch
        {
            // 异常时返回 500 错误响应
            args.Response = _webview?.Environment.CreateWebResourceResponse(
                null, 500, "Internal Server Error", string.Empty);
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>
    /// 处理 WebView2 WebMessageReceived 事件。
    /// 对应 Wails v3 Go 版本中的 onWebMessageReceived。
    /// 接收前端通过 window.chrome.webview.postMessage() 发送的消息，
    /// 优先识别 <see cref="DragMessageType"/> 拖拽请求并直接调用 <see cref="StartDrag"/>，
    /// 其他消息转发到 Application.HandleMessageFromFrontend 进行 IPC 消息处理。
    /// </summary>
    /// <param name="sender">事件发送者。</param>
    /// <param name="args">WebMessageReceived 事件参数。</param>
    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            string? message = args.TryGetWebMessageAsString();
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            // 优先识别 CSS 拖拽区域触发的拖拽请求，直接调用 StartDrag 启动窗口拖动。
            // 拖拽消息不进入标准 IPC 处理流程，避免消息队列阻塞导致拖动延迟。
            if (TryHandleDragMessage(message))
            {
                return;
            }

            var app = Application.Get();
            if (app is not null)
            {
                await app.HandleMessageFromFrontend(message, _id);
            }
        }
        catch
        {
            // 消息处理异常时忽略，避免中断 WebView2 消息循环
        }
    }

    /// <summary>
    /// 尝试将消息识别为拖拽请求并触发窗口拖动。
    /// 消息格式为 JSON 字符串：{ "type": "wails:drag", "windowId": &lt;id&gt; }。
    /// 若消息类型匹配且 windowId 与本窗口匹配，则调用 <see cref="StartDrag"/> 并返回 true。
    /// </summary>
    /// <param name="message">前端发送的消息字符串。</param>
    /// <returns>若消息已被识别为拖拽请求并处理则返回 true，否则返回 false。</returns>
    private bool TryHandleDragMessage(string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            if (!doc.RootElement.TryGetProperty("type", out var typeProp))
            {
                return false;
            }

            var typeValue = typeProp.GetString();
            if (!string.Equals(typeValue, DragMessageType, StringComparison.Ordinal))
            {
                return false;
            }

            // 校验 windowId（若存在）是否与本窗口匹配，避免跨窗口误触发。
            if (doc.RootElement.TryGetProperty("windowId", out var windowIdProp) &&
                windowIdProp.TryGetUInt32(out var msgWindowId) &&
                msgWindowId != _id)
            {
                return false;
            }

            StartDrag();
            return true;
        }
        catch (JsonException)
        {
            // 非 JSON 消息，按标准 IPC 流程处理。
            return false;
        }
    }

    /// <summary>
    /// 拖拽请求消息类型常量，与 <see cref="DragRegionHelper.GetStartDragCallbackScript"/> 中
    /// 注入的前端代码约定一致。前端通过 chrome.webview.postMessage 发送此类型消息
    /// 触发后端 StartDrag 调用。
    /// </summary>
    private const string DragMessageType = "wails:drag";

    /// <summary>
    /// 处理 WebView2 NavigationCompleted 事件。
    /// 对应 Wails v3 Go 版本中的 onNavigationCompleted。
    /// 导航完成后注入运行时 JS 并触发 WindowRuntimeReady 事件。
    /// 若窗口为无边框模式，同时注入 CSS 拖拽区域脚本以支持 -webkit-app-region: drag。
    /// </summary>
    /// <param name="sender">事件发送者。</param>
    /// <param name="args">NavigationCompleted 事件参数。</param>
    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (!args.IsSuccess || _webview is null)
        {
            return;
        }

        // 运行时 JS 已在 InitializeWebViewAsync 中通过 AddScriptToExecuteOnDocumentCreatedAsync
        // 注入，无需在此重复注入。

        // 无边框窗口注入 CSS 拖拽区域脚本，支持 -webkit-app-region: drag。
        // 对应 Tauri v2 / Electron 的 frameless window drag region 实现。
        if (_options.Frameless)
        {
            // 先注册全局拖拽回调，再注入拖拽区域监听脚本。
            // 顺序很重要：监听脚本会在 mousedown 时调用 window.__wails_start_drag__，
            // 因此回调必须先于监听脚本注入。
            _ = _webview.ExecuteScriptAsync(DragRegionHelper.GetStartDragCallbackScript(_id));
            _ = _webview.ExecuteScriptAsync(DragRegionHelper.GetDragRegionScript());
        }

        // 触发 WindowRuntimeReady 事件，通知应用层运行时已就绪。
        // 对应 Wails v3 Go 版本中的 emit(WindowRuntimeReady) 调用。
        Application.Get()?.DispatchWindowEvent(_id, (uint)WindowEventType.WindowRuntimeReady);
    }

    /// <summary>
    /// 注入 Wails 运行时 JavaScript 代码到 WebView2。
    /// 对应 Wails v3 Go 版本中的 injectRuntimeJs 方法。
    /// 通过 Application.GenerateRuntimeJs 生成运行时代码并执行。
    /// 使用 AddScriptToExecuteOnDocumentCreatedAsync 注入，
    /// 确保运行时在页面任何脚本执行前就绪（否则页面脚本访问 wails 时会得到 undefined）。
    /// 仅注入一次，后续导航不重复注入。
    /// </summary>
    private void InjectRuntimeJs()
    {
        if (_runtimeInjected || _webview is null)
        {
            return;
        }

        var app = Application.Get();
        if (app is null)
        {
            return;
        }

        try
        {
            var js = app.GenerateRuntimeJs(false);
            if (!string.IsNullOrEmpty(js))
            {
                // AddScriptToExecuteOnDocumentCreatedAsync 会在每个新文档创建时、
                // 页面脚本执行前注入 JS，确保 window.wails 在页面脚本中可用。
                // ExecuteScriptAsync 只在调用时执行一次，页面已加载完，时机太晚。
                _ = _webview.AddScriptToExecuteOnDocumentCreatedAsync(js);
                _runtimeInjected = true;
            }
        }
        catch
        {
            // 运行时注入失败时忽略，不影响窗口正常使用
        }
    }

    /// <summary>
    /// 向 WebView 前端发送消息。
    /// 对应 Wails v3 Go 版本中的 postMessage 方法。
    /// 后端通过此方法将消息推送到前端 JavaScript。
    /// </summary>
    /// <param name="message">要发送的消息字符串。</param>
    public void PostMessageToWebView(string message)
    {
        _webview?.PostWebMessageAsString(message);
    }

    /// <summary>
    /// DragQueryFileW 的手动 P/Invoke 声明。
    /// CsWin32 源生成器无法生成此函数，因此使用手动 DllImport 作为回退。
    /// 对应 Win32 shell32.dll 中的 DragQueryFileW 函数。
    /// </summary>
    /// <param name="hDrop">HDROP 句柄。</param>
    /// <param name="iFile">文件索引；0xFFFFFFFF 时返回文件数量。</param>
    /// <param name="lpszFile">接收文件路径的缓冲区；null 时返回路径长度。</param>
    /// <param name="cch">缓冲区大小（字符数）。</param>
    /// <returns>iFile 为 0xFFFFFFFF 时返回文件数量；否则返回路径长度或复制字符数。</returns>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern unsafe uint DragQueryFileW(IntPtr hDrop, uint iFile, char* lpszFile, uint cch);

    /// <summary>
    /// DragFinish 的手动 P/Invoke 声明。
    /// CsWin32 源生成器无法生成此函数，因此使用手动 DllImport 作为回退。
    /// 对应 Win32 shell32.dll 中的 DragFinish 函数。
    /// </summary>
    /// <param name="hDrop">要释放的 HDROP 句柄。</param>
    [DllImport("shell32.dll", SetLastError = true)]
    private static extern void DragFinish(IntPtr hDrop);

    /// <summary>
    /// 解析 WM_DROPFILES 消息中的拖放文件列表。
    /// 对应 Wails v3 Go 版本中的 parseDropFiles 方法。
    /// 使用 Win32 DragQueryFileW 和 DragFinish API 获取文件路径数组。
    /// </summary>
    /// <param name="hDropPtr">HDROP 句柄值（来自 WM_DROPFILES 的 wParam）。</param>
    /// <returns>拖放的文件路径数组。</returns>
    private unsafe string[] ParseDropFiles(nint hDropPtr)
    {
        // iFile = 0xFFFFFFFF 时返回拖放文件数量
        var count = DragQueryFileW(hDropPtr, 0xFFFFFFFF, null, 0);

        var files = new string[count];
        for (uint i = 0; i < count; i++)
        {
            // 获取文件路径长度（不含 null 终止符）
            var length = DragQueryFileW(hDropPtr, i, null, 0);
            if (length == 0)
            {
                continue;
            }

            // 获取文件路径
            var buffer = new char[length + 1];
            fixed (char* ptr = buffer)
            {
                DragQueryFileW(hDropPtr, i, ptr, (uint)buffer.Length);
            }

            files[i] = new string(buffer, 0, (int)length);
        }

        // 释放拖放数据句柄
        DragFinish(hDropPtr);

        return files;
    }

    /// <summary>
    /// 获取当前窗口样式。
    /// </summary>
    /// <returns>窗口样式枚举。</returns>
    private WINDOW_STYLE GetWindowStyle()
    {
        var value = PInvoke.GetWindowLong(_hwnd, (WINDOW_LONG_PTR_INDEX)GwlStyle);
        return (WINDOW_STYLE)(uint)value;
    }

    /// <summary>
    /// 设置当前窗口样式。
    /// </summary>
    /// <param name="style">要设置的窗口样式。</param>
    private void SetWindowStyle(WINDOW_STYLE style)
    {
        PInvoke.SetWindowLong(_hwnd, (WINDOW_LONG_PTR_INDEX)GwlStyle, (int)(uint)style);
    }

    /// <summary>
    /// 获取当前扩展窗口样式。
    /// </summary>
    /// <returns>扩展窗口样式枚举。</returns>
    private WINDOW_EX_STYLE GetWindowExStyle()
    {
        var value = PInvoke.GetWindowLong(_hwnd, (WINDOW_LONG_PTR_INDEX)GwlExStyle);
        return (WINDOW_EX_STYLE)(uint)value;
    }

    /// <summary>
    /// 设置当前扩展窗口样式。
    /// </summary>
    /// <param name="style">要设置的扩展窗口样式。</param>
    private void SetWindowExStyle(WINDOW_EX_STYLE style)
    {
        PInvoke.SetWindowLong(_hwnd, (WINDOW_LONG_PTR_INDEX)GwlExStyle, (int)(uint)style);
    }

    /// <summary>
    /// 应用样式变更并刷新窗口框架。
    /// </summary>
    private void ApplyFrameChange()
    {
        PInvoke.SetWindowPos(_hwnd, default, 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE |
            SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE |
            SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED);
    }

    /// <summary>
    /// 处理 WM_GETMINMAXINFO 消息，约束窗口最小/最大跟踪尺寸。
    /// </summary>
    /// <param name="lParam">指向 MINMAXINFO 结构的 LPARAM。</param>
    private void HandleGetMinMaxInfo(LPARAM lParam)
    {
        unsafe
        {
            var mmi = (MINMAXINFO*)lParam.Value;
            if (_minWidth > 0 || _minHeight > 0)
            {
                mmi->ptMinTrackSize = new POINT { x = _minWidth, y = _minHeight };
            }

            if (_maxWidth > 0 || _maxHeight > 0)
            {
                mmi->ptMaxTrackSize = new POINT { x = _maxWidth, y = _maxHeight };
            }
        }
    }

    /// <summary>
    /// 处理 WM_DPICHANGED 消息，按建议矩形调整窗口大小。
    /// </summary>
    /// <param name="lParam">指向建议 RECT 的 LPARAM。</param>
    private void HandleDpiChanged(LPARAM lParam)
    {
        unsafe
        {
            var rect = (RECT*)lParam.Value;
            PInvoke.SetWindowPos(_hwnd, default, rect->left, rect->top,
                rect->right - rect->left, rect->bottom - rect->top,
                SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
        }

        UpdateBounds();

        // 分发 WindowDPIChanged 事件，通知应用层窗口 DPI 已变化。
        Application.Get()?.DispatchWindowEvent(
            _id, (uint)WindowEventType.WindowDPIChanged);
    }

    /// <summary>
    /// 静态窗口过程，通过 HWND 查找实例并转发消息。
    /// 对应 Go 版 webview_window_windows.go 中的 WndProc。
    /// </summary>
    private static LRESULT StaticWindowProc(HWND hWnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        // 检查系统托盘消息（系统托盘窗口也使用此类）
        if (SystemTray.Win32SystemTray.TryHandleTrayMessage(hWnd, msg, wParam, lParam, out var trayResult))
        {
            return trayResult;
        }

        // 查找窗口实例
        Win32WebviewWindow? instance = null;
        lock (_instancesLock)
        {
            _instancesByHwnd.TryGetValue((IntPtr)hWnd, out instance);
        }

        switch (msg)
        {
            case WmActivate:
                // 窗口激活状态变化，wParam 低字指示激活状态。
                // WA_INACTIVE (0) = 失活，WA_ACTIVE (1) = 通过非鼠标点击激活，WA_CLICKACTIVE (2) = 通过鼠标点击激活。
                if (instance is not null)
                {
                    var activateState = (uint)wParam.Value & 0xFFFF;
                    if (activateState == 0)
                    {
                        Application.Get()?.DispatchWindowEvent(
                            instance._id, (uint)WindowEventType.WindowFocusLost);
                    }
                    else
                    {
                        Application.Get()?.DispatchWindowEvent(
                            instance._id, (uint)WindowEventType.WindowFocus);
                    }
                }

                break;

            case WmDestroy:
                // 从实例表移除，并在锁内判断是否应退出消息循环（消除竞态条件）。
                bool shouldQuit;
                lock (_instancesLock)
                {
                    _instancesByHwnd.Remove((IntPtr)hWnd);
                    shouldQuit = _instancesByHwnd.Count == 0;
                }

                // 触发 WindowClosed 事件，通知应用层窗口已销毁。
                // 对应 Wails v3 Go 版本中的 emit(WindowClosed) 调用。
                if (instance is not null)
                {
                    Application.Get()?.DispatchWindowEvent(
                        instance._id, (uint)WindowEventType.WindowClosed);
                }

                // 当所有窗口都关闭时，退出消息循环使应用退出。
                // 对应 Wails v3 Go 版本中最后一个窗口关闭时调用 application.Quit() 的行为。
                if (shouldQuit)
                {
                    PInvoke.PostQuitMessage(0);
                }

                break;

            case WmClose:
                // 触发 WindowClosing 事件，通知应用层窗口即将关闭。
                // 不拦截关闭操作，交由 DefWindowProc 完成默认销毁流程。
                // 对应 Wails v3 Go 版本中的 emit(WindowClosing) 调用。
                if (instance is not null)
                {
                    Application.Get()?.DispatchWindowEvent(
                        instance._id, (uint)WindowEventType.WindowClosing);
                }

                break;

            case WmCommand:
                // 分发菜单命令：wParam 低字为命令 ID
                Win32Menu.TryDispatchCommand((uint)(wParam.Value & 0xFFFF));
                return default;

            case WindowsPlatformApp.WmAppDispatchAction:
                // 排空主线程 Action 队列
                WindowsPlatformApp.DrainActionQueue();
                return default;

            case WindowsPlatformApp.WmAppPlatformEvent:
                // 平台事件分发
                Application.Get()?.HandlePlatformEvent((uint)wParam.Value);
                return default;

            case WindowsPlatformApp.WmAppSingleInstance:
                // 单实例通知：读取新实例传入的命令行参数并分发事件。
                WindowsPlatformApp.HandleSingleInstanceNotification();
                return default;

            case WmSize:
                // 窗口大小变化，同步 WebView2 边界并触发 WindowResized 事件。
                // 对应 Wails v3 Go 版本中的 emit(WindowResized) 调用。
                instance?.UpdateBounds();
                if (instance is not null)
                {
                    Application.Get()?.DispatchWindowEvent(
                        instance._id, (uint)WindowEventType.WindowResized);

                    // 检查窗口状态变化：wParam 低字指示 SIZE_MINIMIZED(1)/SIZE_MAXIMIZED(2)/SIZE_RESTORED(0)。
                    var sizeType = (uint)wParam.Value & 0xFFFF;
                    if (sizeType == 1) // SIZE_MINIMIZED
                    {
                        instance._wasMinimised = true;
                        Application.Get()?.DispatchWindowEvent(
                            instance._id, (uint)WindowEventType.WindowMinimised);
                    }
                    else if (sizeType == 2) // SIZE_MAXIMIZED
                    {
                        instance._wasMaximised = true;
                        Application.Get()?.DispatchWindowEvent(
                            instance._id, (uint)WindowEventType.WindowMaximised);
                    }
                    else if (sizeType == 0) // SIZE_RESTORED
                    {
                        // 根据之前的状态触发对应的恢复事件。
                        if (instance._wasMinimised)
                        {
                            instance._wasMinimised = false;
                            Application.Get()?.DispatchWindowEvent(
                                instance._id, (uint)WindowEventType.WindowUnminimised);
                        }

                        if (instance._wasMaximised)
                        {
                            instance._wasMaximised = false;
                            Application.Get()?.DispatchWindowEvent(
                                instance._id, (uint)WindowEventType.WindowUnmaximised);
                        }
                    }
                }

                break;

            case WmMove:
                // 窗口移动，触发 WindowMoved 事件。
                // 对应 Wails v3 Go 版本中的 emit(WindowMoved) 调用。
                if (instance is not null)
                {
                    Application.Get()?.DispatchWindowEvent(
                        instance._id, (uint)WindowEventType.WindowMoved);
                }

                break;

            case WmDpiChanged:
                // DPI 变化，按建议矩形调整并更新布局
                instance?.HandleDpiChanged(lParam);
                return default;

            case WmGetMinMaxInfo:
                // 约束窗口最小/最大尺寸
                if (instance is not null)
                {
                    instance.HandleGetMinMaxInfo(lParam);
                    return default;
                }

                break;

            case WmHotkey:
                // 全局热键：wParam 为热键 ID，转发到快捷键绑定管理器处理。
                // 对应 Wails v3 Go 版本中的 keybindings.HandleHotKey 调用。
                Application.Get()?.KeyBindingManager?.HandleHotKey((int)wParam.Value);
                break;

            case WmDropFiles:
                // 文件拖放：解析文件列表并触发 WindowFileDropped 事件。
                // wParam 为 HDROP 句柄，通过 DragQueryFileW 解析为文件路径数组。
                // 对应 Wails v3 Go 版本中的 emit(WindowFileDropped, files) 调用。
                if (instance is not null)
                {
                    var files = instance.ParseDropFiles((nint)wParam.Value);
                    Application.Get()?.Events.Emit(
                        KnownEvents.WindowFileDropped, files, instance._id);
                }

                break;

            case WmSettingChange:
                // 系统设置变化：检测暗/亮模式切换。
                // lParam 指向变化类别的字符串，"ImmersiveColorSet" 表示主题变化。
                // 对应 Wails v3 和 Tauri v2 的系统主题变化监听。
                if (instance is not null && lParam.Value != 0)
                {
                    try
                    {
                        var category = Marshal.PtrToStringUni(lParam.Value);
                        if (category == "ImmersiveColorSet")
                        {
                            Application.Get()?.Events.Emit(KnownEvents.ThemeChanged, null, null);
                        }
                    }
                    catch
                    {
                        // 忽略指针读取异常
                    }
                }

                break;

            case WmDisplayChange:
                // 显示器配置变化（分辨率改变、显示器热插拔）。
                // 对应 Wails v3 的 DisplayChanged 事件。
                Application.Get()?.HandlePlatformEvent(
                    (uint)ApplicationEventType.DisplayChanged);
                break;

            case WmClipboardUpdate:
                // 剪贴板内容变化（需要窗口已通过 AddClipboardFormatListener 注册）。
                // 对应 Wails v3 的 ClipboardChanged 事件。
                Application.Get()?.HandlePlatformEvent(
                    (uint)ApplicationEventType.ClipboardChanged);
                break;

            case WmKeyDown:
                // 监听 F12 键打开 DevTools，对应 Wails v3 / Tauri v2 / 浏览器的开发者工具快捷键。
                // wParam 为虚拟键码。
                if (instance is not null && (uint)wParam.Value == VkF12)
                {
                    instance.OpenDevTools();
                    return default; // 已处理，不传递给 DefWindowProc
                }

                break;

            case WmContextMenu:
                // 右键上下文菜单请求。
                // 若应用启用了默认上下文菜单（EnableDefaultContextMenu），则弹出内置菜单，
                // 包含"检查元素"（Inspect Element）和"重新加载"（Reload）选项。
                // 对应 Wails v3 / Tauri v2 的右键菜单功能。
                if (instance is not null)
                {
                    var app = Application.Get();
                    if (app?.Options.EnableDefaultContextMenu == true)
                    {
                        // lParam 低字为 X 坐标，高字为 Y 坐标（屏幕坐标）
                        var x = unchecked((short)(lParam.Value & 0xFFFF));
                        var y = unchecked((short)((lParam.Value >> 16) & 0xFFFF));
                        instance.ShowDefaultContextMenu(x, y);
                        return default;
                    }
                }

                break;
        }

        return PInvoke.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    /// <summary>
    /// 显示默认的右键上下文菜单，包含 DevTools 和重新加载选项。
    /// 对应 Tauri v2 的默认右键菜单和 Wails v3 的上下文菜单功能。
    /// 使用 CreatePopupMenu + AppendMenuW + TrackPopupMenu 构建 Win32 弹出菜单。
    /// </summary>
    /// <param name="screenX">屏幕 X 坐标。</param>
    /// <param name="screenY">屏幕 Y 坐标。</param>
    private unsafe void ShowDefaultContextMenu(int screenX, int screenY)
    {
        // 创建弹出式菜单
        var hMenu = PInvoke.CreatePopupMenu();
        if (hMenu.IsNull)
        {
            return;
        }

        try
        {
            // 追加菜单项（使用手动 P/Invoke 声明的 AppendMenuW，因为 CsWin32 生成的重载仅接受 SafeHandle）
            // ID 1: 开发者工具（F12）
            // ID 2: 重新加载
            // ID 3: 分隔符
            // ID 4: 检查元素（Inspect Element）
            AppendMenuW((IntPtr)hMenu, MENU_ITEM_FLAGS.MF_STRING, (UIntPtr)1, "开发者工具 (F12)");
            AppendMenuW((IntPtr)hMenu, MENU_ITEM_FLAGS.MF_STRING, (UIntPtr)2, "重新加载");
            AppendMenuW((IntPtr)hMenu, MENU_ITEM_FLAGS.MF_SEPARATOR, UIntPtr.Zero, null);
            AppendMenuW((IntPtr)hMenu, MENU_ITEM_FLAGS.MF_STRING, (UIntPtr)4, "检查元素");

            // 弹出菜单前必须将所有者窗口设为前台，否则菜单可能无法正常关闭。
            PInvoke.SetForegroundWindow(_hwnd);

            // 显示菜单并获取用户选择
            // TPM_RETURNCMD 使 TrackPopupMenu 返回用户选择的菜单项 ID 而非 BOOL
            var cmd = PInvoke.TrackPopupMenu(
                hMenu,
                TRACK_POPUP_MENU_FLAGS.TPM_LEFTALIGN | TRACK_POPUP_MENU_FLAGS.TPM_TOPALIGN |
                TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_NONOTIFY,
                screenX, screenY, 0, _hwnd, null);

            // TrackPopupMenu 返回 BOOL，但使用 TPM_RETURNCMD 时实际返回命令 ID（int）。
            var selectedId = *(int*)&cmd;

            // 处理用户选择
            switch (selectedId)
            {
                case 1:
                    OpenDevTools();
                    break;
                case 2:
                    _webview?.Reload();
                    break;
                case 4:
                    // 检查元素：直接打开 DevTools
                    _webview?.OpenDevToolsWindow();
                    break;
            }
        }
        finally
        {
            PInvoke.DestroyMenu(hMenu);
        }
    }

    /// <summary>
    /// AppendMenuW P/Invoke 声明。
    /// CsWin32 生成的 AppendMenu 重载仅接受 SafeHandle，无法直接传入 HMENU，
    /// 此处手动声明以支持 HMENU 句柄（通过 IntPtr 传递）。
    /// </summary>
    /// <param name="hMenu">父菜单句柄（IntPtr 形式）。</param>
    /// <param name="uFlags">菜单项标志（MF_* 组合）。</param>
    /// <param name="uIDNewItem">命令 ID 或子菜单句柄（MF_POPUP 时为菜单句柄）。</param>
    /// <param name="lpNewItem">菜单项文本，分隔符时为 null。</param>
    /// <returns>成功返回 true，否则 false。</returns>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(IntPtr hMenu, MENU_ITEM_FLAGS uFlags, UIntPtr uIDNewItem, string? lpNewItem);

    /// <summary>
    /// 清除窗口菜单（设置为 null 并重绘菜单栏）。
    /// </summary>
    internal void ClearMenu()
    {
        if (_hwnd.IsNull)
        {
            return;
        }

        PInvoke.SetMenu(_hwnd, default);
        PInvoke.DrawMenuBar(_hwnd);
    }

    /// <summary>
    /// 设置窗口菜单句柄。
    /// </summary>
    /// <param name="menu">Win32 菜单句柄。</param>
    internal void SetMenuHandle(HMENU menu)
    {
        if (_hwnd.IsNull)
        {
            return;
        }

        PInvoke.SetMenu(_hwnd, menu);
        PInvoke.DrawMenuBar(_hwnd);
    }

    /// <summary>
    /// 设置窗口图标句柄（同时设置大图标和小图标）。
    /// </summary>
    /// <param name="icon">Win32 图标句柄。</param>
    internal unsafe void SetIconHandle(HICON icon)
    {
        if (_hwnd.IsNull)
        {
            return;
        }

        // ICON_BIG = 1, ICON_SMALL = 0
        PInvoke.SendMessage(_hwnd, WmSetIcon, (WPARAM)(nuint)1, (LPARAM)(nint)icon.Value);
        PInvoke.SendMessage(_hwnd, WmSetIcon, (WPARAM)(nuint)0, (LPARAM)(nint)icon.Value);
    }

    /// <inheritdoc />
    public void SetTitle(string title)
    {
        if (!_hwnd.IsNull)
        {
            PInvoke.SetWindowText(_hwnd, title);
        }
    }

    /// <inheritdoc />
    public void SetSize(int width, int height)
    {
        if (_hwnd.IsNull)
        {
            return;
        }

        PInvoke.GetWindowRect(_hwnd, out var rect);
        PInvoke.MoveWindow(_hwnd, rect.left, rect.top, width, height, true);
        UpdateBounds();
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
        if (_hwnd.IsNull)
        {
            return;
        }

        PInvoke.GetWindowRect(_hwnd, out var rect);
        var w = rect.right - rect.left;
        var h = rect.bottom - rect.top;
        PInvoke.MoveWindow(_hwnd, x, y, w, h, true);
    }

    /// <inheritdoc />
    public void Show()
    {
        if (!_hwnd.IsNull)
        {
            PInvoke.ShowWindow(_hwnd, SHOW_WINDOW_CMD.SW_SHOWNORMAL);
        }

        // 分发 WindowShow 事件，通知应用层窗口已显示。
        Application.Get()?.DispatchWindowEvent(
            _id, (uint)WindowEventType.WindowShow);
    }

    /// <inheritdoc />
    public void Hide()
    {
        if (!_hwnd.IsNull)
        {
            PInvoke.ShowWindow(_hwnd, SHOW_WINDOW_CMD.SW_HIDE);
        }

        // 分发 WindowHide 事件，通知应用层窗口已隐藏。
        Application.Get()?.DispatchWindowEvent(
            _id, (uint)WindowEventType.WindowHide);
    }

    /// <inheritdoc />
    public void Maximise()
    {
        if (!_hwnd.IsNull)
        {
            PInvoke.ShowWindow(_hwnd, SHOW_WINDOW_CMD.SW_MAXIMIZE);
        }
    }

    /// <inheritdoc />
    public void UnMaximise()
    {
        if (!_hwnd.IsNull)
        {
            PInvoke.ShowWindow(_hwnd, SHOW_WINDOW_CMD.SW_RESTORE);
        }
    }

    /// <inheritdoc />
    public void Minimise()
    {
        if (!_hwnd.IsNull)
        {
            PInvoke.ShowWindow(_hwnd, SHOW_WINDOW_CMD.SW_MINIMIZE);
        }
    }

    /// <inheritdoc />
    public void UnMinimise()
    {
        if (!_hwnd.IsNull)
        {
            PInvoke.ShowWindow(_hwnd, SHOW_WINDOW_CMD.SW_RESTORE);
        }
    }

    /// <inheritdoc />
    public void Fullscreen()
    {
        if (_hwnd.IsNull || _isFullscreen)
        {
            return;
        }

        _isFullscreen = true;

        // 保存当前样式和矩形
        _savedStyle = GetWindowStyle();
        PInvoke.GetWindowRect(_hwnd, out _savedRect);

        // 获取窗口所在显示器信息
        var hmon = PInvoke.MonitorFromWindow(_hwnd, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var monitorInfo = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        PInvoke.GetMonitorInfo(hmon, ref monitorInfo);

        // 设置为弹出式样式（移除标题栏和边框）
        SetWindowStyle(WINDOW_STYLE.WS_POPUP | WINDOW_STYLE.WS_VISIBLE);

        // 调整窗口为全屏尺寸
        var rc = monitorInfo.rcMonitor;
        PInvoke.SetWindowPos(_hwnd, default, rc.left, rc.top,
            rc.right - rc.left, rc.bottom - rc.top,
            SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE |
            SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED);

        UpdateBounds();

        // 分发 WindowFullscreen 事件，通知应用层窗口已进入全屏。
        Application.Get()?.DispatchWindowEvent(
            _id, (uint)WindowEventType.WindowFullscreen);
    }

    /// <inheritdoc />
    public void UnFullscreen()
    {
        if (_hwnd.IsNull || !_isFullscreen)
        {
            return;
        }

        _isFullscreen = false;

        // 恢复窗口样式
        SetWindowStyle(_savedStyle);

        // 恢复窗口矩形
        PInvoke.SetWindowPos(_hwnd, default, _savedRect.left, _savedRect.top,
            _savedRect.right - _savedRect.left, _savedRect.bottom - _savedRect.top,
            SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE |
            SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED);

        UpdateBounds();

        // 分发 WindowUnfullscreen 事件，通知应用层窗口已退出全屏。
        Application.Get()?.DispatchWindowEvent(
            _id, (uint)WindowEventType.WindowUnfullscreen);
    }

    /// <inheritdoc />
    public void Restore()
    {
        if (!_hwnd.IsNull)
        {
            PInvoke.ShowWindow(_hwnd, SHOW_WINDOW_CMD.SW_RESTORE);
        }
    }

    /// <inheritdoc />
    public void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;

        if (!_hwnd.IsNull)
        {
            // 移除剪贴板格式监听器，避免销毁后仍接收 WM_CLIPBOARDUPDATE。
            PInvoke.RemoveClipboardFormatListener(_hwnd);
            PInvoke.DestroyWindow(_hwnd);
            _hwnd = default;
        }

        _controller?.Close();
        _controller = null;
        _webview = null;
    }

    /// <inheritdoc />
    public void Focus()
    {
        if (!_hwnd.IsNull)
        {
            PInvoke.SetForegroundWindow(_hwnd);
        }
    }

    /// <inheritdoc />
    public void ShowMenuBar()
    {
        if (!_hwnd.IsNull && !_windowMenu.IsNull)
        {
            PInvoke.SetMenu(_hwnd, _windowMenu);
            PInvoke.DrawMenuBar(_hwnd);
        }
    }

    /// <inheritdoc />
    public void HideMenuBar()
    {
        if (!_hwnd.IsNull)
        {
            PInvoke.SetMenu(_hwnd, default);
            PInvoke.DrawMenuBar(_hwnd);
        }
    }

    /// <inheritdoc />
    public void ToggleMenuBar()
    {
        if (_hwnd.IsNull)
        {
            return;
        }

        var current = PInvoke.GetMenu(_hwnd);
        if (current.IsNull)
        {
            ShowMenuBar();
        }
        else
        {
            HideMenuBar();
        }
    }

    /// <inheritdoc />
    public void SetAlwaysOnTop(bool onTop)
    {
        if (_hwnd.IsNull)
        {
            return;
        }

        // HWND_TOPMOST = (HWND)-1, HWND_NOTOPMOST = (HWND)-2
        var hwndInsertAfter = onTop ? (HWND)new IntPtr(-1) : (HWND)new IntPtr(-2);
        PInvoke.SetWindowPos(_hwnd, hwndInsertAfter, 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE);
    }

    /// <inheritdoc />
    public void SetBackgroundColour(byte r, byte g, byte b, byte a)
    {
        if (_controller is not null)
        {
            _controller.DefaultBackgroundColor = Color.FromArgb(a, r, g, b);
        }
    }

    /// <inheritdoc />
    public void SetBackgroundColour(int r, int g, int b, int a)
    {
        SetBackgroundColour((byte)r, (byte)g, (byte)b, (byte)a);
    }

    /// <summary>
    /// 设置窗口背景类型。对应 Go 版 SetBackgroundType。
    /// </summary>
    /// <param name="type">背景类型字符串（"transparent"、"translucent"、"solid"）。</param>
    public void SetBackgroundType(string type)
    {
        if (_controller is null)
        {
            return;
        }

        if (string.Equals(type, "transparent", StringComparison.OrdinalIgnoreCase))
        {
            _controller.DefaultBackgroundColor = Color.Transparent;
        }
        else
        {
            _controller.DefaultBackgroundColor = Color.White;
        }
    }

    /// <summary>
    /// 设置全屏按钮是否可用。对应 Go 版 SetFullscreenButtonEnabled。
    /// 通过修改窗口样式 WS_MAXIMIZEBOX 控制最大化（全屏）按钮的可用状态。
    /// </summary>
    /// <param name="enabled">是否可用。</param>
    public void SetFullscreenButtonEnabled(bool enabled)
    {
        if (_hwnd.IsNull)
        {
            return;
        }

        // 通过切换 WS_MAXIMIZEBOX 样式控制全屏（最大化）按钮的可用状态。
        var style = GetWindowStyle();
        if (enabled)
        {
            style |= WINDOW_STYLE.WS_MAXIMIZEBOX;
        }
        else
        {
            style &= ~WINDOW_STYLE.WS_MAXIMIZEBOX;
        }

        SetWindowStyle(style);
        ApplyFrameChange();
    }

    /// <inheritdoc />
    public bool IsFullscreen()
    {
        return _isFullscreen;
    }

    /// <inheritdoc />
    public bool IsMaximised()
    {
        return !_hwnd.IsNull && PInvoke.IsZoomed(_hwnd);
    }

    /// <inheritdoc />
    public bool IsMinimised()
    {
        return !_hwnd.IsNull && PInvoke.IsIconic(_hwnd);
    }

    /// <inheritdoc />
    public bool IsVisible()
    {
        return !_hwnd.IsNull && PInvoke.IsWindowVisible(_hwnd);
    }

    /// <inheritdoc />
    public bool IsFocused()
    {
        if (_hwnd.IsNull)
        {
            return false;
        }

        var foreground = PInvoke.GetForegroundWindow();
        return foreground == _hwnd;
    }

    /// <inheritdoc />
    public void SetFrameless(bool frameless)
    {
        if (_hwnd.IsNull)
        {
            return;
        }

        var style = GetWindowStyle();
        if (frameless)
        {
            // 移除标题栏和边框相关样式
            style &= ~(WINDOW_STYLE.WS_CAPTION | WINDOW_STYLE.WS_THICKFRAME |
                       WINDOW_STYLE.WS_SYSMENU | WINDOW_STYLE.WS_MINIMIZEBOX |
                       WINDOW_STYLE.WS_MAXIMIZEBOX);
        }
        else
        {
            // 恢复标准窗口样式
            style |= WINDOW_STYLE.WS_CAPTION | WINDOW_STYLE.WS_THICKFRAME |
                     WINDOW_STYLE.WS_SYSMENU | WINDOW_STYLE.WS_MINIMIZEBOX |
                     WINDOW_STYLE.WS_MAXIMIZEBOX;
        }

        SetWindowStyle(style);
        ApplyFrameChange();
    }

    /// <inheritdoc />
    public void OpenDevTools()
    {
        _webview?.OpenDevToolsWindow();
    }

    /// <inheritdoc />
    public void CloseDevTools()
    {
        // WebView2 未提供直接关闭 DevTools 的 API。
        // 通过设置 AreDevToolsEnabled = false 再 = true 间接关闭已打开的 DevTools 窗口。
        if (_webview is not null)
        {
            try
            {
                _webview.Settings.AreDevToolsEnabled = false;
                _webview.Settings.AreDevToolsEnabled = true;
            }
            catch
            {
                // 忽略关闭失败
            }
        }
    }

    /// <inheritdoc />
    public void SetZoom(float zoom)
    {
        if (_controller is not null)
        {
            _controller.ZoomFactor = zoom;
        }
    }

    /// <summary>
    /// 设置缩放比例（double 重载）。
    /// </summary>
    /// <param name="zoom">缩放比例。</param>
    public void SetZoom(double zoom)
    {
        if (_controller is not null)
        {
            _controller.ZoomFactor = zoom;
        }
    }

    /// <inheritdoc />
    public void SetZoomLevel(float level)
    {
        if (_controller is not null)
        {
            _controller.ZoomFactor = level;
        }
    }

    /// <summary>
    /// 设置是否启用缩放控制。
    /// </summary>
    /// <param name="enabled">是否启用缩放。</param>
    public void SetZoomEnabled(bool enabled)
    {
        if (_webview is not null)
        {
            _webview.Settings.IsZoomControlEnabled = enabled;
        }
    }

    /// <inheritdoc />
    public (int Width, int Height) GetSize()
    {
        if (_hwnd.IsNull)
        {
            return (0, 0);
        }

        PInvoke.GetWindowRect(_hwnd, out var rect);
        return (rect.Width, rect.Height);
    }

    /// <inheritdoc />
    public (int Width, int Height) GetContentSize()
    {
        if (_hwnd.IsNull)
        {
            return (0, 0);
        }

        PInvoke.GetClientRect(_hwnd, out var rect);
        return (rect.Width, rect.Height);
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
        if (_hwnd.IsNull)
        {
            return (0, 0);
        }

        PInvoke.GetWindowRect(_hwnd, out var rect);
        return (rect.left, rect.top);
    }

    /// <inheritdoc />
    public float GetZoom()
    {
        return _controller is not null ? (float)_controller.ZoomFactor : 1.0f;
    }

    /// <inheritdoc />
    public float GetZoomLevel()
    {
        return _controller is not null ? (float)_controller.ZoomFactor : 1.0f;
    }

    /// <inheritdoc />
    public void ExecJS(string js)
    {
        _ = _webview?.ExecuteScriptAsync(js);
    }

    /// <inheritdoc />
    public void GoBack()
    {
        _webview?.GoBack();
    }

    /// <inheritdoc />
    public void GoForward()
    {
        _webview?.GoForward();
    }

    /// <inheritdoc />
    public void Reload()
    {
        _webview?.Reload();
    }

    /// <inheritdoc />
    public void SetURL(string url)
    {
        LoadURL(url);
    }

    /// <inheritdoc />
    public void SetHTML(string html)
    {
        if (_webview is not null)
        {
            _webview.NavigateToString(html);
        }
    }

    /// <inheritdoc />
    public void Print()
    {
        _webview?.ExecuteScriptAsync("window.print();");
    }

    /// <inheritdoc />
    public void PrintToPDF(string path)
    {
        // 异步打印 PDF：通过 WebView2 的 PrintToPdfAsync 实现。
        // 此处触发异步操作但不等待结果（同步方法签名限制）。
        if (_webview is not null)
        {
            _ = _webview.PrintToPdfAsync(path, null);
        }
    }

    /// <summary>
    /// 将窗口内容导出为 PDF（字节数组选项重载）。
    /// 简化实现：暂不支持自定义页面选项。
    /// </summary>
    /// <param name="pageOptions">PDF 导出选项字节数组，可为 null。</param>
    public void PrintToPDF(byte[]? pageOptions)
    {
        // 简化实现：暂不处理字节数组选项，使用默认设置
    }

    /// <inheritdoc />
    public void PrintToPDF(string path, PrintToPdfOptions? options)
    {
        if (_webview is null)
        {
            return;
        }

        // WebView2 的 PrintToPdfAsync 接受 CoreWebView2PrintSettings 参数。
        // PrintToPdfSettings 需通过 CoreWebView2Environment.CreatePrintSettings() 创建，
        // 当前实现暂不应用自定义选项（需存储 environment 引用以完整支持）。
        // 对应 Tauri v2 的 WebviewWindow.printToPDF(options) 功能。
        _ = _webview.PrintToPdfAsync(path, null);
    }

    /// <inheritdoc />
    public void RegisterCustomScheme(string scheme)
    {
        if (_webview is null || string.IsNullOrEmpty(scheme))
        {
            return;
        }

        // 注册自定义协议方案，拦截指定 scheme 的所有请求。
        // 对应 Tauri v2 的自定义协议（asset protocol）功能。
        var filter = $"{scheme}://*";
        _webview.AddWebResourceRequestedFilter(filter, CoreWebView2WebResourceContext.All);
    }

    /// <inheritdoc />
    public async Task<byte[]?> CapturePreviewAsync()
    {
        if (_webview is null)
        {
            return null;
        }

        using var ms = new MemoryStream();
        await _webview.CapturePreviewAsync(
            CoreWebView2CapturePreviewImageFormat.Png,
            ms);
        return ms.ToArray();
    }

    /// <inheritdoc />
    public void SetMenu(Menu? menu)
    {
        if (menu is null)
        {
            _contextMenu = default;
            return;
        }

        // 创建弹出式菜单用于上下文菜单显示
        var win32Menu = new Win32Menu(menu, isPopup: true);
        win32Menu.Build();
        _contextMenu = win32Menu.Hmenu;
    }

    /// <summary>
    /// 在指定坐标打开上下文菜单。
    /// </summary>
    /// <param name="x">X 坐标（屏幕坐标）。</param>
    /// <param name="y">Y 坐标（屏幕坐标）。</param>
    public unsafe void OpenContextMenu(int x, int y)
    {
        if (_hwnd.IsNull || _contextMenu.IsNull)
        {
            return;
        }

        PInvoke.SetForegroundWindow(_hwnd);
        var cmd = PInvoke.TrackPopupMenu(
            _contextMenu,
            TRACK_POPUP_MENU_FLAGS.TPM_LEFTALIGN | TRACK_POPUP_MENU_FLAGS.TPM_TOPALIGN |
            TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_NONOTIFY,
            x, y, 0, _hwnd, null);

        // TrackPopupMenu 返回 BOOL，但使用 TPM_RETURNCMD 时实际返回命令 ID（int）。
        var cmdValue = *(int*)&cmd;
        if (cmdValue != 0)
        {
            Win32Menu.TryDispatchCommand((uint)cmdValue);
        }
    }

    /// <inheritdoc />
    public void StartDrag()
    {
        if (_hwnd.IsNull)
        {
            return;
        }

        // 释放鼠标捕获并通过 WM_NCLBUTTONDOWN 模拟标题栏拖动
        PInvoke.ReleaseCapture();
        PInvoke.SendMessage(_hwnd, WmNclButtonDown, (WPARAM)(nuint)Htcaption, default);
    }

    /// <inheritdoc />
    public void StartResize()
    {
        if (_hwnd.IsNull)
        {
            return;
        }

        // 释放鼠标捕获并通过 WM_NCLBUTTONDOWN 模拟右下角调整大小
        PInvoke.ReleaseCapture();
        PInvoke.SendMessage(_hwnd, WmNclButtonDown, (WPARAM)(nuint)HtBottomRight, default);
    }

    /// <inheritdoc />
    public void SetEnabled(bool enabled)
    {
        if (!_hwnd.IsNull)
        {
            PInvoke.EnableWindow(_hwnd, enabled);
        }
    }

    /// <inheritdoc />
    public void SetContentProtection(bool enabled)
    {
        if (_hwnd.IsNull)
        {
            return;
        }

        // WDA_NONE = 0, WDA_MONITOR = 1
        PInvoke.SetWindowDisplayAffinity(_hwnd, (WINDOW_DISPLAY_AFFINITY)(enabled ? 1 : 0));
    }

    /// <inheritdoc />
    public void AttachAsModal(uint parentWindowId)
    {
        if (_hwnd.IsNull)
        {
            return;
        }

        // 查找父窗口实例，获取其 Win32 句柄。
        var parentWindow = Application.Get()?.GetWindow(parentWindowId);
        if (parentWindow?.Impl is not Win32WebviewWindow parentWin32)
        {
            return;
        }

        var parentHwnd = parentWin32.Hwnd;
        if (parentHwnd.IsNull)
        {
            return;
        }

        // 禁用父窗口，使其无法接收用户输入（模态行为）。
        // 对应 Go 版 webview_window_windows.go 中 AttachAsModal 的 EnableWindow 调用。
        PInvoke.EnableWindow(parentHwnd, false);
    }

    /// <inheritdoc />
    public void SetResizable(bool resizable)
    {
        if (_hwnd.IsNull)
        {
            return;
        }

        var style = GetWindowStyle();
        if (resizable)
        {
            style |= WINDOW_STYLE.WS_THICKFRAME;
        }
        else
        {
            style &= ~WINDOW_STYLE.WS_THICKFRAME;
        }

        SetWindowStyle(style);
        ApplyFrameChange();
    }

    /// <inheritdoc />
    public void SetMaximisable(bool maximisable)
    {
        if (_hwnd.IsNull)
        {
            return;
        }

        var style = GetWindowStyle();
        if (maximisable)
        {
            style |= WINDOW_STYLE.WS_MAXIMIZEBOX;
        }
        else
        {
            style &= ~WINDOW_STYLE.WS_MAXIMIZEBOX;
        }

        SetWindowStyle(style);
        ApplyFrameChange();
    }

    /// <inheritdoc />
    public void SetMinimisable(bool minimisable)
    {
        if (_hwnd.IsNull)
        {
            return;
        }

        var style = GetWindowStyle();
        if (minimisable)
        {
            style |= WINDOW_STYLE.WS_MINIMIZEBOX;
        }
        else
        {
            style &= ~WINDOW_STYLE.WS_MINIMIZEBOX;
        }

        SetWindowStyle(style);
        ApplyFrameChange();
    }

    /// <inheritdoc />
    public void SetClosable(bool closable)
    {
        if (_hwnd.IsNull)
        {
            return;
        }

        // 通过系统菜单控制关闭按钮状态
        var hSysMenu = PInvoke.GetSystemMenu(_hwnd, false);
        if (hSysMenu.IsNull)
        {
            return;
        }

        var flags = closable
            ? MENU_ITEM_FLAGS.MF_BYCOMMAND | MENU_ITEM_FLAGS.MF_ENABLED
            : MENU_ITEM_FLAGS.MF_BYCOMMAND | MENU_ITEM_FLAGS.MF_GRAYED;
        PInvoke.EnableMenuItem(hSysMenu, ScClose, flags);
    }

    /// <inheritdoc />
    public void SetHasShadow(bool hasShadow)
    {
        // Windows 自动为 WS_OVERLAPPEDWINDOW 样式窗口添加阴影
        // 简化实现：暂不支持单独控制阴影
    }

    /// <summary>
    /// 设置窗口是否半透明。通过 WS_EX_LAYERED 扩展样式实现。
    /// </summary>
    /// <param name="translucent">是否半透明。</param>
    public void SetTranslucent(bool translucent)
    {
        if (_hwnd.IsNull)
        {
            return;
        }

        var exStyle = GetWindowExStyle();
        if (translucent)
        {
            exStyle |= WINDOW_EX_STYLE.WS_EX_LAYERED;
            SetWindowExStyle(exStyle);
            // 设置 200/255 透明度
            PInvoke.SetLayeredWindowAttributes(_hwnd, new COLORREF(0), 200, (LAYERED_WINDOW_ATTRIBUTES_FLAGS)LwaAlpha);
        }
        else
        {
            exStyle &= ~WINDOW_EX_STYLE.WS_EX_LAYERED;
            SetWindowExStyle(exStyle);
        }
    }

    /// <summary>
    /// 设置窗口透明度（0.0 完全透明 ~ 1.0 完全不透明）。
    /// 通过 WS_EX_LAYERED 扩展样式和 SetLayeredWindowAttributes 实现。
    /// 对应 Wails v3 的 window.setOpacity 和 Tauri v2 的 window.setAlpha。
    /// </summary>
    /// <param name="opacity">透明度值，范围 0.0 到 1.0，会被截断到 [0, 1] 区间。</param>
    public void SetOpacity(float opacity)
    {
        if (_hwnd.IsNull)
        {
            return;
        }

        // 截断到 [0, 1] 区间
        opacity = Math.Max(0f, Math.Min(1f, opacity));

        // 确保窗口具有 WS_EX_LAYERED 扩展样式
        var exStyle = GetWindowExStyle();
        if ((exStyle & WINDOW_EX_STYLE.WS_EX_LAYERED) == 0)
        {
            exStyle |= WINDOW_EX_STYLE.WS_EX_LAYERED;
            SetWindowExStyle(exStyle);
        }

        // 将 0.0-1.0 映射到 0-255
        var alpha = (byte)(opacity * 255);
        PInvoke.SetLayeredWindowAttributes(_hwnd, new COLORREF(0), alpha, (LAYERED_WINDOW_ATTRIBUTES_FLAGS)LwaAlpha);
    }

    /// <summary>
    /// 获取窗口透明度（0.0 完全透明 ~ 1.0 完全不透明）。
    /// 通过 GetLayeredWindowAttributes 读取当前透明度设置。
    /// </summary>
    /// <returns>当前透明度值，范围 0.0 到 1.0。</returns>
    public float GetOpacity()
    {
        if (_hwnd.IsNull)
        {
            return 1.0f;
        }

        var exStyle = GetWindowExStyle();
        if ((exStyle & WINDOW_EX_STYLE.WS_EX_LAYERED) == 0)
        {
            return 1.0f;
        }

        // 读取分层窗口属性
        byte alpha = 255;
        LAYERED_WINDOW_ATTRIBUTES_FLAGS flags = 0;
        COLORREF crKey = default;
        PInvoke.GetLayeredWindowAttributes(_hwnd, out crKey, MemoryMarshal.CreateSpan(ref alpha, 1), out flags);

        // 如果未使用 LWA_ALPHA 标志，则完全不透明
        if ((flags & (LAYERED_WINDOW_ATTRIBUTES_FLAGS)LwaAlpha) == 0)
        {
            return 1.0f;
        }

        return alpha / 255f;
    }

    /// <inheritdoc />
    public void SetTitleBarStyle(TitleBarStyle style)
    {
        if (_hwnd.IsNull)
        {
            return;
        }

        // Win32 平台仅支持 Hidden 样式（移除标题栏）
        if (style == TitleBarStyle.Hidden || style == TitleBarStyle.HiddenInset)
        {
            var winStyle = GetWindowStyle();
            winStyle &= ~WINDOW_STYLE.WS_CAPTION;
            SetWindowStyle(winStyle);
            ApplyFrameChange();
        }
    }

    /// <summary>
    /// 设置标题栏样式（字符串重载）。
    /// </summary>
    /// <param name="style">标题栏样式字符串（如 "hidden"、"hiddenInset"、"unified"）。</param>
    public void SetTitleBarStyle(string style)
    {
        if (Enum.TryParse<TitleBarStyle>(style, true, out var barStyle))
        {
            SetTitleBarStyle(barStyle);
        }
    }

    /// <summary>
    /// 注入 CSS 样式到当前页面。
    /// </summary>
    /// <param name="css">CSS 样式字符串。</param>
    public void InjectCSS(string css)
    {
        if (_webview is null || string.IsNullOrEmpty(css))
        {
            return;
        }

        // 转义 CSS 字符串中的特殊字符
        var escaped = css
            .Replace("\\", "\\\\")
            .Replace("'", "\\'")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r");
        var js = $"var s=document.createElement('style');s.textContent='{escaped}';document.head.appendChild(s);";
        _ = _webview.ExecuteScriptAsync(js);
    }

    /// <summary>
    /// 放大缩放。
    /// </summary>
    public void ZoomIn()
    {
        if (_controller is not null)
        {
            _controller.ZoomFactor *= 1.1;
        }
    }

    /// <summary>
    /// 缩小缩放。
    /// </summary>
    public void ZoomOut()
    {
        if (_controller is not null)
        {
            _controller.ZoomFactor /= 1.1;
        }
    }

    /// <summary>
    /// 重置缩放到 1.0。
    /// </summary>
    public void ZoomReset()
    {
        if (_controller is not null)
        {
            _controller.ZoomFactor = 1.0;
        }
    }

    /// <summary>
    /// 将窗口设置为最小化状态。
    /// </summary>
    public void SetMinimised()
    {
        if (!_hwnd.IsNull)
        {
            PInvoke.ShowWindow(_hwnd, SHOW_WINDOW_CMD.SW_MINIMIZE);
        }
    }

    /// <summary>
    /// 将窗口设置为最大化状态。
    /// </summary>
    public void SetMaximised()
    {
        if (!_hwnd.IsNull)
        {
            PInvoke.ShowWindow(_hwnd, SHOW_WINDOW_CMD.SW_MAXIMIZE);
        }
    }

    /// <summary>
    /// 将窗口设置为正常状态（恢复）。
    /// </summary>
    public void SetNormal()
    {
        if (!_hwnd.IsNull)
        {
            PInvoke.ShowWindow(_hwnd, SHOW_WINDOW_CMD.SW_RESTORE);
        }
    }

    /// <summary>
    /// 注册窗口就绪回调。在 WebView2 初始化完成后执行回调。
    /// </summary>
    /// <param name="callback">窗口就绪时执行的回调。</param>
    public void Run(Action callback)
    {
        if (_initTcs.Task.IsCompletedSuccessfully)
        {
            callback();
        }
        else
        {
            _initTcs.Task.ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully)
                {
                    callback();
                }
            }, TaskScheduler.Default);
        }
    }

    /// <inheritdoc />
    public void Centre()
    {
        if (_hwnd.IsNull)
        {
            return;
        }

        // 获取窗口矩形和屏幕矩形，计算居中位置。
        PInvoke.GetWindowRect(_hwnd, out var windowRect);
        var hwndMonitor = PInvoke.MonitorFromWindow(_hwnd, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var monitorInfo = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        PInvoke.GetMonitorInfo(hwndMonitor, ref monitorInfo);

        var workArea = monitorInfo.rcWork;
        var x = workArea.left + ((workArea.Width - windowRect.Width) / 2);
        var y = workArea.top + ((workArea.Height - windowRect.Height) / 2);

        PInvoke.MoveWindow(_hwnd, x, y, windowRect.Width, windowRect.Height, true);
    }

    /// <inheritdoc />
    public void SetDebuggingEnabled(bool enabled)
    {
        // 调试模式通过 WebView2 环境变量或 AreDevToolsEnabled 属性控制。
        if (_webview is not null)
        {
            _webview.Settings.AreDevToolsEnabled = enabled;
        }
    }

    /// <inheritdoc />
    public string GetURL()
    {
        return _webview?.Source ?? _currentUrl;
    }

    /// <inheritdoc />
    public void LoadURL(string url)
    {
        if (_webview is not null)
        {
            _webview.Navigate(url);
            _currentUrl = url;
        }
        else
        {
            // WebView2 尚未初始化完成，记录 URL 待初始化后加载。
            _currentUrl = url;
        }
    }

    /// <inheritdoc />
    public void LoadHTML(string html)
    {
        SetHTML(html);
    }

    /// <summary>
    /// ITaskbarList3 COM 实例，懒加载。
    /// 在首次调用任务栏相关方法时创建。
    /// </summary>
    private ITaskbarList3? _taskbarList;

    /// <inheritdoc />
    public void SetTaskbarProgress(TaskbarProgressState state, ulong completed, ulong total)
    {
        if (_hwnd.IsNull)
        {
            return;
        }

        var taskbar = GetTaskbarList();
        if (taskbar is null)
        {
            return;
        }

        try
        {
            // 将 TaskbarProgressState 枚举值映射到 Windows TBPF 标志
            var tbpfFlag = (uint)state;

            unsafe
            {
                var hwndPtr = new IntPtr(_hwnd.Value);

                if (state == TaskbarProgressState.None)
                {
                    taskbar.SetProgressState(hwndPtr, tbpfFlag);
                    return;
                }

                if (state == TaskbarProgressState.Indeterminate)
                {
                    taskbar.SetProgressState(hwndPtr, tbpfFlag);
                    return;
                }

                // Normal/Paused/Error 需要设置状态和值
                taskbar.SetProgressState(hwndPtr, tbpfFlag);
                if (total > 0)
                {
                    taskbar.SetProgressValue(hwndPtr, completed, total);
                }
            }
        }
        catch
        {
            // COM 调用失败时静默处理
        }
    }

    /// <inheritdoc />
    public void SetOverlayIcon(byte[]? iconBytes, string? description)
    {
        if (_hwnd.IsNull)
        {
            return;
        }

        var taskbar = GetTaskbarList();
        if (taskbar is null)
        {
            return;
        }

        try
        {
            unsafe
            {
                var hwndPtr = new IntPtr(_hwnd.Value);

                if (iconBytes is null || iconBytes.Length == 0)
                {
                    taskbar.SetOverlayIcon(hwndPtr, IntPtr.Zero, null);
                    return;
                }

                // 通过 WindowsPlatformApp 将 ICO 字节加载为 HICON
                var hIcon = LoadIconFromBytes(iconBytes);
                if (hIcon == IntPtr.Zero)
                {
                    return;
                }

                taskbar.SetOverlayIcon(hwndPtr, hIcon, description ?? string.Empty);

                // Taskbar 复制了图标，可销毁本地句柄
                PInvoke.DestroyIcon((HICON)hIcon);
            }
        }
        catch
        {
            // COM 调用失败时静默处理
        }
    }

    /// <summary>
    /// 从 ICO 字节数据加载 HICON。
    /// 复用 WindowsPlatformApp.LoadIconFromBytes 的逻辑。
    /// </summary>
    /// <param name="iconBytes">ICO 格式字节数据。</param>
    /// <returns>HICON 句柄，失败返回 IntPtr.Zero。</returns>
    private static IntPtr LoadIconFromBytes(byte[] iconBytes)
    {
        try
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"wails_overlay_{Guid.NewGuid():N}.ico");
            try
            {
                File.WriteAllBytes(tempPath, iconBytes);
                var handle = PInvoke.LoadImage(
                    default,
                    tempPath,
                    (GDI_IMAGE_TYPE)1, // IMAGE_ICON = 1
                    0,
                    0,
                    IMAGE_FLAGS.LR_LOADFROMFILE | IMAGE_FLAGS.LR_DEFAULTSIZE);

                var iconPtr = handle.DangerousGetHandle();
                GC.SuppressFinalize(handle);
                return iconPtr;
            }
            finally
            {
                try { File.Delete(tempPath); } catch { /* 忽略 */ }
            }
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// 获取或创建 ITaskbarList3 COM 实例。
    /// </summary>
    /// <returns>ITaskbarList3 实例，失败返回 null。</returns>
    private ITaskbarList3? GetTaskbarList()
    {
        if (_taskbarList is not null)
        {
            return _taskbarList;
        }

        _taskbarList = Win32Taskbar.CreateTaskbarList();
        return _taskbarList;
    }

    /// <summary>
    /// DWMWA_SYSTEMBACKDROP_TYPE 属性索引（38），Windows 11 22000+ 用于设置 Mica/Acrylic 特效。
    /// </summary>
    private const uint DwmwaSystemBackdropType = 38;

    /// <summary>
    /// DWMWA_BORDER_COLOR 属性索引（34），Windows 11 22000+ 用于设置窗口边框颜色。
    /// </summary>
    private const uint DwmwaBorderColor = 34;

    /// <summary>
    /// WS_EX_TOOLWINDOW 扩展样式标志（0x00000080），使窗口不在任务栏显示。
    /// </summary>
    private const uint WsExToolWindow = 0x00000080;

    /// <summary>
    /// WS_EX_TRANSPARENT 扩展样式标志（0x00000020），使鼠标事件穿透窗口。
    /// </summary>
    private const uint WsExTransparent = 0x00000020;

    /// <inheritdoc />
    public void SetSkipTaskbar(bool skip)
    {
        if (_hwnd.IsNull)
        {
            return;
        }

        try
        {
            var exStyle = (uint)PInvoke.GetWindowLong(_hwnd, (WINDOW_LONG_PTR_INDEX)GwlExStyle);

            if (skip)
            {
                exStyle |= WsExToolWindow;
            }
            else
            {
                exStyle &= ~WsExToolWindow;
            }

            PInvoke.SetWindowLong(_hwnd, (WINDOW_LONG_PTR_INDEX)GwlExStyle, (int)exStyle);

            // 同步任务栏列表
            var taskbar = GetTaskbarList();
            if (taskbar is not null)
            {
                unsafe
                {
                    var hwndPtr = new IntPtr(_hwnd.Value);
                    if (skip)
                    {
                        taskbar.DeleteTab(hwndPtr);
                    }
                    else
                    {
                        taskbar.AddTab(hwndPtr);
                    }
                }
            }
        }
        catch
        {
            // 静默处理
        }
    }

    /// <inheritdoc />
    public void SetIgnoreCursorEvents(bool ignore)
    {
        if (_hwnd.IsNull)
        {
            return;
        }

        try
        {
            var exStyle = (uint)PInvoke.GetWindowLong(_hwnd, (WINDOW_LONG_PTR_INDEX)GwlExStyle);

            if (ignore)
            {
                exStyle |= WsExTransparent;
            }
            else
            {
                exStyle &= ~WsExTransparent;
            }

            PInvoke.SetWindowLong(_hwnd, (WINDOW_LONG_PTR_INDEX)GwlExStyle, (int)exStyle);
        }
        catch
        {
            // 静默处理
        }
    }

    /// <inheritdoc />
    public void SetEffects(WindowEffects effects)
    {
        if (_hwnd.IsNull)
        {
            return;
        }

        try
        {
            unsafe
            {
                // DWMWA_SYSTEMBACKDROP_TYPE 值映射：
                // 0 = DWMSBT_AUTO, 1 = DWMSBT_NONE, 2 = DWMSBT_MAINWINDOW (Mica),
                // 3 = DWMSBT_TRANSIENTWINDOW (Acrylic), 4 = DWMSBT_BLURBEHIND
                uint backdropType = effects.State switch
                {
                    true => effects.Effect switch
                    {
                        WindowEffect.Mica => 2,
                        WindowEffect.Acrylic => 3,
                        WindowEffect.BlurBehind => 4,
                        WindowEffect.Transparent => 1, // 透明，无特效
                        _ => 1,
                    },
                    false => 1, // DWMSBT_NONE
                };

                var value = backdropType;
                PInvoke.DwmSetWindowAttribute(
                    _hwnd,
                    (DWMWINDOWATTRIBUTE)DwmwaSystemBackdropType,
                    &value,
                    (uint)sizeof(uint));
            }
        }
        catch
        {
            // DWM 调用失败时静默处理（可能非 Windows 11）
        }
    }

    /// <inheritdoc />
    public void SetBadgeCount(int count)
    {
        if (count <= 0)
        {
            SetOverlayIcon(null, null);
            return;
        }

        var iconBytes = GenerateBadgeIcon(count.ToString(), System.Drawing.Color.Red);
        SetOverlayIcon(iconBytes, count.ToString());
    }

    /// <inheritdoc />
    public void SetBadgeLabel(string? label)
    {
        if (string.IsNullOrEmpty(label))
        {
            SetOverlayIcon(null, null);
            return;
        }

        var iconBytes = GenerateBadgeIcon(label, System.Drawing.Color.Blue);
        SetOverlayIcon(iconBytes, label);
    }

    /// <inheritdoc />
    public void SetVisibleOnAllWorkspaces(bool visible)
    {
        if (_hwnd.IsNull)
        {
            return;
        }

        try
        {
            // Windows 上近似实现：通过 HWND_TOPMOST 让窗口始终在所有工作区之上
            var hwndInsertAfter = visible ? new HWND(new IntPtr(-1)) : new HWND(IntPtr.Zero); // HWND_TOPMOST = -1, HWND_NOTOPMOST = 0
            var flags = SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE;
            PInvoke.SetWindowPos(_hwnd, hwndInsertAfter, 0, 0, 0, 0, flags);
        }
        catch
        {
            // 静默处理
        }
    }

    /// <inheritdoc />
    public void SetBorderColor(string? color)
    {
        if (_hwnd.IsNull)
        {
            return;
        }

        try
        {
            unsafe
            {
                // DWMWA_BORDER_COLOR 接受 COLORREF (0xRRGGBB) 或 DWMWA_COLOR_DEFAULT = 0xFFFFFFFF
                uint colorRef;
                if (string.IsNullOrEmpty(color))
                {
                    colorRef = 0xFFFFFFFF; // 恢复默认
                }
                else
                {
                    colorRef = ParseColorToColorRef(color);
                }

                PInvoke.DwmSetWindowAttribute(
                    _hwnd,
                    (DWMWINDOWATTRIBUTE)DwmwaBorderColor,
                    &colorRef,
                    (uint)sizeof(uint));
            }
        }
        catch
        {
            // DWM 调用失败时静默处理
        }
    }

    /// <inheritdoc />
    public void SetFileDropEnabled(bool enabled)
    {
        if (_hwnd.IsNull)
        {
            return;
        }

        try
        {
            PInvoke.DragAcceptFiles(_hwnd, enabled);
        }
        catch
        {
            // 静默处理
        }
    }

    /// <summary>
    /// 解析十六进制颜色字符串为 Win32 COLORREF 值。
    /// </summary>
    /// <param name="color">颜色字符串（如 #FF0000 或 FF0000）。</param>
    /// <returns>COLORREF 值（0xRRGGBB）。</returns>
    private static uint ParseColorToColorRef(string color)
    {
        var hex = color.TrimStart('#');
        if (hex.Length == 6 && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
        {
            return rgb;
        }

        return 0xFFFFFFFF; // 默认
    }

    /// <summary>
    /// 生成带有文本的 ICO 字节数组，用于任务栏徽章。
    /// 使用 System.Drawing 在内存中绘制文本，然后转换为 ICO 格式。
    /// </summary>
    /// <param name="text">徽章文本。</param>
    /// <param name="backgroundColor">背景颜色。</param>
    /// <returns>ICO 格式字节数组。</returns>
    private static byte[] GenerateBadgeIcon(string text, System.Drawing.Color backgroundColor)
    {
        const int size = 32;

        using var bitmap = new System.Drawing.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(bitmap))
        {
            g.Clear(backgroundColor);

            // 绘制文本
            using var font = new System.Drawing.Font("Segoe UI", text.Length <= 2 ? 16f : 12f, System.Drawing.FontStyle.Bold);
            using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.White);

            var sf = new System.Drawing.StringFormat
            {
                Alignment = System.Drawing.StringAlignment.Center,
                LineAlignment = System.Drawing.StringAlignment.Center,
            };

            var rect = new System.Drawing.Rectangle(0, 0, size, size);
            g.DrawString(text, font, brush, rect, sf);
        }

        // 将 Bitmap 转换为 HICON
        var hIcon = bitmap.GetHicon();
        try
        {
            // 获取 ICO 字节
            using var icon = System.Drawing.Icon.FromHandle(hIcon);
            using var ms = new MemoryStream();
            icon.Save(ms);
            return ms.ToArray();
        }
        finally
        {
            PInvoke.DestroyIcon((HICON)hIcon);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Close();
    }

    /// <summary>
    /// Win32 POINT 结构，对应 native POINT（两个 LONG 坐标）。
    /// CsWin32 拒绝生成 POINT（PInvoke003：建议使用 System.Drawing.Point），
    /// 但此处需要可直接用于 unsafe 指针运算的 blittable 结构，
    /// 因此手动定义与 Win32 内存布局一致的 POINT。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        /// <summary>x 坐标。</summary>
        public int x;

        /// <summary>y 坐标。</summary>
        public int y;
    }

    /// <summary>
    /// MINMAXINFO 结构，用于 WM_GETMINMAXINFO 消息处理。
    /// CsWin32 未生成此结构，此处手动定义。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        /// <summary>保留点。</summary>
        public POINT ptReserved;

        /// <summary>最大尺寸。</summary>
        public POINT ptMaxSize;

        /// <summary>最大位置。</summary>
        public POINT ptMaxPosition;

        /// <summary>最小跟踪尺寸。</summary>
        public POINT ptMinTrackSize;

        /// <summary>最大跟踪尺寸。</summary>
        public POINT ptMaxTrackSize;
    }
}
