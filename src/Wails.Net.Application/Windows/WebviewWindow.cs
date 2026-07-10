using Wails.Net.Application.Menus;
using Wails.Net.Application.Options;

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
}
