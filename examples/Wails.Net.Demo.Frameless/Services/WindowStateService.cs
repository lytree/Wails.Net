using Wails.Net.Application.Bindings;

namespace Wails.Net.Demo.Frameless.Services;

/// <summary>
/// 窗口状态服务，演示无边框窗口下保存与查询窗口状态。
/// 与内置 WindowPlugin 配合使用：插件负责窗口原生操作（minimize/maximize/close），
/// 本服务负责持久化用户最后选择的窗口状态。
/// </summary>
public sealed class WindowStateService
{
    /// <summary>
    /// 当前保存的窗口状态，使用锁保证线程安全。
    /// </summary>
    private string _state = "normal";

    /// <summary>
    /// 状态字段锁。
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// 获取当前保存的窗口状态。
    /// </summary>
    /// <returns>窗口状态字符串：normal / maximized / minimized。</returns>
    [Binding]
    public string GetWindowState()
    {
        lock (_lock)
        {
            return _state;
        }
    }

    /// <summary>
    /// 保存窗口状态。前端在点击最小化/最大化/还原按钮后调用此方法。
    /// </summary>
    /// <param name="state">窗口状态字符串：normal / maximized / minimized。</param>
    [Binding]
    public void SaveState(string state)
    {
        lock (_lock)
        {
            _state = state;
        }
    }
}
