using System.Collections.Concurrent;
using Wails.Net.Application;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Options;

namespace Wails.Net.Demo.MultiWindow.Services;

/// <summary>
/// 自定义窗口信息记录，包含 ID、名称、标题与尺寸。
/// 与内置 WindowsPlugin 的 WindowInfo（仅 Id/Name）互补，提供更丰富的窗口元数据。
/// </summary>
/// <param name="Id">窗口 ID。</param>
/// <param name="Name">窗口名称。</param>
/// <param name="Title">窗口标题。</param>
/// <param name="Width">窗口宽度。</param>
/// <param name="Height">窗口高度。</param>
public sealed record DemoWindowInfo(uint Id, string Name, string Title, int Width, int Height);

/// <summary>
/// 窗口管理服务，演示多窗口的创建、查询、聚焦与关闭。
/// 与内置 WindowPlugin / WindowsPlugin 配合使用：
/// <list type="bullet">
/// <item>WindowPlugin 提供单窗口操作命令（window.focus / window.close）</item>
/// <item>WindowsPlugin 提供窗口列表查询命令（windows.getAll）</item>
/// <item>本服务封装窗口创建与元数据维护，便于前端以绑定方法形式调用</item>
/// </list>
/// </summary>
public sealed class WindowManagerService
{
    /// <summary>
    /// 全局 Application 实例，由 Program.cs 在注册绑定时注入。
    /// </summary>
    private readonly Wails.Net.Application.Application _app;

    /// <summary>
    /// 窗口元数据字典（按窗口名记录标题），使用并发字典保证线程安全。
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _titles = new();

    /// <summary>
    /// 构造窗口管理服务实例。
    /// </summary>
    /// <param name="app">全局 Application 实例。</param>
    public WindowManagerService(Wails.Net.Application.Application app)
    {
        _app = app;
    }

    /// <summary>
    /// 创建子窗口并返回窗口 ID。
    /// </summary>
    /// <param name="name">窗口名称（唯一）。</param>
    /// <param name="title">窗口标题。</param>
    /// <returns>新创建窗口的 ID。</returns>
    [Binding]
    public uint CreateChildWindow(string name, string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        _titles[name] = title;

        var window = _app.CreateWebviewWindow(new WebviewWindowOptions
        {
            Name = name,
            Title = title,
            Width = 600,
            Height = 400,
        });

        return window.ID;
    }

    /// <summary>
    /// 获取所有窗口信息列表。
    /// </summary>
    /// <returns>窗口信息列表。</returns>
    [Binding]
    public List<DemoWindowInfo> GetAllWindows()
    {
        var result = new List<DemoWindowInfo>();
        foreach (var window in _app.Windows)
        {
            var (width, height) = window.GetSize();
            var title = _titles.TryGetValue(window.Name, out var t) ? t : window.Name;
            result.Add(new DemoWindowInfo(window.ID, window.Name, title, width, height));
        }
        return result;
    }

    /// <summary>
    /// 聚焦指定 ID 的窗口。
    /// </summary>
    /// <param name="id">窗口 ID。</param>
    [Binding]
    public void FocusWindow(uint id)
    {
        _app.GetWindow(id)?.Focus();
    }

    /// <summary>
    /// 关闭指定 ID 的窗口。
    /// </summary>
    /// <param name="id">窗口 ID。</param>
    [Binding]
    public void CloseWindow(uint id)
    {
        var window = _app.GetWindow(id);
        if (window is null) return;

        _titles.TryRemove(window.Name, out _);
        window.Close();
    }
}
