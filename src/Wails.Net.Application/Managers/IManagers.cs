using Wails.Net.Application.Dialogs;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Options;
using Wails.Net.Application.Screens;
using Wails.Net.Application.SystemTray;
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
    /// 注册窗口创建回调，在新窗口创建后触发。
    /// 对应 Wails v3 Go 版本 <c>WindowManager.OnCreate</c>。
    /// </summary>
    /// <param name="callback">窗口创建回调，参数为新创建的窗口实例。</param>
    /// <returns>取消订阅函数，调用后移除该回调。</returns>
    Action OnCreate(Action<WebviewWindow> callback);

    /// <summary>
    /// 销毁窗口。
    /// </summary>
    /// <param name="id">窗口 ID。</param>
    void DestroyWindow(uint id);

    /// <summary>
    /// 获取当前活动窗口。
    /// </summary>
    /// <returns>当前活动窗口，无则 null。</returns>
    WebviewWindow? GetActiveWindow();
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

    /// <summary>
    /// 注册上下文菜单（P1-4）。
    /// 将 <see cref="ContextMenu"/> 按字符串 ID 注册到全局表，供前端通过 ID 触发弹出。
    /// 对应 Wails v3 Go 版本 <c>application.go</c> 中的 <c>contextMenus</c> 字典。
    /// </summary>
    /// <param name="id">菜单 ID，由前端 CSS 变量 <c>--custom-contextmenu</c> 引用。</param>
    /// <param name="menu">要注册的上下文菜单实例。</param>
    void RegisterContextMenu(string id, ContextMenu menu);

    /// <summary>
    /// 根据 ID 获取已注册的上下文菜单（P1-4）。
    /// </summary>
    /// <param name="id">菜单 ID。</param>
    /// <returns>匹配的上下文菜单实例，若未注册则返回 null。</returns>
    ContextMenu? GetContextMenu(string id);

    /// <summary>
    /// 移除已注册的上下文菜单（P1-4）。
    /// </summary>
    /// <param name="id">要移除的菜单 ID。</param>
    void RemoveContextMenu(string id);
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

    /// <summary>
    /// 将 DIP 坐标点转换为物理像素坐标点。
    /// 对应 Wails v3 Go 版本 <c>ScreenManager.DipToPhysicalPoint</c>。
    /// </summary>
    /// <param name="dipPoint">DIP 坐标点。</param>
    /// <returns>物理像素坐标点；无屏幕时返回原值。</returns>
    Point DipToPhysicalPoint(Point dipPoint);

    /// <summary>
    /// 将物理像素坐标点转换为 DIP 坐标点。
    /// 对应 Wails v3 Go 版本 <c>ScreenManager.PhysicalToDipPoint</c>。
    /// </summary>
    /// <param name="physicalPoint">物理像素坐标点。</param>
    /// <returns>DIP 坐标点；无屏幕时返回原值。</returns>
    Point PhysicalToDipPoint(Point physicalPoint);

    /// <summary>
    /// 将 DIP 矩形转换为物理像素矩形。
    /// 对应 Wails v3 Go 版本 <c>ScreenManager.DipToPhysicalRect</c>。
    /// </summary>
    /// <param name="dipRect">DIP 矩形。</param>
    /// <returns>物理像素矩形；无屏幕时返回原值。</returns>
    Rect DipToPhysicalRect(Rect dipRect);

    /// <summary>
    /// 将物理像素矩形转换为 DIP 矩形。
    /// 对应 Wails v3 Go 版本 <c>ScreenManager.PhysicalToDipRect</c>。
    /// </summary>
    /// <param name="physicalRect">物理像素矩形。</param>
    /// <returns>DIP 矩形；无屏幕时返回原值。</returns>
    Rect PhysicalToDipRect(Rect physicalRect);

    /// <summary>
    /// 找到距离指定 DIP 点最近的屏幕（点在屏幕内则返回该屏幕，否则返回主屏幕）。
    /// 对应 Wails v3 Go 版本 <c>ScreenManager.ScreenNearestDipPoint</c>。
    /// </summary>
    /// <param name="dipPoint">DIP 坐标点。</param>
    /// <returns>最近的屏幕实例；无屏幕时返回 null。</returns>
    Screen? ScreenNearestDipPoint(Point dipPoint);

    /// <summary>
    /// 找到距离指定物理像素点最近的屏幕。
    /// 对应 Wails v3 Go 版本 <c>ScreenManager.ScreenNearestPhysicalPoint</c>。
    /// </summary>
    /// <param name="physicalPoint">物理像素坐标点。</param>
    /// <returns>最近的屏幕实例；无屏幕时返回 null。</returns>
    Screen? ScreenNearestPhysicalPoint(Point physicalPoint);

    /// <summary>
    /// 找到距离指定 DIP 矩形最近的屏幕。
    /// 对应 Wails v3 Go 版本 <c>ScreenManager.ScreenNearestDipRect</c>。
    /// </summary>
    /// <param name="dipRect">DIP 矩形。</param>
    /// <returns>最近的屏幕实例；无屏幕时返回 null。</returns>
    Screen? ScreenNearestDipRect(Rect dipRect);

    /// <summary>
    /// 找到距离指定物理像素矩形最近的屏幕。
    /// 对应 Wails v3 Go 版本 <c>ScreenManager.ScreenNearestPhysicalRect</c>。
    /// </summary>
    /// <param name="physicalRect">物理像素矩形。</param>
    /// <returns>最近的屏幕实例；无屏幕时返回 null。</returns>
    Screen? ScreenNearestPhysicalRect(Rect physicalRect);
}

/// <summary>
/// 系统托盘管理器接口。
/// 借鉴 Tauri v2 的 tray 插件设计：托盘是单实例资源，
/// 通过管理器统一管理其生命周期和属性。
/// </summary>
public interface ISystemTrayManager
{
    /// <summary>
    /// 创建系统托盘。
    /// </summary>
    /// <param name="icon">图标字节数据。</param>
    /// <returns>系统托盘平台实现实例。</returns>
    ISystemTrayImpl CreateSystemTray(byte[] icon);

    /// <summary>
    /// 销毁系统托盘。
    /// </summary>
    /// <param name="tray">系统托盘实例。</param>
    void DestroySystemTray(ISystemTrayImpl tray);

    /// <summary>
    /// 设置托盘图标。
    /// </summary>
    /// <param name="tray">托盘实例。</param>
    /// <param name="iconData">图标字节数据。</param>
    void SetIcon(ISystemTrayImpl tray, byte[]? iconData);

    /// <summary>
    /// 设置托盘标签（部分平台仅显示图标，标签可能不可见）。
    /// </summary>
    /// <param name="tray">托盘实例。</param>
    /// <param name="label">标签文本。</param>
    void SetLabel(ISystemTrayImpl tray, string label);

    /// <summary>
    /// 设置托盘右键菜单。
    /// </summary>
    /// <param name="tray">托盘实例。</param>
    /// <param name="menu">菜单实例，可为 null。</param>
    void SetMenu(ISystemTrayImpl tray, Menu? menu);

    /// <summary>
    /// 设置托盘提示文本。
    /// </summary>
    /// <param name="tray">托盘实例。</param>
    /// <param name="tooltip">提示文本。</param>
    void SetTooltip(ISystemTrayImpl tray, string tooltip);

    /// <summary>
    /// 显示托盘。
    /// </summary>
    /// <param name="tray">托盘实例。</param>
    void Show(ISystemTrayImpl tray);

    /// <summary>
    /// 隐藏托盘。
    /// </summary>
    /// <param name="tray">托盘实例。</param>
    void Hide(ISystemTrayImpl tray);

    /// <summary>
    /// 判断托盘是否可见。
    /// </summary>
    /// <param name="tray">托盘实例。</param>
    /// <returns>可见返回 true，否则 false。</returns>
    bool IsVisible(ISystemTrayImpl tray);
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

    /// <summary>
    /// 处理全局热键触发。
    /// 当平台层收到热键按下事件时调用此方法，由管理器分发到对应回调。
    /// </summary>
    /// <param name="hotkeyId">热键 ID。</param>
    void HandleHotKey(int hotkeyId);
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
