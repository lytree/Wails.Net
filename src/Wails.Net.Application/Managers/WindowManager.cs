using System.Collections.Concurrent;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Managers;

/// <summary>
/// 窗口管理器，负责窗口的创建、销毁和查询。
/// 对应 Wails v3 Go 版本中的 windowManager。
/// </summary>
public class WindowManager : IWindowManager
{
    /// <summary>
    /// 窗口字典，按 ID 索引。
    /// </summary>
    private readonly ConcurrentDictionary<uint, WebviewWindow> _windows = new();

    /// <summary>
    /// 窗口名称字典，按名称索引（用于按名称查找）。
    /// </summary>
    private readonly ConcurrentDictionary<string, uint> _windowNames = new();

    /// <summary>
    /// 下一个窗口 ID（线程安全递增）。
    /// </summary>
    private uint _nextWindowId = 1;

    /// <summary>
    /// 平台应用实例。
    /// </summary>
    private readonly IPlatformApp? _platformApp;

    /// <summary>
    /// 使用指定的平台应用构造 WindowManager 实例。
    /// </summary>
    /// <param name="platformApp">平台应用实例，可为 null（Server 模式）。</param>
    public WindowManager(IPlatformApp? platformApp)
    {
        _platformApp = platformApp;
    }

    /// <summary>
    /// 获取所有窗口的只读列表。
    /// </summary>
    public IReadOnlyList<WebviewWindow> AllWindows => _windows.Values.ToList().AsReadOnly();

    /// <summary>
    /// 获取当前窗口数量。
    /// </summary>
    public int Count => _windows.Count;

    /// <summary>
    /// 创建新窗口。
    /// </summary>
    /// <param name="options">窗口选项。</param>
    /// <returns>新创建窗口的 ID。</returns>
    public uint CreateWebviewWindow(WebviewWindowOptions options)
    {
        var id = Interlocked.Increment(ref _nextWindowId) - 1;
        var window = new WebviewWindow(id, options.Name, options);

        _windows[id] = window;
        if (!string.IsNullOrEmpty(options.Name))
        {
            _windowNames[options.Name] = id;
        }

        // 通知平台应用创建窗口
        _platformApp?.CreateWebviewWindow(id, options);

        return id;
    }

    /// <summary>
    /// 根据 ID 获取窗口。
    /// </summary>
    /// <param name="id">窗口 ID。</param>
    /// <returns>匹配的窗口实例，若不存在则返回 null。</returns>
    public WebviewWindow? GetWindow(uint id)
    {
        return _windows.TryGetValue(id, out var window) ? window : null;
    }

    /// <summary>
    /// 根据名称获取窗口。
    /// </summary>
    /// <param name="name">窗口名称。</param>
    /// <returns>匹配的窗口实例，若不存在则返回 null。</returns>
    public WebviewWindow? GetWindowByName(string name)
    {
        if (_windowNames.TryGetValue(name, out var id))
        {
            return GetWindow(id);
        }
        return null;
    }

    /// <summary>
    /// 获取所有窗口。
    /// </summary>
    /// <returns>窗口只读列表。</returns>
    public IReadOnlyList<WebviewWindow> GetAllWindows()
    {
        return _windows.Values.ToList().AsReadOnly();
    }

    /// <summary>
    /// 销毁窗口。
    /// </summary>
    /// <param name="id">窗口 ID。</param>
    public void DestroyWindow(uint id)
    {
        if (_windows.TryRemove(id, out var window))
        {
            if (!string.IsNullOrEmpty(window.Name))
            {
                _windowNames.TryRemove(window.Name, out _);
            }

            try
            {
                window.Close();
            }
            catch
            {
                // 关闭窗口时的异常不应中断管理器操作
            }
        }
    }

    /// <summary>
    /// 清除所有窗口。
    /// </summary>
    public void Clear()
    {
        foreach (var id in _windows.Keys)
        {
            DestroyWindow(id);
        }
    }
}
