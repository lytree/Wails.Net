using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Gio;
using GLib;
using Gtk;
using WebKit;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Webview;
using Wails.Net.Application.Options;
using Wails.Net.Application.Windows;
using Wails.Net.Events;
using Menu = Wails.Net.Application.Menus.Menu;
using WailsApplication = Wails.Net.Application.Application;

namespace Wails.Net.Application.Platform;

/// <summary>
/// Linux 窗口的三态快照，对应 Go 版 webview_window_linux.go 中的 <c>linuxWindowState</c>。
/// 用于比较 GDK Toplevel 状态位变化并派发窗口事件。
/// </summary>
/// <param name="Minimised">是否最小化。</param>
/// <param name="Maximised">是否最大化。</param>
/// <param name="Fullscreen">是否全屏。</param>
internal readonly record struct LinuxWindowState(bool Minimised, bool Maximised, bool Fullscreen);

/// <summary>
/// Linux 平台的 Webview 窗口实现，对应 Go 版的 webview_window_linux.go。
/// 通过 Gtk.Window 创建原生窗口，使用 WebKitGTK 承载 Web 内容。
/// </summary>
public sealed class LinuxWebviewWindow : IWebviewWindowImpl, IDisposable
{
    /// <summary>
    /// 原生 gdk_file_list_get_files 函数，从 GdkFileList 取出 GFile* 链表。
    /// GirCore 0.8.0 未生成此方法的托管包装，需手动 P/Invoke。
    /// </summary>
    /// <param name="fileList">GdkFileList* 原生指针。</param>
    /// <returns>返回 GSList* 链表，包含 GFile* 指针。</returns>
    [DllImport("gtk-4", EntryPoint = "gdk_file_list_get_files")]
    private static extern IntPtr GdkFileListGetFiles(IntPtr fileList);

    /// <summary>
    /// 原生 g_file_get_path 函数，从 GFile* 取出本地路径字符串。
    /// </summary>
    /// <param name="file">GFile* 原生指针。</param>
    /// <returns>返回新分配的 UTF-8 路径字符串，调用者需用 g_free 释放。</returns>
    [DllImport("gio-2.0", EntryPoint = "g_file_get_path")]
    private static extern IntPtr GFileGetPath(IntPtr file);

    /// <summary>
    /// 原生 g_free 函数，释放 GLib 分配的内存。
    /// </summary>
    /// <param name="ptr">待释放的内存指针。</param>
    [DllImport("glib-2.0", EntryPoint = "g_free")]
    private static extern void GFree(IntPtr ptr);

    /// <summary>
    /// 原生 g_slist_free 函数，释放 GSList 链表结构。
    /// </summary>
    /// <param name="slist">GSList* 原生指针。</param>
    [DllImport("glib-2.0", EntryPoint = "g_slist_free")]
    private static extern void GSlistFree(IntPtr slist);

    /// <summary>
    /// 原生 g_bytes_get_data 函数，从 GBytes 取出数据指针与大小。
    /// 用于读取 IPC 请求体（GInputStream → GLib.Bytes → 原生字节）。
    /// </summary>
    /// <param name="bytes">GBytes* 原生指针。</param>
    /// <param name="size">输出：数据字节数。</param>
    /// <returns>指向数据的只读指针，无需释放（数据由 GBytes 拥有）。</returns>
    [DllImport("glib-2.0", EntryPoint = "g_bytes_get_data")]
    private static extern IntPtr GBytesGetData(IntPtr bytes, out nuint size);

    /// <summary>
    /// 应用内页面（含 <see cref="SetHTML"/> 生成的文档）的基线 URL。
    /// 对齐 Wails v3.1.0 <c>linux_purego.go</c> 的 <c>setHTML</c>：使用 <c>wails://</c> 作为基线 URL
    /// （<c>webkit_web_view_load_alternate_html</c>），而不是 <c>about:blank</c>。
    /// 这里采用带 host 的形式，使相对资源解析与 IPC 同源校验都能正常工作。
    /// </summary>
    private const string WailsBaseUrl = "wails://localhost/";

    /// <summary>
    /// 窗口 ID。
    /// </summary>
    private readonly uint _id;

    /// <summary>
    /// 窗口选项。
    /// </summary>
    private readonly WebviewWindowOptions _options;

    /// <summary>
    /// Linux 平台特定应用级选项，用于配置 WebKitGTK 运行时环境。
    /// 对应 Wails v3 Go 版本 <c>Options.Linux</c>，全局生效于所有窗口共享的浏览器环境。
    /// 为 null 时使用系统默认 WebKitGTK 配置。
    /// </summary>
    private readonly LinuxOptions? _linuxOptions;

    /// <summary>
    /// R2-4：当前窗口的 <see cref="WebviewRequestPipeline"/>，懒加载并缓存。
    /// </summary>
    private WebviewRequestPipeline? _pipeline;

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
    /// 上次观测到的窗口最小化状态（由 GDK Toplevel state 派生）。
    /// </summary>
    private bool _minimised;

    /// <summary>
    /// 上次观测到的窗口最大化状态（由 GDK Toplevel state 派生）。
    /// </summary>
    private bool _maximised;

    /// <summary>
    /// 上次观测到的窗口全屏状态（由 GDK Toplevel state 派生）。
    /// </summary>
    private bool _fullscreen;

    /// <summary>
    /// 是否已建立 GDK Toplevel 状态基线。
    /// 首次观测只派发当前处于激活态（最大化/最小化/全屏）的事件，
    /// 对应 Go 版 <c>changedLinuxWindowStateEvents</c> 的 <c>observed</c> 参数。
    /// </summary>
    private bool _surfaceStateObserved;

    /// <summary>
    /// 窗口的 GDK Surface（Toplevel），用于订阅 surface 级信号（state 通知 / 尺寸布局）。
    /// 在窗口 realize 后通过 <c>Gtk.Window.GetSurface()</c> 获取并持有引用，
    /// 确保其原生句柄在整个窗口生命周期内有效。
    /// 对应 Go 版 webview_window_linux.go 中从窗口取出 GdkSurface 后监听其状态与尺寸变化（PR #5830）。
    /// </summary>
    private Gdk.Surface? _surface;

    /// <summary>
    /// surface 级信号是否已连接。GTK4 窗口的 GdkSurface 在 realize 后才创建，
    /// 故在首次 realize 时连接一次，避免重复连接导致重复派发。
    /// </summary>
    private bool _surfaceSignalsConnected;

    /// <summary>
    /// 上次已知的 surface 宽度（-1 表示尚未建立基线），用于感知外部（WM/用户）触发的缩放。
    /// </summary>
    private int _surfaceWidth = -1;

    /// <summary>
    /// 上次已知的 surface 高度（-1 表示尚未建立基线）。
    /// </summary>
    private int _surfaceHeight = -1;

    /// <summary>
    /// 当前 URL。
    /// </summary>
    private string _currentUrl = string.Empty;

    /// <summary>
    /// wails 自定义 URI scheme 是否已注册（全局，所有窗口共享同一 WebContext）。
    /// </summary>
    private static bool _wailsSchemeRegistered;

    /// <summary>
    /// Wails 运行时 JS 是否已注入到 WebView。
    /// 通过 <see cref="UserContentManager.AddScript"/> 注入后，
    /// 脚本会在每个新文档创建时、页面脚本执行前运行（等价于 WebView2 的
    /// <c>AddScriptToExecuteOnDocumentCreatedAsync</c>）。仅注入一次。
    /// </summary>
    /// <summary>
    /// 运行时引导协调器（RH-IPC-02）：统一「document-start 注入」与
    /// 「WindowRuntimeReady 派发」的时序，替代原 <c>_runtimeInjected</c> 布尔标记。
    /// 派发语义为「导航完成 + 运行时注入完成」二者皆满足且仅一次；
    /// 改造前本平台在注入完成后立即派发（早于导航完成），与其他三平台语义不一致。
    /// </summary>
    private readonly WindowRuntimeBootstrap _runtimeBootstrap;

    /// <summary>
    /// JSON 序列化选项，用于 IPC 响应序列化。
    /// 对应 Win32 平台 Win32WebviewWindow._jsonOptions。
    /// </summary>
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

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
    /// 窗口 X 坐标（DIP，GTK4 窗口位置由窗口管理器控制，此处缓存以便查询）。
    /// 依据 Wails v3 PR #6346，对外坐标统一为 DIP 逻辑像素。
    /// </summary>
    private int _x = -1;

    /// <summary>
    /// 窗口 Y 坐标（DIP）。
    /// </summary>
    private int _y = -1;

    /// <summary>
    /// 初始屏幕摆放是否尚未在窗口呈现后应用。
    /// 上游 Wails v3.1.0 在 run() 中于 show() 前后各应用一次 applyScreenPlacement
    /// （webview_window_linux.go:504-514），因为未映射的 GTK 窗口无法被 WM 定位。
    /// </summary>
    private bool _initialPlacementPending;

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
    /// <param name="linuxOptions">Linux 平台特定应用级选项，可为 null。用于配置 WebKitGTK 运行时环境。</param>
    public LinuxWebviewWindow(uint id, WebviewWindowOptions options, LinuxOptions? linuxOptions = null)
    {
        _id = id;
        _options = options;
        _linuxOptions = linuxOptions;

        // RH-IPC-02：必须在 CreateWebView()（内部会调用 InjectRuntimeJs）之前就绪。
        _runtimeBootstrap = new WindowRuntimeBootstrap(
            id,
            static wid => WailsApplication.Get()?.DispatchWindowEvent(wid, (uint)WindowEventType.WindowRuntimeReady),
            static () => WailsApplication.Get()?.GenerateRuntimeJs(false));
        _minWidth = options.MinWidth;
        _minHeight = options.MinHeight;
        _maxWidth = options.MaxWidth;
        _maxHeight = options.MaxHeight;

        // 确保 GTK 已初始化（在创建任何窗口或控件之前）。
        // OnAfterStart 回调在 _platformApp.Run() 之前触发，因此 GTK 可能尚未初始化。
        // 使用静态标志确保只初始化一次，且在当前线程（UI 线程）上初始化。
        LinuxPlatformApp.EnsureGtkInitialized();

        CreateNativeWindow();
        CreateWebView();

        // 连接 WebKit 信号，使 WebView 生命周期事件可观测。
        ConnectWebViewSignals();

        // 设置文件拖放目标，使窗口能够接收文件拖放操作。
        SetupFileDropTarget();

        // 加载初始 URL 或 HTML。
        if (!string.IsNullOrEmpty(options.URL))
        {
            _webView?.LoadUri(options.URL);
            _currentUrl = options.URL;
        }
        else if (!string.IsNullOrEmpty(options.HTML))
        {
            LoadHtmlWithWailsBase(options.HTML);
        }
        else
        {
            // 未设置 URL 或 HTML 时，若 Application 已配置 AssetServer，
            // 注册 wails 自定义 URI scheme 并导航到 wails://localhost/（仿 Wails v3）。
            // 避免使用 file:// 协议导致的权限问题。
            var app = WailsApplication.Get();
            if (app?.AssetServer is not null)
            {
                EnsureWailsSchemeRegistered();
                _webView?.LoadUri(WailsBaseUrl);
                _currentUrl = WailsBaseUrl;
            }
        }

        // 对齐 Wails v3.1.0 linuxWebviewWindow.run()（webview_window_linux.go:465,484,504-514）：
        // 显示前应用初始缩放、置顶状态、屏幕摆放与起始窗口状态。
        SetZoom(_options.Zoom);
        if (_options.AlwaysOnTop)
        {
            // 仅缓存；实际置顶在 Show() 中 present 后通过 wmctrl 应用
            // （对应上游 windowShow() 的 windowApplyPendingAlwaysOnTop）。
            _alwaysOnTop = true;
        }

        _initialPlacementPending = true;
        ApplyInitialPlacement();

        if (_options.Maximised)
        {
            Maximise();
        }
        else if (_options.Minimised)
        {
            Minimise();
        }
        else if (_options.Fullscreen)
        {
            Fullscreen();
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

        // 注入 Wails 运行时 JS（必须在导航之前！）
        // 通过 UserContentManager.AddScript 注册的脚本会在每个新文档创建时、
        // 页面脚本执行前运行，确保 window.wails 在页面脚本中可用。
        // 对应 Win32 平台的 AddScriptToExecuteOnDocumentCreatedAsync。
        // 若放在导航之后，页面脚本已执行完，wails 会是 undefined。
        InjectRuntimeJs();
    }

    /// <summary>
    /// 注入 Wails 运行时 JavaScript 到 WebView。
    /// 对应 Wails v3 Go 版本中的 injectRuntimeJs 方法。
    /// 通过 <see cref="Application.GenerateRuntimeJs"/> 生成运行时代码，
    /// 使用 <see cref="UserContentManager.AddScript"/> 注册 <see cref="UserScript"/>，
    /// 确保运行时在页面任何脚本执行前就绪（否则页面脚本访问 wails 时会得到 undefined）。
    /// 仅注入一次，后续导航不重复注入。
    /// <para>
    /// §11.2 第 3 项（GHSA-2p5f-2gx8-mh8m 缓解）：先注册 <see cref="LinuxFrameGuard"/> 守卫脚本
    /// （所有帧），再注册被守卫包裹的运行时标志脚本，使跨源 / 不透明源子帧无法使用后端 IPC。
    /// </para>
    /// </summary>
    private void InjectRuntimeJs()
    {
        if (_webView is null)
        {
            return;
        }

        // RH-IPC-02：脚本生成与「只注册一次」由 WindowRuntimeBootstrap 统一处理。
        if (!_runtimeBootstrap.TryGetScript(out var js))
        {
            return;
        }

        try
        {
            // 通过 UserContentManager.AddScript 注册 UserScript，
            // 脚本会在每个新文档创建时、页面脚本执行前注入。
            // UserContentInjectedFrames.AllFrames 确保主框架与子框架均注入。
            // UserScriptInjectionTime.Start 对应 WEBKIT_USER_SCRIPT_INJECT_AT_DOCUMENT_START。
            var ucm = _webView.GetUserContentManager();

            // §11.2 第 3 项（GHSA-2p5f-2gx8-mh8m 缓解）：先注入帧守卫（所有帧），
            // 使跨源 / 不透明源子帧标记为不可信并阻断 wails: 端点访问；
            // WebKit 按注册顺序在同一注入时机执行脚本，因此守卫先于运行时标志生效。
            var guardScript = UserScript.New(
                LinuxFrameGuard.GetGuardScript(),
                UserContentInjectedFrames.AllFrames,
                UserScriptInjectionTime.Start,
                null,
                null);
            ucm.AddScript(guardScript);

            // 运行时标志仅在可信帧注入（守卫未执行时按可信处理，保持兼容）。
            var script = UserScript.New(
                LinuxFrameGuard.WrapRuntimeScript(js),
                UserContentInjectedFrames.AllFrames,
                UserScriptInjectionTime.Start,
                null,
                null);
            ucm.AddScript(script);

            // 注入成功；ready 事件待「导航完成」后由 WindowRuntimeBootstrap 统一派发。
            _runtimeBootstrap.MarkRuntimeInjected();
        }
        catch
        {
            // 运行时注入失败时忽略，不影响窗口正常使用
        }
    }

    /// <summary>
    /// 注册 wails 自定义 URI scheme 到 WebKit WebContext。
    /// 通过 <see cref="WebContext.RegisterUriScheme"/> 注册后，所有
    /// <c>wails://localhost/...</c> 请求将由 <see cref="OnWailsSchemeRequest"/> 处理。
    /// 仿照 Wails v3 / Tauri v2 的静态资源处理方式，避免使用 file:// 协议。
    /// 全局只注册一次（所有窗口共享同一 WebContext）。
    /// </summary>
    private void EnsureWailsSchemeRegistered()
    {
        if (_wailsSchemeRegistered || _webView is null)
        {
            return;
        }

        _wailsSchemeRegistered = true;
        var context = _webView.GetContext();
        context.RegisterUriScheme("wails", OnWailsSchemeRequest);
    }

    /// <summary>
    /// 处理 wails:// URI scheme 请求。
    /// 支持两类请求：
    /// 1. <b>POST /wails/*</b>：IPC 消息端点，转发到 <see cref="WailsApplication.HandleMessageFromFrontend"/>。
    ///    运行时 API（call/window/dialog 等）通过 _wailsInvoke 发送到 /wails/&lt;type&gt; 端点，
    ///    对应 Win32 平台的 WebView2 WebResourceRequested 拦截。
    /// 2. <b>GET /&lt;path&gt;</b>：静态资源请求，由 <see cref="WailsApplication.AssetServer"/> 提供。
    ///    支持 SPA 路由回退：当资源不存在且路径无扩展名时，回退到 index.html。
    /// 使用 <see cref="URISchemeResponse"/> 设置状态码与 Content-Type，
    /// 避免直接构造 <see cref="GLib.Error"/>（GirCore 0.8.0 未公开便利构造器）。
    /// <para>
    /// Streams 长轮询：<c>/wails/stream/poll</c> 会挂起等待，若用同步包装会阻塞 GTK 主线程约 20s。
    /// 因此不再使用 <c>RunOnThreadPool</c> 同步包装，而是 <c>await</c> 管线任务（内部使用
    /// <c>ConfigureAwait(false)</c> 不捕获上下文），完成后在 GTK 主线程延续里写响应。
    /// 非流式请求（IPC / 静态资源）通常已同步完成，行为与改造前一致。
    /// </para>
    /// <para>
    /// 注意：此处将 <paramref name="request"/> 捕获进异步延续以保持托管包装存活；WebKitGTK 在
    /// 请求被 finish 或取消前持有原生请求引用，故可安全延迟完成。
    /// </para>
    /// </summary>
    /// <param name="request">URI scheme 请求对象。</param>
    private async void OnWailsSchemeRequest(URISchemeRequest request)
    {
        try
        {
            // R2-4：统一委托给 WebviewRequestPipeline。
            // Streams 控制数据通过 HTTP header（x-wails-stream-*）传递，需一并透传；
            // 二进制帧负载经 RequestBodyBytes 传递（batch / chunk 重组体无法用字符串承载）。
            // P1-9（未实现）：WebKitGTK 的 URISchemeRequest（libsoup）不暴露请求取消/中止钩子——
            // 仅提供 FinishWithResponse / FinishWithError，无对应 macOS 的 stopURLSchemeTask 回调，
            // 也无 Windows WebView2 的 CDP Fetch/Network 中止（见 request_cancellation_windows.go）。
            // 因此此处保持原行为，未接独立 CancellationTokenSource；如后续 GirCore 暴露取消信号可在此接入。
            var pipeline = GetOrCreatePipeline();
            var bodyBytes = ReadRequestBodyBytes(request);
            var method = request.GetHttpMethod();
            var path = request.GetPath();
            var headers = ReadRequestHeaders(request);

            // §11.2 第 3 项（GHSA-2p5f-2gx8-mh8m 缓解）：IPC 来源校验。
            // WebKitGTK 不暴露发起帧信息，改为校验 Origin / Sec-Fetch-Site 是否与窗口当前页面同源；
            // 可疑的跨源 / 不透明源 IPC 请求直接返回 403（纵深防御见 LinuxFrameGuard 脚本守卫）。
            if (!LinuxIpcOriginGuard.IsIpcRequestAllowed(method, path, headers, GetCurrentWindowUrl()))
            {
                WailsApplication.Get()?.Logger?.LogWarning(
                    "已拒绝跨源 IPC 请求：window={WindowName} method={Method} path={Path}",
                    _options.Name,
                    method,
                    path);
                FinishWithStatus(request, 403, "Forbidden");
                return;
            }

            headers.TryGetValue("Origin", out var requestOrigin);

            var webviewRequest = new WebviewRequest
            {
                Method = method,
                Path = path,
                // P2-12：WebKitGTK 的 URISchemeRequest 不暴露查询串 API，无法透传 HMR 查询（保持 null）。
                Query = null,
                RequestBody = bodyBytes.Length == 0 ? "{}" : System.Text.Encoding.UTF8.GetString(bodyBytes),
                RequestBodyBytes = bodyBytes,
                WindowId = _id,
                WindowName = _options.Name,
                Origin = requestOrigin,
                Headers = headers
            };
            var response = await pipeline.HandleAsync(webviewRequest, CancellationToken.None).ConfigureAwait(true);
            FinishWithResponse(request, response);
        }
        catch (Exception ex)
        {
            try
            {
                FinishWithStatus(request, 500, ex.Message);
            }
            catch
            {
                // 请求可能已被取消/销毁，忽略。
            }
        }
    }

    /// <summary>
    /// 读取 WebKit URI scheme 请求头（<c>Soup.MessageHeaders</c>）为大小写不敏感字典。
    /// 供 Streams 双向字节流通过 HTTP header（<c>x-wails-stream-*</c>）传递控制数据。
    /// GirCore 0.8.0 的 <c>Soup.MessageHeaders.Foreach</c> 以 (name, value) 回调枚举全部请求头。
    /// </summary>
    /// <param name="request">URI scheme 请求对象。</param>
    /// <returns>大小写不敏感的请求头字典。</returns>
    private static IReadOnlyDictionary<string, string> ReadRequestHeaders(URISchemeRequest request)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var soupHeaders = request.GetHttpHeaders();
        if (soupHeaders is null)
        {
            return headers;
        }

        soupHeaders.Foreach((name, value) =>
        {
            if (!string.IsNullOrEmpty(name))
            {
                headers[name] = value ?? string.Empty;
            }
        });
        return headers;
    }


    /// <summary>
    /// 使用 <see cref="URISchemeResponse"/> 构造并完成请求响应。
    /// 通过 <c>MemoryInputStream</c> 包装字节数组，并设置状态码与 Content-Type。
    /// </summary>
    /// <param name="request">URI scheme 请求对象。</param>
    /// <param name="content">响应体字节。</param>
    /// <param name="contentType">MIME 类型。</param>
    /// <param name="statusCode">HTTP 状态码。</param>
    /// <param name="reasonPhrase">状态描述。</param>
    private static void FinishResponse(URISchemeRequest request, byte[] content, string contentType, uint statusCode, string reasonPhrase)
    {
        var bytes = GLib.Bytes.New(content);
        var stream = MemoryInputStream.NewFromBytes(bytes);
        var response = URISchemeResponse.New(stream, content.Length);
        response.SetContentType(contentType);
        response.SetStatus(statusCode, reasonPhrase);
        request.FinishWithResponse(response);
    }

    /// <summary>
    /// 使用空响应体完成请求，仅设置状态码与原因短语。
    /// 用于 404/500 等错误响应，避免构造 <see cref="GLib.Error"/>。
    /// </summary>
    /// <param name="request">URI scheme 请求对象。</param>
    /// <param name="statusCode">HTTP 状态码。</param>
    /// <param name="reasonPhrase">状态描述。</param>
    private static void FinishWithStatus(URISchemeRequest request, uint statusCode, string reasonPhrase)
    {
        var empty = System.Array.Empty<byte>();
        var bytes = GLib.Bytes.New(empty);
        var stream = MemoryInputStream.NewFromBytes(bytes);
        var response = URISchemeResponse.New(stream, 0);
        response.SetContentType("text/plain");
        response.SetStatus(statusCode, reasonPhrase);
        request.FinishWithResponse(response);
    }

    /// <summary>
    /// R2-4：创建或获取当前窗口的 <see cref="WebviewRequestPipeline"/>，懒加载并缓存。
    /// </summary>
    private WebviewRequestPipeline GetOrCreatePipeline()
    {
        if (_pipeline is not null)
        {
            return _pipeline;
        }
        var app = WailsApplication.Get();
        var assetServer = app?.AssetServer;
        _pipeline = new WebviewRequestPipeline(
            ipcHandler: async (body, windowId, _, _) =>
            {
                if (app is null) return null;
                var msg = await app.HandleMessageFromFrontend(body, windowId).ConfigureAwait(false);
                return (object?)msg;
            },
            assetHandler: async (path, windowName, _) =>
            {
                if (assetServer is null) return null;
                var bytes = await assetServer.ServeAsync(path, windowName).ConfigureAwait(false);
                return (byte[]?)bytes;
            },
            mimeTypeResolver: path => assetServer?.GetMimeType(path) ?? "application/octet-stream");
        return _pipeline;
    }

    /// <summary>
    /// R2-4：读取 Linux URI scheme 请求体（POST）为原始字节。
    /// 保留原 HandleIpcRequest 中的 GInputStream → GLib.Bytes → Marshal.Copy 流程；
    /// 返回原始字节以便 Streams 二进制帧负载完整承载。
    /// </summary>
    private static byte[] ReadRequestBodyBytes(URISchemeRequest request)
    {
        var bodyStream = request.GetHttpBody();
        if (bodyStream is null)
        {
            return [];
        }
        var gBytes = bodyStream.ReadBytes(4 * 1024 * 1024, null);
        if (gBytes is null)
        {
            return [];
        }
        var dataPtr = GBytesGetData(gBytes.Handle.DangerousGetHandle(), out var size);
        if (dataPtr == IntPtr.Zero || size <= 0)
        {
            return [];
        }
        var data = new byte[(int)size];
        Marshal.Copy(dataPtr, data, 0, (int)size);
        return data;
    }

    /// <summary>
    /// R2-4：将 <see cref="WebviewResponse"/> 写入 <see cref="URISchemeRequest"/>。
    /// </summary>
    private void FinishWithResponse(URISchemeRequest request, WebviewResponse response)
    {
        FinishResponse(request, response.Body, response.ContentType, (uint)response.StatusCode, response.StatusText);
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
            // 连接 close-request 信号，处理用户点击窗口关闭按钮的事件。
            // 对应 Win32 平台的 WM_CLOSE/WM_DESTROY 消息处理。
            _window.OnCloseRequest += OnWindowCloseRequest;
            // 连接 realize 信号：GTK4 窗口的 GdkSurface 在 realize 时才创建，
            // 在此之后才能取得 GdkSurface 并订阅其 state / layout 信号，
            // 以感知外部（WM/用户）触发的最小化与缩放。对应 Go 版 PR #5830。
            _window.OnRealize += OnWindowRealize;
        }
    }

    /// <summary>
    /// 处理 GTK 窗口的 close-request 信号（用户点击关闭按钮时触发）。
    /// 分发 WindowClosing 事件，并在最后一个窗口关闭时退出应用。
    /// 对应 Win32 平台 <see cref="Win32WebviewWindow"/> 中的 WM_CLOSE/WM_DESTROY 处理：
    /// WM_CLOSE 分发 WindowClosing 事件；WM_DESTROY 在所有窗口关闭时调用 PostQuitMessage(0)。
    /// <para>
    /// 退出确认流程（与 <see cref="ApplicationOptions.ShowExitConfirmationDialog"/> 配合）：
    /// 1. 若是最后一个窗口且 <see cref="ApplicationOptions.ShowExitConfirmationDialog"/> 为 true，
    ///    先弹出 GTK AlertDialog 询问用户是否退出
    /// 2. 用户选择"取消"则返回 true 阻止窗口关闭，应用继续运行
    /// 3. 用户选择"确定"则调用 <see cref="Application.Quit"/> 触发 Shutdown 退出应用
    /// </para>
    /// <para>
    /// 注意：AlertDialog.ChooseAsync 在 GTK 主线程上运行模态对话框，
    /// 会阻塞 close-request 信号处理直到用户响应，这是 GTK 推荐的同步确认模式。
    /// </para>
    /// </summary>
    /// <param name="sender">触发事件的 GTK 窗口。</param>
    /// <param name="args">事件参数。</param>
    /// <returns>返回 false 表示允许关闭窗口；返回 true 会阻止关闭。</returns>
    private bool OnWindowCloseRequest(Window sender, EventArgs args)
    {
        var app = WailsApplication.Get();
        if (app is null)
        {
            return false;
        }

        // 分发 WindowClosing 事件，通知应用层窗口即将关闭。
        app.DispatchWindowEvent(_id, (uint)WindowEventType.WindowClosing);

        // 当所有窗口都关闭时，退出应用。
        // 对应 Win32 平台 WmDestroy 中的 shouldQuit 检查与 PostQuitMessage(0) 调用。
        if (!app.Options.DisableQuitOnLastWindowClosed && app.Windows.Count <= 1)
        {
            // 最后一个窗口关闭时，检查是否需要弹出退出确认对话框。
            if (app.Options.ShowExitConfirmationDialog)
            {
                // 弹出 GTK 原生确认对话框（模态，阻塞当前信号处理直到用户响应）。
                // ChooseAsync 在 GTK 主线程运行，返回按钮索引（0=第一个按钮，1=第二个按钮）。
                var dialog = AlertDialog.NewWithProperties([]);
                dialog.SetMessage(app.Options.ExitDialogTitle);
                dialog.SetDetail(app.Options.ExitDialogMessage);
                dialog.SetButtons(["取消", "确定退出"]);

                // ConfigureAwait(true) 保持 GTK 同步上下文，确保对话框在 UI 线程运行。
                var result = dialog.ChooseAsync(null!).ConfigureAwait(true).GetAwaiter().GetResult();

                // 用户选择"取消"（索引 0）：阻止窗口关闭，应用继续运行。
                if (result == 0)
                {
                    return true;
                }
            }

            // 最后一个窗口关闭时，通过 Application.Quit() 触发 Shutdown，
            // 最终调用 LinuxPlatformApp.Destroy() 中的 _mainLoop?.Quit() 退出 GTK 主循环。
            app.Quit();
        }

        return false;
    }

    /// <summary>
    /// 设置文件拖放目标，使窗口能够接收文件拖放操作。
    /// GTK4 使用 <see cref="Gtk.DropTarget"/> 替代 GTK3 的 drag-data-received 信号。
    /// 使用 GdkFileList 的 GType 创建 DropTarget，拖放时解析文件路径并触发
    /// <see cref="WindowEventType.WindowFileDropped"/> 事件。
    /// 对应 Wails v3 Go 版本 webview_window_linux.go 中的 drag-data-received 信号处理。
    /// </summary>
    private void SetupFileDropTarget()
    {
        if (_window is null)
        {
            return;
        }

        // 使用 GdkFileList 的 GType 创建 DropTarget，接受文件列表拖放。
        var dropTarget = Gtk.DropTarget.New(Gdk.FileList.GetGType(), Gdk.DragAction.Copy);
        dropTarget.OnDrop += OnFileDrop;

        // 将 DropTarget 作为事件控制器附加到窗口。
        _window.AddController(dropTarget);
    }

    /// <summary>
    /// 处理文件拖放事件，从 <see cref="Gdk.FileList"/> 提取文件路径并触发
    /// <see cref="WindowEventType.WindowFileDropped"/> 事件。
    /// GirCore 0.8.0 的 Gdk.FileList 未公开 GetFiles 方法，需通过 P/Invoke 调用
    /// 原生 <c>gdk_file_list_get_files()</c> 获取 GSList，再遍历 GFile* 调用
    /// <c>g_file_get_path()</c> 提取本地路径。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="args">拖放事件参数，包含封装 <see cref="Gdk.FileList"/> 的值。</param>
    /// <returns>返回 true 表示已处理拖放。</returns>
    private bool OnFileDrop(Gtk.DropTarget sender, Gtk.DropTarget.DropSignalArgs args)
    {
        // 从 Value 中取出 Gdk.FileList 的原始指针。
        // GObject.Value.GetBoxed() 返回 IntPtr，对应 GdkFileList* 原生指针。
        IntPtr fileListPtr = args.Value.GetBoxed();
        if (fileListPtr == IntPtr.Zero)
        {
            return false;
        }

        // 调用原生 gdk_file_list_get_files() 获取 GSList*（GFile* 链表）。
        IntPtr slistPtr = GdkFileListGetFiles(fileListPtr);
        if (slistPtr == IntPtr.Zero)
        {
            return false;
        }

        // GSList 结构为 { gpointer data; GSList *next; }，手动遍历。
        var paths = new List<string>();
        IntPtr current = slistPtr;
        while (current != IntPtr.Zero)
        {
            IntPtr filePtr = Marshal.ReadIntPtr(current);
            if (filePtr != IntPtr.Zero)
            {
                IntPtr pathPtr = GFileGetPath(filePtr);
                if (pathPtr != IntPtr.Zero)
                {
                    string path = Marshal.PtrToStringUTF8(pathPtr) ?? string.Empty;
                    if (!string.IsNullOrEmpty(path))
                    {
                        paths.Add(path);
                    }
                    // g_file_get_path 返回新分配的字符串，需要释放。
                    GFree(pathPtr);
                }
            }
            // next 字段位于 data 之后，偏移量等于指针大小。
            current = Marshal.ReadIntPtr(current, IntPtr.Size);
        }

        // 释放 GSList 链表结构本身（data 指针不释放，由 GFile 持有）。
        GSlistFree(slistPtr);

        if (paths.Count == 0)
        {
            return false;
        }

        // P1-4：通过 Gtk.DropTarget.GetDropActions 获取拖放坐标，异步查询目标 HTML 元素，
        // 同时触发 WindowFileDropTarget 事件携带 DropTargetDetails。
        // 对应 Wails v3 Go 版本 DropTargetDetails 结构。
        // GTK4 DropTarget 通过 GetCurrentDrop() 返回 Gdk.Drop，坐标通过 gtk_drop_get_position() 获取。
        // 由于 GirCore 0.8.0 API 限制，此处坐标取 (0, 0) 表示坐标未知（前端仍可识别元素）。
        _ = QueryDropTargetAndEmitAsync(0, 0);

        // 直接调用 Events.Emit 传递文件路径数组作为数据负载。
        // 对应 Win32 实现中的 Events.Emit(KnownEvents.WindowFileDropped, files, windowId)。
        WailsApplication.Get()?.Events.Emit(
            KnownEvents.WindowFileDropped, paths.ToArray(), _id);

        return true;
    }

    /// <summary>
    /// 异步查询拖放目标 HTML 元素详情并触发 <see cref="KnownEvents.WindowFileDropTarget"/> 事件。
    /// 对应 Wails v3 Go 版本中通过 ExecJS 调用 <c>elementFromPoint</c> 提取 DropTargetDetails 的逻辑。
    /// </summary>
    /// <param name="x">拖放位置 X 坐标。</param>
    /// <param name="y">拖放位置 Y 坐标。</param>
    private async System.Threading.Tasks.Task QueryDropTargetAndEmitAsync(int x, int y)
    {
        try
        {
            if (_webView is null)
            {
                return;
            }

            var js = Wails.Net.Application.Windows.DragRegionHelper.GetDropTargetDetailsScript(x, y);
            var result = await _webView.EvaluateJavascriptAsync(js);
            var json = result?.ToString();
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            var details = System.Text.Json.JsonSerializer.Deserialize<Wails.Net.Application.Windows.DropTargetDetails>(json);
            if (details is not null)
            {
                WailsApplication.Get()?.Events.Emit(
                    KnownEvents.WindowFileDropTarget, details, _id);
            }
        }
        catch
        {
            // 查询目标元素失败不应中断文件拖放主流程
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

                // RH-IPC-02：上报「导航完成」这一事实，由 WindowRuntimeBootstrap 与注入状态共同裁决
                // 是否派发 WindowRuntimeReady（改造前在注入完成时就派发，早于页面真正就绪）。
                _runtimeBootstrap.MarkNavigationCompleted();
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
    /// 处理 GTK 窗口的 notify 信号，分发窗口状态事件。
    /// 对齐 Wails v3 beta.2（PR #5830）：除焦点外，从窗口状态属性发出 maximise / fullscreen 事件，
    /// 使通过标题栏按钮或窗口管理器执行的状态切换也能被前端感知（而不只是方法调用时派发）。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="args">通知事件参数，包含 Pspec。</param>
    private void OnWindowNotify(GObject.Object sender, GObject.Object.NotifySignalArgs args)
    {
        if (args.Pspec is null)
        {
            return;
        }

        var app = WailsApplication.Get();
        if (app is null || _window is null)
        {
            return;
        }

        switch (args.Pspec.GetName())
        {
            case "is-active":
                // 读取窗口激活状态分发对应的焦点事件。
                if (_window.IsActive)
                {
                    app.DispatchWindowEvent(_id, (uint)WindowEventType.WindowFocus);
                }
                else
                {
                    app.DispatchWindowEvent(_id, (uint)WindowEventType.WindowFocusLost);
                }
                break;

            case "is-maximized":
                app.DispatchWindowEvent(
                    _id,
                    _window.IsMaximized()
                        ? (uint)WindowEventType.WindowMaximised
                        : (uint)WindowEventType.WindowUnmaximised);
                break;

            case "is-fullscreen":
                app.DispatchWindowEvent(
                    _id,
                    _window.IsFullscreen()
                        ? (uint)WindowEventType.WindowFullscreen
                        : (uint)WindowEventType.WindowUnfullscreen);
                break;
        }
    }

    /// <summary>
    /// 处理 GTK 窗口的 realize 信号。GTK4 窗口的 <c>GdkSurface</c> 在 realize 时才创建，
    /// 故在此取得 surface 并订阅其 <c>state</c> 与 <c>layout</c> 信号。
    /// 仅连接一次，避免重复触发导致重复派发。
    /// </summary>
    /// <param name="sender">触发信号的 GTK 控件（窗口本身）。</param>
    /// <param name="args">事件参数。</param>
    private void OnWindowRealize(Gtk.Widget sender, EventArgs args)
    {
        if (_closed || _surfaceSignalsConnected || _window is null)
        {
            return;
        }

        var surface = _window.GetSurface();
        if (surface is null)
        {
            return;
        }

        // 持有 surface 引用，确保其原生句柄在窗口生命周期内始终有效（不被 GC 回收包装）。
        _surface = surface;
        // GdkToplevel:state 属性变化（WM/用户触发的最大化/最小化/全屏等状态切换）。
        surface.OnNotify += OnSurfaceNotify;
        // Gdk.Surface::layout 信号，携带重算后的宽高（外部缩放）。
        surface.OnLayout += OnSurfaceLayout;
        _surfaceSignalsConnected = true;
    }

    /// <summary>
    /// 处理 GDK Surface 的属性通知信号。仅关注 <c>state</c> 属性：
    /// <c>GdkToplevel:state</c> 为可读属性（<c>G_PARAM_READABLE</c>，非 EXPLICIT_NOTIFY），
    /// 窗口管理器或用户触发的最小化/恢复会更新该属性并发出 <c>notify::state</c>。
    /// 对应 Go 版 webview_window_linux.go 中从 surface 状态发出 minimise 事件的逻辑（PR #5830）。
    /// </summary>
    /// <param name="sender">触发信号的 surface。</param>
    /// <param name="args">通知事件参数，包含 Pspec。</param>
    private void OnSurfaceNotify(GObject.Object sender, GObject.Object.NotifySignalArgs args)
    {
        if (_closed || args.Pspec is null)
        {
            return;
        }

        if (args.Pspec.GetName() == "state")
        {
            EvaluateToplevelState();
        }
    }

    /// <summary>
    /// 读取 GDK Toplevel 的状态位，检测最小化 / 最大化 / 全屏三态变化并派发窗口事件。
    /// 与 Go 版 <c>handleSurfaceStateChangedPtr</c>（linux_purego_callbacks.go）一致：
    /// 首次观测（<c>observed = false</c>）只派发当前处于激活态的事件，
    /// 之后按前后状态差分派发；窗口的管理器/标题栏按钮触发的状态变化因此可被应用层感知。
    /// 使用 <c>Gdk.Internal.Toplevel.GetState</c>（对应 C API <c>gdk_toplevel_get_state</c>），
    /// 与本文件 StartDrag/StartResize 使用 <c>Gdk.Internal.Toplevel</c> 的方式一致。
    /// </summary>
    private void EvaluateToplevelState()
    {
        if (_closed || _surface is null)
        {
            return;
        }

        var state = Gdk.Internal.Toplevel.GetState(_surface.Handle.DangerousGetHandle());
        var current = new LinuxWindowState(
            Minimised: state.HasFlag(Gdk.ToplevelState.Minimized),
            Maximised: state.HasFlag(Gdk.ToplevelState.Maximized),
            Fullscreen: state.HasFlag(Gdk.ToplevelState.Fullscreen));

        var previous = new LinuxWindowState(_minimised, _maximised, _fullscreen);
        var events = ChangedWindowStateEvents(previous, current, _surfaceStateObserved);

        _minimised = current.Minimised;
        _maximised = current.Maximised;
        _fullscreen = current.Fullscreen;
        _surfaceStateObserved = true;

        var app = WailsApplication.Get();
        if (app is null)
        {
            return;
        }

        foreach (var eventType in events)
        {
            app.DispatchWindowEvent(_id, (uint)eventType);
        }
    }

    /// <summary>
    /// 计算窗口状态变化对应的事件列表，对应 Go 版 <c>changedLinuxWindowStateEvents</c>
    /// （webview_window_linux.go）。
    /// </summary>
    /// <param name="previous">上一次观测到的状态。</param>
    /// <param name="current">当前观测到的状态。</param>
    /// <param name="observed">是否已建立基线；为 false 时只报告当前处于激活态的事件。</param>
    /// <returns>需要派发的窗口事件，顺序为最大化、最小化、全屏。</returns>
    internal static IReadOnlyList<WindowEventType> ChangedWindowStateEvents(
        LinuxWindowState previous, LinuxWindowState current, bool observed)
    {
        var events = new List<WindowEventType>(3);

        if (!observed)
        {
            if (current.Maximised)
            {
                events.Add(WindowEventType.WindowMaximised);
            }
            if (current.Minimised)
            {
                events.Add(WindowEventType.WindowMinimised);
            }
            if (current.Fullscreen)
            {
                events.Add(WindowEventType.WindowFullscreen);
            }
            return events;
        }

        if (previous.Maximised != current.Maximised)
        {
            events.Add(current.Maximised
                ? WindowEventType.WindowMaximised
                : WindowEventType.WindowUnmaximised);
        }
        if (previous.Minimised != current.Minimised)
        {
            events.Add(current.Minimised
                ? WindowEventType.WindowMinimised
                : WindowEventType.WindowUnminimised);
        }
        if (previous.Fullscreen != current.Fullscreen)
        {
            events.Add(current.Fullscreen
                ? WindowEventType.WindowFullscreen
                : WindowEventType.WindowUnfullscreen);
        }

        return events;
    }

    /// <summary>
    /// 处理 GDK Surface 的 layout 信号，感知外部（窗口管理器/用户拖拽）触发的尺寸变化，
    /// 并按需派发 <see cref="WindowEventType.WindowResized"/> 事件（与 Windows 的 WM_SIZE、
    /// macOS 的 DidResize 对齐，复用既有事件常量）。
    /// </summary>
    /// <param name="sender">触发信号的 surface。</param>
    /// <param name="args">layout 事件参数，包含重算后的 Width / Height。</param>
    private void OnSurfaceLayout(Gdk.Surface sender, Gdk.Surface.LayoutSignalArgs args)
    {
        if (_closed || _minimised)
        {
            return;
        }

        var width = args.Width;
        var height = args.Height;

        // 首次 layout 仅建立基线，不派发事件，避免窗口创建时产生冗余事件。
        if (_surfaceWidth < 0 || _surfaceHeight < 0)
        {
            _surfaceWidth = width;
            _surfaceHeight = height;
            return;
        }

        if (width == _surfaceWidth && height == _surfaceHeight)
        {
            return;
        }

        _surfaceWidth = width;
        _surfaceHeight = height;
        WailsApplication.Get()?.DispatchWindowEvent(
            _id, (uint)WindowEventType.WindowResized);
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
    /// <remarks>
    /// GTK4 未提供对已显示窗口重新设置尺寸的公开 API（<c>gtk_window_resize</c> 已在 GTK4 中移除），
    /// <c>gtk_window_set_default_size</c> 只记录默认尺寸，对已实现（realize）的窗口通常不立即生效。
    /// 此处与 Wails v3.1.0（<c>linuxWebviewWindow.setSize</c> → <c>gtk_window_set_default_size</c>）完全一致，
    /// 属于 GTK4 平台限制而非移植缺陷；窗口的实际尺寸请以 <see cref="GetSize"/> 为准。
    /// </remarks>
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
        // 对外坐标一律为 DIP（GTK 逻辑像素），与 Wails v3 PR #6346 的 DIP 语义一致：
        // GDK 的显示器几何与窗口位置都定义在 DIP 空间，仅 wmctrl 需要物理像素。
        _x = x;
        _y = y;
        // GTK4 中窗口位置由窗口管理器控制。
        // X11 后端可通过 wmctrl 设置位置；Wayland 协议不允许客户端定位顶层窗口，
        // 应用无法直接控制窗口位置（上游 Wails v3.1.0 window_move_x11 同样在非 X11 直接返回），
        // 此时仅缓存坐标，且 GetPosition 按上游语义报告 (0, 0) 而非缓存的请求值。
        if (!LinuxBackendDetector.SupportsWindowPositionControl() || _window is null)
        {
            return;
        }

        MoveWindowByDipBounds(x, y);
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

        // 对齐上游 windowShow()：present 后重新应用待生效的置顶状态
        // （linux_purego.go 的 windowApplyPendingAlwaysOnTop），
        // 以及首次显示时的初始屏幕摆放（未映射窗口此前无法被 WM 定位）。
        if (_alwaysOnTop)
        {
            ApplyAlwaysOnTop(true);
        }

        if (_initialPlacementPending)
        {
            _initialPlacementPending = false;
            ApplyInitialPlacement();
        }

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
    /// <remarks>
    /// 对齐 Wails v3.1.0 <c>linuxWebviewWindow.maximise()</c>：只调用 GTK，
    /// 事件由 GDK Toplevel 状态观察者（<see cref="EvaluateToplevelState"/>）在状态实际变化后派发，
    /// 避免与窗口管理器触发的状态变化重复发事件。
    /// </remarks>
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
    }

    /// <inheritdoc />
    public void UnMinimise()
    {
        _window?.Unminimize();
    }

    /// <inheritdoc />
    public void Fullscreen()
    {
        _window?.Fullscreen();
    }

    /// <inheritdoc />
    /// <remarks>
    /// 对齐 Wails v3.1.0 <c>linux_purego.go</c> 的 <c>unfullscreen()</c>：
    /// 退出全屏后仍会调用一次 <c>gtk_window_unmaximize</c>，
    /// 因为窗口通常是由最大化状态进入全屏的，上游要求同时恢复最大化前的尺寸。
    /// </remarks>
    public void UnFullscreen()
    {
        _window?.Unfullscreen();
        _window?.Unmaximize();
    }

    /// <inheritdoc />
    /// <remarks>
    /// 上游 Go 版 <c>restore()</c> 为空实现（注释 <c>FIXME: never called!</c>），
    /// 本实现保留“退出最大化 + 取消最小化”的合理行为，事件同样由状态观察者派发。
    /// </remarks>
    public void Restore()
    {
        _window?.Unmaximize();
        _window?.Unminimize();
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

        // 断开 surface 级信号（state / layout），避免窗口销毁后仍收到回调导致泄漏。
        // 与既有信号处理一致：窗口/WebView 的托管信号随原生对象销毁由 GirCore 自动清理，
        // 而 surface 包装需在此显式解绑并释放引用。
        if (_surface is not null)
        {
            _surface.OnNotify -= OnSurfaceNotify;
            _surface.OnLayout -= OnSurfaceLayout;
            _surface = null;
        }
        _surfaceSignalsConnected = false;

        _window?.Close();
        _window?.Destroy();
        _window = null;
        _webView = null;

        // 分发 WindowClosed 事件，通知应用层窗口已销毁。
        // 对应 Win32 平台 WmDestroy 中的 DispatchWindowEvent(WindowClosed) 调用，
        // 以及 Wails v3 Go 版本中的 emit(WindowClosed)。
        // WindowManager 通过此事件从 _windows 字典中移除窗口。
        WailsApplication.Get()?.DispatchWindowEvent(_id, (uint)WindowEventType.WindowClosed);
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
    /// <remarks>
    /// 对齐 Wails v3.1.0 <c>windowShow()</c> / <c>setAlwaysOnTop()</c> 的延迟应用语义：
    /// 窗口尚未显示时仅缓存状态，待 <see cref="Show"/> 呈现后再通过 X11 EWMH 应用，
    /// 避免此时 <c>wmctrl :ACTIVE:</c> 命中其它窗口。
    /// </remarks>
    public void SetAlwaysOnTop(bool onTop)
    {
        _alwaysOnTop = onTop;
        if (_window is null || !OperatingSystem.IsLinux())
        {
            return;
        }

        if (!_visible)
        {
            return;
        }

        ApplyAlwaysOnTop(onTop);
    }

    /// <summary>
    /// 通过 X11 EWMH（wmctrl）应用置顶状态。
    /// GDK4 移除了 <c>gtk_window_set_keep_above</c>；Wayland 后端没有通用置顶协议，
    /// 需 compositor 专有扩展（如 layer-shell），此时仅缓存状态供查询。
    /// </summary>
    /// <param name="onTop">是否置顶。</param>
    private void ApplyAlwaysOnTop(bool onTop)
    {
        if (!LinuxBackendDetector.SupportsAlwaysOnTop())
        {
            return;
        }

        try
        {
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
            process?.WaitForExit(1000);
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
    /// <remarks>
    /// 对齐 Wails v3.1.0 <c>linux_purego.go</c> 的 <c>isMaximised()</c>：
    /// 最大化后进入全屏时 GTK 仍可能保留 maximized 状态位，
    /// 上游要求全屏窗口不得同时报告为最大化，故显式排除全屏。
    /// </remarks>
    public bool IsMaximised()
    {
        return _window is not null && _window.IsMaximized() && !_window.IsFullscreen();
    }

    /// <inheritdoc />
    /// <remarks>
    /// 对齐 Wails v3.1.0 <c>linux_purego.go</c> 的 <c>isMinimised()</c>：
    /// 窗口已 realize 时直接读取 GDK Toplevel 状态位（实时），
    /// 尚未 realize 时回退到上次观测值（上游此时同样返回 false）。
    /// </remarks>
    public bool IsMinimised()
    {
        if (_surface is null)
        {
            return _minimised;
        }

        var state = Gdk.Internal.Toplevel.GetState(_surface.Handle.DangerousGetHandle());
        return state.HasFlag(Gdk.ToplevelState.Minimized);
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
    /// <remarks>
    /// 与 Wails v3.1.0 <c>linuxWebviewWindow.size()</c> 对齐：优先读取窗口已分配的实际尺寸
    /// （<c>gtk_widget_get_width/get_height</c>），仅在窗口尚未布局（返回 0）时回退到默认尺寸。
    /// 这样用户或窗口管理器改变尺寸后不会返回过期的默认值。
    /// </remarks>
    public (int Width, int Height) GetSize()
    {
        if (_window is null)
        {
            return (0, 0);
        }

        // GTK 的分配尺寸与默认尺寸都以应用像素（即 DIP 逻辑像素）表示，
        // 与 Wails v3 PR #6346 的 GTK DIP 语义一致，无需再按缩放因子换算。
        var width = _window.GetWidth();
        var height = _window.GetHeight();
        if (width > 0 && height > 0)
        {
            return (width, height);
        }

        _window.GetDefaultSize(out var defaultWidth, out var defaultHeight);
        return (defaultWidth, defaultHeight);
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
    /// <remarks>
    /// 与 Wails v3.1.0 <c>linuxWebviewWindow.position()</c> 对齐：仅 X11 后端可定位窗口，
    /// 非 X11（Wayland 等）返回 <c>(0, 0)</c>，而不是把未生效的请求坐标当作真实位置返回。
    /// X11 下返回调用方传入的 DIP 坐标（GTK4 没有查询窗口绝对位置的公开 API，只能缓存请求值）。
    /// </remarks>
    public (int X, int Y) GetPosition()
    {
        return ResolveReportedPosition(
            LinuxBackendDetector.SupportsWindowPositionControl(), _x, _y);
    }

    /// <summary>
    /// 计算 <see cref="GetPosition"/> 对外报告的坐标。
    /// 与 Wails v3.1.0 <c>linuxWebviewWindow.position()</c> 对齐：
    /// 后端不支持窗口定位（Wayland 等）时返回 <c>(0, 0)</c>。
    /// </summary>
    /// <param name="supportsPositionControl">当前后端是否支持窗口定位。</param>
    /// <param name="cachedX">缓存的 X 坐标（DIP）。</param>
    /// <param name="cachedY">缓存的 Y 坐标（DIP）。</param>
    /// <returns>对外报告的位置。</returns>
    internal static (int X, int Y) ResolveReportedPosition(
        bool supportsPositionControl, int cachedX, int cachedY)
    {
        return supportsPositionControl ? (cachedX, cachedY) : (0, 0);
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
    public System.Threading.Tasks.Task<byte[]?> CapturePreviewAsync()
    {
        // WebKitGTK 无直接的 CapturePreview API，返回 null 表示不支持。
        // 可通过 GTK 截图功能间接实现。
        return System.Threading.Tasks.Task.FromResult<byte[]?>(null);
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
    /// <remarks>
    /// 对齐 Wails v3.1.0 <c>linux_purego.go</c> 的 <c>setHTML</c>：以 <c>wails://</c> 作为基线 URL，
    /// 而不是 <c>about:blank</c>。否则文档来源变成不透明源，<see cref="LinuxIpcOriginGuard"/>
    /// 会按安全策略拒绝该页面的 <c>/wails/*</c> 请求，导致 SetHTML 窗口无法调用绑定。
    /// </remarks>
    public void SetHTML(string html)
    {
        LoadHtmlWithWailsBase(html);
    }

    /// <summary>
    /// 以 <see cref="WailsBaseUrl"/> 为基线加载 HTML 文档，并同步记录当前 URL。
    /// 加载前确保 <c>wails</c> URI scheme 已注册，使页面内的 IPC 请求有处理器可用。
    /// </summary>
    /// <param name="html">HTML 内容。</param>
    private void LoadHtmlWithWailsBase(string html)
    {
        if (_webView is null)
        {
            return;
        }

        EnsureWailsSchemeRegistered();
        _webView.LoadHtml(html, WailsBaseUrl);
        _currentUrl = WailsBaseUrl;
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
    public void SetTaskbarProgress(TaskbarProgressState state, ulong completed, ulong total)
    {
        // Linux 无统一任务栏进度 API：经 Unity LauncherEntry D-Bus 信号广播，
        // 由桌面环境（Unity/KDE Plasma/兼容扩展）在启动器图标上绘制进度。
        var appId = ResolveLauncherAppId();
        if (appId is null)
        {
            return;
        }

        LinuxLauncherEntry.TryUpdate(appId, LinuxLauncherEntry.BuildProgressState(state, completed, total));
    }

    /// <inheritdoc />
    public void SetBadgeCount(int count)
    {
        var appId = ResolveLauncherAppId();
        if (appId is null)
        {
            return;
        }

        LinuxLauncherEntry.TryUpdate(appId, LinuxLauncherEntry.BuildBadgeState(count));
    }

    /// <inheritdoc />
    public void SetBadgeLabel(string? label)
    {
        // Unity LauncherEntry 仅支持数字计数；空标签清除徽章，非数字文本忽略
        // （Linux 无跨桌面文本徽章机制）。
        if (string.IsNullOrEmpty(label))
        {
            SetBadgeCount(0);
            return;
        }

        if (int.TryParse(label, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var count))
        {
            SetBadgeCount(count);
        }
    }

    /// <summary>
    /// 解析用于启动器 URI 的 application id。
    /// 优先使用平台应用已解析的 id，退化时按应用名重新推导。
    /// </summary>
    /// <returns>application id；无法解析时为 null。</returns>
    private static string? ResolveLauncherAppId()
    {
        var app = WailsApplication.Get();
        if (app?.PlatformApp is LinuxPlatformApp linux)
        {
            return linux.ApplicationId;
        }

        return app is null
            ? null
            : LinuxApplicationId.ResolveApplicationId(
                app.Options.Name, LinuxApplicationId.ApplicationIdOverride, out _);
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
    /// <remarks>
    /// 对齐 Wails v3.1.0 <c>linuxWebviewWindow.center()</c>（webview_window_linux.go:187-199）：
    /// 使用窗口<strong>当前所在显示器</strong>的几何信息（<c>gdk_display_get_monitor_at_surface</c>），
    /// 而不是固定取主显示器；且只移动窗口、不改变窗口可见性。
    /// 依据 Wails v3 PR #6346，GDK 的显示器几何是 DIP（逻辑像素），
    /// 而窗口尺寸同为 DIP，故居中计算全程在 DIP 空间完成，
    /// 仅在调用 wmctrl 时再按显示器缩放因子换算为物理像素。
    /// </remarks>
    public void Centre()
    {
        if (_window is null || !OperatingSystem.IsLinux())
        {
            return;
        }

        var monitor = ResolveCurrentMonitor();
        if (monitor is null)
        {
            return;
        }

        var geometry = monitor.Geometry;
        // 与 Wails v3.1.0 center() 对齐：按窗口实际尺寸（而非默认尺寸）计算居中坐标；
        // 窗口尚未布局时 GetSize 回退到默认尺寸。
        var (width, height) = GetSize();

        // 计算居中坐标（DIP）。
        var x = geometry.X + (geometry.Width - width) / 2;
        var y = geometry.Y + (geometry.Height - height) / 2;
        _x = x;
        _y = y;

        // X11 后端通过 wmctrl 移动窗口到计算后的居中坐标；
        // Wayland 后端下应用无法直接控制窗口位置，compositor 通常会自动居中，
        // 此时坐标仅缓存，GetPosition 按上游语义返回 (0, 0)。
        if (LinuxBackendDetector.SupportsWindowPositionControl())
        {
            MoveWindowByDipBounds(x, y);
        }
    }

    /// <summary>
    /// 应用初始屏幕摆放。对应 Wails v3.1.0 <c>applyScreenPlacement()</c>：
    /// 居中或未给出有效坐标时调用 <see cref="Centre"/>，否则按 DIP 绝对坐标定位。
    /// </summary>
    private void ApplyInitialPlacement()
    {
        if (ShouldCenterOnInitialPlacement(_options.X, _options.Y, _options.Centered))
        {
            Centre();
            return;
        }

        SetPosition(_options.X, _options.Y);
    }

    /// <summary>
    /// 判断初始摆放是否应居中。对应上游 <c>applyScreenPlacement()</c> 的居中分支；
    /// 本移植的选项模型沿用 Windows 语义，<c>X/Y = -1</c> 表示“未指定”，同样按居中处理。
    /// </summary>
    /// <param name="x">窗口 X 坐标（DIP，-1 表示未指定）。</param>
    /// <param name="y">窗口 Y 坐标（DIP，-1 表示未指定）。</param>
    /// <param name="centered">调用方是否显式要求居中。</param>
    /// <returns>应居中返回 true。</returns>
    internal static bool ShouldCenterOnInitialPlacement(int x, int y, bool centered)
    {
        return centered || x < 0 || y < 0;
    }

    /// <summary>
    /// 解析窗口当前所在显示器，对应 Go 版 <c>getCurrentMonitor()</c>（linux_purego.go:684-693）：
    /// 已 realize 时使用 <c>gdk_display_get_monitor_at_surface</c>；
    /// 尚未 realize 时回退到首显示器（上游此时返回 0 并放弃居中，
    /// 这里为保持“创建后默认居中”的可预期行为而回退）。
    /// </summary>
    private Gdk.Monitor? ResolveCurrentMonitor()
    {
        var display = Gdk.Display.GetDefault();
        if (display is null)
        {
            return null;
        }

        if (_surface is not null)
        {
            return display.GetMonitorAtSurface(_surface);
        }

        var monitors = display.GetMonitors();
        if (monitors.GetNItems() == 0)
        {
            return null;
        }

        return monitors.GetObject(0) as Gdk.Monitor;
    }

    /// <summary>
    /// 按 DIP 坐标移动窗口（X11 下通过 wmctrl 实现）。
    /// 依据 Wails v3 PR #6346，窗口对外坐标与显示器几何都在 DIP 空间，
    /// 而 wmctrl 使用 X11 根窗口坐标（物理像素），因此仅在真正调用 wmctrl 前
    /// 按目标显示器的缩放因子换算，避免把缩放后的像素坐标暴露给调用方。
    /// 宽度/高度参数传 <c>-1</c> 表示保持当前尺寸（wmctrl 不设置对应 presence 标志），
    /// 与上游 Wails v3.1.0 <c>linuxWebviewWindow.move()</c> 只移动不改变尺寸的行为一致。
    /// 不具备位置控制能力（Wayland）或 wmctrl 不可用时静默忽略，坐标仍已缓存。
    /// </summary>
    /// <param name="dipX">窗口左上角 X 坐标（DIP）。</param>
    /// <param name="dipY">窗口左上角 Y 坐标（DIP）。</param>
    private void MoveWindowByDipBounds(int dipX, int dipY)
    {
        if (!LinuxBackendDetector.SupportsWindowPositionControl() || _window is null)
        {
            return;
        }

        try
        {
            var title = _window.GetTitle() ?? string.Empty;
            if (string.IsNullOrEmpty(title))
            {
                return;
            }

            var target = LinuxDipCoordinates.SelectTargetMonitor(
                CollectMonitorInfos(), dipX, dipY);
            var scaleFactor = target?.ScaleFactor ?? LinuxDipCoordinates.DefaultScaleFactor;

            var x = LinuxDipCoordinates.DipToPhysical(dipX, scaleFactor);
            var y = LinuxDipCoordinates.DipToPhysical(dipY, scaleFactor);

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "wmctrl",
                // 宽度/高度传 -1：wmctrl 据此清空对应的 presence 标志，只移动、不改尺寸。
                Arguments = $"-r \"{title}\" -e 0,{x},{y},-1,-1",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(psi);
            process?.WaitForExit(1000);
        }
        catch
        {
            // wmctrl 不可用时忽略，坐标已缓存供 GetPosition 查询。
        }
    }

    /// <summary>
    /// 收集当前显示器的 DIP 几何与缩放因子，供 DIP → 物理像素换算选择目标显示器。
    /// GDK 报告的 <c>Geometry</c> 为逻辑像素（DIP），<c>ScaleFactor</c> 为该显示器的缩放比例。
    /// </summary>
    /// <returns>显示器信息列表；无显示器或 GDK 不可用时返回空列表。</returns>
    private static IReadOnlyList<LinuxMonitorInfo> CollectMonitorInfos()
    {
        var display = Gdk.Display.GetDefault();
        if (display is null)
        {
            return System.Array.Empty<LinuxMonitorInfo>();
        }

        var monitors = display.GetMonitors();
        var count = monitors.GetNItems();
        if (count == 0)
        {
            return System.Array.Empty<LinuxMonitorInfo>();
        }

        var infos = new List<LinuxMonitorInfo>((int)count);
        for (uint i = 0; i < count; i++)
        {
            var monitor = monitors.GetObject(i) as Gdk.Monitor;
            if (monitor is null)
            {
                continue;
            }

            var geometry = monitor.Geometry;
            infos.Add(new LinuxMonitorInfo(
                geometry.X,
                geometry.Y,
                geometry.Width,
                geometry.Height,
                (double)monitor.ScaleFactor,
                isPrimary: i == 0));
        }

        return infos;
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

    /// <summary>
    /// 获取窗口当前页面的 URL，供 IPC 来源（Origin）同源校验使用。
    /// 优先读取 WebKit 当前 URI；WebView 尚未就绪或读取失败时回退到最近一次导航记录的 URL。
    /// </summary>
    /// <returns>窗口当前页面 URL；不可用时返回空字符串。</returns>
    private string GetCurrentWindowUrl()
    {
        try
        {
            var uri = _webView?.GetUri();
            return string.IsNullOrEmpty(uri) ? _currentUrl : uri;
        }
        catch
        {
            // WebView 已销毁 / 原生调用失败时回退到记录值。
            return _currentUrl;
        }
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
    public void SetOpacity(float opacity)
    {
        if (_window is null || !OperatingSystem.IsLinux())
        {
            return;
        }

        // GTK4 通过 set_opacity 设置窗口整体透明度（0.0-1.0）。
        // 对应 Wails v3 的 window.setOpacity 和 Tauri v2 的 window.setAlpha。
        opacity = Math.Max(0f, Math.Min(1f, opacity));
        _window.SetOpacity(opacity);
    }

    /// <inheritdoc />
    public float GetOpacity()
    {
        if (_window is null || !OperatingSystem.IsLinux())
        {
            return 1.0f;
        }

        return (float)_window.GetOpacity();
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
    public void PrintToPDF(string path, PrintToPdfOptions? options)
    {
        // Linux 实现委托到无选项重载，WebKitGTK 的 PrintOperation 对选项支持有限。
        // options 中的 Landscape/PrintBackground 等设置可通过 Gtk.PageSetup 应用，此处简化处理。
        PrintToPDF(path);
    }

    /// <inheritdoc />
    public void RegisterCustomScheme(string scheme)
    {
        // Linux WebKitGTK 自定义协议需通过 WebContext 的 security 注册。
        // 简化实现：不拦截自定义 scheme，由 WebKit 使用默认网络栈处理。
        // 完整实现需使用 webkit_web_context_register_uri_scheme() C API。
    }

    /// <inheritdoc />
    public void Run(System.Action callback)
    {
        // 窗口就绪后立即执行回调；GTK4 中窗口在 Present 后即可交互。
        callback();
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
