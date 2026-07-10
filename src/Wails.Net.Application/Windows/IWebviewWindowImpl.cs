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
}
