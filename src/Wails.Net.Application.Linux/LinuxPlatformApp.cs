using Wails.Net.Application.Dialogs;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Screens;

namespace Wails.Net.Application.Platform;

/// <summary>
/// Linux 平台应用实现，对应 Go 版 application.go 中的 Linux platformApp。
/// 通过 GTK4 环境变量读取系统主题，GTK 主循环与窗口管理将在后续阶段实现。
/// </summary>
public sealed class LinuxPlatformApp : IPlatformApp
{
    /// <summary>
    /// 应用名称。
    /// </summary>
    private readonly string _name;

    /// <summary>
    /// 主线程 ID，用于 IsOnMainThread 判断。
    /// </summary>
    private readonly int _mainThreadId;

    /// <summary>
    /// 构造 LinuxPlatformApp 实例。
    /// </summary>
    /// <param name="options">应用配置选项。</param>
    public LinuxPlatformApp(ApplicationOptions options)
    {
        _name = options.Name;
        _mainThreadId = Environment.CurrentManagedThreadId;
    }

    /// <inheritdoc />
    public string Name => _name;

    /// <inheritdoc />
    public bool IsOnMainThread()
    {
        return Environment.CurrentManagedThreadId == _mainThreadId;
    }

    /// <inheritdoc />
    public bool IsDarkMode()
    {
        // 读取 GTK_THEME 环境变量，若包含 "dark" 则为暗色模式。
        var gtkTheme = Environment.GetEnvironmentVariable("GTK_THEME");
        if (gtkTheme is not null && gtkTheme.Contains("dark", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 读取 COLOR_SCHEME 环境变量（freedesktop portal 约定），值为 "dark" 时为暗色模式。
        var colorScheme = Environment.GetEnvironmentVariable("COLOR_SCHEME");
        if (string.Equals(colorScheme, "dark", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 默认返回亮色模式。
        return false;
    }

    /// <inheritdoc />
    public string GetAccentColor()
    {
        // TODO: 将在后续通过 D-Bus 读取 GNOME 强调色设置
        // 当前返回默认蓝色，因为从 GTK 读取强调色需要 D-Bus 调用，在无头测试环境中不可用。
        return "#0078D4";
    }

    /// <inheritdoc />
    public Screen? GetPrimaryScreen()
    {
        // TODO: 将在后续实现完整的屏幕信息获取
        return null;
    }

    /// <inheritdoc />
    public Screen[] GetScreens()
    {
        // TODO: 将在后续实现完整的屏幕信息获取
        return Array.Empty<Screen>();
    }

    /// <inheritdoc />
    public Dictionary<string, object?> GetFlags(ApplicationOptions options)
    {
        return new Dictionary<string, object?>();
    }

    /// <inheritdoc />
    public void Run()
    {
        // TODO: 将在后续实现完整的 GTK 主循环
        throw new NotImplementedException("GTK 主循环将在后续实现");
    }

    /// <inheritdoc />
    public void Destroy()
    {
        // TODO: 将在后续实现资源清理
    }

    /// <inheritdoc />
    public void SetApplicationMenu(Menu? menu)
    {
        // TODO: 将在后续实现完整的 GTK 集成
    }

    /// <inheritdoc />
    public uint GetCurrentWindowId()
    {
        // TODO: 将在后续实现完整的 GTK 集成
        return 0;
    }

    /// <inheritdoc />
    public void ShowAboutDialog(string name, string description, byte[]? icon)
    {
        // TODO: 将在后续实现完整的 GTK 集成
    }

    /// <inheritdoc />
    public void SetIcon(byte[]? icon)
    {
        // TODO: 将在后续实现完整的 GTK 集成
    }

    /// <inheritdoc />
    public void On(uint id)
    {
        // TODO: 将在后续实现完整的 GTK 集成
    }

    /// <inheritdoc />
    public void DispatchOnMainThread(uint id)
    {
        // TODO: 将在后续实现完整的 GTK 集成
    }

    /// <inheritdoc />
    public void Hide()
    {
        // TODO: 将在后续实现完整的 GTK 集成
    }

    /// <inheritdoc />
    public void Show()
    {
        // TODO: 将在后续实现完整的 GTK 集成
    }

    /// <inheritdoc />
    public void DispatchOnMainThread(Action action)
    {
        // TODO: 将在后续使用 GLib.Idle.Add 投递到 GTK 主循环
        action();
    }

    /// <inheritdoc />
    public void CreateWebviewWindow(uint id, WebviewWindowOptions options)
    {
        // TODO: 将在后续实现完整的 WebKitGTK 集成
    }

    /// <inheritdoc />
    public Task<int> ShowMessageDialog(string title, string message, DialogStyle style, string[] buttons)
    {
        // TODO: 将在后续实现完整的 GTK 集成
        return Task.FromResult(0);
    }

    /// <inheritdoc />
    public Task<string?> OpenFileDialog(OpenFileDialogOptions options)
    {
        // TODO: 将在后续实现完整的 GTK 集成
        return Task.FromResult<string?>(null);
    }

    /// <inheritdoc />
    public Task<string?> SaveFileDialog(SaveFileDialogOptions options)
    {
        // TODO: 将在后续实现完整的 GTK 集成
        return Task.FromResult<string?>(null);
    }

    /// <inheritdoc />
    public Task<string[]?> OpenMultipleFilesDialog(OpenFileDialogOptions options)
    {
        // TODO: 将在后续实现完整的 GTK 集成
        return Task.FromResult<string[]?>(null);
    }
}
