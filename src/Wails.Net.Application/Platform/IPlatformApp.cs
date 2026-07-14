using Wails.Net.Application.Dialogs;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Options;
using Wails.Net.Application.Screens;

namespace Wails.Net.Application.Platform;

/// <summary>
/// 平台特定的应用接口，对应 Go 版 application.go 中的 platformApp。
/// </summary>
public interface IPlatformApp
{
    /// <summary>
    /// 获取应用名称。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 启动平台应用主循环，阻塞直到主循环退出。
    /// </summary>
    /// <returns>进程退出码。</returns>
    int Run();

    /// <summary>
    /// 尝试获取单实例锁。返回 true 表示当前进程为首实例。
    /// 对应 Wails v3 Go 版本 application.go 中 SingleInstanceLock 的协调入口。
    /// </summary>
    /// <param name="uniqueId">用于标识应用的唯一 ID（通常为应用名称）。</param>
    /// <returns>成功获取锁（首实例）返回 true，否则返回 false。</returns>
    bool AcquireSingleInstanceLock(string uniqueId) => true;

    /// <summary>
    /// 通知已运行的前一个实例：有新的实例尝试启动（携带命令行参数）。
    /// </summary>
    /// <param name="args">新实例启动时传入的命令行参数。</param>
    void NotifySingleInstance(string[] args)
    {
        // 默认空实现，由平台特定实现覆盖。
    }

    /// <summary>
    /// 销毁平台应用。
    /// </summary>
    void Destroy();

    /// <summary>
    /// 设置应用菜单。
    /// </summary>
    /// <param name="menu">应用菜单实例，可为 null。</param>
    void SetApplicationMenu(Menu? menu);

    /// <summary>
    /// 获取当前活动窗口 ID。
    /// </summary>
    /// <returns>当前活动窗口的 ID。</returns>
    uint GetCurrentWindowId();

    /// <summary>
    /// 将应用主窗口的父窗口设置为指定平台原生句柄。
    /// 主要用于将应用嵌入到外部宿主进程（如插件宿主、IDE 面板等）。
    /// 默认实现为 no-op，由支持的平台（如 Windows）覆盖。
    /// 对应 Wails v3 Go 版本 application.go 中的 SetParent 方法。
    /// </summary>
    /// <param name="parent">父窗口的平台原生句柄（如 Win32 HWND）。</param>
    void SetParent(IntPtr parent)
    {
        // 默认空实现，由平台特定实现覆盖。
    }

    /// <summary>
    /// 显示关于对话框。
    /// </summary>
    /// <param name="name">应用名称。</param>
    /// <param name="description">应用描述。</param>
    /// <param name="icon">应用图标字节数据，可为 null。</param>
    void ShowAboutDialog(string name, string description, byte[]? icon);

    /// <summary>
    /// 设置应用图标。
    /// </summary>
    /// <param name="icon">图标字节数据，可为 null。</param>
    void SetIcon(byte[]? icon);

    /// <summary>
    /// 处理平台事件。
    /// </summary>
    /// <param name="id">事件 ID。</param>
    void On(uint id);

    /// <summary>
    /// 在主线程分发事件。
    /// </summary>
    /// <param name="id">事件 ID。</param>
    void DispatchOnMainThread(uint id);

    /// <summary>
    /// 隐藏应用。
    /// </summary>
    void Hide();

    /// <summary>
    /// 显示应用。
    /// </summary>
    void Show();

    /// <summary>
    /// 获取主屏幕信息。
    /// </summary>
    /// <returns>主屏幕实例，可为 null。</returns>
    Screen? GetPrimaryScreen();

    /// <summary>
    /// 获取所有屏幕信息。
    /// </summary>
    /// <returns>屏幕数组。</returns>
    Screen[] GetScreens();

    /// <summary>
    /// 获取平台标志。
    /// </summary>
    /// <param name="options">应用选项。</param>
    /// <returns>平台标志字典。</returns>
    Dictionary<string, object?> GetFlags(ApplicationOptions options);

    /// <summary>
    /// 是否在主线程。
    /// </summary>
    /// <returns>如果在主线程则返回 true，否则返回 false。</returns>
    bool IsOnMainThread();

    /// <summary>
    /// 是否暗色模式。
    /// </summary>
    /// <returns>如果为暗色模式则返回 true，否则返回 false。</returns>
    bool IsDarkMode();

    /// <summary>
    /// 获取系统强调色。
    /// </summary>
    /// <returns>系统强调色字符串。</returns>
    string GetAccentColor();

    /// <summary>
    /// 在主线程执行操作。
    /// </summary>
    /// <param name="action">要执行的操作。</param>
    void DispatchOnMainThread(Action action);

    /// <summary>
    /// 创建平台特定的 Webview 窗口。
    /// </summary>
    /// <param name="id">窗口 ID。</param>
    /// <param name="options">窗口选项。</param>
    void CreateWebviewWindow(uint id, WebviewWindowOptions options);

    /// <summary>
    /// 异步显示消息对话框。
    /// </summary>
    /// <param name="title">对话框标题。</param>
    /// <param name="message">消息内容。</param>
    /// <param name="style">对话框样式。</param>
    /// <param name="buttons">按钮文本数组。</param>
    /// <returns>被点击按钮的索引。</returns>
    Task<int> ShowMessageDialog(string title, string message, DialogStyle style, string[] buttons);

    /// <summary>
    /// 异步打开文件对话框。
    /// </summary>
    /// <param name="options">打开文件对话框选项。</param>
    /// <returns>选中的文件路径，可为 null。</returns>
    Task<string?> OpenFileDialog(OpenFileDialogOptions options);

    /// <summary>
    /// 异步保存文件对话框。
    /// </summary>
    /// <param name="options">保存文件对话框选项。</param>
    /// <returns>保存的文件路径，可为 null。</returns>
    Task<string?> SaveFileDialog(SaveFileDialogOptions options);

    /// <summary>
    /// 异步打开多文件选择对话框。
    /// </summary>
    /// <param name="options">打开文件对话框选项。</param>
    /// <returns>选中的文件路径数组，可为 null。</returns>
    Task<string[]?> OpenMultipleFilesDialog(OpenFileDialogOptions options);
}
