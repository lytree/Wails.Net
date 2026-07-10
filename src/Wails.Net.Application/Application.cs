using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Services;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application;

/// <summary>
/// 核心应用类，对应 Wails v3 中的 application.go。
/// </summary>
public class Application
{
    private readonly ApplicationOptions _options;
    private IPlatformApp? _platformApp = null;
    private readonly ServiceRegistry _serviceRegistry = new();
    private uint _nextWindowId = 1;
    private readonly Dictionary<uint, WebviewWindow> _windows = new();

    private static Application? _globalApplication;

    /// <summary>
    /// 获取应用选项。
    /// </summary>
    public ApplicationOptions Options => _options;

    /// <summary>
    /// 获取平台应用实例。
    /// </summary>
    public IPlatformApp? PlatformApp => _platformApp;

    /// <summary>
    /// 获取服务注册表。
    /// </summary>
    public ServiceRegistry Services => _serviceRegistry;

    /// <summary>
    /// 获取所有窗口的只读列表。
    /// </summary>
    public IReadOnlyList<WebviewWindow> Windows => _windows.Values.ToList().AsReadOnly();

    /// <summary>
    /// 获取全局应用实例。
    /// </summary>
    /// <returns>全局应用实例，若未创建则返回 null。</returns>
    public static Application? Get() => _globalApplication;

    /// <summary>
    /// 使用指定选项构造应用实例。
    /// </summary>
    /// <param name="options">应用选项。</param>
    public Application(ApplicationOptions options)
    {
        _options = options;
        _globalApplication = this;
    }

    /// <summary>
    /// 注册服务。
    /// </summary>
    /// <param name="service">要注册的服务实例。</param>
    public virtual void RegisterService(object service)
    {
        _serviceRegistry.Register(service);
    }

    /// <summary>
    /// 创建 Webview 窗口，使用自动递增的 ID。
    /// </summary>
    /// <param name="options">窗口选项。</param>
    /// <returns>新创建的窗口实例。</returns>
    public virtual WebviewWindow CreateWebviewWindow(WebviewWindowOptions options)
    {
        var id = _nextWindowId++;
        var window = new WebviewWindow(id, options.Name, options);
        _windows[id] = window;
        return window;
    }

    /// <summary>
    /// 根据 ID 获取窗口。
    /// </summary>
    /// <param name="id">窗口 ID。</param>
    /// <returns>匹配的窗口实例，若不存在则返回 null。</returns>
    public virtual WebviewWindow? GetWindow(uint id)
    {
        return _windows.TryGetValue(id, out var window) ? window : null;
    }

    /// <summary>
    /// 根据名称获取窗口。
    /// </summary>
    /// <param name="name">窗口名称。</param>
    /// <returns>匹配的窗口实例，若不存在则返回 null。</returns>
    public virtual WebviewWindow? GetWindowByName(string name)
    {
        return _windows.Values.FirstOrDefault(w => w.Name == name);
    }

    /// <summary>
    /// 销毁窗口，从窗口字典中移除并关闭其平台实现。
    /// </summary>
    /// <param name="id">窗口 ID。</param>
    public virtual void DestroyWindow(uint id)
    {
        if (_windows.TryGetValue(id, out var window))
        {
            _windows.Remove(id);
            window.Close();
        }
    }

    /// <summary>
    /// 运行应用主循环。
    /// </summary>
    public virtual void Run()
    {
        throw new NotImplementedException("Run() 尚未实现，将在阶段 3 实现");
    }

    /// <summary>
    /// 关闭应用。
    /// </summary>
    public virtual void Shutdown()
    {
        throw new NotImplementedException("Shutdown() 尚未实现");
    }

    /// <summary>
    /// 退出应用，触发关闭流程。
    /// </summary>
    public virtual void Quit()
    {
        Shutdown();
    }

    /// <summary>
    /// 在主线程上分发执行指定操作。
    /// 若平台应用存在则委托给平台应用，否则直接执行。
    /// </summary>
    /// <param name="action">要执行的操作。</param>
    public virtual void DispatchOnMainThread(Action action)
    {
        if (_platformApp is not null)
        {
            _platformApp.DispatchOnMainThread(action);
        }
        else
        {
            action();
        }
    }
}
