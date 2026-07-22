using Wails.Net.Application.Menus;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Platform.ServerMode;

/// <summary>
/// Server 模式下的 Webview 窗口桩实现。
/// 所有窗口操作均为空操作，适用于无头运行场景。
/// </summary>
public class ServerWebviewWindow : IWebviewWindowImpl
{
    /// <summary>
    /// 窗口 ID。
    /// </summary>
    private readonly uint _id;

    /// <summary>
    /// 构造 ServerWebviewWindow 实例。
    /// </summary>
    /// <param name="id">窗口 ID。</param>
    public ServerWebviewWindow(uint id = 0)
    {
        _id = id;
    }

    /// <inheritdoc />
    public uint Id => _id;

    /// <inheritdoc />
    public void SetTitle(string title)
    {
        // Server 模式下不支持设置标题。
    }

    /// <inheritdoc />
    public void SetSize(int width, int height)
    {
        // Server 模式下不支持设置大小。
    }

    /// <inheritdoc />
    public void SetMinSize(int width, int height)
    {
        // Server 模式下不支持设置最小尺寸。
    }

    /// <inheritdoc />
    public void SetMaxSize(int width, int height)
    {
        // Server 模式下不支持设置最大尺寸。
    }

    /// <inheritdoc />
    public void SetPosition(int x, int y)
    {
        // Server 模式下不支持设置位置。
    }

    /// <inheritdoc />
    public void Show()
    {
        // Server 模式下不支持显示窗口。
    }

    /// <inheritdoc />
    public void Hide()
    {
        // Server 模式下不支持隐藏窗口。
    }

    /// <inheritdoc />
    public void Maximise()
    {
        // Server 模式下不支持最大化。
    }

    /// <inheritdoc />
    public void UnMaximise()
    {
        // Server 模式下不支持取消最大化。
    }

    /// <inheritdoc />
    public void Minimise()
    {
        // Server 模式下不支持最小化。
    }

    /// <inheritdoc />
    public void UnMinimise()
    {
        // Server 模式下不支持取消最小化。
    }

    /// <inheritdoc />
    public void Fullscreen()
    {
        // Server 模式下不支持全屏。
    }

    /// <inheritdoc />
    public void UnFullscreen()
    {
        // Server 模式下不支持退出全屏。
    }

    /// <inheritdoc />
    public void Restore()
    {
        // Server 模式下不支持恢复窗口状态。
    }

    /// <inheritdoc />
    public void Close()
    {
        // Server 模式下不支持关闭窗口。
    }

    /// <inheritdoc />
    public void Focus()
    {
        // Server 模式下不支持聚焦窗口。
    }

    /// <inheritdoc />
    public void ShowMenuBar()
    {
        // Server 模式下不支持显示菜单栏。
    }

    /// <inheritdoc />
    public void HideMenuBar()
    {
        // Server 模式下不支持隐藏菜单栏。
    }

    /// <inheritdoc />
    public void ToggleMenuBar()
    {
        // Server 模式下不支持切换菜单栏。
    }

    /// <inheritdoc />
    public void SetAlwaysOnTop(bool onTop)
    {
        // Server 模式下不支持设置置顶。
    }

    /// <inheritdoc />
    public void SetBackgroundColour(byte r, byte g, byte b, byte a)
    {
        // Server 模式下不支持设置背景色。
    }

    /// <inheritdoc />
    public void SetBackgroundColour(int r, int g, int b, int a)
    {
        // Server 模式下不支持设置背景色。
    }

    /// <inheritdoc />
    public bool IsFullscreen() => false;

    /// <inheritdoc />
    public bool IsMaximised() => false;

    /// <inheritdoc />
    public bool IsMinimised() => false;

    /// <inheritdoc />
    public bool IsVisible() => false;

    /// <inheritdoc />
    public bool IsFocused() => false;

    /// <inheritdoc />
    public void SetFrameless(bool frameless)
    {
        // Server 模式下不支持设置无边框。
    }

    /// <inheritdoc />
    public void OpenDevTools()
    {
        // Server 模式下不支持开发者工具。
    }

    /// <inheritdoc />
    public void CloseDevTools()
    {
        // Server 模式下不支持开发者工具。
    }

    /// <inheritdoc />
    public void SetZoom(float zoom)
    {
        // Server 模式下不支持设置缩放。
    }

    /// <inheritdoc />
    public void SetZoomLevel(float level)
    {
        // Server 模式下不支持设置缩放级别。
    }

    /// <inheritdoc />
    public (int Width, int Height) GetSize() => (0, 0);

    /// <inheritdoc />
    public (int Width, int Height) GetContentSize() => (0, 0);

    /// <inheritdoc />
    public (int Width, int Height) GetMinSize() => (0, 0);

    /// <inheritdoc />
    public (int Width, int Height) GetMaxSize() => (0, 0);

    /// <inheritdoc />
    public (int X, int Y) GetPosition() => (0, 0);

    /// <inheritdoc />
    public float GetZoom() => 0f;

    /// <inheritdoc />
    public float GetZoomLevel() => 0f;

    /// <inheritdoc />
    public void ExecJS(string js)
    {
        // Server 模式下无 webview，不执行 JavaScript。
    }

    /// <inheritdoc />
    public void GoBack()
    {
        // Server 模式下不支持导航。
    }

    /// <inheritdoc />
    public void GoForward()
    {
        // Server 模式下不支持导航。
    }

    /// <inheritdoc />
    public void Reload()
    {
        // Server 模式下不支持重新加载。
    }

    /// <inheritdoc />
    public void SetURL(string url)
    {
        // Server 模式下不支持设置 URL。
    }

    /// <inheritdoc />
    public void SetHTML(string html)
    {
        // Server 模式下不支持设置 HTML。
    }

    /// <inheritdoc />
    public void Print()
    {
        // Server 模式下不支持打印。
    }

    /// <inheritdoc />
    public void PrintToPDF(string path)
    {
        // Server 模式下不支持导出 PDF。
    }

    /// <inheritdoc />
    public void SetMenu(Menu? menu)
    {
        // Server 模式下不支持设置窗口菜单。
    }

    /// <inheritdoc />
    public void StartDrag()
    {
        // Server 模式下不支持拖动窗口。
    }

    /// <inheritdoc />
    public void StartResize()
    {
        // Server 模式下不支持调整窗口大小。
    }

    /// <inheritdoc />
    public void SetEnabled(bool enabled)
    {
        // Server 模式下不支持设置启用状态。
    }

    /// <inheritdoc />
    public void SetContentProtection(bool enabled)
    {
        // Server 模式下不支持内容保护。
    }

    /// <inheritdoc />
    public void AttachAsModal(uint parentWindowId)
    {
        // Server 模式不支持模态窗口。
    }

    /// <inheritdoc />
    public void SetResizable(bool resizable)
    {
        // Server 模式下不支持设置可调整大小。
    }

    /// <inheritdoc />
    public void SetMaximisable(bool maximisable)
    {
        // Server 模式下不支持设置可最大化。
    }

    /// <inheritdoc />
    public void SetMinimisable(bool minimisable)
    {
        // Server 模式下不支持设置可最小化。
    }

    /// <inheritdoc />
    public void SetClosable(bool closable)
    {
        // Server 模式下不支持设置可关闭。
    }

    /// <inheritdoc />
    public void SetHasShadow(bool hasShadow)
    {
        // Server 模式下不支持设置阴影。
    }

    /// <inheritdoc />
    public void SetTitleBarStyle(TitleBarStyle style)
    {
        // Server 模式下不支持设置标题栏样式。
    }

    /// <inheritdoc />
    public void Centre()
    {
        // Server 模式下不支持居中窗口。
    }

    /// <inheritdoc />
    public void SetDebuggingEnabled(bool enabled)
    {
        // Server 模式下不支持调试模式。
    }

    /// <inheritdoc />
    public string GetURL() => "";

    /// <inheritdoc />
    public void LoadURL(string url)
    {
        // Server 模式下不支持加载 URL。
    }

    /// <inheritdoc />
    public void LoadHTML(string html)
    {
        // Server 模式下不支持加载 HTML。
    }
}
