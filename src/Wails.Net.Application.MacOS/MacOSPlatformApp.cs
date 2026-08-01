using Wails.Net.Application.Dialogs;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform.ServerMode;
using Wails.Net.Application.Screens;

namespace Wails.Net.Application.Platform;

/// <summary>
/// macOS 平台应用骨架实现（G7 阶段）。
/// 对应 Wails v3 Go 版本 <c>application_macos.go</c>。
/// <para>
/// 当前为占位实现，所有 GUI 操作委托到 <see cref="ServerPlatformApp"/> 行为（no-op / 默认值）。
/// 后续阶段将切换 TFM 到 <c>net10.0-macos</c> 并集成：
/// <list type="bullet">
///   <item><c>NSApplication</c> / <c>NSWindow</c> — 应用与窗口管理</item>
///   <item><c>WKWebView</c> — WebKit 渲染（替代 WebView2/WebKitGTK）</item>
///   <item><c>NSMenu</c> — 应用菜单</item>
///   <item><c>NSScreen</c> — 屏幕信息</item>
///   <item><c>NSPasteboard</c> — 剪贴板</item>
///   <item><c>NSAlert</c> / <c>NSSavePanel</c> / <c>NSOpenPanel</c> — 对话框</item>
/// </list>
/// </para>
/// <para>
/// 平台检测：<see cref="PlatformFactory"/> 在 <c>OperatingSystem.IsMacOS()</c> 为 true 时
/// 返回 <c>"macos"</c>，由 <see cref="MacOSPlatformRegistrar"/> 注册的委托创建本类实例。
/// </para>
/// </summary>
public sealed class MacOSPlatformApp : IPlatformApp
{
    /// <summary>
    /// 内部委托的 Server 模式实例，提供 no-op 默认行为。
    /// 后续阶段将逐步替换为 macOS API 实现。
    /// </summary>
    private readonly ServerPlatformApp _stub;

    /// <summary>
    /// 构造 <see cref="MacOSPlatformApp"/> 实例。
    /// </summary>
    /// <param name="options">应用配置选项。</param>
    public MacOSPlatformApp(ApplicationOptions options)
    {
        _stub = new ServerPlatformApp(options);
        Name = options.Name;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public PlatformCapabilities Capabilities => new()
    {
        HasNativeDrag = true,
        GtkVersion = 0, // macOS 无 GTK
        WebKitVersion = string.Empty, // 后续通过 NSBundle 读取 WKWebView 版本
    };

    /// <inheritdoc />
    public int Run() => _stub.Run();

    /// <inheritdoc />
    public bool AcquireSingleInstanceLock(string uniqueId) => _stub.AcquireSingleInstanceLock(uniqueId);

    /// <inheritdoc />
    public void NotifySingleInstance(string[] args) => _stub.NotifySingleInstance(args);

    /// <inheritdoc />
    public void Destroy() => _stub.Destroy();

    /// <inheritdoc />
    public void SetApplicationMenu(Menu? menu)
    {
        // TODO(G7-macOS): 使用 NSMenu 创建应用菜单
    }

    /// <inheritdoc />
    public uint GetCurrentWindowId() => 0;

    /// <inheritdoc />
    public void SetParent(IntPtr parent)
    {
        // macOS 无 SetParent 概念，no-op
    }

    /// <inheritdoc />
    public void ShowAboutDialog(string name, string description, byte[]? icon)
    {
        // TODO(G7-macOS): 使用 NSAlert 或 NSApplication.OrderFrontStandardAboutPanel
    }

    /// <inheritdoc />
    public void SetIcon(byte[]? icon)
    {
        // macOS 应用图标由 Info.plist 配置，运行时不可修改，no-op
    }

    /// <inheritdoc />
    public void On(uint id)
    {
        // TODO(G7-macOS): 分发平台事件到 NSApplication 主线程
    }

    /// <inheritdoc />
    public void DispatchOnMainThread(uint id)
    {
        // TODO(G7-macOS): 使用 NSOperationQueue.MainQueue.AddOperation 分发
    }

    /// <inheritdoc />
    public void Hide()
    {
        // TODO(G7-macOS): 使用 NSApplication.Hide(null)
    }

    /// <inheritdoc />
    public void Show()
    {
        // TODO(G7-macOS): 使用 NSApplication.Unhide(null)
    }

    /// <inheritdoc />
    public Screen? GetPrimaryScreen()
    {
        // TODO(G7-macOS): 使用 NSScreen.MainScreen 读取屏幕信息
        return null;
    }

    /// <inheritdoc />
    public Screen[] GetScreens()
    {
        // TODO(G7-macOS): 使用 NSScreen.Screens 读取所有屏幕
        return Array.Empty<Screen>();
    }

    /// <inheritdoc />
    public Dictionary<string, object?> GetFlags(ApplicationOptions options) => new();

    /// <inheritdoc />
    public bool IsOnMainThread()
    {
        // TODO(G7-macOS): 使用 NSThread.IsMainThread 判断
        return Environment.CurrentManagedThreadId == _mainThreadId;
    }

    /// <summary>构造时记录的调用方线程 ID（占位实现）。</summary>
    private readonly int _mainThreadId = Environment.CurrentManagedThreadId;

    /// <inheritdoc />
    public bool IsDarkMode()
    {
        // TODO(G7-macOS): 使用 NSAppearance.CurrentAppearance.Contains(NSAppearance.NameDarkAqua)
        return false;
    }

    /// <inheritdoc />
    public string GetAccentColor()
    {
        // TODO(G7-macOS): 使用 NSColor.ControlAccentColor 读取系统强调色
        return string.Empty;
    }

    /// <inheritdoc />
    public void DispatchOnMainThread(Action action)
    {
        // TODO(G7-macOS): 使用 NSOperationQueue.MainQueue.AddOperation(action)
        // 当前占位实现：直接同步执行
        action();
    }

    /// <inheritdoc />
    public void CreateWebviewWindow(uint id, WebviewWindowOptions options)
    {
        // TODO(G7-macOS): 使用 WKWebView 创建 Webview 窗口
        // 当前占位实现：no-op
    }

    /// <inheritdoc />
    public Task<int> ShowMessageDialog(string title, string message, DialogStyle style, string[] buttons)
    {
        // TODO(G7-macOS): 使用 NSAlert 显示消息对话框
        return Task.FromResult(0);
    }

    /// <inheritdoc />
    public Task<string?> OpenFileDialog(OpenFileDialogOptions options)
    {
        // TODO(G7-macOS): 使用 NSOpenPanel 显示打开文件对话框
        return Task.FromResult<string?>(null);
    }

    /// <inheritdoc />
    public Task<string?> SaveFileDialog(SaveFileDialogOptions options)
    {
        // TODO(G7-macOS): 使用 NSSavePanel 显示保存文件对话框
        return Task.FromResult<string?>(null);
    }

    /// <inheritdoc />
    public Task<string[]?> OpenMultipleFilesDialog(OpenFileDialogOptions options)
    {
        // TODO(G7-macOS): 使用 NSOpenPanel + AllowsMultipleSelection
        return Task.FromResult<string[]?>(null);
    }
}
