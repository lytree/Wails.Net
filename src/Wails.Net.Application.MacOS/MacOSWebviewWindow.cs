using System.Text;
using System.Text.Json;
using Wails.Net.Application.Logging;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Webview;
using Wails.Net.Application.Options;
using Wails.Net.Application.Windows;
using Menu = Wails.Net.Application.Menus.Menu;
using WailsApplication = Wails.Net.Application.Application;

#if MACOS
using AppKit;
using CoreGraphics;
using Foundation;
using WebKit;
using Wails.Net.Application.Screens;
using Wails.Net.Events;
#endif

namespace Wails.Net.Application.Platform;

/// <summary>
/// macOS 平台 Webview 窗口实现（WKWebView + NSWindow）。
/// 对应 Wails v3 Go 版本 webview_window_darwin.go + webview_window_darwin.m。
/// <para>
/// 桥接模式：
/// <list type="bullet">
///   <item><b>上行消息</b>：前端 <c>window.webkit.messageHandlers.external.postMessage(json)</c>
///   经 <see cref="MacScriptMessageHandler"/> 转发到 <c>Application.HandleMessageFromFrontend</c>；
///   大消息回退 HTTP <c>POST /wails/message</c>，由 <see cref="MacUrlSchemeHandler"/> 的
///   <c>wails://</c> 自定义协议处理。</item>
///   <item><b>下行消息</b>：<c>EvaluateJavaScriptAsync</c> 调用 <c>window.__wailsNative.onMessage(json)</c>。
///   后端事件经 <c>window._wailsEmitEvent(name, data, windowId)</c> 下发。</item>
///   <item><b>资源加载</b>：<c>wails://localhost/</c> GET 请求由 AssetServer 提供（含 SPA 回退）。</item>
///   <item><b>运行时注入</b>：<c>WKUserScript</c>（document start）注入 <c>window._wails</c> 标志。</item>
///   <item><b>窗口事件</b>：NSWindow 强类型事件 → <c>DispatchWindowEvent</c>（WindowEventType 映射）。</item>
/// </list>
/// </para>
/// <para>
/// 非 macOS 目标（<c>#if !MACOS</c>）保留 no-op 骨架保证任意宿主编译。
/// </para>
/// </summary>
public sealed class MacOSWebviewWindow : IWebviewWindowImpl
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
    /// R2-4：当前窗口的 <see cref="WebviewRequestPipeline"/>，懒加载并缓存。
    /// </summary>
    private WebviewRequestPipeline? _pipeline;

    /// <summary>
    /// 应用级 macOS 选项（可为 null）。
    /// </summary>
    private readonly MacOptions? _macOptions;

    /// <summary>
    /// 窗口级 macOS 选项（可为 null）。
    /// </summary>
    private readonly WebviewWindowMacOptions? _macWindowOptions;

#if MACOS
    /// <summary>
    /// 原生 NSWindow 实例。
    /// </summary>
    private NSWindow? _window;

    /// <summary>
    /// 原生 WKWebView 实例。
    /// </summary>
    private WKWebView? _webView;

    /// <summary>
    /// WKWebView 配置实例（保存引用防止提前释放）。
    /// </summary>
    private WKWebViewConfiguration? _configuration;

    /// <summary>
    /// 脚本消息处理器（external）。
    /// </summary>
    private MacScriptMessageHandler? _scriptHandler;

    /// <summary>
    /// 导航代理。
    /// </summary>
    private MacNavigationDelegate? _navigationDelegate;

    /// <summary>
    /// UI 代理（alert/confirm/prompt 对话框）。
    /// </summary>
    private MacUiDelegate? _uiDelegate;

    /// <summary>
    /// 运行时是否已注入。
    /// </summary>
    private bool _runtimeInjected;
#endif

    /// <summary>
    /// 显式关闭标志（Close() 调用后允许 windowShouldClose 直接关闭）。
    /// </summary>
    private int _forceClose;

    /// <summary>
    /// 原生消息回调（SetNativeMessageHandler 注册）。
    /// </summary>
    private Func<string, Task>? _nativeMessageHandler;

    /// <summary>
    /// 控制台消息回调（SetConsoleMessageHandler 注册）。
    /// </summary>
    private Action<BrowserConsoleMessageLevel, string>? _consoleMessageHandler;

    /// <summary>
    /// 构造 MacOSWebviewWindow 实例。
    /// </summary>
    /// <param name="id">窗口 ID。</param>
    /// <param name="options">窗口选项。</param>
    /// <param name="macOptions">应用级 macOS 选项，可为 null。</param>
    public MacOSWebviewWindow(uint id, WebviewWindowOptions options, MacOptions? macOptions)
    {
        _id = id;
        _options = options;
        _macOptions = macOptions;
        _macWindowOptions = options.Mac;
    }

    /// <summary>
    /// 获取窗口 ID。
    /// </summary>
    public uint Id => _id;

#if MACOS
    /// <summary>
    /// 获取原生 NSWindow 实例（无则 null）。
    /// </summary>
    public NSWindow? NativeWindow => _window;

    /// <summary>
    /// 获取原生 WKWebView 实例（无则 null）。
    /// </summary>
    public WKWebView? NativeWebView => _webView;
#endif

    /// <summary>
    /// 初始化并显示窗口（在主线程创建 NSWindow + WKWebView）。
    /// 由 <see cref="MacOSPlatformApp.CreateWebviewWindow"/> 调用。
    /// </summary>
    public void Create()
    {
#if MACOS
        MacOSPlatformApp.DispatchOnMainThreadSync(() =>
        {
            var macOptions = _macOptions ?? new MacOptions();
            var windowMac = _macWindowOptions;
            var titleBar = windowMac?.TitleBar ?? macOptions.TitleBar ?? new MacTitleBarOptions();

            var styleMask = NSWindowStyle.Titled | NSWindowStyle.Closable
                | NSWindowStyle.Miniaturizable | NSWindowStyle.Resizable;
            if (_options.Frameless && (macOptions.CornerType is 1 or 2))
            {
                styleMask = NSWindowStyle.Borderless | NSWindowStyle.Resizable | NSWindowStyle.Miniaturizable;
            }
            else if (_options.Frameless)
            {
                styleMask |= NSWindowStyle.FullSizeContentView;
            }

            var width = Math.Max(1, _options.Width);
            var height = Math.Max(1, _options.Height);
            _window = new NSWindow(
                new CGRect(0, 0, width, height),
                styleMask,
                NSBackingStore.Buffered,
                false);

            // 内容视图（承载 WKWebView，支持圆角）。
            var contentView = new NSView(new CGRect(0, 0, width, height))
            {
                AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable,
            };
            if (_options.Frameless && macOptions.CornerType == 2 && macOptions.CornerRadius > 0)
            {
                contentView.WantsLayer = true;
                if (contentView.Layer is not null)
                {
                    contentView.Layer.CornerRadius = (nfloat)macOptions.CornerRadius;
                    contentView.Layer.MasksToBounds = true;
                }
            }

            _window.ContentView = contentView;
            if (_options.Frameless && macOptions.CornerType != 2)
            {
                _window.TitlebarAppearsTransparent = true;
                _window.TitleVisibility = NSWindowTitleVisibility.Hidden;
            }

            // WKWebView 配置。
            _configuration = new WKWebViewConfiguration
            {
                SuppressesIncrementalRendering = true,
            };
            _configuration.ApplicationNameForUserAgent =
                string.IsNullOrEmpty(macOptions.WebviewPreferences?.ApplicationNameForUserAgent)
                    ? "wails"
                    : macOptions.WebviewPreferences.ApplicationNameForUserAgent;

            ApplyWebviewPreferences(_configuration, macOptions);

            // 用户内容控制器：注入运行时 + 注册 external 消息处理器。
            var userContentController = new WKUserContentController();
            _scriptHandler = new MacScriptMessageHandler(this);
            userContentController.AddScriptMessageHandler(_scriptHandler, "external");
            _configuration.UserContentController = userContentController;

            // wails:// 自定义协议（资源 + IPC）。
            var schemeHandler = new MacUrlSchemeHandler(this);
            _configuration.SetUrlSchemeHandler(schemeHandler, "wails");

            _webView = new WKWebView(new CGRect(0, 0, width, height), _configuration)
            {
                AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable,
            };
            if (macOptions.WebviewPreferences?.AllowsBackForwardNavigationGestures is { } gestures)
            {
                _webView.AllowsBackForwardNavigationGestures = gestures;
            }

            if (macOptions.WebviewPreferences?.AllowsMagnification is { } magnification)
            {
                _webView.AllowsMagnification = magnification;
            }

            _navigationDelegate = new MacNavigationDelegate(this);
            _uiDelegate = new MacUiDelegate(this);
            _webView.NavigationDelegate = _navigationDelegate;
            _webView.UIDelegate = _uiDelegate;

            contentView.AddSubview(_webView);

            // 挂接窗口事件。
            AttachWindowEvents(_window);

            // 运行时注入（document start，导航前）。
            InjectRuntimeJs();

            // 调试模式：启用 Web Inspector（参照 DevToys developerExtrasEnabled 方式）。
            if (_options.ShowDevmodeEnabled)
            {
                _configuration.Preferences.SetValueForKey(
                    NSObject.FromObject(true),
                    new NSString("developerExtrasEnabled"));
            }

            // 应用窗口选项。
            ApplyWindowOptions(macOptions, titleBar);
        });
#endif
    }

#if MACOS
    /// <summary>
    /// 应用 WKWebView 偏好设置。
    /// 对应 Wails v3 Go 版本 webview_window_darwin.go 的 preferences 应用逻辑。
    /// </summary>
    /// <param name="config">WKWebView 配置。</param>
    /// <param name="macOptions">应用级 macOS 选项。</param>
    private static void ApplyWebviewPreferences(WKWebViewConfiguration config, MacOptions macOptions)
    {
        var prefs = macOptions.WebviewPreferences;
        if (prefs is null)
        {
            return;
        }

        if (prefs.TabFocusesLinks is { } tabFocuses)
        {
            config.Preferences.TabFocusesLinks = tabFocuses;
        }

        if (prefs.JavaScriptCanOpenWindowsAutomatically is { } canOpen)
        {
            config.Preferences.JavaScriptCanOpenWindowsAutomatically = canOpen;
        }

        if (prefs.MinimumFontSize is { } minFont)
        {
            config.Preferences.MinimumFontSize = (nfloat)minFont;
        }

        if (prefs.AllowsAirPlayForMediaPlayback is { } airPlay)
        {
            config.AllowsAirPlayForMediaPlayback = airPlay;
        }

        if (prefs.EnableAutoplayWithoutUserAction == true)
        {
            config.MediaTypesRequiringUserActionForPlayback = WKAudiovisualMediaTypes.None;
        }

        config.Preferences.FraudulentWebsiteWarningEnabled = macOptions.EnableFraudulentWebsiteWarnings;
    }

    /// <summary>
    /// 应用窗口级选项（大小、标题、背景、全屏等）。
    /// 对应 Wails v3 Go 版本 run() 中的窗口配置逻辑。
    /// </summary>
    /// <param name="macOptions">应用级 macOS 选项。</param>
    /// <param name="titleBar">标题栏选项。</param>
    private void ApplyWindowOptions(MacOptions macOptions, MacTitleBarOptions titleBar)
    {
        if (_window is null || _webView is null)
        {
            return;
        }

        SetTitle(_options.Title);
        SetResizable(_options.Resizable);
        if (_options.MinWidth > 0 || _options.MinHeight > 0)
        {
            SetMinSize(_options.MinWidth, _options.MinHeight);
        }

        if (_options.MaxWidth > 0 || _options.MaxHeight > 0)
        {
            SetMaxSize(_options.MaxWidth, _options.MaxHeight);
        }

        SetBackgroundColour(_options.R, _options.G, _options.B, _options.A);

        // 背景类型（透明/半透明）。
        var backdrop = _macWindowOptions?.Backdrop ?? macOptions.Backdrop;
        if (backdrop == 1 || string.Equals(_options.BackgroundType, "transparent", StringComparison.OrdinalIgnoreCase))
        {
            _window.Opaque = false;
            _window.BackgroundColor = NSColor.Clear;
            // 参照 DevToys BlazorWKWebView：通过 KVC 键 drawsBackground 使 WKWebView 透明。
            _webView.SetValueForKey(NSObject.FromObject(false), new NSString("drawsBackground"));
        }
        else if (backdrop == 2 || string.Equals(_options.BackgroundType, "translucent", StringComparison.OrdinalIgnoreCase))
        {
            _window.Opaque = false;
            var effectView = new NSVisualEffectView(_window.ContentView.Bounds)
            {
                AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable,
                BlendingMode = NSVisualEffectBlendingMode.BehindWindow,
                State = NSVisualEffectState.Active,
            };
            _window.ContentView.AddSubview(effectView, NSWindowOrderingMode.Below, null);
            _webView.SetValueForKey(NSObject.FromObject(false), new NSString("drawsBackground"));
        }

        // 标题栏选项。
        if (!_options.Frameless)
        {
            _window.TitlebarAppearsTransparent = titleBar.AppearsTransparent;
            if (titleBar.Hide)
            {
                _window.StyleMask &= ~NSWindowStyle.Titled;
            }

            _window.TitleVisibility = titleBar.HideTitle ? NSWindowTitleVisibility.Hidden : NSWindowTitleVisibility.Visible;
            if (titleBar.FullSizeContent)
            {
                _window.StyleMask |= NSWindowStyle.FullSizeContentView;
            }
        }

        // 外观（明暗主题覆盖）。
        var appearanceName = _macWindowOptions?.Appearance;
        if (!string.IsNullOrEmpty(appearanceName))
        {
            var appearance = NSAppearance.GetAppearance(appearanceName);
            if (appearance is not null)
            {
                _window.Appearance = appearance;
            }
        }

        // 阴影。
        _window.HasShadow = !(_macWindowOptions?.DisableShadow ?? macOptions.DisableShadow);

        // 层级（WindowLevel）。
        var level = _macWindowOptions?.WindowLevel ?? 0;
        if (level != 0)
        {
            _window.Level = (nfloat)level;
        }

        // 初始状态。
        if (_options.Fullscreen)
        {
            Fullscreen();
        }
        else if (_options.Maximised)
        {
            Maximise();
        }
        else if (_options.Minimised)
        {
            Minimise();
        }

        // 位置（Y 翻转为主屏 top-left 原点）。
        if (_options.Centered || (_options.X == -1 && _options.Y == -1))
        {
            Centre();
        }
        else
        {
            SetPosition(_options.X, _options.Y);
        }

        // 加载内容：URL > HTML > wails:// 资源。
        if (!string.IsNullOrEmpty(_options.URL))
        {
            LoadURL(_options.URL);
        }
        else if (!string.IsNullOrEmpty(_options.HTML))
        {
            LoadHTML(_options.HTML);
        }
        else
        {
            LoadURL("wails://localhost/");
        }

        if (!_options.Hidden)
        {
            Show();
        }
    }

    /// <summary>
    /// 注入 Wails 运行时 JS（document start 的 WKUserScript）。
    /// 对应 Linux 平台 InjectRuntimeJs：在页面脚本执行前注入 <c>window._wails</c> 标志。
    /// </summary>
    private void InjectRuntimeJs()
    {
        if (_runtimeInjected || _webView is null || _configuration?.UserContentController is not { } ucc)
        {
            return;
        }

        var app = WailsApplication.Get();
        if (app is null)
        {
            return;
        }

        try
        {
            var js = app.GenerateRuntimeJs(false);
            if (!string.IsNullOrEmpty(js))
            {
                var script = new WKUserScript(js, WKUserScriptInjectionTime.AtDocumentStart, false);
                ucc.AddUserScript(script);
                _runtimeInjected = true;
            }
        }
        catch
        {
            // 运行时注入失败时忽略，不影响窗口正常使用。
        }
    }
#endif

    /// <inheritdoc />
    public void SetTitle(string title)
    {
#if MACOS
        if (_window is not null && !_options.Frameless)
        {
            _window.Title = title;
        }
#endif
    }

    /// <inheritdoc />
    public void SetSize(int width, int height)
    {
#if MACOS
        if (_window is null)
        {
            return;
        }

        _window.SetContentSize(new CGSize(width, height));
#endif
    }

    /// <inheritdoc />
    public void SetMinSize(int width, int height)
    {
#if MACOS
        if (_window is not null)
        {
            _window.MinSize = new CGSize(width, height);
        }
#endif
    }

    /// <inheritdoc />
    public void SetMaxSize(int width, int height)
    {
#if MACOS
        if (_window is not null)
        {
            _window.MaxSize = width > 0 && height > 0 ? new CGSize(width, height) : new CGSize(float.MaxValue, float.MaxValue);
        }
#endif
    }

    /// <inheritdoc />
    public void SetPosition(int x, int y)
    {
#if MACOS
        if (_window is null)
        {
            return;
        }

        // 将 top-left 原点坐标转换为 AppKit 的 bottom-left 原点。
        var primaryHeight = GetPrimaryScreenHeight();
        var frame = _window.Frame;
        frame.X = x;
        frame.Y = primaryHeight - frame.Height - y;
        _window.SetFrame(frame, true);
#endif
    }

    /// <inheritdoc />
    public void Show()
    {
#if MACOS
        _window?.MakeKeyAndOrderFront(null);
        NSApplication.SharedApplication.ActivateIgnoringOtherApps(true);
#endif
    }

    /// <inheritdoc />
    public void Hide()
    {
#if MACOS
        _window?.OrderOut(null);
#endif
    }

    /// <inheritdoc />
    public void Maximise()
    {
#if MACOS
        if (_window is not null && !_window.IsZoomed)
        {
            _window.Zoom(null);
        }
#endif
    }

    /// <inheritdoc />
    public void UnMaximise()
    {
#if MACOS
        if (_window is not null && _window.IsZoomed)
        {
            _window.Zoom(null);
        }
#endif
    }

    /// <inheritdoc />
    public void Minimise()
    {
#if MACOS
        _window?.Miniaturize(null);
#endif
    }

    /// <inheritdoc />
    public void UnMinimise()
    {
#if MACOS
        _window?.Deminiaturize(null);
#endif
    }

    /// <inheritdoc />
    public void Fullscreen()
    {
#if MACOS
        if (_window is not null && !IsFullscreen())
        {
            _window.ToggleFullScreen(null);
        }
#endif
    }

    /// <inheritdoc />
    public void UnFullscreen()
    {
#if MACOS
        if (_window is not null && IsFullscreen())
        {
            _window.ToggleFullScreen(null);
        }
#endif
    }

    /// <inheritdoc />
    public void Restore()
    {
#if MACOS
        if (_window is null)
        {
            return;
        }

        if (IsFullscreen())
        {
            _window.ToggleFullScreen(null);
        }

        if (_window.IsZoomed)
        {
            _window.Zoom(null);
        }

        if (_window.IsMiniaturized)
        {
            _window.Deminiaturize(null);
        }
#endif
    }

    /// <inheritdoc />
    public void Close()
    {
#if MACOS
        Interlocked.Exchange(ref _forceClose, 1);
        _window?.Close();
#endif
    }

    /// <inheritdoc />
    public void Focus()
    {
#if MACOS
        if (_window is not null)
        {
            if (!NSApplication.SharedApplication.Active)
            {
                NSApplication.SharedApplication.ActivateIgnoringOtherApps(true);
            }

            _window.MakeKeyAndOrderFront(null);
            _window.MakeKeyWindow();
        }
#endif
    }

    /// <inheritdoc />
    /// <remarks>
    /// 请求用户注意：通过 <c>NSApp.requestUserAttention:</c> 让应用 Dock 图标弹跳（bounce），
    /// 把用户注意力拉回需要处理的窗口。对应 Wails v3 Go 版本
    /// <c>webview_window_darwin.go</c> 中的 <c>windowFlash</c>。
    /// <para>
    /// 对齐 Go 语义：仅当 <paramref name="enabled"/> 为 true 时发起一次
    /// <c>NSInformationalRequest</c>（信息性请求，图标弹跳一次，窗口参数在原生实现中未使用）；
    /// 该请求会自行结束，因此禁用（false）为 no-op，不调用 cancel。原生调用返回的令牌仅供
    /// <c>cancelUserAttentionRequest</c> 取消使用，信息性请求无需取消，故与 Go 一致地忽略返回值。
    /// </para>
    /// <para>
    /// 注意：需在 AppKit 主线程调用（由平台事件循环保证）；非 macOS 目标下为 no-op 骨架。
    /// </para>
    /// </remarks>
    public void Flash(bool enabled)
    {
#if MACOS
        if (enabled)
        {
            // NSApplication.SharedApplication 为 NSApp 的托管映射。
            NSApplication.SharedApplication.RequestUserAttention(NSRequestUserAttentionType.InformationalRequest);
        }
#endif
    }

    /// <inheritdoc />
    public void ShowMenuBar()
    {
        // macOS 菜单栏由 NSApp.MainMenu 管理，窗口级菜单栏 no-op。
    }

    /// <inheritdoc />
    public void HideMenuBar()
    {
        // macOS 菜单栏由 NSApp.MainMenu 管理，窗口级菜单栏 no-op。
    }

    /// <inheritdoc />
    public void ToggleMenuBar()
    {
        // macOS 菜单栏由 NSApp.MainMenu 管理，窗口级菜单栏 no-op。
    }

    /// <inheritdoc />
    public void SetAlwaysOnTop(bool onTop)
    {
#if MACOS
        if (_window is not null)
        {
            _window.Level = onTop ? (nfloat)NSWindowLevel.Floating : (nfloat)NSWindowLevel.Normal;
        }
#endif
    }

    /// <inheritdoc />
    public void SetBackgroundColour(byte r, byte g, byte b, byte a)
    {
#if MACOS
        if (_window is not null)
        {
            _window.BackgroundColor = NSColor.FromDeviceRgba(r / 255f, g / 255f, b / 255f, a / 255f);
        }
#endif
    }

    /// <inheritdoc />
    public void SetBackgroundColour(int r, int g, int b, int a)
        => SetBackgroundColour((byte)r, (byte)g, (byte)b, (byte)a);

    /// <inheritdoc />
    public bool IsFullscreen()
    {
#if MACOS
        return _window is not null && (_window.StyleMask & NSWindowStyle.FullScreen) != 0;
#else
        return false;
#endif
    }

    /// <inheritdoc />
    public bool IsMaximised()
    {
#if MACOS
        return _window?.IsZoomed ?? false;
#else
        return false;
#endif
    }

    /// <inheritdoc />
    public bool IsMinimised()
    {
#if MACOS
        return _window?.IsMiniaturized ?? false;
#else
        return false;
#endif
    }

    /// <inheritdoc />
    public bool IsVisible()
    {
#if MACOS
        return _window?.IsVisible ?? false;
#else
        return false;
#endif
    }

    /// <inheritdoc />
    public bool IsFocused()
    {
#if MACOS
        return _window?.IsKeyWindow ?? false;
#else
        return false;
#endif
    }

    /// <inheritdoc />
    public void SetFrameless(bool frameless)
    {
#if MACOS
        if (_window is null)
        {
            return;
        }

        var macOptions = _macOptions ?? new MacOptions();
        if (frameless && macOptions.CornerType is 1 or 2)
        {
            _window.StyleMask = NSWindowStyle.Borderless | NSWindowStyle.Resizable | NSWindowStyle.Miniaturizable;
        }
        else if (frameless)
        {
            _window.StyleMask = NSWindowStyle.Titled | NSWindowStyle.Closable
                | NSWindowStyle.Miniaturizable | NSWindowStyle.Resizable | NSWindowStyle.FullSizeContentView;
            _window.TitlebarAppearsTransparent = true;
            _window.TitleVisibility = NSWindowTitleVisibility.Hidden;
        }
        else
        {
            _window.StyleMask = NSWindowStyle.Titled | NSWindowStyle.Closable
                | NSWindowStyle.Miniaturizable | NSWindowStyle.Resizable;
        }
#endif
    }

    /// <inheritdoc />
    public void OpenDevTools()
    {
#if MACOS
        // 参照 DevToys BlazorWKWebView：通过 KVC 键 developerExtrasEnabled 启用 Web Inspector，
        // 之后可用 Safari 的"开发"菜单调试本窗口（WKWebView 无公开的弹窗 inspector API）。
        _webView?.Configuration.Preferences.SetValueForKey(
            NSObject.FromObject(true),
            new NSString("developerExtrasEnabled"));
#endif
    }

    /// <inheritdoc />
    public void CloseDevTools()
    {
#if MACOS
        // 关闭后 Safari 开发菜单仍可调试已启用 inspectable 的页面；此处恢复默认设置。
        _webView?.Configuration.Preferences.SetValueForKey(
            NSObject.FromObject(false),
            new NSString("developerExtrasEnabled"));
#endif
    }

    /// <inheritdoc />
    public void SetZoom(float zoom) => SetZoom((double)zoom);

    /// <inheritdoc />
    public void SetZoomLevel(float level) => SetZoom((double)level);

    /// <inheritdoc />
    public (int Width, int Height) GetSize()
    {
#if MACOS
        if (_window is null)
        {
            return (0, 0);
        }

        var frame = _window.Frame;
        return ((int)frame.Width, (int)frame.Height);
#else
        return (0, 0);
#endif
    }

    /// <inheritdoc />
    public (int Width, int Height) GetContentSize()
    {
#if MACOS
        if (_window is null)
        {
            return (0, 0);
        }

        var size = _window.ContentView.Frame.Size;
        return ((int)size.Width, (int)size.Height);
#else
        return (0, 0);
#endif
    }

    /// <inheritdoc />
    public (int Width, int Height) GetMinSize()
    {
#if MACOS
        if (_window is null)
        {
            return (0, 0);
        }

        var size = _window.MinSize;
        return ((int)size.Width, (int)size.Height);
#else
        return (0, 0);
#endif
    }

    /// <inheritdoc />
    public (int Width, int Height) GetMaxSize()
    {
#if MACOS
        if (_window is null)
        {
            return (0, 0);
        }

        var size = _window.MaxSize;
        return ((int)size.Width, (int)size.Height);
#else
        return (0, 0);
#endif
    }

    /// <inheritdoc />
    public (int X, int Y) GetPosition()
    {
#if MACOS
        if (_window is null)
        {
            return (0, 0);
        }

        var primaryHeight = GetPrimaryScreenHeight();
        var frame = _window.Frame;
        return ((int)frame.X, (int)(primaryHeight - frame.Y - frame.Height));
#else
        return (0, 0);
#endif
    }

    /// <inheritdoc />
    public float GetZoom() => 1.0f;

    /// <inheritdoc />
    public float GetZoomLevel() => 1.0f;

    /// <inheritdoc />
    public void ExecJS(string js)
    {
#if MACOS
        if (_webView is null)
        {
            return;
        }

        try
        {
            // 带 completionHandler 的同步重载（参照 DevToys），fire-and-forget。
            _webView.EvaluateJavaScript(
                js,
                (NSObject result, NSError error) => { });
        }
        catch
        {
            // 页面未就绪或已销毁时忽略。
        }
#endif
    }

    /// <inheritdoc />
    public void GoBack()
    {
#if MACOS
        if (_webView?.CanGoBack == true)
        {
            _webView.GoBack();
        }
#endif
    }

    /// <inheritdoc />
    public void GoForward()
    {
#if MACOS
        if (_webView?.CanGoForward == true)
        {
            _webView.GoForward();
        }
#endif
    }

    /// <inheritdoc />
    public void Reload()
    {
#if MACOS
        _webView?.Reload();
#endif
    }

    /// <inheritdoc />
    public void ForceReload()
    {
#if MACOS
        _webView?.ReloadFromOrigin();
#endif
    }

    /// <inheritdoc />
    public void SetURL(string url) => LoadURL(url);

    /// <inheritdoc />
    public void SetHTML(string html) => LoadHTML(html);

    /// <inheritdoc />
    public void Print()
    {
        // WKWebView 打印需 NSPrintOperation（macOS 11+），暂未实现。
    }

    /// <inheritdoc />
    public void PrintToPDF(string path)
    {
        // WKWebView 无同步 PDF 导出 API，暂未实现。
    }

    /// <inheritdoc />
    public void SetMenu(Menu? menu)
    {
        // macOS 窗口级菜单由应用菜单（NSApp.MainMenu）管理，窗口内菜单 no-op。
    }

    /// <inheritdoc />
    public void StartDrag()
    {
        // macOS 窗口拖动由标题栏原生处理，无边框窗口需自定义拖动区域。
        // Wails v3 通过 delegate startDrag 实现，暂未实现。
    }

    /// <inheritdoc />
    public void StartResize()
    {
        // macOS 窗口调整大小由系统原生处理，no-op。
    }

    /// <inheritdoc />
    public void SetEnabled(bool enabled)
    {
#if MACOS
        if (_window is not null)
        {
            _window.IgnoresMouseEvents = !enabled;
        }
#endif
    }

    /// <inheritdoc />
    public void SetContentProtection(bool enabled)
    {
#if MACOS
        if (_window is not null)
        {
            _window.SharingType = enabled ? NSWindowSharingType.None : NSWindowSharingType.ReadOnly;
        }
#endif
    }

    /// <inheritdoc />
    public void AttachAsModal(uint parentWindowId)
    {
        // 模态窗口以 sheet 形式附加（需要父窗口句柄），暂未实现。
    }

    /// <inheritdoc />
    public void SetResizable(bool resizable)
    {
#if MACOS
        if (_window is null)
        {
            return;
        }

        if (resizable)
        {
            _window.StyleMask |= NSWindowStyle.Resizable;
        }
        else
        {
            _window.StyleMask &= ~NSWindowStyle.Resizable;
        }
#endif
    }

    /// <inheritdoc />
    public void SetMaximisable(bool maximisable)
    {
#if MACOS
        // 最大化按钮状态由 styleMask 控制，此处保持 no-op（系统原生）。
#endif
    }

    /// <inheritdoc />
    public void SetMinimisable(bool minimisable)
    {
#if MACOS
        if (_window is null)
        {
            return;
        }

        var button = _window.StandardWindowButton(NSWindowButton.MiniaturizeButton);
        if (button is not null)
        {
            button.Hidden = !minimisable;
            button.Enabled = minimisable;
        }
#endif
    }

    /// <inheritdoc />
    public void SetClosable(bool closable)
    {
#if MACOS
        if (_window is null)
        {
            return;
        }

        var button = _window.StandardWindowButton(NSWindowButton.CloseButton);
        if (button is not null)
        {
            button.Hidden = !closable;
            button.Enabled = closable;
        }
#endif
    }

    /// <inheritdoc />
    public void SetHasShadow(bool hasShadow)
    {
#if MACOS
        if (_window is not null)
        {
            _window.HasShadow = hasShadow;
        }
#endif
    }

    /// <inheritdoc />
    public void SetTitleBarStyle(TitleBarStyle style)
    {
#if MACOS
        if (_window is null)
        {
            return;
        }

        switch (style)
        {
            case TitleBarStyle.Hidden:
            case TitleBarStyle.HiddenInset:
                _window.TitlebarAppearsTransparent = true;
                _window.TitleVisibility = NSWindowTitleVisibility.Hidden;
                _window.StyleMask |= NSWindowStyle.FullSizeContentView;
                break;
            case TitleBarStyle.Unified:
                _window.TitleVisibility = NSWindowTitleVisibility.Visible;
                _window.TitlebarAppearsTransparent = false;
                break;
            default:
                _window.TitleVisibility = NSWindowTitleVisibility.Visible;
                _window.TitlebarAppearsTransparent = false;
                break;
        }
#endif
    }

    /// <inheritdoc />
    public void Centre()
    {
#if MACOS
        if (_window is null)
        {
            return;
        }

        var screen = _window.Screen ?? NSScreen.MainScreen;
        if (screen is null)
        {
            return;
        }

        var screenFrame = screen.VisibleFrame;
        var windowFrame = _window.Frame;
        var x = screenFrame.X + (screenFrame.Width - windowFrame.Width) / 2;
        var y = screenFrame.Y + (screenFrame.Height - windowFrame.Height) / 2;
        _window.SetFrame(new CGRect(x, y, windowFrame.Width, windowFrame.Height), true);
#endif
    }

    /// <inheritdoc />
    public void SetDebuggingEnabled(bool enabled)
    {
        // WKWebView 调试能力受系统 Web Inspector 开关控制，no-op。
    }

    /// <inheritdoc />
    public string GetURL()
    {
#if MACOS
        return _webView?.Url?.AbsoluteString ?? string.Empty;
#else
        return string.Empty;
#endif
    }

    /// <inheritdoc />
    public void LoadURL(string url)
    {
#if MACOS
        if (_webView is null || string.IsNullOrEmpty(url))
        {
            return;
        }

        using var nsUrl = NSUrl.FromString(url);
        if (nsUrl is null)
        {
            return;
        }

        _webView.LoadRequest(NSUrlRequest.FromUrl(nsUrl));
#endif
    }

    /// <inheritdoc />
    public void LoadHTML(string html)
    {
#if MACOS
        _webView?.LoadHtmlString(html, null);
#endif
    }

    /// <inheritdoc />
    public void SetZoom(double zoom)
    {
#if MACOS
        if (_webView is not null && zoom >= 1.0)
        {
            _webView.Magnification = (nfloat)zoom;
        }
#endif
    }

    /// <inheritdoc />
    public override string ToString() => $"MacOSWebviewWindow({_id})";

#if MACOS
    /// <summary>
    /// 挂接 NSWindow 事件到 Wails 窗口事件系统。
    /// </summary>
    /// <param name="window">NSWindow 实例。</param>
    private void AttachWindowEvents(NSWindow window)
    {
        window.WillClose += (_, _) => DispatchWindowEvent(WindowEventType.WindowClosed);
        window.DidResize += (_, _) => DispatchWindowEvent(WindowEventType.WindowResized);
        window.DidMove += (_, _) => DispatchWindowEvent(WindowEventType.WindowMoved);
        window.DidBecomeKey += (_, _) => DispatchWindowEvent(WindowEventType.WindowFocus);
        window.DidResignKey += (_, _) => DispatchWindowEvent(WindowEventType.WindowFocusLost);
        window.DidMiniaturize += (_, _) => DispatchWindowEvent(WindowEventType.WindowMinimised);
        window.DidDeminiaturize += (_, _) => DispatchWindowEvent(WindowEventType.WindowUnminimised);
        window.DidEnterFullScreen += (_, _) => DispatchWindowEvent(WindowEventType.WindowEnterFullScreen);
        window.DidExitFullScreen += (_, _) => DispatchWindowEvent(WindowEventType.WindowExitFullScreen);
        window.DidChangeScreen += (_, _) => DispatchWindowEvent(WindowEventType.WindowDPIChanged);
    }

    /// <summary>
    /// 导航完成回调：注入用户 JS/CSS 并触发 RuntimeReady 事件。
    /// 对应 Linux 平台 OnLoadChanged LoadState.Finished 分支。
    /// </summary>
    internal void OnNavigationFinished()
    {
        // 注入用户 JS。
        if (!string.IsNullOrEmpty(_options.JS))
        {
            ExecJS(_options.JS);
        }

        // 注入用户 CSS（动态 style 标签）。
        if (!string.IsNullOrEmpty(_options.CSS))
        {
            var css = JsonSerializer.Serialize(_options.CSS);
            ExecJS($"(function(){{var s=document.createElement('style');s.appendChild(document.createTextNode({css}));document.head.appendChild(s);}})();");
        }

        DispatchWindowEvent(WindowEventType.WindowRuntimeReady);
    }

    /// <summary>
    /// 通过 Application 分发窗口事件。
    /// </summary>
    /// <param name="eventType">窗口事件类型。</param>
    internal void DispatchWindowEvent(WindowEventType eventType)
    {
        WailsApplication.Get()?.DispatchWindowEvent(_id, (uint)eventType);
    }

    /// <summary>
    /// 处理前端脚本消息（webkit.messageHandlers.external）。
    /// </summary>
    /// <param name="message">消息体字符串。</param>
    internal void HandleScriptMessage(string message)
    {
        try
        {
            var app = WailsApplication.Get();
            if (app is null)
            {
                return;
            }

            if (_nativeMessageHandler is not null)
            {
                _ = _nativeMessageHandler(message);
                return;
            }

            var response = app.HandleMessageFromFrontend(message, _id).GetAwaiter().GetResult();
            if (response is not null)
            {
                var json = JsonSerializer.Serialize(response);
                PostNativeMessage(json);
            }
        }
        catch
        {
            // 消息处理失败时忽略。
        }
    }

    /// <summary>
    /// 通过 EvaluateJavaScript 向前端推送原生消息。
    /// 对应 transport 下行：window.__wailsNative.onMessage(json)。
    /// </summary>
    /// <param name="json">JSON 字符串。</param>
    internal void PostNativeMessage(string json)
    {
        var escaped = JsonSerializer.Serialize(json);
        ExecJS($"window.__wailsNative && window.__wailsNative.onMessage({escaped});");
    }

    /// <summary>
    /// 处理 wails:// 自定义协议请求。
    /// <para>
    /// 支持异步完成（WKURLSchemeTask 允许延迟调用 DidReceiveResponse/DidReceiveData/DidFinish）：
    /// IPC / 静态资源请求的管线任务通常同步完成，立即写响应；
    /// <c>/wails/stream/poll</c> 长轮询会挂起等待，此时 <c>await</c> 管线任务（不阻塞 RunLoop），
    /// 完成后若任务未被 WebKit 停止则写响应，避免对已停止任务回调导致崩溃。
    /// </para>
    /// </summary>
    /// <param name="urlSchemeTask">URL scheme 任务。</param>
    /// <param name="owner">发起该任务的协议处理器，用于检测任务是否已被 WebKit 停止；可为 null。</param>
    internal void HandleSchemeTask(IWKUrlSchemeTask urlSchemeTask, MacUrlSchemeHandler? owner = null)
    {
        HandleSchemeTaskAsync(urlSchemeTask, owner);
    }

    /// <summary>
    /// 异步完成 wails:// 协议请求（<see cref="HandleSchemeTask"/> 的异步实现）。
    /// </summary>
    /// <param name="urlSchemeTask">URL scheme 任务。</param>
    /// <param name="owner">协议处理器，用于检测任务是否已被停止；可为 null。</param>
    private async void HandleSchemeTaskAsync(IWKUrlSchemeTask urlSchemeTask, MacUrlSchemeHandler? owner)
    {
        try
        {
            var request = urlSchemeTask.Request;
            var path = request.Url?.Path ?? string.Empty;
            var httpMethod = request.HttpMethod ?? "GET";

            // R2-4：统一委托给 WebviewRequestPipeline。
            // Streams 控制数据通过 HTTP header（x-wails-stream-*）传递，需一并透传；
            // 二进制帧负载经 RequestBodyBytes 传递（batch / chunk 重组体无法用字符串承载）。
            // P1-9：为每个 scheme task 建立独立取消源；StopUrlSchemeTask 会取消它，
            // 从而中止在途管线任务（如 /wails/stream/poll 长轮询），令牌同时贯穿到管线内部。
            var cancellationToken = owner?.TrackTask(urlSchemeTask) ?? CancellationToken.None;
            var pipeline = GetOrCreatePipeline();
            var rawBody = ReadRequestBody(request);
            var webviewRequest = new WebviewRequest
            {
                Method = httpMethod,
                Path = path,
                // P2-12：携带查询串，供 dev 资产反向代理向 vite 透传 HMR 查询（如 ?import / ?t=）。
                Query = request.Url?.Query,
                RequestBody = rawBody.Text,
                RequestBodyBytes = rawBody.Bytes,
                WindowId = _id,
                WindowName = _options.Name,
                Origin = null,
                Headers = ReadRequestHeaders(request)
            };
            var response = await pipeline.HandleAsync(webviewRequest, cancellationToken).ConfigureAwait(true);

            if (owner?.IsStopped(urlSchemeTask) == true)
            {
                return; // 任务已被 WebKit 停止，不能再回调避免 crash
            }
            CompleteTask(urlSchemeTask, (nint)response.StatusCode, response.ContentType, response.Body);
        }
        catch (OperationCanceledException)
        {
            // 任务已被取消（页面导航/刷新）：不再回调任务，避免 crash。
        }
        catch
        {
            if (owner?.IsStopped(urlSchemeTask) != true)
            {
                CompleteTask(urlSchemeTask, 500, "text/plain", Encoding.UTF8.GetBytes("internal error"));
            }
        }
        finally
        {
            owner?.ClearStopped(urlSchemeTask);
        }
    }

    /// <summary>
    /// 读取 wails:// 请求头（<c>NSUrlRequest.AllHTTPHeaderFields</c>）为大小写不敏感字典。
    /// 供 Streams 双向字节流通过 HTTP header（<c>x-wails-stream-*</c>）传递控制数据。
    /// </summary>
    /// <param name="request">URL 请求。</param>
    /// <returns>大小写不敏感的请求头字典。</returns>
    private static IReadOnlyDictionary<string, string> ReadRequestHeaders(NSUrlRequest request)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var allHeaders = request.AllHTTPHeaderFields;
        if (allHeaders is null)
        {
            return headers;
        }

        foreach (KeyValuePair<NSObject, NSObject> pair in allHeaders)
        {
            var name = pair.Key?.ToString();
            if (!string.IsNullOrEmpty(name))
            {
                headers[name] = pair.Value?.ToString() ?? string.Empty;
            }
        }
        return headers;
    }


    /// <summary>
    /// 完成 URL scheme 任务响应。
    /// </summary>
    /// <param name="task">URL scheme 任务。</param>
    /// <param name="statusCode">HTTP 状态码。</param>
    /// <param name="contentType">Content-Type。</param>
    /// <param name="body">响应体。</param>
    private static void CompleteTask(IWKUrlSchemeTask task, nint statusCode, string contentType, byte[] body)
    {
        try
        {
            // 禁用本地缓存（参照 DevToys：no-store 防止用户脚本/资源更新后仍命中旧缓存）。
            var headers = NSDictionary.FromObjectsAndKeys(
                new object[] { contentType, body.Length.ToString(), "no-cache, max-age=0, must-revalidate, no-store" },
                new object[] { "Content-Type", "Content-Length", "Cache-Control" });
            var response = new NSHttpUrlResponse(
                task.Request.Url ?? NSUrl.FromString("wails://localhost/")!,
                statusCode,
                "HTTP/1.1",
                headers);
            task.DidReceiveResponse(response);
            if (body.Length > 0)
            {
                using var data = NSData.FromArray(body);
                task.DidReceiveData(data);
            }

            task.DidFinish();
        }
        catch
        {
            // 任务可能已取消（页面导航），忽略。
        }
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
    /// 读取请求体。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <returns>请求体字符串与其原始字节（Streams 二进制帧负载需原始字节）。</returns>
    private static (string Text, byte[] Bytes) ReadRequestBody(NSUrlRequest request)
    {
        try
        {
            if (request.Body is { } body && body.Length > 0)
            {
                return (body.ToString(), body.ToArray());
            }

            if (request.BodyStream is { } stream)
            {
                using var ms = new MemoryStream();
                stream.Open();
                var buffer = new byte[81920];
                while (true)
                {
                    var read = (int)stream.Read(buffer, 0, buffer.Length);
                    if (read <= 0)
                    {
                        break;
                    }

                    ms.Write(buffer, 0, read);
                }

                var bytes = ms.ToArray();
                return (Encoding.UTF8.GetString(bytes), bytes);
            }
        }
        catch
        {
            // 读取失败返回空体。
        }

        return ("{}", []);
    }

    /// <summary>
    /// 获取主屏幕高度（用于 Y 坐标翻转）。
    /// </summary>
    /// <returns>主屏幕高度（points）。</returns>
    private static nfloat GetPrimaryScreenHeight()
    {
        var screens = NSScreen.Screens;
        if (screens is null || screens.Length == 0)
        {
            return 0;
        }

        return screens[0].Frame.Height;
    }

    /// <summary>
    /// 解析 getUserMedia 媒体捕获权限请求（相机 / 麦克风）。
    /// 对应 Wails v3 Go 版本 <c>permissions_darwin.go</c> 中的
    /// <c>mediaCaptureDecision</c> / <c>resolveMediaCapturePermission</c> /
    /// <c>strictestCaptureDecision</c>：按窗口选项的权限策略分别判定相机与麦克风，
    /// 取更严格者（Deny &gt; Prompt &gt; Grant）作为 WebKit 的最终决定。
    /// <para>
    /// 未覆写该代理方法时 WebKit 走默认行为（Cocoa 上即其自带的授权弹窗），
    /// 因此 <see cref="Permission.Default"/> 保持该弹窗，<see cref="Permission.Allow"/> /
    /// <see cref="Permission.Deny"/> 才赋予二者明确含义（与 Go 语义一致）。
    /// </para>
    /// </summary>
    /// <param name="needAudio">请求是否包含麦克风（音频）。</param>
    /// <param name="needVideo">请求是否包含相机（视频）。</param>
    /// <returns>WebKit 权限决定。</returns>
    internal WKPermissionDecision ResolveMediaCapturePermission(bool needAudio, bool needVideo)
    {
        if (!needAudio && !needVideo)
        {
            return WKPermissionDecision.Prompt;
        }

        var decision = WKPermissionDecision.Grant;
        if (needAudio)
        {
            decision = StrictestCaptureDecision(
                decision,
                CaptureDecisionFor(_macWindowOptions?.MicrophonePermission ?? Permission.Default));
        }
        if (needVideo)
        {
            decision = StrictestCaptureDecision(
                decision,
                CaptureDecisionFor(_macWindowOptions?.CameraPermission ?? Permission.Default));
        }
        return decision;
    }

    /// <summary>
    /// 将窗口级权限策略映射为 WebKit 权限决定。
    /// 对应 Go <c>captureDecisionFor</c>。
    /// </summary>
    private static WKPermissionDecision CaptureDecisionFor(Permission permission) => permission switch
    {
        Permission.Allow => WKPermissionDecision.Grant,
        Permission.Deny => WKPermissionDecision.Deny,
        // 对应 Go PermissionDefault：交由 WebKit 默认弹窗处理。
        _ => WKPermissionDecision.Prompt,
    };

    /// <summary>
    /// 相机与麦克风同时被请求时只有一个答案，且不得比任一单项更宽松。
    /// 对应 Go <c>strictestCaptureDecision</c>：Deny 优先于 Prompt，Prompt 优先于 Grant。
    /// </summary>
    private static WKPermissionDecision StrictestCaptureDecision(WKPermissionDecision a, WKPermissionDecision b)
    {
        if (a == WKPermissionDecision.Deny || b == WKPermissionDecision.Deny)
        {
            return WKPermissionDecision.Deny;
        }
        if (a == WKPermissionDecision.Prompt || b == WKPermissionDecision.Prompt)
        {
            return WKPermissionDecision.Prompt;
        }
        return WKPermissionDecision.Grant;
    }
#endif

    /// <inheritdoc />
    public void SetZoomEnabled(bool enabled)
    {
        // WKWebView 捏合缩放由 AllowsMagnification 控制，运行期切换暂不实现。
    }

    /// <inheritdoc />
    public void SetTranslucent(bool translucent)
    {
#if MACOS
        if (_window is not null)
        {
            _window.Opaque = !translucent;
        }
#endif
    }

    /// <inheritdoc />
    public void SetOpacity(float opacity)
    {
#if MACOS
        if (_window is not null)
        {
            _window.AlphaValue = (nfloat)Math.Clamp(opacity, 0f, 1f);
        }
#endif
    }

    /// <inheritdoc />
    public void SetNativeMessageHandler(Func<string, Task>? callback)
    {
        _nativeMessageHandler = callback;
    }

    /// <inheritdoc />
    public void SetConsoleMessageHandler(Action<BrowserConsoleMessageLevel, string>? handler)
    {
        _consoleMessageHandler = handler;
    }

    /// <inheritdoc />
    public void ZoomIn()
    {
#if MACOS
        if (_webView is not null)
        {
            _webView.Magnification = _webView.Magnification + 0.05f;
        }
#endif
    }

    /// <inheritdoc />
    public void ZoomOut()
    {
#if MACOS
        if (_webView is null)
        {
            return;
        }

        _webView.Magnification = _webView.Magnification > 1.05f
            ? _webView.Magnification - 0.05f
            : 1.0f;
#endif
    }

    /// <inheritdoc />
    public void ZoomReset()
    {
#if MACOS
        if (_webView is not null)
        {
            _webView.Magnification = 1.0f;
        }
#endif
    }
}

#if MACOS
/// <summary>
/// WKWebView 脚本消息处理器（webkit.messageHandlers.external）。
/// </summary>
internal sealed class MacScriptMessageHandler : NSObject, IWKScriptMessageHandler
{
    /// <summary>
    /// 关联的窗口实现。
    /// </summary>
    private readonly MacOSWebviewWindow _window;

    /// <summary>
    /// 构造脚本消息处理器。
    /// </summary>
    /// <param name="window">关联窗口。</param>
    public MacScriptMessageHandler(MacOSWebviewWindow window)
    {
        _window = window;
    }

    /// <inheritdoc />
    [Export("userContentController:didReceiveScriptMessage:")]
    public void DidReceiveScriptMessage(WKUserContentController userContentController, WKScriptMessage message)
    {
        var body = message.Body?.ToString() ?? string.Empty;
        _window.HandleScriptMessage(body);
    }
}

/// <summary>
/// WKWebView 导航代理：页面加载完成时注入 JS/CSS 并显示窗口。
/// </summary>
internal sealed class MacNavigationDelegate : WKNavigationDelegate
{
    /// <summary>
    /// 关联的窗口实现。
    /// </summary>
    private readonly MacOSWebviewWindow _window;

    /// <summary>
    /// 构造导航代理。
    /// </summary>
    /// <param name="window">关联窗口。</param>
    public MacNavigationDelegate(MacOSWebviewWindow window)
    {
        _window = window;
    }

    /// <inheritdoc />
    public override void DidFinishNavigation(WKWebView webView, WKNavigation navigation)
    {
        _window.OnNavigationFinished();
    }

    /// <inheritdoc />
    public override void DidFailNavigation(WKWebView webView, WKNavigation navigation, NSError error)
    {
        // 导航失败静默处理。
    }

    /// <inheritdoc />
    public override void DecidePolicy(
        WKWebView webView,
        WKNavigationAction navigationAction,
        Action<WKNavigationActionPolicy> decisionHandler)
    {
        // 外部链接策略（参照 DevToys BlazorWebViewManager.DecidePolicy + Wails v3 默认行为）：
        //   - wails:// 与 http(s)://localhost 留在 WebView 内；
        //   - 其余（http/https 外部站点或自定义 scheme）交由系统默认应用打开并取消导航。
        // 注意：DecidePolicy 仅拦截顶层导航，不影响页面内 fetch/XHR。
        try
        {
            var url = navigationAction.Request.Url;
            var scheme = url?.Scheme ?? string.Empty;
            var host = url?.Host ?? string.Empty;

            var isInternal = string.Equals(scheme, "wails", StringComparison.OrdinalIgnoreCase)
                || (string.Equals(scheme, "http", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
                || (string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase));

            if (isInternal)
            {
                decisionHandler(WKNavigationActionPolicy.Allow);
                return;
            }

            // 新窗口（_blank）或外部链接：用默认浏览器打开。
            if (url is not null)
            {
                NSWorkspace.SharedWorkspace.OpenUrl(url);
            }

            decisionHandler(WKNavigationActionPolicy.Cancel);
        }
        catch
        {
            // 策略判定失败时放行，避免阻断导航。
            decisionHandler(WKNavigationActionPolicy.Allow);
        }
    }
}

/// <summary>
/// WKWebView UI 代理：JS alert/confirm/prompt 对话框转发到 NSAlert。
/// </summary>
internal sealed class MacUiDelegate : WKUIDelegate
{
    /// <summary>
    /// 关联的窗口实现。
    /// </summary>
    private readonly MacOSWebviewWindow _window;

    /// <summary>
    /// 构造 UI 代理。
    /// </summary>
    /// <param name="window">关联窗口。</param>
    public MacUiDelegate(MacOSWebviewWindow window)
    {
        _window = window;
    }

    /// <inheritdoc />
    public override void RunJavaScriptAlertPanel(WKWebView webView, string message, WKFrameInfo frame, Action completionHandler)
    {
        var alert = new NSAlert { MessageText = message, AlertStyle = NSAlertStyle.Informational };
        alert.RunModal();
        completionHandler();
    }

    /// <inheritdoc />
    public override void RunJavaScriptConfirmPanel(WKWebView webView, string message, WKFrameInfo frame, Action<bool> completionHandler)
    {
        var alert = new NSAlert { MessageText = message, AlertStyle = NSAlertStyle.Informational };
        alert.AddButton("确定");
        alert.AddButton("取消");
        // 参照 DevToys：以 sheet 形式呈现，不阻塞主线程。
        alert.BeginSheetForResponse(webView.Window, result =>
        {
            completionHandler(result == 1000); // NSAlertFirstButtonReturn
        });
    }

    /// <inheritdoc />
    public override void RunJavaScriptTextInputPanel(
        WKWebView webView,
        string prompt,
        string? defaultText,
        WKFrameInfo frame,
        Action<string> completionHandler)
    {
        var alert = new NSAlert { MessageText = prompt, AlertStyle = NSAlertStyle.Informational };
        var textField = new NSTextField(new CGRect(0, 0, 300, 22))
        {
            PlaceholderString = defaultText ?? string.Empty,
            StringValue = defaultText ?? string.Empty,
        };
        alert.AccessoryView = textField;
        alert.AddButton("确定");
        alert.AddButton("取消");
        alert.BeginSheetForResponse(webView.Window, result =>
        {
            completionHandler(result == 1000 ? textField.StringValue : null!);
        });
    }

    /// <summary>
    /// 处理 getUserMedia 相机 / 麦克风捕获权限请求（macOS 12.0+）。
    /// 对应 Wails v3 Go 版本 webview_window_darwin.m 中的
    /// <c>webView:requestMediaCapturePermissionForOrigin:initiatedByFrame:type:decisionHandler:</c>，
    /// 决定由 <see cref="MacOSWebviewWindow.ResolveMediaCapturePermission"/> 依据窗口权限策略给出。
    /// </summary>
    /// <param name="webView">发起请求的 WebView。</param>
    /// <param name="origin">请求来源安全域（Go 实现同样不参与判定）。</param>
    /// <param name="frame">发起请求的 frame（Go 实现同样不参与判定）。</param>
    /// <param name="type">媒体捕获类型（相机 / 麦克风 / 二者）。</param>
    /// <param name="decisionHandler">权限决定回调。</param>
    public override void RequestMediaCapturePermission(
        WKWebView webView,
        WKSecurityOrigin origin,
        WKFrameInfo frame,
        WKMediaCaptureType type,
        Action<WKPermissionDecision> decisionHandler)
    {
        // 对照 Go webview_window_darwin.m：由 type 推导是否请求音频 / 视频后交给窗口策略判定。
        var needAudio = type == WKMediaCaptureType.Microphone
            || type == WKMediaCaptureType.CameraAndMicrophone;
        var needVideo = type == WKMediaCaptureType.Camera
            || type == WKMediaCaptureType.CameraAndMicrophone;
        decisionHandler(_window.ResolveMediaCapturePermission(needAudio, needVideo));
    }
}

/// <summary>
/// wails:// 自定义协议处理器（资源 + IPC）。
/// </summary>
internal sealed class MacUrlSchemeHandler : NSObject, IWKUrlSchemeHandler
{
    /// <summary>
    /// 关联的窗口实现。
    /// </summary>
    private readonly MacOSWebviewWindow _window;

    /// <summary>
    /// 记录已被 WebKit 停止（取消）的 scheme task，供异步完成时检测任务是否失效。
    /// 长轮询等延迟完成场景下，若任务已被停止则不能再回调，否则会崩溃。
    /// </summary>
    private readonly HashSet<IWKUrlSchemeTask> _stoppedTasks = new();

    /// <summary>
    /// 每个 URL scheme task 独立的取消源（P1-9 请求取消/上下文转发）。
    /// <c>StopUrlSchemeTask</c> 触发 <see cref="CancellationTokenSource.Cancel"/>，
    /// 使在途管线任务（如 <c>/wails/stream/poll</c> 长轮询）尽快结束，
    /// 并将同一令牌贯穿到 <see cref="WebviewRequestPipeline.HandleAsync"/>。
    /// </summary>
    private readonly Dictionary<IWKUrlSchemeTask, CancellationTokenSource> _taskCancellations = new();

    /// <summary>
    /// 保护 <see cref="_stoppedTasks"/> 与 <see cref="_taskCancellations"/> 的锁
    /// （Stop 回调与完成延续可能并发）。
    /// </summary>
    private readonly object _stoppedLock = new();

    /// <summary>
    /// 构造协议处理器。
    /// </summary>
    /// <param name="window">关联窗口。</param>
    public MacUrlSchemeHandler(MacOSWebviewWindow window)
    {
        _window = window;
    }

    /// <summary>
    /// 记录某个 scheme task 已被 WebKit 停止，并取消其独立取消源（若有）。
    /// </summary>
    /// <param name="task">被停止的任务。</param>
    internal void MarkStopped(IWKUrlSchemeTask task)
    {
        CancellationTokenSource? cts;
        lock (_stoppedLock)
        {
            _stoppedTasks.Add(task);
            _taskCancellations.TryGetValue(task, out cts);
        }

        // 先记录 stopped 再取消，确保取消引发的 OperationCanceledException
        // 在完成延续里观察到 IsStopped 为真，从而不再回调任务（避免 crash）。
        if (cts is not null)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // 任务已完成并释放取消源，忽略。
            }
        }
    }

    /// <summary>
    /// 为某个 scheme task 建立独立的 <see cref="CancellationTokenSource"/> 并返回其令牌。
    /// 若该任务此前已被停止，则返回已取消的令牌，管线可立即结束。
    /// </summary>
    /// <param name="task">正在启动的任务。</param>
    /// <returns>该任务的取消令牌。</returns>
    internal CancellationToken TrackTask(IWKUrlSchemeTask task)
    {
        lock (_stoppedLock)
        {
            var cts = new CancellationTokenSource();
            if (_stoppedTasks.Contains(task))
            {
                // 任务在注册前已被停止：立即取消，使管线尽快结束。
                cts.Cancel();
            }
            _taskCancellations[task] = cts;
            return cts.Token;
        }
    }

    /// <summary>
    /// 判断某个 scheme task 是否已被 WebKit 停止。
    /// </summary>
    /// <param name="task">待检测的任务。</param>
    /// <returns>已停止返回 true；否则返回 false。</returns>
    internal bool IsStopped(IWKUrlSchemeTask task)
    {
        lock (_stoppedLock)
        {
            return _stoppedTasks.Contains(task);
        }
    }

    /// <summary>
    /// 清除停止记录并释放该任务的取消源（任务完成后调用，避免集合无限增长）。
    /// </summary>
    /// <param name="task">已完成的任务。</param>
    internal void ClearStopped(IWKUrlSchemeTask task)
    {
        lock (_stoppedLock)
        {
            _stoppedTasks.Remove(task);
            if (_taskCancellations.Remove(task, out var cts))
            {
                cts.Dispose();
            }
        }
    }

    /// <inheritdoc />
    [Export("webView:startURLSchemeTask:")]
    public void StartUrlSchemeTask(WKWebView webView, IWKUrlSchemeTask urlSchemeTask)
    {
        _window.HandleSchemeTask(urlSchemeTask, this);
    }

    /// <inheritdoc />
    [Export("webView:stopURLSchemeTask:")]
    public void StopUrlSchemeTask(WKWebView webView, IWKUrlSchemeTask urlSchemeTask)
    {
        // 任务被取消（页面导航/刷新）。记录该任务，供长轮询等异步完成场景检测，
        // 避免对已停止的任务调用 DidReceiveResponse/DidFinish 导致崩溃。
        MarkStopped(urlSchemeTask);
    }
}
#endif
