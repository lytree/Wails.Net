using Wails.Net.Application.Menus;

namespace Wails.Net.Application.Windows;

/// <summary>
/// 标题栏样式枚举。
/// </summary>
public enum TitleBarStyle
{
    /// <summary>
    /// 默认样式。
    /// </summary>
    Default = 0,

    /// <summary>
    /// 隐藏标题栏。
    /// </summary>
    Hidden = 1,

    /// <summary>
    /// 隐藏标题栏并使用内嵌样式。
    /// </summary>
    HiddenInset = 2,

    /// <summary>
    /// 统一样式。
    /// </summary>
    Unified = 3
}

/// <summary>
/// 平台特定的 Webview 窗口实现接口，对应 Go 版的 webviewWindowImpl。
/// </summary>
public interface IWebviewWindowImpl
{
    /// <summary>
    /// 设置窗口标题。
    /// </summary>
    /// <param name="title">窗口标题。</param>
    void SetTitle(string title);

    /// <summary>
    /// 设置窗口大小。
    /// </summary>
    /// <param name="width">窗口宽度。</param>
    /// <param name="height">窗口高度。</param>
    void SetSize(int width, int height);

    /// <summary>
    /// 设置窗口最小尺寸。
    /// </summary>
    /// <param name="width">最小宽度。</param>
    /// <param name="height">最小高度。</param>
    void SetMinSize(int width, int height);

    /// <summary>
    /// 设置窗口最大尺寸。
    /// </summary>
    /// <param name="width">最大宽度。</param>
    /// <param name="height">最大高度。</param>
    void SetMaxSize(int width, int height);

    /// <summary>
    /// 设置窗口位置。
    /// </summary>
    /// <param name="x">X 坐标。</param>
    /// <param name="y">Y 坐标。</param>
    void SetPosition(int x, int y);

    /// <summary>
    /// 显示窗口。
    /// </summary>
    void Show();

    /// <summary>
    /// 隐藏窗口。
    /// </summary>
    void Hide();

    /// <summary>
    /// 最大化窗口。
    /// </summary>
    void Maximise();

    /// <summary>
    /// 取消最大化窗口。
    /// </summary>
    void UnMaximise();

    /// <summary>
    /// 最小化窗口。
    /// </summary>
    void Minimise();

    /// <summary>
    /// 取消最小化窗口。
    /// </summary>
    void UnMinimise();

    /// <summary>
    /// 进入全屏模式。
    /// </summary>
    void Fullscreen();

    /// <summary>
    /// 退出全屏模式。
    /// </summary>
    void UnFullscreen();

    /// <summary>
    /// 恢复窗口状态。
    /// </summary>
    void Restore();

    /// <summary>
    /// 关闭窗口。
    /// </summary>
    void Close();

    /// <summary>
    /// 聚焦窗口。
    /// </summary>
    void Focus();

    /// <summary>
    /// 显示菜单栏。
    /// </summary>
    void ShowMenuBar();

    /// <summary>
    /// 隐藏菜单栏。
    /// </summary>
    void HideMenuBar();

    /// <summary>
    /// 切换菜单栏显示状态。
    /// </summary>
    void ToggleMenuBar();

    /// <summary>
    /// 设置窗口是否总置顶。
    /// </summary>
    /// <param name="onTop">是否总置顶。</param>
    void SetAlwaysOnTop(bool onTop);

    /// <summary>
    /// 设置窗口背景色（字节分量）。
    /// </summary>
    /// <param name="r">红色分量。</param>
    /// <param name="g">绿色分量。</param>
    /// <param name="b">蓝色分量。</param>
    /// <param name="a">透明度分量。</param>
    void SetBackgroundColour(byte r, byte g, byte b, byte a);

    /// <summary>
    /// 设置窗口背景色（整数分量）。
    /// </summary>
    /// <param name="r">红色分量。</param>
    /// <param name="g">绿色分量。</param>
    /// <param name="b">蓝色分量。</param>
    /// <param name="a">透明度分量。</param>
    void SetBackgroundColour(int r, int g, int b, int a);

    /// <summary>
    /// 是否处于全屏模式。
    /// </summary>
    /// <returns>如果处于全屏则返回 true，否则返回 false。</returns>
    bool IsFullscreen();

    /// <summary>
    /// 是否已最大化。
    /// </summary>
    /// <returns>如果已最大化则返回 true，否则返回 false。</returns>
    bool IsMaximised();

    /// <summary>
    /// 是否已最小化。
    /// </summary>
    /// <returns>如果已最小化则返回 true，否则返回 false。</returns>
    bool IsMinimised();

    /// <summary>
    /// 是否可见。
    /// </summary>
    /// <returns>如果可见则返回 true，否则返回 false。</returns>
    bool IsVisible();

    /// <summary>
    /// 是否已聚焦。
    /// </summary>
    /// <returns>如果已聚焦则返回 true，否则返回 false。</returns>
    bool IsFocused();

    /// <summary>
    /// 设置是否无边框。
    /// </summary>
    /// <param name="frameless">是否无边框。</param>
    void SetFrameless(bool frameless);

    /// <summary>
    /// 打开开发者工具。
    /// </summary>
    void OpenDevTools();

    /// <summary>
    /// 关闭开发者工具。
    /// </summary>
    void CloseDevTools();

    /// <summary>
    /// 设置缩放比例。
    /// </summary>
    /// <param name="zoom">缩放比例。</param>
    void SetZoom(float zoom);

    /// <summary>
    /// 设置缩放级别。
    /// </summary>
    /// <param name="level">缩放级别。</param>
    void SetZoomLevel(float level);

    /// <summary>
    /// 获取窗口大小。
    /// </summary>
    /// <returns>包含宽度和高度的元组。</returns>
    (int Width, int Height) GetSize();

    /// <summary>
    /// 获取内容区域大小。
    /// </summary>
    /// <returns>包含宽度和高度的元组。</returns>
    (int Width, int Height) GetContentSize();

    /// <summary>
    /// 获取窗口最小尺寸。
    /// </summary>
    /// <returns>包含宽度和高度的元组。</returns>
    (int Width, int Height) GetMinSize();

    /// <summary>
    /// 获取窗口最大尺寸。
    /// </summary>
    /// <returns>包含宽度和高度的元组。</returns>
    (int Width, int Height) GetMaxSize();

    /// <summary>
    /// 获取窗口位置。
    /// </summary>
    /// <returns>包含 X 和 Y 坐标的元组。</returns>
    (int X, int Y) GetPosition();

    /// <summary>
    /// 获取缩放比例。
    /// </summary>
    /// <returns>缩放比例。</returns>
    float GetZoom();

    /// <summary>
    /// 获取缩放级别。
    /// </summary>
    /// <returns>缩放级别。</returns>
    float GetZoomLevel();

    /// <summary>
    /// 执行 JavaScript 代码。
    /// </summary>
    /// <param name="js">JavaScript 代码字符串。</param>
    void ExecJS(string js);

    /// <summary>
    /// 后退导航。
    /// </summary>
    void GoBack();

    /// <summary>
    /// 前进导航。
    /// </summary>
    void GoForward();

    /// <summary>
    /// 重新加载页面。
    /// </summary>
    void Reload();

    /// <summary>
    /// 设置 URL。
    /// </summary>
    /// <param name="url">目标 URL。</param>
    void SetURL(string url);

    /// <summary>
    /// 设置 HTML 内容。
    /// </summary>
    /// <param name="html">HTML 内容字符串。</param>
    void SetHTML(string html);

    /// <summary>
    /// 打印窗口内容。
    /// </summary>
    void Print();

    /// <summary>
    /// 将窗口内容导出为 PDF 文件。
    /// </summary>
    /// <param name="path">PDF 文件路径。</param>
    void PrintToPDF(string path);

    /// <summary>
    /// 设置窗口菜单。
    /// </summary>
    /// <param name="menu">菜单实例，可为 null。</param>
    void SetMenu(Menu? menu);

    /// <summary>
    /// 开始拖动窗口。
    /// </summary>
    void StartDrag();

    /// <summary>
    /// 开始调整窗口大小。
    /// </summary>
    void StartResize();

    /// <summary>
    /// 设置窗口是否启用。
    /// </summary>
    /// <param name="enabled">是否启用。</param>
    void SetEnabled(bool enabled);

    /// <summary>
    /// 设置内容保护。
    /// </summary>
    /// <param name="enabled">是否启用内容保护。</param>
    void SetContentProtection(bool enabled);

    /// <summary>
    /// 将窗口作为指定父窗口的模态窗口附加。
    /// </summary>
    /// <param name="parentWindowId">父窗口 ID。</param>
    void AttachAsModal(uint parentWindowId);

    /// <summary>
    /// 设置窗口是否可调整大小。
    /// </summary>
    /// <param name="resizable">是否可调整大小。</param>
    void SetResizable(bool resizable);

    /// <summary>
    /// 设置窗口是否可最大化。
    /// </summary>
    /// <param name="maximisable">是否可最大化。</param>
    void SetMaximisable(bool maximisable);

    /// <summary>
    /// 设置窗口是否可最小化。
    /// </summary>
    /// <param name="minimisable">是否可最小化。</param>
    void SetMinimisable(bool minimisable);

    /// <summary>
    /// 设置窗口是否可关闭。
    /// </summary>
    /// <param name="closable">是否可关闭。</param>
    void SetClosable(bool closable);

    /// <summary>
    /// 设置窗口是否有阴影。
    /// </summary>
    /// <param name="hasShadow">是否有阴影。</param>
    void SetHasShadow(bool hasShadow);

    /// <summary>
    /// 设置标题栏样式。
    /// </summary>
    /// <param name="style">标题栏样式。</param>
    void SetTitleBarStyle(TitleBarStyle style);

    /// <summary>
    /// 将窗口居中显示。
    /// </summary>
    void Centre();

    /// <summary>
    /// 设置是否启用调试模式。
    /// </summary>
    /// <param name="enabled">是否启用调试。</param>
    void SetDebuggingEnabled(bool enabled);

    /// <summary>
    /// 获取当前 URL。
    /// </summary>
    /// <returns>当前 URL 字符串。</returns>
    string GetURL();

    /// <summary>
    /// 加载指定 URL。
    /// </summary>
    /// <param name="url">要加载的 URL。</param>
    void LoadURL(string url);

    /// <summary>
    /// 加载指定 HTML 内容。
    /// </summary>
    /// <param name="html">要加载的 HTML 内容。</param>
    void LoadHTML(string html);

    /// <summary>
    /// 设置窗口背景类型。
    /// </summary>
    /// <param name="type">背景类型字符串（如 "transparent"、"translucent"、"solid"）。</param>
    void SetBackgroundType(string type)
    {
        // 默认空实现，平台实现可重写。
    }

    /// <summary>
    /// 设置全屏按钮是否可用。
    /// </summary>
    /// <param name="enabled">是否可用。</param>
    void SetFullscreenButtonEnabled(bool enabled)
    {
        // 默认空实现，平台实现可重写。
    }

    /// <summary>
    /// 设置缩放比例（double 重载）。
    /// </summary>
    /// <param name="zoom">缩放比例。</param>
    void SetZoom(double zoom)
    {
        // 默认空实现，平台实现可重写。
    }

    /// <summary>
    /// 设置是否启用缩放。
    /// </summary>
    /// <param name="enabled">是否启用缩放。</param>
    void SetZoomEnabled(bool enabled)
    {
        // 默认空实现，平台实现可重写。
    }

    /// <summary>
    /// 设置窗口是否半透明。
    /// </summary>
    /// <param name="translucent">是否半透明。</param>
    void SetTranslucent(bool translucent)
    {
        // 默认空实现，平台实现可重写。
    }

    /// <summary>
    /// 设置窗口透明度（0.0 完全透明 ~ 1.0 完全不透明）。
    /// 对应 Wails v3 的 window.setOpacity 和 Tauri v2 的 window.setAlpha。
    /// 默认实现为空操作，平台实现可重写以提供实际透明度控制。
    /// </summary>
    /// <param name="opacity">透明度值，范围 0.0 到 1.0。</param>
    void SetOpacity(float opacity)
    {
        // 默认空实现，平台实现可重写。
    }

    /// <summary>
    /// 获取窗口透明度（0.0 完全透明 ~ 1.0 完全不透明）。
    /// 默认实现返回 1.0（完全不透明），平台实现可重写。
    /// </summary>
    /// <returns>当前透明度值。</returns>
    float GetOpacity()
    {
        return 1.0f;
    }

    /// <summary>
    /// 设置标题栏样式（字符串重载）。
    /// </summary>
    /// <param name="style">标题栏样式字符串。</param>
    void SetTitleBarStyle(string style)
    {
        // 默认空实现，平台实现可重写。
    }

    /// <summary>
    /// 注入 CSS 样式到当前页面。
    /// </summary>
    /// <param name="css">CSS 样式字符串。</param>
    void InjectCSS(string css)
    {
        // 默认空实现，平台实现可重写。
    }

    /// <summary>
    /// 放大缩放。
    /// </summary>
    void ZoomIn()
    {
        // 默认空实现，平台实现可重写。
    }

    /// <summary>
    /// 缩小缩放。
    /// </summary>
    void ZoomOut()
    {
        // 默认空实现，平台实现可重写。
    }

    /// <summary>
    /// 重置缩放。
    /// </summary>
    void ZoomReset()
    {
        // 默认空实现，平台实现可重写。
    }

    /// <summary>
    /// 将窗口设置为最小化状态。
    /// </summary>
    void SetMinimised()
    {
        // 默认空实现，平台实现可重写。
    }

    /// <summary>
    /// 将窗口设置为最大化状态。
    /// </summary>
    void SetMaximised()
    {
        // 默认空实现，平台实现可重写。
    }

    /// <summary>
    /// 将窗口设置为正常状态。
    /// </summary>
    void SetNormal()
    {
        // 默认空实现，平台实现可重写。
    }

    /// <summary>
    /// 在指定坐标打开上下文菜单。
    /// </summary>
    /// <param name="x">X 坐标。</param>
    /// <param name="y">Y 坐标。</param>
    void OpenContextMenu(int x, int y)
    {
        // 默认空实现，平台实现可重写。
    }

    /// <summary>
    /// 将窗口内容导出为 PDF（字节数组选项重载）。
    /// </summary>
    /// <param name="pageOptions">PDF 导出选项字节数组，可为 null。</param>
    void PrintToPDF(byte[]? pageOptions)
    {
        // 默认空实现，平台实现可重写。
    }

    /// <summary>
    /// 注册窗口就绪回调。
    /// </summary>
    /// <param name="callback">窗口就绪时执行的回调。</param>
    void Run(Action callback)
    {
        // 默认立即执行回调，平台实现可重写以在合适时机调用。
        callback();
    }

    /// <summary>
    /// 捕获窗口内容为图片。
    /// 默认实现返回 null 表示不支持，平台实现可重写以提供截图能力。
    /// </summary>
    /// <returns>PNG 格式的图片字节数据，不支持时返回 null。</returns>
    Task<byte[]?> CapturePreviewAsync()
    {
        // 默认实现：不支持截图，返回 null。
        return Task.FromResult<byte[]?>(null);
    }

    /// <summary>
    /// 注册自定义协议方案，使 WebView 拦截指定 scheme 的请求。
    /// 对应 Tauri v2 的自定义协议（asset protocol、自定义 scheme）功能。
    /// 默认实现为空操作，平台实现可重写以提供实际拦截。
    /// </summary>
    /// <param name="scheme">协议方案名称（如 "myapp"）。</param>
    void RegisterCustomScheme(string scheme)
    {
        // 默认空实现，平台实现可重写。
    }

    /// <summary>
    /// 将窗口内容导出为 PDF，使用指定的导出选项。
    /// 对应 Tauri v2 的 WebviewWindow.printToPDF(options) 功能。
    /// 默认实现委托到无选项重载，平台实现可重写以支持完整选项。
    /// </summary>
    /// <param name="path">PDF 文件保存路径。</param>
    /// <param name="options">PDF 导出选项，为 null 时使用默认选项。</param>
    void PrintToPDF(string path, PrintToPdfOptions? options)
    {
        // 默认实现：忽略选项，委托到无选项重载。
        PrintToPDF(path);
    }

    /// <summary>
    /// 设置任务栏进度条状态。
    /// 对应 Tauri v2 的 window.setProgressBar(progress) 和 Wails v3 的 TaskbarProgress。
    /// 默认实现为空操作，Windows 平台实现通过 ITaskbarList3 COM 接口提供实际功能。
    /// </summary>
    /// <param name="state">进度状态枚举。</param>
    /// <param name="completed">已完成值（0 ~ total）。</param>
    /// <param name="total">总值。</param>
    void SetTaskbarProgress(TaskbarProgressState state, ulong completed, ulong total)
    {
        // 默认空实现，平台实现可重写。
    }

    /// <summary>
    /// 设置任务栏叠加图标。
    /// 对应 Tauri v2 的 window.setOverlayIcon(icon, description)。
    /// 默认实现为空操作，Windows 平台实现通过 ITaskbarList3 COM 接口提供实际功能。
    /// </summary>
    /// <param name="iconBytes">图标字节数据（ICO 格式），为 null 时清除叠加图标。</param>
    /// <param name="description">无障碍描述文本。</param>
    void SetOverlayIcon(byte[]? iconBytes, string? description)
    {
        // 默认空实现，平台实现可重写。
    }

    /// <summary>
    /// 设置窗口是否跳过任务栏（不在任务栏显示）。
    /// 对应 Tauri v2 的 window.setSkipTaskbar(skip)。
    /// 默认实现为空操作，平台实现可重写。
    /// </summary>
    /// <param name="skip">true 表示隐藏任务栏按钮。</param>
    void SetSkipTaskbar(bool skip)
    {
        // 默认空实现，平台实现可重写。
    }

    /// <summary>
    /// 设置窗口是否忽略鼠标事件（点击穿透）。
    /// 对应 Tauri v2 的 window.setIgnoreCursorEvents(ignore)。
    /// 默认实现为空操作，平台实现可重写。
    /// </summary>
    /// <param name="ignore">true 表示鼠标事件穿透窗口。</param>
    void SetIgnoreCursorEvents(bool ignore)
    {
        // 默认空实现，平台实现可重写。
    }

    /// <summary>
    /// 设置窗口视觉特效（Mica/Acrylic/BlurBehind 等）。
    /// 对应 Tauri v2 的 window.setEffects(effects)。
    /// 默认实现为空操作，Windows 11 平台实现通过 DwmSetWindowAttribute 提供实际功能。
    /// </summary>
    /// <param name="effects">窗口特效参数。</param>
    void SetEffects(WindowEffects effects)
    {
        // 默认空实现，平台实现可重写。
    }

    /// <summary>
    /// 设置任务栏徽章计数。
    /// 对应 Tauri v2 的 window.setBadgeCount(count)。
    /// 默认实现为空操作，Windows 平台实现通过 ITaskbarList3.SetOverlayIcon 生成数字徽章。
    /// </summary>
    /// <param name="count">徽章计数值，0 表示清除。</param>
    void SetBadgeCount(int count)
    {
        // 默认空实现，平台实现可重写。
    }

    /// <summary>
    /// 设置任务栏徽章文本。
    /// 对应 Tauri v2 的 window.setBadgeLabel(label)。
    /// 默认实现为空操作，Windows 平台实现通过 ITaskbarList3.SetOverlayIcon 生成文本徽章。
    /// </summary>
    /// <param name="label">徽章文本，null 或空字符串表示清除。</param>
    void SetBadgeLabel(string? label)
    {
        // 默认空实现，平台实现可重写。
    }

    /// <summary>
    /// 设置窗口是否在所有工作区可见。
    /// 对应 Tauri v2 的 window.setVisibleOnAllWorkspaces(visible)。
    /// 默认实现为空操作，平台实现可重写。
    /// </summary>
    /// <param name="visible">true 表示在所有工作区可见。</param>
    void SetVisibleOnAllWorkspaces(bool visible)
    {
        // 默认空实现，平台实现可重写。
    }

    /// <summary>
    /// 设置窗口边框颜色。
    /// 对应 Tauri v2 的 window.setBorderColor(color)。
    /// 默认实现为空操作，Windows 11 平台实现通过 DwmSetWindowAttribute。
    /// </summary>
    /// <param name="color">十六进制颜色字符串（如 #FF0000），null 表示恢复默认。</param>
    void SetBorderColor(string? color)
    {
        // 默认空实现，平台实现可重写。
    }

    /// <summary>
    /// 设置是否启用文件拖放。
    /// 对应 Tauri v2 的 window.setFileDropEnabled(enabled)。
    /// 默认实现为空操作，平台实现可重写。
    /// </summary>
    /// <param name="enabled">true 表示启用文件拖放。</param>
    void SetFileDropEnabled(bool enabled)
    {
        // 默认空实现，平台实现可重写。
    }

    /// <summary>
    /// 注册原生 postMessage 接收回调（P0-C2）。
    /// <para>
    /// 平台实现应连接 WebView 的原生消息事件：
    /// <list type="bullet">
    /// <item>Windows: WebView2 <c>WebMessageReceived</c> 事件</item>
    /// <item>Linux: GirCore <c>UserContentManager.ScriptMessageReceived</c> 信号</item>
    /// <item>Android: <c>JavascriptInterface</c> 标记方法</item>
    /// </list>
    /// </para>
    /// <para>
    /// 注册后，前端通过 <c>window.chrome.webview.postMessage(jsonStr)</c>（Windows）或
    /// <c>window.webkit.messageHandlers.external.postMessage(msg)</c>（Linux）发送的消息
    /// 会路由到此回调，而非默认的 <c>Application.HandleMessageFromFrontend</c> 路径。
    /// </para>
    /// <para>
    /// 默认实现为空操作。未注册时，平台实现应保持原有行为（路由到 Application）。
    /// </para>
    /// </summary>
    /// <param name="callback">消息回调，参数为前端发送的原始字符串内容。</param>
    void SetNativeMessageHandler(Func<string, Task>? callback)
    {
        // 默认空实现，平台实现可重写。
    }

    /// <summary>
    /// 通过原生 postMessage 通道向前端推送消息（P0-C2）。
    /// <para>
    /// 平台实现应调用 WebView 原生 API：
    /// <list type="bullet">
    /// <item>Windows: <c>CoreWebView2.PostWebMessageAsString</c></item>
    /// <item>Linux: <c>EvaluateJavascript("window.__wailsNative.onMessage(...)")</c></item>
    /// <item>Android: <c>EvaluateJavascript("window.__wailsNative.onMessage(...)")</c></item>
    /// </list>
    /// </para>
    /// <para>
    /// 默认实现回退到 <see cref="ExecJS"/>，调用前端 <c>window.__wailsNative.onMessage</c> 处理函数。
    /// 平台实现重写以使用更高效的原生 API。
    /// </para>
    /// </summary>
    /// <param name="message">要推送的 JSON 字符串。</param>
    /// <returns>表示推送操作的异步任务。</returns>
    Task PostNativeMessageAsync(string message)
    {
        // 默认实现：通过 ExecJS 调用前端处理函数（兼容未实现原生 API 的平台）。
        // 使用 JSON.stringify 转义字符串，避免引号注入问题。
        ExecJS($"window.__wailsNative && window.__wailsNative.onMessage({System.Text.Json.JsonSerializer.Serialize(message)});");
        return Task.CompletedTask;
    }
}

/// <summary>
/// 任务栏进度条状态枚举。
/// 对应 Windows TBPF（Taskbar Button Progress Flags）和 Tauri v2 的进度状态。
/// </summary>
public enum TaskbarProgressState
{
    /// <summary>
    /// 无进度条（TBPF_NOPROGRESS）。
    /// </summary>
    None = 0,

    /// <summary>
    /// 不确定进度（TBPF_INDETERMINATE），显示滚动动画。
    /// </summary>
    Indeterminate = 1,

    /// <summary>
    /// 正常进度（TBPF_NORMAL），显示绿色进度条。
    /// </summary>
    Normal = 2,

    /// <summary>
    /// 错误进度（TBPF_ERROR），显示红色进度条。
    /// </summary>
    Error = 4,

    /// <summary>
    /// 暂停进度（TBPF_PAUSED），显示黄色进度条。
    /// </summary>
    Paused = 8,
}

/// <summary>
/// 窗口视觉特效类型。
/// 对应 Tauri v2 的 window.setEffects() 中的 effects 和 Wails v3 的窗口背景类型。
/// </summary>
public enum WindowEffect
{
    /// <summary>无特效（恢复正常窗口背景）。</summary>
    None = 0,

    /// <summary>
    /// Mica 特效（Windows 11 22000+），亚克力云母材质背景。
    /// 对应 DWMWA_SYSTEMBACKDROP_TYPE = DWMSBT_MAINWINDOW。
    /// </summary>
    Mica = 1,

    /// <summary>
    /// Acrylic 特效（Windows 11 22000+），亚克力模糊背景。
    /// 对应 DWMWA_SYSTEMBACKDROP_TYPE = DWMSBT_TRANSIENTWINDOW。
    /// </summary>
    Acrylic = 2,

    /// <summary>
    /// 模糊背景特效（Windows 7+，已过时但兼容）。
    /// 对应 DWMWA_SYSTEMBACKDROP_TYPE = DWMSBT_BLURBEHIND。
    /// </summary>
    BlurBehind = 3,

    /// <summary>
    /// Linux 透明背景（通过 GTK CSS 实现）。
    /// </summary>
    Transparent = 4,
}

/// <summary>
/// 窗口视觉特效参数。
/// 对应 Tauri v2 的 window.setEffects(effects) 参数。
/// </summary>
public sealed class WindowEffects
{
    /// <summary>
    /// 特效类型。
    /// </summary>
    public WindowEffect Effect { get; set; } = WindowEffect.None;

    /// <summary>
    /// 特效状态（true 表示应用特效，false 表示移除）。
    /// 对应 Tauri v2 的 effects.state。
    /// </summary>
    public bool State { get; set; } = true;

    /// <summary>
    /// 特效半径（用于模糊等效果），单位像素。
    /// 对应 Tauri v2 的 effects.radius。
    /// </summary>
    public int Radius { get; set; }

    /// <summary>
    /// 背景色（十六进制颜色字符串，如 #80000000 表示半透明黑）。
    /// 对应 Tauri v2 的 effects.color。
    /// </summary>
    public string? Color { get; set; }
}
