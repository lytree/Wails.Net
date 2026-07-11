using Wails.Net.Application.Dialogs;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Options;
using Wails.Net.Application.Screens;

namespace Wails.Net.Application.Platform.ServerMode;

/// <summary>
/// Server（无界面）模式下的平台应用桩实现。
/// 所有 GUI 操作均为空操作，适用于无头运行场景。
/// </summary>
public class ServerPlatformApp : IPlatformApp
{
    /// <summary>
    /// 应用名称。
    /// </summary>
    private readonly string _name;

    /// <summary>
    /// 用于阻塞主循环直到关闭信号到达的等待句柄。
    /// </summary>
    private readonly ManualResetEventSlim _shutdownEvent = new(initialState: false);

    /// <summary>
    /// 构造 ServerPlatformApp 实例。
    /// </summary>
    /// <param name="options">应用配置选项。</param>
    public ServerPlatformApp(ApplicationOptions options)
    {
        _name = options.Name;
    }

    /// <inheritdoc />
    public string Name => _name;

    /// <inheritdoc />
    public int Run()
    {
        // Server 模式下阻塞直到 SignalShutdown 被调用，模拟无 GUI 的运行-阻塞模型。
        _shutdownEvent.Wait();
        return 0;
    }

    /// <summary>
    /// 释放主循环阻塞，使 <see cref="Run"/> 返回。
    /// </summary>
    public void SignalShutdown()
    {
        _shutdownEvent.Set();
    }

    /// <inheritdoc />
    public bool AcquireSingleInstanceLock(string uniqueId)
    {
        // Server 模式下始终视作首实例。
        return true;
    }

    /// <inheritdoc />
    public void NotifySingleInstance(string[] args)
    {
        // Server 模式下不支持单实例通知。
    }

    /// <inheritdoc />
    public void Destroy()
    {
        // 释放主循环阻塞，确保 Run() 能够返回。
        SignalShutdown();
    }

    /// <inheritdoc />
    public void SetApplicationMenu(Menu? menu)
    {
        // Server 模式下不支持应用菜单。
    }

    /// <inheritdoc />
    public uint GetCurrentWindowId() => 0;

    /// <inheritdoc />
    public void ShowAboutDialog(string name, string description, byte[]? icon)
    {
        // Server 模式下不支持对话框。
    }

    /// <inheritdoc />
    public void SetIcon(byte[]? icon)
    {
        // Server 模式下不支持设置图标。
    }

    /// <inheritdoc />
    public void On(uint id)
    {
        // Server 模式下不处理平台事件。
    }

    /// <inheritdoc />
    public void DispatchOnMainThread(uint id)
    {
        // Server 模式下不分发事件到主线程。
    }

    /// <inheritdoc />
    public void Hide()
    {
        // Server 模式下不支持隐藏应用。
    }

    /// <inheritdoc />
    public void Show()
    {
        // Server 模式下不支持显示应用。
    }

    /// <inheritdoc />
    public Screen? GetPrimaryScreen() => null;

    /// <inheritdoc />
    public Screen[] GetScreens() => Array.Empty<Screen>();

    /// <inheritdoc />
    public Dictionary<string, object?> GetFlags(ApplicationOptions options) => new();

    /// <inheritdoc />
    public bool IsOnMainThread() => true;

    /// <inheritdoc />
    public bool IsDarkMode() => false;

    /// <inheritdoc />
    public string GetAccentColor() => "#000000";

    /// <inheritdoc />
    public void DispatchOnMainThread(Action action)
    {
        // Server 模式下同步执行操作。
        action();
    }

    /// <inheritdoc />
    public void CreateWebviewWindow(uint id, WebviewWindowOptions options)
    {
        // Server 模式下不支持创建窗口。
    }

    /// <inheritdoc />
    public Task<int> ShowMessageDialog(string title, string message, DialogStyle style, string[] buttons)
    {
        // Server 模式下返回默认按钮索引（第一个按钮）。
        return Task.FromResult(0);
    }

    /// <inheritdoc />
    public Task<string?> OpenFileDialog(OpenFileDialogOptions options)
    {
        // Server 模式下不支持文件对话框，返回 null。
        return Task.FromResult<string?>(null);
    }

    /// <inheritdoc />
    public Task<string?> SaveFileDialog(SaveFileDialogOptions options)
    {
        // Server 模式下不支持文件对话框，返回 null。
        return Task.FromResult<string?>(null);
    }

    /// <inheritdoc />
    public Task<string[]?> OpenMultipleFilesDialog(OpenFileDialogOptions options)
    {
        // Server 模式下不支持文件对话框，返回 null。
        return Task.FromResult<string[]?>(null);
    }
}
