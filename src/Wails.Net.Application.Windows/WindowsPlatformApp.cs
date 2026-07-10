using Microsoft.Win32;
using Wails.Net.Application.Dialogs;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;
using Menu = Wails.Net.Application.Menus.Menu;
using Screen = Wails.Net.Application.Screens.Screen;

namespace Wails.Net.Application.Platform;

/// <summary>
/// Windows 平台应用实现，对应 Go 版 application.go 中的 Windows platformApp。
/// 通过注册表读取系统主题与强调色，消息循环与窗口管理将在后续阶段实现。
/// </summary>
public sealed class WindowsPlatformApp : IPlatformApp
{
    /// <summary>
    /// 暗色模式注册表键路径：HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize。
    /// </summary>
    private const string PersonalizeKeyPath = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>
    /// DWM 强调色注册表键路径：HKCU\Software\Microsoft\Windows\DWM。
    /// </summary>
    private const string DwmKeyPath = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM";

    /// <summary>
    /// 应用名称。
    /// </summary>
    private readonly string _name;

    /// <summary>
    /// 主线程 ID，用于 IsOnMainThread 判断。
    /// </summary>
    private readonly int _mainThreadId;

    /// <summary>
    /// 构造 WindowsPlatformApp 实例。
    /// </summary>
    /// <param name="options">应用配置选项。</param>
    public WindowsPlatformApp(ApplicationOptions options)
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
        // 读取 AppsUseLightTheme 值：0 表示暗色模式，1 表示亮色模式。
        var value = Registry.GetValue(PersonalizeKeyPath, "AppsUseLightTheme", null);
        if (value is int intValue)
        {
            return intValue == 0;
        }

        // 默认返回亮色模式。
        return false;
    }

    /// <inheritdoc />
    public string GetAccentColor()
    {
        // 读取 DWM AccentColor DWORD 值（0xAARRGGBB 格式），提取 RGB 分量转换为 #RRGGBB。
        var value = Registry.GetValue(DwmKeyPath, "AccentColor", null);
        if (value is int intValue)
        {
            int r = (intValue >> 16) & 0xFF;
            int g = (intValue >> 8) & 0xFF;
            int b = intValue & 0xFF;
            return $"#{r:X2}{g:X2}{b:X2}";
        }

        return "#000000";
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
        // TODO: 将在后续实现完整的 Windows 消息循环
        throw new NotImplementedException("Windows 消息循环将在后续实现");
    }

    /// <inheritdoc />
    public void Destroy()
    {
        // TODO: 将在后续实现资源清理
    }

    /// <inheritdoc />
    public void SetApplicationMenu(Menu? menu)
    {
        // TODO: 将在后续实现完整的 WebView2 集成
    }

    /// <inheritdoc />
    public uint GetCurrentWindowId()
    {
        // TODO: 将在后续实现完整的 WebView2 集成
        return 0;
    }

    /// <inheritdoc />
    public void ShowAboutDialog(string name, string description, byte[]? icon)
    {
        // TODO: 将在后续实现完整的 WebView2 集成
    }

    /// <inheritdoc />
    public void SetIcon(byte[]? icon)
    {
        // TODO: 将在后续实现完整的 WebView2 集成
    }

    /// <inheritdoc />
    public void On(uint id)
    {
        // TODO: 将在后续实现完整的 WebView2 集成
    }

    /// <inheritdoc />
    public void DispatchOnMainThread(uint id)
    {
        // TODO: 将在后续实现完整的 WebView2 集成
    }

    /// <inheritdoc />
    public void Hide()
    {
        // TODO: 将在后续实现完整的 WebView2 集成
    }

    /// <inheritdoc />
    public void Show()
    {
        // TODO: 将在后续实现完整的 WebView2 集成
    }

    /// <inheritdoc />
    public void DispatchOnMainThread(Action action)
    {
        // TODO: 将在后续使用 PostMessage 投递到主线程消息队列
        action();
    }

    /// <inheritdoc />
    public void CreateWebviewWindow(uint id, WebviewWindowOptions options)
    {
        // TODO: 将在后续实现完整的 WebView2 集成
    }

    /// <inheritdoc />
    public Task<int> ShowMessageDialog(string title, string message, DialogStyle style, string[] buttons)
    {
        // TODO: 将在后续实现完整的 WebView2 集成
        return Task.FromResult(0);
    }

    /// <inheritdoc />
    public Task<string?> OpenFileDialog(OpenFileDialogOptions options)
    {
        // TODO: 将在后续实现完整的 WebView2 集成
        return Task.FromResult<string?>(null);
    }

    /// <inheritdoc />
    public Task<string?> SaveFileDialog(SaveFileDialogOptions options)
    {
        // TODO: 将在后续实现完整的 WebView2 集成
        return Task.FromResult<string?>(null);
    }

    /// <inheritdoc />
    public Task<string[]?> OpenMultipleFilesDialog(OpenFileDialogOptions options)
    {
        // TODO: 将在后续实现完整的 WebView2 集成
        return Task.FromResult<string[]?>(null);
    }
}
