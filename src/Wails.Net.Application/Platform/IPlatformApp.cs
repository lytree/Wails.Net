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
    /// 启动平台应用主循环。
    /// </summary>
    void Run();

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
}
