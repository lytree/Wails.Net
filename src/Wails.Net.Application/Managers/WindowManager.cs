using System.Collections.Concurrent;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Windows;
using Wails.Net.Events;

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
    /// 窗口创建事件，在新窗口创建后触发。
    /// 用于让外部组件（如日志桥接器）订阅新窗口的生命周期事件。
    /// </summary>
    public event Action<WebviewWindow>? WindowCreated;

    /// <summary>
    /// 注册窗口创建回调，在新窗口创建后触发。
    /// 对应 Wails v3 Go 版本 <c>WindowManager.OnCreate</c>。
    /// 内部委托给 <see cref="WindowCreated"/> 事件，返回的取消订阅函数可用于移除回调。
    /// </summary>
    /// <param name="callback">窗口创建回调，参数为新创建的窗口实例。</param>
    /// <returns>取消订阅函数，调用后移除该回调。</returns>
    public Action OnCreate(Action<WebviewWindow> callback)
    {
        WindowCreated += callback;
        return () => WindowCreated -= callback;
    }

    /// <summary>
    /// 创建新窗口，并注册窗口关闭事件监听器以自动清理窗口列表。
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

        // P0-4：注册窗口级 CSP 到 AssetServer（通过 Application 静态引用）
        Application.Get()?.RegisterWindowCsp(options.Name, options.Csp);

        // 注册窗口关闭事件监听器，当窗口发出 WindowClosed 事件时自动从管理器中移除
        window.On((uint)WindowEventType.WindowClosed, () =>
        {
            if (_windows.TryRemove(id, out _))
            {
                if (!string.IsNullOrEmpty(window.Name))
                {
                    _windowNames.TryRemove(window.Name, out _);
                    // P0-4：清理窗口级 CSP 注册，避免内存泄漏
                    Application.Get()?.AssetServer?.SetCspHeaderForWindow(window.Name, null);
                }
            }
        });

        // 通知平台应用创建窗口
        _platformApp?.CreateWebviewWindow(id, options);

        // P1-3-4：触发窗口创建事件，让外部组件可以订阅新窗口的控制台消息等
        WindowCreated?.Invoke(window);

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
    /// 获取当前活动窗口。
    /// 若平台应用支持获取当前窗口 ID，则返回对应窗口；否则返回 null。
    /// </summary>
    /// <returns>当前活动窗口，无则 null。</returns>
    public WebviewWindow? GetActiveWindow()
    {
        if (_platformApp is null)
        {
            return _windows.Values.FirstOrDefault();
        }

        var currentId = _platformApp.GetCurrentWindowId();
        return GetWindow(currentId);
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
