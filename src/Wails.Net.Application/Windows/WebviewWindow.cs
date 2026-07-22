using Wails.Net.Application.Menus;
using Wails.Net.Application.Options;
using Wails.Net.Application.Screens;
using Wails.Net.Events;

namespace Wails.Net.Application.Windows;

/// <summary>
/// Webview 窗口的公共 API，对应 Wails v3 中的 webview_window.go。
/// </summary>
public class WebviewWindow
{
    /// <summary>
    /// 获取平台实现实例，若未设置则抛出异常。
    /// </summary>
    /// <exception cref="InvalidOperationException">当平台实现为 null 时抛出。</exception>
    private IWebviewWindowImpl ImplRequired => Impl ?? throw new InvalidOperationException("窗口平台实现尚未设置。");

    /// <summary>
    /// 窗口唯一 ID。
    /// </summary>
    public uint ID { get; }

    /// <summary>
    /// 窗口名称。
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 窗口选项。
    /// </summary>
    public WebviewWindowOptions Options { get; }

    /// <summary>
    /// 平台实现实例。
    /// </summary>
    public IWebviewWindowImpl? Impl { get; internal set; }

    /// <summary>
    /// 窗口关闭事件。
    /// </summary>
    public event Action<uint>? OnClose;

    /// <summary>
    /// 窗口运行时就绪事件。
    /// </summary>
    public event Action? RuntimeReady;

    /// <summary>
    /// 窗口事件监听器字典，按事件类型分组存储回调列表。
    /// </summary>
    private readonly Dictionary<uint, List<Action>> _eventListeners = new();

    /// <summary>
    /// 事件监听器字典的锁对象，保证线程安全。
    /// </summary>
    private readonly object _eventLock = new();

    /// <summary>
    /// 使用指定 ID、名称和选项构造窗口实例。
    /// </summary>
    /// <param name="id">窗口唯一 ID。</param>
    /// <param name="name">窗口名称。</param>
    /// <param name="options">窗口选项。</param>
    public WebviewWindow(uint id, string name, WebviewWindowOptions options)
    {
        ID = id;
        Name = name;
        Options = options;
    }

    /// <summary>
    /// 关闭窗口并触发 OnClose 事件。
    /// </summary>
    public void Close()
    {
        ImplRequired.Close();
        OnClose?.Invoke(ID);
    }

    /// <summary>
    /// 设置窗口标题。
    /// </summary>
    /// <param name="title">窗口标题。</param>
    public void SetTitle(string title) => ImplRequired.SetTitle(title);

    /// <summary>
    /// 设置窗口大小。
    /// </summary>
    /// <param name="width">窗口宽度。</param>
    /// <param name="height">窗口高度。</param>
    public void SetSize(int width, int height) => ImplRequired.SetSize(width, height);

    /// <summary>
    /// 设置窗口最小尺寸。
    /// </summary>
    /// <param name="width">最小宽度。</param>
    /// <param name="height">最小高度。</param>
    public void SetMinSize(int width, int height) => ImplRequired.SetMinSize(width, height);

    /// <summary>
    /// 设置窗口最大尺寸。
    /// </summary>
    /// <param name="width">最大宽度。</param>
    /// <param name="height">最大高度。</param>
    public void SetMaxSize(int width, int height) => ImplRequired.SetMaxSize(width, height);

    /// <summary>
    /// 设置窗口位置。
    /// </summary>
    /// <param name="x">X 坐标。</param>
    /// <param name="y">Y 坐标。</param>
    public void SetPosition(int x, int y) => ImplRequired.SetPosition(x, y);

    /// <summary>
    /// 显示窗口。
    /// </summary>
    public void Show() => ImplRequired.Show();

    /// <summary>
    /// 隐藏窗口。
    /// </summary>
    public void Hide() => ImplRequired.Hide();

    /// <summary>
    /// 最大化窗口。
    /// </summary>
    public void Maximise() => ImplRequired.Maximise();

    /// <summary>
    /// 取消最大化窗口。
    /// </summary>
    public void UnMaximise() => ImplRequired.UnMaximise();

    /// <summary>
    /// 最小化窗口。
    /// </summary>
    public void Minimise() => ImplRequired.Minimise();

    /// <summary>
    /// 取消最小化窗口。
    /// </summary>
    public void UnMinimise() => ImplRequired.UnMinimise();

    /// <summary>
    /// 进入全屏模式。
    /// </summary>
    public void Fullscreen() => ImplRequired.Fullscreen();

    /// <summary>
    /// 退出全屏模式。
    /// </summary>
    public void UnFullscreen() => ImplRequired.UnFullscreen();

    /// <summary>
    /// 恢复窗口状态。
    /// </summary>
    public void Restore() => ImplRequired.Restore();

    /// <summary>
    /// 聚焦窗口。
    /// </summary>
    public void Focus() => ImplRequired.Focus();

    /// <summary>
    /// 显示菜单栏。
    /// </summary>
    public void ShowMenuBar() => ImplRequired.ShowMenuBar();

    /// <summary>
    /// 隐藏菜单栏。
    /// </summary>
    public void HideMenuBar() => ImplRequired.HideMenuBar();

    /// <summary>
    /// 切换菜单栏显示状态。
    /// </summary>
    public void ToggleMenuBar() => ImplRequired.ToggleMenuBar();

    /// <summary>
    /// 设置窗口是否总置顶。
    /// </summary>
    /// <param name="onTop">是否总置顶。</param>
    public void SetAlwaysOnTop(bool onTop) => ImplRequired.SetAlwaysOnTop(onTop);

    /// <summary>
    /// 设置窗口背景色（字节分量）。
    /// </summary>
    /// <param name="r">红色分量。</param>
    /// <param name="g">绿色分量。</param>
    /// <param name="b">蓝色分量。</param>
    /// <param name="a">透明度分量。</param>
    public void SetBackgroundColour(byte r, byte g, byte b, byte a) => ImplRequired.SetBackgroundColour(r, g, b, a);

    /// <summary>
    /// 设置窗口背景色（整数分量）。
    /// </summary>
    /// <param name="r">红色分量。</param>
    /// <param name="g">绿色分量。</param>
    /// <param name="b">蓝色分量。</param>
    /// <param name="a">透明度分量。</param>
    public void SetBackgroundColour(int r, int g, int b, int a) => ImplRequired.SetBackgroundColour(r, g, b, a);

    /// <summary>
    /// 是否处于全屏模式。
    /// </summary>
    /// <returns>如果处于全屏则返回 true，否则返回 false。</returns>
    public bool IsFullscreen() => ImplRequired.IsFullscreen();

    /// <summary>
    /// 是否已最大化。
    /// </summary>
    /// <returns>如果已最大化则返回 true，否则返回 false。</returns>
    public bool IsMaximised() => ImplRequired.IsMaximised();

    /// <summary>
    /// 是否已最小化。
    /// </summary>
    /// <returns>如果已最小化则返回 true，否则返回 false。</returns>
    public bool IsMinimised() => ImplRequired.IsMinimised();

    /// <summary>
    /// 是否可见。
    /// </summary>
    /// <returns>如果可见则返回 true，否则返回 false。</returns>
    public bool IsVisible() => ImplRequired.IsVisible();

    /// <summary>
    /// 是否已聚焦。
    /// </summary>
    /// <returns>如果已聚焦则返回 true，否则返回 false。</returns>
    public bool IsFocused() => ImplRequired.IsFocused();

    /// <summary>
    /// 设置是否无边框。
    /// </summary>
    /// <param name="frameless">是否无边框。</param>
    public void SetFrameless(bool frameless) => ImplRequired.SetFrameless(frameless);

    /// <summary>
    /// 打开开发者工具。
    /// </summary>
    public void OpenDevTools() => ImplRequired.OpenDevTools();

    /// <summary>
    /// 关闭开发者工具。
    /// </summary>
    public void CloseDevTools() => ImplRequired.CloseDevTools();

    /// <summary>
    /// 设置缩放比例。
    /// </summary>
    /// <param name="zoom">缩放比例。</param>
    public void SetZoom(float zoom) => ImplRequired.SetZoom(zoom);

    /// <summary>
    /// 设置缩放级别。
    /// </summary>
    /// <param name="level">缩放级别。</param>
    public void SetZoomLevel(float level) => ImplRequired.SetZoomLevel(level);

    /// <summary>
    /// 获取窗口大小。
    /// </summary>
    /// <returns>包含宽度和高度的元组。</returns>
    public (int Width, int Height) GetSize() => ImplRequired.GetSize();

    /// <summary>
    /// 获取内容区域大小。
    /// </summary>
    /// <returns>包含宽度和高度的元组。</returns>
    public (int Width, int Height) GetContentSize() => ImplRequired.GetContentSize();

    /// <summary>
    /// 获取窗口最小尺寸。
    /// </summary>
    /// <returns>包含宽度和高度的元组。</returns>
    public (int Width, int Height) GetMinSize() => ImplRequired.GetMinSize();

    /// <summary>
    /// 获取窗口最大尺寸。
    /// </summary>
    /// <returns>包含宽度和高度的元组。</returns>
    public (int Width, int Height) GetMaxSize() => ImplRequired.GetMaxSize();

    /// <summary>
    /// 获取窗口位置。
    /// </summary>
    /// <returns>包含 X 和 Y 坐标的元组。</returns>
    public (int X, int Y) GetPosition() => ImplRequired.GetPosition();

    /// <summary>
    /// 获取窗口当前 DIP 边界矩形（位置 + 大小）。
    /// </summary>
    /// <returns>DIP 边界矩形。</returns>
    public Rect GetBounds() => ImplRequired.GetBounds();

    /// <summary>
    /// 设置窗口 DIP 边界矩形（位置 + 大小）。
    /// </summary>
    /// <param name="bounds">目标 DIP 边界矩形。</param>
    public void SetBounds(Rect bounds) => ImplRequired.SetBounds(bounds);

    /// <summary>
    /// 获取窗口物理像素边界矩形。
    /// </summary>
    /// <returns>物理像素边界矩形。</returns>
    public Rect GetPhysicalBounds() => ImplRequired.GetPhysicalBounds();

    /// <summary>
    /// 设置窗口物理像素边界矩形。
    /// </summary>
    /// <param name="bounds">目标物理像素边界矩形。</param>
    public void SetPhysicalBounds(Rect bounds) => ImplRequired.SetPhysicalBounds(bounds);

    /// <summary>
    /// 获取窗口相对于所在屏幕工作区的位置。
    /// </summary>
    /// <returns>相对工作区的坐标。</returns>
    public (int X, int Y) GetRelativePosition() => ImplRequired.GetRelativePosition();

    /// <summary>
    /// 设置窗口相对于所在屏幕工作区的位置。
    /// </summary>
    /// <param name="x">相对工作区的 X 坐标。</param>
    /// <param name="y">相对工作区的 Y 坐标。</param>
    public void SetRelativePosition(int x, int y) => ImplRequired.SetRelativePosition(x, y);

    /// <summary>
    /// 获取窗口边框尺寸。
    /// </summary>
    /// <returns>边框尺寸结构。</returns>
    public LRTB GetBorderSizes() => ImplRequired.GetBorderSizes();

    /// <summary>
    /// 获取窗口所在的屏幕。
    /// </summary>
    /// <returns>窗口所在屏幕，不支持时返回 null。</returns>
    public Screen? GetScreen() => ImplRequired.GetScreen();

    /// <summary>
    /// 获取窗口宽度。
    /// </summary>
    /// <returns>窗口宽度（DIP）。</returns>
    public int GetWidth() => ImplRequired.GetWidth();

    /// <summary>
    /// 获取窗口高度。
    /// </summary>
    /// <returns>窗口高度（DIP）。</returns>
    public int GetHeight() => ImplRequired.GetHeight();

    /// <summary>
    /// 查询窗口是否可调整大小。
    /// </summary>
    /// <returns>如果可调整返回 true，否则 false。</returns>
    public bool IsResizable() => ImplRequired.IsResizable();

    /// <summary>
    /// 查询窗口是否处于正常状态。
    /// </summary>
    /// <returns>如果处于正常状态返回 true，否则 false。</returns>
    public bool IsNormal() => ImplRequired.IsNormal();

    /// <summary>
    /// 查询窗口是否忽略鼠标事件（点击穿透）。
    /// </summary>
    /// <returns>如果忽略鼠标事件返回 true，否则 false。</returns>
    public bool IsIgnoreMouseEvents() => ImplRequired.IsIgnoreMouseEvents();

    /// <summary>
    /// 查询窗口是否总置顶。
    /// </summary>
    /// <returns>如果总置顶返回 true，否则 false。</returns>
    public bool IsAlwaysOnTop() => ImplRequired.IsAlwaysOnTop();

    /// <summary>
    /// 获取缩放比例。
    /// </summary>
    /// <returns>缩放比例。</returns>
    public float GetZoom() => ImplRequired.GetZoom();

    /// <summary>
    /// 获取缩放级别。
    /// </summary>
    /// <returns>缩放级别。</returns>
    public float GetZoomLevel() => ImplRequired.GetZoomLevel();

    /// <summary>
    /// 执行 JavaScript 代码。
    /// </summary>
    /// <param name="js">JavaScript 代码字符串。</param>
    public void ExecJS(string js) => ImplRequired.ExecJS(js);

    /// <summary>
    /// 捕获窗口内容为 PNG 图片。
    /// </summary>
    /// <returns>PNG 图片字节数据，不支持时返回 null。</returns>
    public async Task<byte[]?> CapturePreviewAsync()
    {
        return await ImplRequired.CapturePreviewAsync();
    }

    /// <summary>
    /// 注册自定义协议方案，使 WebView 拦截指定 scheme 的请求。
    /// 对应 Tauri v2 的自定义协议功能。
    /// </summary>
    /// <param name="scheme">协议方案名称（如 "myapp"）。</param>
    public void RegisterCustomScheme(string scheme) => ImplRequired.RegisterCustomScheme(scheme);

    /// <summary>
    /// 将窗口内容导出为 PDF，使用指定的导出选项。
    /// </summary>
    /// <param name="path">PDF 文件保存路径。</param>
    /// <param name="options">PDF 导出选项，为 null 时使用默认选项。</param>
    public void PrintToPDF(string path, PrintToPdfOptions? options) => ImplRequired.PrintToPDF(path, options);

    /// <summary>
    /// 设置窗口透明度（0.0 完全透明 ~ 1.0 完全不透明）。
    /// 对应 Wails v3 的 window.setOpacity 和 Tauri v2 的 window.setAlpha。
    /// </summary>
    /// <param name="opacity">透明度值，范围 0.0 到 1.0。</param>
    public void SetOpacity(float opacity) => ImplRequired.SetOpacity(opacity);

    /// <summary>
    /// 获取窗口透明度（0.0 完全透明 ~ 1.0 完全不透明）。
    /// </summary>
    /// <returns>当前透明度值。</returns>
    public float GetOpacity() => ImplRequired.GetOpacity();

    /// <summary>
    /// 设置任务栏进度条状态。
    /// 对应 Tauri v2 的 window.setProgressBar(progress)。
    /// </summary>
    /// <param name="state">进度状态枚举。</param>
    /// <param name="completed">已完成值（0 ~ total），默认 0。</param>
    /// <param name="total">总值，默认 100。</param>
    public void SetTaskbarProgress(TaskbarProgressState state, ulong completed = 0, ulong total = 100)
        => ImplRequired.SetTaskbarProgress(state, completed, total);

    /// <summary>
    /// 设置任务栏叠加图标。
    /// 对应 Tauri v2 的 window.setOverlayIcon(icon, description)。
    /// </summary>
    /// <param name="iconBytes">图标字节数据（ICO 格式），为 null 时清除。</param>
    /// <param name="description">无障碍描述文本。</param>
    public void SetOverlayIcon(byte[]? iconBytes, string? description = null)
        => ImplRequired.SetOverlayIcon(iconBytes, description);

    /// <summary>
    /// 设置窗口是否跳过任务栏（不在任务栏显示）。
    /// 对应 Tauri v2 的 window.setSkipTaskbar(skip)。
    /// </summary>
    /// <param name="skip">true 表示隐藏任务栏按钮。</param>
    public void SetSkipTaskbar(bool skip)
        => ImplRequired.SetSkipTaskbar(skip);

    /// <summary>
    /// 设置窗口是否忽略鼠标事件（点击穿透）。
    /// 对应 Tauri v2 的 window.setIgnoreCursorEvents(ignore)。
    /// </summary>
    /// <param name="ignore">true 表示鼠标事件穿透窗口。</param>
    public void SetIgnoreCursorEvents(bool ignore)
        => ImplRequired.SetIgnoreCursorEvents(ignore);

    /// <summary>
    /// 设置窗口视觉特效（Mica/Acrylic/BlurBehind 等）。
    /// 对应 Tauri v2 的 window.setEffects(effects)。
    /// 仅 Windows 11 22000+ 支持 Mica/Acrylic。
    /// </summary>
    /// <param name="effects">窗口特效参数。</param>
    public void SetEffects(WindowEffects effects)
        => ImplRequired.SetEffects(effects);

    /// <summary>
    /// 设置任务栏徽章计数。
    /// 对应 Tauri v2 的 window.setBadgeCount(count)。
    /// </summary>
    /// <param name="count">徽章计数值，0 表示清除。</param>
    public void SetBadgeCount(int count)
        => ImplRequired.SetBadgeCount(count);

    /// <summary>
    /// 设置任务栏徽章文本。
    /// 对应 Tauri v2 的 window.setBadgeLabel(label)。
    /// </summary>
    /// <param name="label">徽章文本，null 或空字符串表示清除。</param>
    public void SetBadgeLabel(string? label)
        => ImplRequired.SetBadgeLabel(label);

    /// <summary>
    /// 设置窗口是否在所有工作区可见。
    /// 对应 Tauri v2 的 window.setVisibleOnAllWorkspaces(visible)。
    /// </summary>
    /// <param name="visible">true 表示在所有工作区可见。</param>
    public void SetVisibleOnAllWorkspaces(bool visible)
        => ImplRequired.SetVisibleOnAllWorkspaces(visible);

    /// <summary>
    /// 设置窗口边框颜色。
    /// 对应 Tauri v2 的 window.setBorderColor(color)。
    /// 仅 Windows 11 22000+ 支持。
    /// </summary>
    /// <param name="color">十六进制颜色字符串（如 #FF0000），null 表示恢复默认。</param>
    public void SetBorderColor(string? color)
        => ImplRequired.SetBorderColor(color);

    /// <summary>
    /// 设置是否启用文件拖放。
    /// 对应 Tauri v2 的 window.setFileDropEnabled(enabled)。
    /// </summary>
    /// <param name="enabled">true 表示启用文件拖放。</param>
    public void SetFileDropEnabled(bool enabled)
        => ImplRequired.SetFileDropEnabled(enabled);

    /// <summary>
    /// 后退导航。
    /// </summary>
    public void GoBack() => ImplRequired.GoBack();

    /// <summary>
    /// 前进导航。
    /// </summary>
    public void GoForward() => ImplRequired.GoForward();

    /// <summary>
    /// 重新加载页面。
    /// </summary>
    public void Reload() => ImplRequired.Reload();

    /// <summary>
    /// 设置 URL。
    /// </summary>
    /// <param name="url">目标 URL。</param>
    public void SetURL(string url) => ImplRequired.SetURL(url);

    /// <summary>
    /// 设置 HTML 内容。
    /// </summary>
    /// <param name="html">HTML 内容字符串。</param>
    public void SetHTML(string html) => ImplRequired.SetHTML(html);

    /// <summary>
    /// 打印窗口内容。
    /// </summary>
    public void Print() => ImplRequired.Print();

    /// <summary>
    /// 将窗口内容导出为 PDF 文件。
    /// </summary>
    /// <param name="path">PDF 文件路径。</param>
    public void PrintToPDF(string path) => ImplRequired.PrintToPDF(path);

    /// <summary>
    /// 设置窗口菜单。
    /// </summary>
    /// <param name="menu">菜单实例，可为 null。</param>
    public void SetMenu(Menu? menu) => ImplRequired.SetMenu(menu);

    /// <summary>
    /// 开始拖动窗口。
    /// </summary>
    public void StartDrag() => ImplRequired.StartDrag();

    /// <summary>
    /// 开始调整窗口大小。
    /// </summary>
    public void StartResize() => ImplRequired.StartResize();

    /// <summary>
    /// 设置窗口是否启用。
    /// </summary>
    /// <param name="enabled">是否启用。</param>
    public void SetEnabled(bool enabled) => ImplRequired.SetEnabled(enabled);

    /// <summary>
    /// 设置内容保护。
    /// </summary>
    /// <param name="enabled">是否启用内容保护。</param>
    public void SetContentProtection(bool enabled) => ImplRequired.SetContentProtection(enabled);

    /// <summary>
    /// 将窗口作为指定父窗口的模态窗口附加。
    /// </summary>
    /// <param name="parentWindowId">父窗口 ID。</param>
    public void AttachAsModal(uint parentWindowId) => ImplRequired.AttachAsModal(parentWindowId);

    /// <summary>
    /// 设置窗口是否可调整大小。
    /// </summary>
    /// <param name="resizable">是否可调整大小。</param>
    public void SetResizable(bool resizable) => ImplRequired.SetResizable(resizable);

    /// <summary>
    /// 设置窗口是否可最大化。
    /// </summary>
    /// <param name="maximisable">是否可最大化。</param>
    public void SetMaximisable(bool maximisable) => ImplRequired.SetMaximisable(maximisable);

    /// <summary>
    /// 设置窗口是否可最小化。
    /// </summary>
    /// <param name="minimisable">是否可最小化。</param>
    public void SetMinimisable(bool minimisable) => ImplRequired.SetMinimisable(minimisable);

    /// <summary>
    /// 设置窗口是否可关闭。
    /// </summary>
    /// <param name="closable">是否可关闭。</param>
    public void SetClosable(bool closable) => ImplRequired.SetClosable(closable);

    /// <summary>
    /// 设置窗口是否有阴影。
    /// </summary>
    /// <param name="hasShadow">是否有阴影。</param>
    public void SetHasShadow(bool hasShadow) => ImplRequired.SetHasShadow(hasShadow);

    /// <summary>
    /// 设置标题栏样式。
    /// </summary>
    /// <param name="style">标题栏样式。</param>
    public void SetTitleBarStyle(TitleBarStyle style) => ImplRequired.SetTitleBarStyle(style);

    /// <summary>
    /// 将窗口居中显示。
    /// </summary>
    public void Centre() => ImplRequired.Centre();

    /// <summary>
    /// 设置是否启用调试模式。
    /// </summary>
    /// <param name="enabled">是否启用调试。</param>
    public void SetDebuggingEnabled(bool enabled) => ImplRequired.SetDebuggingEnabled(enabled);

    /// <summary>
    /// 获取当前 URL。
    /// </summary>
    /// <returns>当前 URL 字符串。</returns>
    public string GetURL() => ImplRequired.GetURL();

    /// <summary>
    /// 加载指定 URL。
    /// </summary>
    /// <param name="url">要加载的 URL。</param>
    public void LoadURL(string url) => ImplRequired.LoadURL(url);

    /// <summary>
    /// 加载指定 HTML 内容。
    /// </summary>
    /// <param name="html">要加载的 HTML 内容。</param>
    public void LoadHTML(string html) => ImplRequired.LoadHTML(html);

    /// <summary>
    /// 设置窗口背景类型。
    /// </summary>
    /// <param name="type">背景类型字符串。</param>
    public void SetBackgroundType(string type) => ImplRequired.SetBackgroundType(type);

    /// <summary>
    /// 设置全屏按钮是否可用。
    /// </summary>
    /// <param name="enabled">是否可用。</param>
    public void SetFullscreenButtonEnabled(bool enabled) => ImplRequired.SetFullscreenButtonEnabled(enabled);

    /// <summary>
    /// 设置缩放比例（double 重载）。
    /// </summary>
    /// <param name="zoom">缩放比例。</param>
    public void SetZoom(double zoom) => ImplRequired.SetZoom(zoom);

    /// <summary>
    /// 设置是否启用缩放。
    /// </summary>
    /// <param name="enabled">是否启用缩放。</param>
    public void SetZoomEnabled(bool enabled) => ImplRequired.SetZoomEnabled(enabled);

    /// <summary>
    /// 设置窗口是否半透明。
    /// </summary>
    /// <param name="translucent">是否半透明。</param>
    public void SetTranslucent(bool translucent) => ImplRequired.SetTranslucent(translucent);

    /// <summary>
    /// 设置标题栏样式（字符串重载）。
    /// </summary>
    /// <param name="style">标题栏样式字符串。</param>
    public void SetTitleBarStyle(string style) => ImplRequired.SetTitleBarStyle(style);

    /// <summary>
    /// 注入 CSS 样式到当前页面。
    /// </summary>
    /// <param name="css">CSS 样式字符串。</param>
    public void InjectCSS(string css) => ImplRequired.InjectCSS(css);

    /// <summary>
    /// 放大缩放。
    /// </summary>
    public void ZoomIn() => ImplRequired.ZoomIn();

    /// <summary>
    /// 缩小缩放。
    /// </summary>
    public void ZoomOut() => ImplRequired.ZoomOut();

    /// <summary>
    /// 重置缩放。
    /// </summary>
    public void ZoomReset() => ImplRequired.ZoomReset();

    /// <summary>
    /// 将窗口设置为最小化状态。
    /// </summary>
    public void SetMinimised() => ImplRequired.SetMinimised();

    /// <summary>
    /// 将窗口设置为最大化状态。
    /// </summary>
    public void SetMaximised() => ImplRequired.SetMaximised();

    /// <summary>
    /// 将窗口设置为正常状态。
    /// </summary>
    public void SetNormal() => ImplRequired.SetNormal();

    /// <summary>
    /// 在指定坐标打开上下文菜单。
    /// </summary>
    /// <param name="x">X 坐标。</param>
    /// <param name="y">Y 坐标。</param>
    public void OpenContextMenu(int x, int y) => ImplRequired.OpenContextMenu(x, y);

    /// <summary>
    /// 按 <see cref="Menus.ContextMenuData"/> 打开已注册的上下文菜单（P1-4）。
    /// 对应 Wails v3 Go 版本 <c>window.OpenContextMenu(data *ContextMenuData)</c> 契约。
    /// </summary>
    /// <param name="data">上下文菜单数据。</param>
    public void OpenContextMenu(Menus.ContextMenuData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Impl?.OpenContextMenu(data);
    }

    /// <summary>
    /// 将窗口内容导出为 PDF（字节数组选项重载）。
    /// </summary>
    /// <param name="pageOptions">PDF 导出选项字节数组，可为 null。</param>
    public void PrintToPDF(byte[]? pageOptions) => ImplRequired.PrintToPDF(pageOptions);

    /// <summary>
    /// 注册窗口就绪回调。
    /// </summary>
    /// <param name="callback">窗口就绪时执行的回调。</param>
    public void Run(Action callback) => ImplRequired.Run(callback);

    /// <summary>
    /// 订阅指定事件类型的回调。
    /// </summary>
    /// <param name="eventType">事件类型数值。</param>
    /// <param name="callback">事件触发时执行的回调。</param>
    public void On(uint eventType, Action callback)
    {
        lock (_eventLock)
        {
            if (!_eventListeners.TryGetValue(eventType, out var list))
            {
                list = new List<Action>();
                _eventListeners[eventType] = list;
            }
            list.Add(callback);
        }
    }

    /// <summary>
    /// 取消订阅指定事件类型的所有回调。
    /// </summary>
    /// <param name="eventType">事件类型数值。</param>
    public void Off(uint eventType)
    {
        lock (_eventLock)
        {
            _eventListeners.Remove(eventType);
        }
    }

    /// <summary>
    /// 发射指定事件类型，触发所有已订阅的回调。
    /// 当事件类型为 <see cref="WindowEventType.WindowRuntimeReady"/> 时，
    /// 同时触发 <see cref="RuntimeReady"/> 事件。
    /// 同时将事件传播到应用级 <see cref="Application.Events"/> 事件处理器，
    /// 以便窗口事件能够被应用级订阅者接收。
    /// </summary>
    /// <param name="eventType">事件类型数值。</param>
    /// <param name="data">附加数据，可为 null。</param>
    public void Emit(uint eventType, object? data = null)
    {
        Action[] callbacks;
        lock (_eventLock)
        {
            if (!_eventListeners.TryGetValue(eventType, out var list))
            {
                callbacks = Array.Empty<Action>();
            }
            else
            {
                callbacks = list.ToArray();
            }
        }
        foreach (var callback in callbacks)
        {
            callback();
        }

        if (eventType == (uint)WindowEventType.WindowRuntimeReady)
        {
            RuntimeReady?.Invoke();
        }

        // 将窗口事件传播到应用级事件处理器，使窗口事件接入 EventProcessor。
        // 通过 Application.Get() 获取全局应用实例，避免持有额外引用。
        // 使用 KnownEvents.GetEventName 将事件类型数值转换为事件名称字符串。
        Application.Get()?.Events?.Emit(KnownEvents.GetEventName(eventType), data, ID);
    }
}
