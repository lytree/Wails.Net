using Wails.Net.Application.Dialogs;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Options;
using Wails.Net.Application.Screens;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Managers;

/// <summary>
/// 窗口管理器接口。
/// </summary>
public interface IWindowManager
{
    /// <summary>
    /// 根据 ID 获取窗口。
    /// </summary>
    /// <param name="id">窗口 ID。</param>
    /// <returns>匹配的窗口实例，若不存在则返回 null。</returns>
    WebviewWindow? GetWindow(uint id);

    /// <summary>
    /// 根据名称获取窗口。
    /// </summary>
    /// <param name="name">窗口名称。</param>
    /// <returns>匹配的窗口实例，若不存在则返回 null。</returns>
    WebviewWindow? GetWindowByName(string name);

    /// <summary>
    /// 获取所有窗口。
    /// </summary>
    /// <returns>窗口只读列表。</returns>
    IReadOnlyList<WebviewWindow> GetAllWindows();

    /// <summary>
    /// 创建新窗口。
    /// </summary>
    /// <param name="options">窗口选项。</param>
    /// <returns>新创建窗口的 ID。</returns>
    uint CreateWebviewWindow(WebviewWindowOptions options);

    /// <summary>
    /// 销毁窗口。
    /// </summary>
    /// <param name="id">窗口 ID。</param>
    void DestroyWindow(uint id);
}

/// <summary>
/// 对话框管理器接口。
/// </summary>
public interface IDialogManager
{
    /// <summary>
    /// 显示消息对话框。
    /// </summary>
    /// <param name="title">对话框标题。</param>
    /// <param name="message">消息内容。</param>
    /// <param name="style">对话框样式。</param>
    /// <param name="buttons">按钮文本数组。</param>
    /// <returns>被点击按钮的索引。</returns>
    Task<int> ShowMessageDialog(string title, string message, DialogStyle style, string[] buttons);

    /// <summary>
    /// 打开文件对话框。
    /// </summary>
    /// <param name="options">打开文件对话框选项。</param>
    /// <returns>选中的文件路径，可为 null。</returns>
    Task<string?> OpenFileDialog(OpenFileDialogOptions options);

    /// <summary>
    /// 保存文件对话框。
    /// </summary>
    /// <param name="options">保存文件对话框选项。</param>
    /// <returns>保存的文件路径，可为 null。</returns>
    Task<string?> SaveFileDialog(SaveFileDialogOptions options);

    /// <summary>
    /// 打开多文件选择对话框。
    /// </summary>
    /// <param name="options">打开文件对话框选项。</param>
    /// <returns>选中的文件路径数组，可为 null。</returns>
    Task<string[]?> OpenMultipleFilesDialog(OpenFileDialogOptions options);
}

/// <summary>
/// 事件管理器接口。
/// </summary>
public interface IEventManager
{
    /// <summary>
    /// 发射事件。
    /// </summary>
    /// <param name="eventName">事件名称。</param>
    /// <param name="data">事件数据，可为 null。</param>
    void Emit(string eventName, object? data);

    /// <summary>
    /// 订阅事件，返回取消订阅函数。
    /// </summary>
    /// <param name="eventName">事件名称。</param>
    /// <param name="callback">事件回调。</param>
    /// <returns>取消订阅函数。</returns>
    Action On(string eventName, Action<object?> callback);

    /// <summary>
    /// 订阅一次。
    /// </summary>
    /// <param name="eventName">事件名称。</param>
    /// <param name="callback">事件回调。</param>
    /// <returns>取消订阅函数。</returns>
    Action Once(string eventName, Action<object?> callback);

    /// <summary>
    /// 订阅 N 次。
    /// </summary>
    /// <param name="eventName">事件名称。</param>
    /// <param name="callback">事件回调。</param>
    /// <param name="count">订阅次数。</param>
    /// <returns>取消订阅函数。</returns>
    Action OnMultiple(string eventName, Action<object?> callback, int count);
}

/// <summary>
/// 剪贴板管理器接口。
/// </summary>
public interface IClipboardManager
{
    /// <summary>
    /// 设置文本。
    /// </summary>
    /// <param name="text">文本内容。</param>
    void SetText(string text);

    /// <summary>
    /// 获取文本。
    /// </summary>
    /// <returns>剪贴板中的文本。</returns>
    string GetText();

    /// <summary>
    /// 设置 HTML 内容。
    /// </summary>
    /// <param name="html">HTML 内容。</param>
    /// <param name="fallbackText">回退文本。</param>
    void SetHTML(string html, string fallbackText);

    /// <summary>
    /// 获取 HTML 内容。
    /// </summary>
    /// <returns>剪贴板中的 HTML 内容。</returns>
    string GetHTML();

    /// <summary>
    /// 设置图片。
    /// </summary>
    /// <param name="imageData">图片字节数据。</param>
    void SetImage(byte[] imageData);

    /// <summary>
    /// 获取图片。
    /// </summary>
    /// <returns>图片字节数据，可为 null。</returns>
    byte[]? GetImage();

    /// <summary>
    /// 清空剪贴板。
    /// </summary>
    void Clear();
}

/// <summary>
/// 菜单管理器接口。
/// </summary>
public interface IMenuManager
{
    /// <summary>
    /// 设置应用菜单。
    /// </summary>
    /// <param name="menu">菜单实例，可为 null。</param>
    void SetApplicationMenu(Menu? menu);

    /// <summary>
    /// 获取应用菜单。
    /// </summary>
    /// <returns>菜单实例，可为 null。</returns>
    Menu? GetApplicationMenu();
}

/// <summary>
/// 屏幕管理器接口。
/// </summary>
public interface IScreenManager
{
    /// <summary>
    /// 获取主屏幕。
    /// </summary>
    /// <returns>主屏幕实例，可为 null。</returns>
    Screen? GetPrimaryScreen();

    /// <summary>
    /// 获取所有屏幕。
    /// </summary>
    /// <returns>屏幕数组。</returns>
    Screen[] GetAllScreens();
}

/// <summary>
/// 系统托盘管理器接口。
/// </summary>
public interface ISystemTrayManager
{
    /// <summary>
    /// 创建系统托盘（暂用 object，实际实现后替换）。
    /// </summary>
    /// <param name="icon">图标字节数据。</param>
    /// <returns>系统托盘实例。</returns>
    object CreateSystemTray(byte[] icon);

    /// <summary>
    /// 销毁系统托盘。
    /// </summary>
    /// <param name="tray">系统托盘实例。</param>
    void DestroySystemTray(object tray);
}

/// <summary>
/// 快捷键绑定管理器接口。
/// </summary>
public interface IKeyBindingManager
{
    /// <summary>
    /// 注册快捷键绑定。
    /// </summary>
    /// <param name="accelerator">快捷键描述。</param>
    /// <param name="callback">触发回调。</param>
    void RegisterKeyBinding(string accelerator, Action callback);

    /// <summary>
    /// 注销快捷键绑定。
    /// </summary>
    /// <param name="accelerator">快捷键描述。</param>
    void UnregisterKeyBinding(string accelerator);
}

/// <summary>
/// 浏览器管理器接口。
/// </summary>
public interface IBrowserManager
{
    /// <summary>
    /// 打开 URL。
    /// </summary>
    /// <param name="url">目标 URL。</param>
    void OpenURL(string url);

    /// <summary>
    /// 在默认浏览器中打开 URL。
    /// </summary>
    /// <param name="url">目标 URL。</param>
    void OpenURLInDefaultBrowser(string url);
}

/// <summary>
/// 自启动管理器接口。
/// </summary>
public interface IAutostartManager
{
    /// <summary>
    /// 是否已启用自启动。
    /// </summary>
    /// <returns>如果已启用则返回 true，否则返回 false。</returns>
    bool IsEnabled();

    /// <summary>
    /// 启用自启动。
    /// </summary>
    void Enable();

    /// <summary>
    /// 禁用自启动。
    /// </summary>
    void Disable();
}

/// <summary>
/// 环境信息管理器接口。
/// </summary>
public interface IEnvironmentManager
{
    /// <summary>
    /// 返回操作系统名称（"windows" 或 "linux"）。
    /// </summary>
    /// <returns>操作系统名称。</returns>
    string GetOS();

    /// <summary>
    /// 返回系统架构（"amd64" 或 "arm64" 等）。
    /// </summary>
    /// <returns>系统架构字符串。</returns>
    string GetArch();

    /// <summary>
    /// 获取用户主目录。
    /// </summary>
    /// <returns>主目录路径。</returns>
    string GetHomeDir();

    /// <summary>
    /// 获取应用数据目录。
    /// </summary>
    /// <returns>数据目录路径。</returns>
    string GetDataDir();
}
