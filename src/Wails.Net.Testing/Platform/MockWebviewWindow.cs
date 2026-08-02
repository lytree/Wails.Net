using System.Collections.Concurrent;
using Wails.Net.Application.Logging;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Options;
using Wails.Net.Application.Screens;
using Wails.Net.Application.Windows;
using Wails.Net.Testing.Recording;

namespace Wails.Net.Testing.Platform;

/// <summary>
/// 无头 Mock Webview 窗口实现，内部维护完整的内存状态机并记录全部调用。
/// <para>
/// 与 <c>Wails.Net.Application.Platform.ServerMode.ServerWebviewWindow</c> 的区别：
/// <list type="bullet">
/// <item>ServerWebviewWindow 是<b>生产降级</b>桩：全部 no-op，Get* 返回固定零值；</item>
/// <item>MockWebviewWindow 是<b>测试替身</b>：Set* 写入内存状态，Get* 读回真实值，
/// 因此可以满足 GuiContract 的 L2 功能契约（Set → Get 往返一致），
/// 让"必须有 GUI"的窗口契约测试在 CI 中可跑。</item>
/// </list>
/// </para>
/// <para>
/// 对标 Tauri v2 <c>tauri::test</c> 的 MockRuntime 窗口实现。
/// </para>
/// </summary>
public sealed class MockWebviewWindow : IWebviewWindowImpl, IDisposable
{
    /// <summary>
    /// 调用记录器。
    /// </summary>
    private readonly CallRecorder _recorder;

    /// <summary>
    /// 已执行的 JavaScript 代码队列。
    /// </summary>
    private readonly ConcurrentQueue<string> _executedJavaScript = new();

    /// <summary>
    /// 已注入的 CSS 队列。
    /// </summary>
    private readonly ConcurrentQueue<string> _injectedCss = new();

    /// <summary>
    /// 通过原生通道推送到前端的消息队列。
    /// </summary>
    private readonly ConcurrentQueue<string> _postedNativeMessages = new();

    /// <summary>
    /// 前端注册的原生消息处理器（模拟 WebView2 WebMessageReceived）。
    /// </summary>
    private Func<string, Task>? _nativeMessageHandler;

    /// <summary>
    /// 浏览器控制台消息处理器。
    /// </summary>
    private Action<BrowserConsoleMessageLevel, string>? _consoleMessageHandler;

    /// <summary>
    /// 构造 Mock 窗口实例。
    /// </summary>
    /// <param name="id">窗口 ID。</param>
    /// <param name="options">窗口选项，为 null 时使用默认选项。</param>
    /// <param name="recorder">共享调用记录器，为 null 时创建独立记录器。</param>
    public MockWebviewWindow(uint id, WebviewWindowOptions? options = null, CallRecorder? recorder = null)
    {
        _recorder = recorder ?? new CallRecorder();
        Id = id;
        Options = options ?? new WebviewWindowOptions();

        // 依据选项初始化内存状态，模拟平台实现"按 options 构造窗口"的行为。
        Title = Options.Title;
        Width = Options.Width;
        Height = Options.Height;
        MinWidth = Options.MinWidth;
        MinHeight = Options.MinHeight;
        MaxWidth = Options.MaxWidth;
        MaxHeight = Options.MaxHeight;
        X = Options.X;
        Y = Options.Y;
        Url = Options.URL;
        Html = Options.HTML ?? string.Empty;
        Frameless = Options.Frameless;
        AlwaysOnTop = Options.AlwaysOnTop;
        Resizable = Options.Resizable;
        Maximisable = Options.Maximisable;
        Minimisable = Options.Minimisable;
        Closable = Options.Closable;
        HasShadow = Options.HasShadow;
        Visible = !Options.Hidden;
        Maximised = Options.Maximised;
        Minimised = Options.Minimised;
        FullscreenState = Options.Fullscreen;
        Zoom = (float)Options.Zoom;
        ZoomEnabled = Options.ZoomEnabled;
        Translucent = Options.Translucent;
        MenuBarVisible = Options.ShowMenuBar;

        _recorder.Record(nameof(MockWebviewWindow), id, Options.Title);
    }

    // ============================================================
    // 测试断言入口
    // ============================================================

    /// <summary>
    /// 创建本窗口时使用的选项。
    /// </summary>
    public WebviewWindowOptions Options { get; }

    /// <summary>
    /// 调用记录器，可用于断言方法调用序列。
    /// </summary>
    public CallRecorder Recorder => _recorder;

    /// <summary>
    /// 全部调用记录快照。
    /// </summary>
    public IReadOnlyList<CallRecord> Calls => _recorder.Snapshot();

    /// <summary>
    /// 按调用顺序返回所有通过 <see cref="ExecJS"/> 执行的 JavaScript 代码。
    /// </summary>
    public IReadOnlyList<string> ExecutedJavaScript => _executedJavaScript.ToArray();

    /// <summary>
    /// 按调用顺序返回所有通过 <see cref="InjectCSS"/> 注入的 CSS。
    /// </summary>
    public IReadOnlyList<string> InjectedCss => _injectedCss.ToArray();

    /// <summary>
    /// 按调用顺序返回所有通过 <see cref="PostNativeMessageAsync"/> 推送到前端的消息。
    /// </summary>
    public IReadOnlyList<string> PostedNativeMessages => _postedNativeMessages.ToArray();

    /// <summary>
    /// 窗口是否已被 <see cref="Close"/>。
    /// </summary>
    public bool IsClosed { get; private set; }

    /// <summary>
    /// 窗口是否已被 <see cref="Dispose"/>。
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <summary>
    /// 模拟前端通过原生通道（<c>window.chrome.webview.postMessage</c> 等）发送消息。
    /// <para>
    /// 若未通过 <see cref="SetNativeMessageHandler"/> 注册处理器，则不做任何事。
    /// </para>
    /// </summary>
    /// <param name="message">前端发送的原始字符串内容。</param>
    /// <returns>表示处理完成的异步任务。</returns>
    public Task SimulateFrontendMessageAsync(string message)
    {
        _recorder.Record(nameof(SimulateFrontendMessageAsync), message);
        var handler = _nativeMessageHandler;
        return handler is null ? Task.CompletedTask : handler(message);
    }

    /// <summary>
    /// 模拟前端 <c>console.log/warn/error</c> 输出，触发已注册的控制台消息处理器。
    /// </summary>
    /// <param name="level">消息级别。</param>
    /// <param name="message">消息文本。</param>
    public void SimulateConsoleMessage(BrowserConsoleMessageLevel level, string message)
    {
        _recorder.Record(nameof(SimulateConsoleMessage), level, message);
        _consoleMessageHandler?.Invoke(level, message);
    }

    /// <summary>
    /// 模拟窗口获得/失去焦点（平台通常由窗口管理器驱动，测试中需要手动触发）。
    /// </summary>
    /// <param name="focused">是否获得焦点。</param>
    public void SimulateFocusChanged(bool focused)
    {
        _recorder.Record(nameof(SimulateFocusChanged), focused);
        Focused = focused;
    }

    /// <summary>
    /// 模拟窗口被用户调整大小（平台通常由 WM 驱动）。
    /// </summary>
    /// <param name="width">新宽度。</param>
    /// <param name="height">新高度。</param>
    public void SimulateResized(int width, int height)
    {
        _recorder.Record(nameof(SimulateResized), width, height);
        Width = width;
        Height = height;
    }

    // ============================================================
    // 内存状态（公开只读，供测试直接断言）
    // ============================================================

    /// <inheritdoc />
    public uint Id { get; }

    /// <summary>当前窗口标题。</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>当前窗口宽度。</summary>
    public int Width { get; private set; }

    /// <summary>当前窗口高度。</summary>
    public int Height { get; private set; }

    /// <summary>当前最小宽度。</summary>
    public int MinWidth { get; private set; }

    /// <summary>当前最小高度。</summary>
    public int MinHeight { get; private set; }

    /// <summary>当前最大宽度。</summary>
    public int MaxWidth { get; private set; }

    /// <summary>当前最大高度。</summary>
    public int MaxHeight { get; private set; }

    /// <summary>当前 X 坐标。</summary>
    public int X { get; private set; }

    /// <summary>当前 Y 坐标。</summary>
    public int Y { get; private set; }

    /// <summary>当前是否可见。</summary>
    public bool Visible { get; private set; }

    /// <summary>当前是否已最大化。</summary>
    public bool Maximised { get; private set; }

    /// <summary>当前是否已最小化。</summary>
    public bool Minimised { get; private set; }

    /// <summary>当前是否处于全屏。</summary>
    public bool FullscreenState { get; private set; }

    /// <summary>当前是否已聚焦。</summary>
    public bool Focused { get; private set; }

    /// <summary>当前是否总置顶。</summary>
    public bool AlwaysOnTop { get; private set; }

    /// <summary>当前是否无边框。</summary>
    public bool Frameless { get; private set; }

    /// <summary>当前是否可调整大小。</summary>
    public bool Resizable { get; private set; } = true;

    /// <summary>当前是否可最大化。</summary>
    public bool Maximisable { get; private set; } = true;

    /// <summary>当前是否可最小化。</summary>
    public bool Minimisable { get; private set; } = true;

    /// <summary>当前是否可关闭。</summary>
    public bool Closable { get; private set; } = true;

    /// <summary>当前是否有阴影。</summary>
    public bool HasShadow { get; private set; } = true;

    /// <summary>当前是否启用。</summary>
    public bool Enabled { get; private set; } = true;

    /// <summary>当前是否启用内容保护。</summary>
    public bool ContentProtection { get; private set; }

    /// <summary>当前缩放比例。</summary>
    public float Zoom { get; private set; } = 1.0f;

    /// <summary>当前缩放级别。</summary>
    public float ZoomLevel { get; private set; }

    /// <summary>当前是否启用缩放。</summary>
    public bool ZoomEnabled { get; private set; } = true;

    /// <summary>当前透明度（0.0 ~ 1.0）。</summary>
    public float Opacity { get; private set; } = 1.0f;

    /// <summary>当前是否半透明。</summary>
    public bool Translucent { get; private set; }

    /// <summary>当前 URL。</summary>
    public string Url { get; private set; } = string.Empty;

    /// <summary>当前 HTML 内容。</summary>
    public string Html { get; private set; } = string.Empty;

    /// <summary>当前背景色（R,G,B,A）。</summary>
    public (byte R, byte G, byte B, byte A) BackgroundColour { get; private set; } = (0, 0, 0, 255);

    /// <summary>当前背景类型字符串。</summary>
    public string? BackgroundType { get; private set; }

    /// <summary>当前标题栏样式（枚举形式）。</summary>
    public TitleBarStyle TitleBarStyleValue { get; private set; } = TitleBarStyle.Default;

    /// <summary>当前标题栏样式（字符串形式，仅在使用字符串重载时写入）。</summary>
    public string? TitleBarStyleName { get; private set; }

    /// <summary>当前窗口菜单。</summary>
    public Menu? WindowMenu { get; private set; }

    /// <summary>菜单栏是否可见。</summary>
    public bool MenuBarVisible { get; private set; }

    /// <summary>开发者工具是否已打开。</summary>
    public bool DevToolsOpen { get; private set; }

    /// <summary>是否启用调试模式。</summary>
    public bool DebuggingEnabled { get; private set; }

    /// <summary>是否跳过任务栏。</summary>
    public bool SkipTaskbar { get; private set; }

    /// <summary>是否忽略鼠标事件（点击穿透）。</summary>
    public bool IgnoreCursorEvents { get; private set; }

    /// <summary>是否在所有工作区可见。</summary>
    public bool VisibleOnAllWorkspaces { get; private set; }

    /// <summary>是否启用文件拖放。</summary>
    public bool FileDropEnabled { get; private set; }

    /// <summary>当前边框颜色。</summary>
    public string? BorderColor { get; private set; }

    /// <summary>当前窗口特效。</summary>
    public WindowEffects? Effects { get; private set; }

    /// <summary>当前任务栏徽章计数。</summary>
    public int BadgeCount { get; private set; }

    /// <summary>当前任务栏徽章文本。</summary>
    public string? BadgeLabel { get; private set; }

    /// <summary>当前任务栏进度状态。</summary>
    public (TaskbarProgressState State, ulong Completed, ulong Total) TaskbarProgress { get; private set; }
        = (TaskbarProgressState.None, 0, 0);

    /// <summary>当前任务栏叠加图标数据。</summary>
    public byte[]? OverlayIcon { get; private set; }

    /// <summary>模态父窗口 ID，未附加时为 null。</summary>
    public uint? ModalParentWindowId { get; private set; }

    /// <summary>已注册的自定义协议方案集合。</summary>
    public IReadOnlyList<string> RegisteredSchemes => _registeredSchemes.ToArray();

    /// <summary>
    /// 已注册的自定义协议方案队列。
    /// </summary>
    private readonly ConcurrentQueue<string> _registeredSchemes = new();

    /// <summary>
    /// 窗口所在屏幕，测试可通过 <see cref="SetScreen"/> 注入。
    /// </summary>
    private Screen? _screen;

    /// <summary>
    /// 截图返回的字节数据，测试可通过 <see cref="SetCapturePreviewResult"/> 注入。
    /// </summary>
    private byte[]? _capturePreviewResult;

    /// <summary>
    /// 注入窗口所在屏幕，供 <see cref="GetScreen"/> 返回。
    /// </summary>
    /// <param name="screen">屏幕实例，可为 null。</param>
    public void SetScreen(Screen? screen) => _screen = screen;

    /// <summary>
    /// 注入 <see cref="CapturePreviewAsync"/> 的返回值。
    /// </summary>
    /// <param name="pngBytes">PNG 字节数据，可为 null 表示不支持。</param>
    public void SetCapturePreviewResult(byte[]? pngBytes) => _capturePreviewResult = pngBytes;

    // ============================================================
    // IWebviewWindowImpl 实现：标题
    // ============================================================

    /// <inheritdoc />
    public void SetTitle(string title)
    {
        _recorder.Record(nameof(SetTitle), title);
        Title = title;
    }

    /// <summary>
    /// 获取当前窗口标题（Mock 扩展，接口本身未定义 GetTitle）。
    /// </summary>
    /// <returns>当前标题。</returns>
    public string GetTitle()
    {
        _recorder.Record(nameof(GetTitle));
        return Title;
    }

    // ============================================================
    // 尺寸与位置
    // ============================================================

    /// <inheritdoc />
    public void SetSize(int width, int height)
    {
        _recorder.Record(nameof(SetSize), width, height);
        Width = width;
        Height = height;
    }

    /// <inheritdoc />
    public void SetMinSize(int width, int height)
    {
        _recorder.Record(nameof(SetMinSize), width, height);
        MinWidth = width;
        MinHeight = height;
    }

    /// <inheritdoc />
    public void SetMaxSize(int width, int height)
    {
        _recorder.Record(nameof(SetMaxSize), width, height);
        MaxWidth = width;
        MaxHeight = height;
    }

    /// <inheritdoc />
    public void SetPosition(int x, int y)
    {
        _recorder.Record(nameof(SetPosition), x, y);
        X = x;
        Y = y;
    }

    /// <inheritdoc />
    public (int Width, int Height) GetSize()
    {
        _recorder.Record(nameof(GetSize));
        // 契约要求返回非负值：窗口选项可能给出负数，此处夹紧到 0。
        return (Math.Max(0, Width), Math.Max(0, Height));
    }

    /// <inheritdoc />
    public (int Width, int Height) GetContentSize()
    {
        _recorder.Record(nameof(GetContentSize));
        return (Math.Max(0, Width), Math.Max(0, Height));
    }

    /// <inheritdoc />
    public (int Width, int Height) GetMinSize()
    {
        _recorder.Record(nameof(GetMinSize));
        return (MinWidth, MinHeight);
    }

    /// <inheritdoc />
    public (int Width, int Height) GetMaxSize()
    {
        _recorder.Record(nameof(GetMaxSize));
        return (MaxWidth, MaxHeight);
    }

    /// <inheritdoc />
    public (int X, int Y) GetPosition()
    {
        _recorder.Record(nameof(GetPosition));
        return (X, Y);
    }

    /// <inheritdoc />
    public Rect GetBounds()
    {
        _recorder.Record(nameof(GetBounds));
        return new Rect(X, Y, Math.Max(0, Width), Math.Max(0, Height));
    }

    /// <inheritdoc />
    public void SetBounds(Rect bounds)
    {
        _recorder.Record(nameof(SetBounds), bounds.X, bounds.Y, bounds.Width, bounds.Height);
        X = bounds.X;
        Y = bounds.Y;
        Width = bounds.Width;
        Height = bounds.Height;
    }

    /// <inheritdoc />
    public LRTB GetBorderSizes()
    {
        _recorder.Record(nameof(GetBorderSizes));
        return new LRTB(0, 0, 0, 0);
    }

    /// <inheritdoc />
    public Screen? GetScreen()
    {
        _recorder.Record(nameof(GetScreen));
        return _screen;
    }

    /// <inheritdoc />
    public void Centre()
    {
        _recorder.Record(nameof(Centre));
        // 无真实屏幕时以 1920x1080 虚拟桌面为基准计算居中位置，保证状态可观察。
        var screenWidth = _screen?.Width > 0 ? _screen.Width : 1920;
        var screenHeight = _screen?.Height > 0 ? _screen.Height : 1080;
        X = Math.Max(0, (screenWidth - Math.Max(0, Width)) / 2);
        Y = Math.Max(0, (screenHeight - Math.Max(0, Height)) / 2);
    }

    // ============================================================
    // 显示状态
    // ============================================================

    /// <inheritdoc />
    public void Show()
    {
        _recorder.Record(nameof(Show));
        Visible = true;
    }

    /// <inheritdoc />
    public void Hide()
    {
        _recorder.Record(nameof(Hide));
        Visible = false;
    }

    /// <inheritdoc />
    public void Maximise()
    {
        _recorder.Record(nameof(Maximise));
        Maximised = true;
        Minimised = false;
    }

    /// <inheritdoc />
    public void UnMaximise()
    {
        _recorder.Record(nameof(UnMaximise));
        Maximised = false;
    }

    /// <inheritdoc />
    public void Minimise()
    {
        _recorder.Record(nameof(Minimise));
        Minimised = true;
        Maximised = false;
    }

    /// <inheritdoc />
    public void UnMinimise()
    {
        _recorder.Record(nameof(UnMinimise));
        Minimised = false;
    }

    /// <inheritdoc />
    public void Fullscreen()
    {
        _recorder.Record(nameof(Fullscreen));
        FullscreenState = true;
    }

    /// <inheritdoc />
    public void UnFullscreen()
    {
        _recorder.Record(nameof(UnFullscreen));
        FullscreenState = false;
    }

    /// <inheritdoc />
    public void Restore()
    {
        _recorder.Record(nameof(Restore));
        Maximised = false;
        Minimised = false;
        FullscreenState = false;
    }

    /// <inheritdoc />
    public void SetMinimised()
    {
        _recorder.Record(nameof(SetMinimised));
        Minimised = true;
        Maximised = false;
    }

    /// <inheritdoc />
    public void SetMaximised()
    {
        _recorder.Record(nameof(SetMaximised));
        Maximised = true;
        Minimised = false;
    }

    /// <inheritdoc />
    public void SetNormal()
    {
        _recorder.Record(nameof(SetNormal));
        Maximised = false;
        Minimised = false;
        FullscreenState = false;
    }

    /// <inheritdoc />
    public void Close()
    {
        _recorder.Record(nameof(Close));
        // 幂等：重复关闭不抛异常，仅记录调用。
        IsClosed = true;
        Visible = false;
    }

    /// <inheritdoc />
    public void Focus()
    {
        _recorder.Record(nameof(Focus));
        Focused = true;
    }

    /// <inheritdoc />
    public bool IsFullscreen()
    {
        _recorder.Record(nameof(IsFullscreen));
        return FullscreenState;
    }

    /// <inheritdoc />
    public bool IsMaximised()
    {
        _recorder.Record(nameof(IsMaximised));
        return Maximised;
    }

    /// <inheritdoc />
    public bool IsMinimised()
    {
        _recorder.Record(nameof(IsMinimised));
        return Minimised;
    }

    /// <inheritdoc />
    public bool IsVisible()
    {
        _recorder.Record(nameof(IsVisible));
        return Visible;
    }

    /// <inheritdoc />
    public bool IsFocused()
    {
        _recorder.Record(nameof(IsFocused));
        return Focused;
    }

    /// <inheritdoc />
    public bool IsResizable()
    {
        _recorder.Record(nameof(IsResizable));
        return Resizable;
    }

    /// <inheritdoc />
    public bool IsAlwaysOnTop()
    {
        _recorder.Record(nameof(IsAlwaysOnTop));
        return AlwaysOnTop;
    }

    /// <inheritdoc />
    public bool IsIgnoreMouseEvents()
    {
        _recorder.Record(nameof(IsIgnoreMouseEvents));
        return IgnoreCursorEvents;
    }

    // ============================================================
    // 菜单栏与菜单
    // ============================================================

    /// <inheritdoc />
    public void ShowMenuBar()
    {
        _recorder.Record(nameof(ShowMenuBar));
        MenuBarVisible = true;
    }

    /// <inheritdoc />
    public void HideMenuBar()
    {
        _recorder.Record(nameof(HideMenuBar));
        MenuBarVisible = false;
    }

    /// <inheritdoc />
    public void ToggleMenuBar()
    {
        _recorder.Record(nameof(ToggleMenuBar));
        MenuBarVisible = !MenuBarVisible;
    }

    /// <inheritdoc />
    public void SetMenu(Menu? menu)
    {
        _recorder.Record(nameof(SetMenu), menu?.GetType().Name);
        WindowMenu = menu;
    }

    /// <inheritdoc />
    public void OpenContextMenu(int x, int y)
    {
        _recorder.Record(nameof(OpenContextMenu), x, y);
    }

    /// <inheritdoc />
    public void OpenContextMenu(ContextMenuData data)
    {
        _recorder.Record(nameof(OpenContextMenu), data.Id, data.X, data.Y);
    }

    // ============================================================
    // 窗口样式与行为开关
    // ============================================================

    /// <inheritdoc />
    public void SetAlwaysOnTop(bool onTop)
    {
        _recorder.Record(nameof(SetAlwaysOnTop), onTop);
        AlwaysOnTop = onTop;
    }

    /// <inheritdoc />
    public void SetBackgroundColour(byte r, byte g, byte b, byte a)
    {
        _recorder.Record(nameof(SetBackgroundColour), r, g, b, a);
        BackgroundColour = (r, g, b, a);
    }

    /// <inheritdoc />
    public void SetBackgroundColour(int r, int g, int b, int a)
    {
        _recorder.Record(nameof(SetBackgroundColour), r, g, b, a);
        BackgroundColour = (ClampToByte(r), ClampToByte(g), ClampToByte(b), ClampToByte(a));
    }

    /// <inheritdoc />
    public void SetBackgroundType(string type)
    {
        _recorder.Record(nameof(SetBackgroundType), type);
        BackgroundType = type;
    }

    /// <inheritdoc />
    public void SetFrameless(bool frameless)
    {
        _recorder.Record(nameof(SetFrameless), frameless);
        Frameless = frameless;
    }

    /// <inheritdoc />
    public void SetEnabled(bool enabled)
    {
        _recorder.Record(nameof(SetEnabled), enabled);
        Enabled = enabled;
    }

    /// <inheritdoc />
    public void SetContentProtection(bool enabled)
    {
        _recorder.Record(nameof(SetContentProtection), enabled);
        ContentProtection = enabled;
    }

    /// <inheritdoc />
    public void SetResizable(bool resizable)
    {
        _recorder.Record(nameof(SetResizable), resizable);
        Resizable = resizable;
    }

    /// <inheritdoc />
    public void SetMaximisable(bool maximisable)
    {
        _recorder.Record(nameof(SetMaximisable), maximisable);
        Maximisable = maximisable;
    }

    /// <inheritdoc />
    public void SetMinimisable(bool minimisable)
    {
        _recorder.Record(nameof(SetMinimisable), minimisable);
        Minimisable = minimisable;
    }

    /// <inheritdoc />
    public void SetClosable(bool closable)
    {
        _recorder.Record(nameof(SetClosable), closable);
        Closable = closable;
    }

    /// <inheritdoc />
    public void SetHasShadow(bool hasShadow)
    {
        _recorder.Record(nameof(SetHasShadow), hasShadow);
        HasShadow = hasShadow;
    }

    /// <inheritdoc />
    public void SetTitleBarStyle(TitleBarStyle style)
    {
        _recorder.Record(nameof(SetTitleBarStyle), style);
        TitleBarStyleValue = style;
    }

    /// <inheritdoc />
    public void SetTitleBarStyle(string style)
    {
        _recorder.Record(nameof(SetTitleBarStyle), style);
        TitleBarStyleName = style;
        TitleBarStyleValue = style?.ToLowerInvariant() switch
        {
            "hidden" => TitleBarStyle.Hidden,
            "hiddeninset" => TitleBarStyle.HiddenInset,
            "unified" => TitleBarStyle.Unified,
            _ => TitleBarStyle.Default
        };
    }

    /// <inheritdoc />
    public void SetTranslucent(bool translucent)
    {
        _recorder.Record(nameof(SetTranslucent), translucent);
        Translucent = translucent;
    }

    /// <inheritdoc />
    public void SetFullscreenButtonEnabled(bool enabled)
    {
        _recorder.Record(nameof(SetFullscreenButtonEnabled), enabled);
    }

    /// <inheritdoc />
    public void SetSkipTaskbar(bool skip)
    {
        _recorder.Record(nameof(SetSkipTaskbar), skip);
        SkipTaskbar = skip;
    }

    /// <inheritdoc />
    public void SetIgnoreCursorEvents(bool ignore)
    {
        _recorder.Record(nameof(SetIgnoreCursorEvents), ignore);
        IgnoreCursorEvents = ignore;
    }

    /// <inheritdoc />
    public void SetVisibleOnAllWorkspaces(bool visible)
    {
        _recorder.Record(nameof(SetVisibleOnAllWorkspaces), visible);
        VisibleOnAllWorkspaces = visible;
    }

    /// <inheritdoc />
    public void SetFileDropEnabled(bool enabled)
    {
        _recorder.Record(nameof(SetFileDropEnabled), enabled);
        FileDropEnabled = enabled;
    }

    /// <inheritdoc />
    public void SetBorderColor(string? color)
    {
        _recorder.Record(nameof(SetBorderColor), color);
        BorderColor = color;
    }

    /// <inheritdoc />
    public void SetEffects(WindowEffects effects)
    {
        _recorder.Record(nameof(SetEffects), effects.Effect, effects.State, effects.Radius, effects.Color);
        Effects = effects;
    }

    /// <inheritdoc />
    public void SetBadgeCount(int count)
    {
        _recorder.Record(nameof(SetBadgeCount), count);
        BadgeCount = count;
    }

    /// <inheritdoc />
    public void SetBadgeLabel(string? label)
    {
        _recorder.Record(nameof(SetBadgeLabel), label);
        BadgeLabel = label;
    }

    /// <inheritdoc />
    public void SetTaskbarProgress(TaskbarProgressState state, ulong completed, ulong total)
    {
        _recorder.Record(nameof(SetTaskbarProgress), state, completed, total);
        TaskbarProgress = (state, completed, total);
    }

    /// <inheritdoc />
    public void SetOverlayIcon(byte[]? iconBytes, string? description)
    {
        _recorder.Record(nameof(SetOverlayIcon), iconBytes, description);
        OverlayIcon = iconBytes;
    }

    /// <inheritdoc />
    public void AttachAsModal(uint parentWindowId)
    {
        _recorder.Record(nameof(AttachAsModal), parentWindowId);
        ModalParentWindowId = parentWindowId;
    }

    /// <inheritdoc />
    public void StartDrag()
    {
        _recorder.Record(nameof(StartDrag));
    }

    /// <inheritdoc />
    public void StartResize()
    {
        _recorder.Record(nameof(StartResize));
    }

    /// <inheritdoc />
    public void Flash(bool enabled)
    {
        _recorder.Record(nameof(Flash), enabled);
    }

    // ============================================================
    // 缩放与透明度
    // ============================================================

    /// <inheritdoc />
    public void SetZoom(float zoom)
    {
        _recorder.Record(nameof(SetZoom), zoom);
        Zoom = zoom;
    }

    /// <inheritdoc />
    public void SetZoom(double zoom)
    {
        _recorder.Record(nameof(SetZoom), zoom);
        Zoom = (float)zoom;
    }

    /// <inheritdoc />
    public void SetZoomLevel(float level)
    {
        _recorder.Record(nameof(SetZoomLevel), level);
        ZoomLevel = level;
    }

    /// <inheritdoc />
    public void SetZoomEnabled(bool enabled)
    {
        _recorder.Record(nameof(SetZoomEnabled), enabled);
        ZoomEnabled = enabled;
    }

    /// <inheritdoc />
    public float GetZoom()
    {
        _recorder.Record(nameof(GetZoom));
        return Zoom;
    }

    /// <inheritdoc />
    public float GetZoomLevel()
    {
        _recorder.Record(nameof(GetZoomLevel));
        return ZoomLevel;
    }

    /// <inheritdoc />
    public void ZoomIn()
    {
        _recorder.Record(nameof(ZoomIn));
        Zoom += 0.1f;
    }

    /// <inheritdoc />
    public void ZoomOut()
    {
        _recorder.Record(nameof(ZoomOut));
        Zoom = Math.Max(0.1f, Zoom - 0.1f);
    }

    /// <inheritdoc />
    public void ZoomReset()
    {
        _recorder.Record(nameof(ZoomReset));
        Zoom = 1.0f;
    }

    /// <inheritdoc />
    public void SetOpacity(float opacity)
    {
        _recorder.Record(nameof(SetOpacity), opacity);
        // 契约要求 GetOpacity 返回 [0,1]，此处夹紧输入值。
        Opacity = Math.Clamp(opacity, 0f, 1f);
    }

    /// <inheritdoc />
    public float GetOpacity()
    {
        _recorder.Record(nameof(GetOpacity));
        return Opacity;
    }

    // ============================================================
    // 内容与导航
    // ============================================================

    /// <inheritdoc />
    public void ExecJS(string js)
    {
        _recorder.Record(nameof(ExecJS), js);
        _executedJavaScript.Enqueue(js);
    }

    /// <inheritdoc />
    public void InjectCSS(string css)
    {
        _recorder.Record(nameof(InjectCSS), css);
        _injectedCss.Enqueue(css);
    }

    /// <inheritdoc />
    public void GoBack()
    {
        _recorder.Record(nameof(GoBack));
    }

    /// <inheritdoc />
    public void GoForward()
    {
        _recorder.Record(nameof(GoForward));
    }

    /// <inheritdoc />
    public void Reload()
    {
        _recorder.Record(nameof(Reload));
    }

    /// <inheritdoc />
    public void ForceReload()
    {
        _recorder.Record(nameof(ForceReload));
    }

    /// <inheritdoc />
    public void SetURL(string url)
    {
        _recorder.Record(nameof(SetURL), url);
        Url = url;
    }

    /// <inheritdoc />
    public string GetURL()
    {
        _recorder.Record(nameof(GetURL));
        return Url;
    }

    /// <inheritdoc />
    public void LoadURL(string url)
    {
        _recorder.Record(nameof(LoadURL), url);
        Url = url;
    }

    /// <inheritdoc />
    public void SetHTML(string html)
    {
        _recorder.Record(nameof(SetHTML), html);
        Html = html;
    }

    /// <inheritdoc />
    public void LoadHTML(string html)
    {
        _recorder.Record(nameof(LoadHTML), html);
        Html = html;
    }

    /// <inheritdoc />
    public void RegisterCustomScheme(string scheme)
    {
        _recorder.Record(nameof(RegisterCustomScheme), scheme);
        _registeredSchemes.Enqueue(scheme);
    }

    // ============================================================
    // 开发者工具、打印、截图
    // ============================================================

    /// <inheritdoc />
    public void OpenDevTools()
    {
        _recorder.Record(nameof(OpenDevTools));
        DevToolsOpen = true;
    }

    /// <inheritdoc />
    public void CloseDevTools()
    {
        _recorder.Record(nameof(CloseDevTools));
        DevToolsOpen = false;
    }

    /// <inheritdoc />
    public void SetDebuggingEnabled(bool enabled)
    {
        _recorder.Record(nameof(SetDebuggingEnabled), enabled);
        DebuggingEnabled = enabled;
    }

    /// <inheritdoc />
    public void Print()
    {
        _recorder.Record(nameof(Print));
    }

    /// <inheritdoc />
    public void PrintToPDF(string path)
    {
        _recorder.Record(nameof(PrintToPDF), path);
    }

    /// <inheritdoc />
    public void PrintToPDF(byte[]? pageOptions)
    {
        _recorder.Record(nameof(PrintToPDF), pageOptions);
    }

    /// <inheritdoc />
    public void PrintToPDF(string path, PrintToPdfOptions? options)
    {
        _recorder.Record(nameof(PrintToPDF), path, options?.GetType().Name);
    }

    /// <inheritdoc />
    public Task<byte[]?> CapturePreviewAsync()
    {
        _recorder.Record(nameof(CapturePreviewAsync));
        return Task.FromResult(_capturePreviewResult);
    }

    // ============================================================
    // 消息通道与生命周期回调
    // ============================================================

    /// <inheritdoc />
    public void SetNativeMessageHandler(Func<string, Task>? callback)
    {
        _recorder.Record(nameof(SetNativeMessageHandler), callback is not null);
        _nativeMessageHandler = callback;
    }

    /// <inheritdoc />
    public void SetConsoleMessageHandler(Action<BrowserConsoleMessageLevel, string>? handler)
    {
        _recorder.Record(nameof(SetConsoleMessageHandler), handler is not null);
        _consoleMessageHandler = handler;
    }

    /// <inheritdoc />
    public Task PostNativeMessageAsync(string message)
    {
        _recorder.Record(nameof(PostNativeMessageAsync), message);
        _postedNativeMessages.Enqueue(message);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Run(Action callback)
    {
        _recorder.Record(nameof(Run));
        callback();
    }

    /// <summary>
    /// 释放窗口资源（幂等）。
    /// </summary>
    public void Dispose()
    {
        _recorder.Record(nameof(Dispose));
        if (IsDisposed)
        {
            return;
        }

        IsDisposed = true;
        IsClosed = true;
        Visible = false;
        _nativeMessageHandler = null;
        _consoleMessageHandler = null;
    }

    /// <summary>
    /// 将 int 颜色分量夹紧到 byte 范围。
    /// </summary>
    /// <param name="value">输入值。</param>
    /// <returns>夹紧后的 byte 值。</returns>
    private static byte ClampToByte(int value) => (byte)Math.Clamp(value, 0, 255);
}
