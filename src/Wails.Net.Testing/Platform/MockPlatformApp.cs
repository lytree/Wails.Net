using System.Collections.Concurrent;
using Wails.Net.Application.Dialogs;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Screens;
using Wails.Net.Testing.Recording;
using WailsApplication = Wails.Net.Application.Application;

namespace Wails.Net.Testing.Platform;

/// <summary>
/// 无头 Mock 平台应用实现，用于在没有 GUI 环境的 CI 中驱动完整的应用生命周期。
/// <para>
/// 对标 Tauri v2 <c>tauri::test::mock_builder()</c> 的 MockRuntime：
/// <list type="bullet">
/// <item>窗口创建返回内存态的 <see cref="MockWebviewWindow"/>，可读回全部状态；</item>
/// <item>对话框返回可注入的预设结果，避免测试阻塞在模态窗口上；</item>
/// <item>全部调用写入共享 <see cref="CallRecorder"/>，可断言"是否调用/调用几次/参数是什么"。</item>
/// </list>
/// </para>
/// </summary>
public sealed class MockPlatformApp : IPlatformApp, IDisposable
{
    /// <summary>
    /// 默认虚拟主屏幕宽度（DIP）。
    /// </summary>
    public const int DefaultScreenWidth = 1920;

    /// <summary>
    /// 默认虚拟主屏幕高度（DIP）。
    /// </summary>
    public const int DefaultScreenHeight = 1080;

    /// <summary>
    /// 调用记录器。
    /// </summary>
    private readonly CallRecorder _recorder;

    /// <summary>
    /// 已创建的窗口字典（按窗口 ID 索引）。
    /// </summary>
    private readonly ConcurrentDictionary<uint, MockWebviewWindow> _windows = new();

    /// <summary>
    /// 用于阻塞主循环直到关闭信号到达的等待句柄。
    /// </summary>
    private readonly ManualResetEventSlim _shutdownEvent = new(initialState: false);

    /// <summary>
    /// 应用配置选项。
    /// </summary>
    private readonly ApplicationOptions _options;

    /// <summary>
    /// 最近创建的窗口 ID。
    /// </summary>
    private uint _lastWindowId;

    /// <summary>
    /// 是否已释放。
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// 构造 Mock 平台应用实例。
    /// </summary>
    /// <param name="options">应用配置选项。</param>
    /// <param name="recorder">调用记录器；为 null 时内部新建独立记录器。</param>
    public MockPlatformApp(ApplicationOptions options, CallRecorder? recorder = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _recorder = recorder ?? new CallRecorder();
        Screens =
        [
            new Screen(
                id: "mock-screen-0",
                name: "Mock Screen 0",
                x: 0,
                y: 0,
                width: DefaultScreenWidth,
                height: DefaultScreenHeight,
                workAreaX: 0,
                workAreaY: 0,
                workAreaWidth: DefaultScreenWidth,
                workAreaHeight: DefaultScreenHeight,
                scaleFactor: 1.0f,
                isPrimary: true)
        ];
    }

    // ---------------------------------------------------------------------
    // 测试断言入口
    // ---------------------------------------------------------------------

    /// <summary>
    /// 获取调用记录器，供测试断言调用序列。
    /// </summary>
    public CallRecorder Recorder => _recorder;

    /// <summary>
    /// 获取当前调用记录快照。
    /// </summary>
    public IReadOnlyList<CallRecord> Calls => _recorder.Snapshot();

    /// <summary>
    /// 获取应用配置选项。
    /// </summary>
    public ApplicationOptions Options => _options;

    /// <summary>
    /// 获取已创建的全部 Mock 窗口（按窗口 ID 索引）。
    /// </summary>
    public IReadOnlyDictionary<uint, MockWebviewWindow> Windows => _windows;

    /// <summary>
    /// 获取最近一次创建的 Mock 窗口；尚未创建任何窗口时返回 null。
    /// </summary>
    public MockWebviewWindow? LastWindow =>
        _windows.TryGetValue(Volatile.Read(ref _lastWindowId), out var window) ? window : null;

    /// <summary>
    /// 获取主循环是否正在运行（<see cref="Run"/> 已进入阻塞等待）。
    /// </summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// 获取 <see cref="Destroy"/> 是否已被调用。
    /// </summary>
    public bool IsDestroyed { get; private set; }

    /// <summary>
    /// 获取应用是否处于可见状态（由 <see cref="Show"/> / <see cref="Hide"/> 维护）。
    /// </summary>
    public bool IsVisible { get; private set; } = true;

    /// <summary>
    /// 获取当前设置的应用菜单。
    /// </summary>
    public Menu? ApplicationMenu { get; private set; }

    /// <summary>
    /// 获取当前设置的应用图标字节数据。
    /// </summary>
    public byte[]? IconBytes { get; private set; }

    /// <summary>
    /// 获取"关于对话框"被展示的次数。
    /// </summary>
    public int AboutDialogShownCount { get; private set; }

    /// <summary>
    /// 获取已处理的平台事件 ID 列表。
    /// </summary>
    public IReadOnlyList<uint> HandledEventIds => [.. _handledEventIds];

    /// <summary>
    /// 已处理的平台事件 ID 队列。
    /// </summary>
    private readonly ConcurrentQueue<uint> _handledEventIds = new();

    // ---------------------------------------------------------------------
    // 可注入的行为开关（测试通过这些属性控制 Mock 的返回值）
    // ---------------------------------------------------------------------

    /// <summary>
    /// 获取或设置屏幕列表。默认包含一个 1920x1080 的虚拟主屏幕。
    /// </summary>
    public Screen[] Screens { get; set; }

    /// <summary>
    /// 获取或设置 <see cref="IsOnMainThread"/> 的返回值，默认 true。
    /// </summary>
    public bool OnMainThread { get; set; } = true;

    /// <summary>
    /// 获取或设置 <see cref="IsDarkMode"/> 的返回值，默认 false。
    /// </summary>
    public bool DarkMode { get; set; }

    /// <summary>
    /// 获取或设置 <see cref="GetAccentColor"/> 的返回值，默认 <c>#0078D4</c>。
    /// </summary>
    public string AccentColor { get; set; } = "#0078D4";

    /// <summary>
    /// 获取或设置 <see cref="AcquireSingleInstanceLock"/> 的返回值，默认 true。
    /// </summary>
    public bool SingleInstanceLockResult { get; set; } = true;

    /// <summary>
    /// 获取或设置 <see cref="ShowMessageDialog"/> 返回的按钮索引，默认 0（第一个按钮）。
    /// </summary>
    public int MessageDialogResult { get; set; }

    /// <summary>
    /// 获取或设置 <see cref="OpenFileDialog"/> 返回的路径，默认 null（用户取消）。
    /// </summary>
    public string? OpenFileDialogResult { get; set; }

    /// <summary>
    /// 获取或设置 <see cref="SaveFileDialog"/> 返回的路径，默认 null（用户取消）。
    /// </summary>
    public string? SaveFileDialogResult { get; set; }

    /// <summary>
    /// 获取或设置 <see cref="OpenMultipleFilesDialog"/> 返回的路径数组，默认 null（用户取消）。
    /// </summary>
    public string[]? OpenMultipleFilesDialogResult { get; set; }

    /// <summary>
    /// 获取或设置是否在 <see cref="CreateWebviewWindow"/> 时把窗口注册到
    /// <c>Application.Get().NativeIpcTransport</c>，默认 true。
    /// 关闭后可用于纯窗口契约测试（不需要 IPC 管线）。
    /// </summary>
    public bool RegisterWindowsWithNativeIpc { get; set; } = true;

    /// <summary>
    /// 获取或设置单实例通知回调，用于断言 <see cref="NotifySingleInstance"/> 的参数。
    /// </summary>
    public Action<string[]>? SingleInstanceNotified { get; set; }

    // ---------------------------------------------------------------------
    // IPlatformApp 实现
    // ---------------------------------------------------------------------

    /// <inheritdoc />
    public string Name => _options.Name;

    /// <inheritdoc />
    public PlatformCapabilities Capabilities => PlatformCapabilities.Default;

    /// <inheritdoc />
    public int Run()
    {
        _recorder.Record(nameof(Run));
        IsRunning = true;
        try
        {
            // 与 ServerPlatformApp 一致：阻塞直到 SignalShutdown / Destroy 被调用。
            _shutdownEvent.Wait();
        }
        finally
        {
            IsRunning = false;
        }

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
        _recorder.Record(nameof(AcquireSingleInstanceLock), uniqueId);
        return SingleInstanceLockResult;
    }

    /// <inheritdoc />
    public void NotifySingleInstance(string[] args)
    {
        _recorder.Record(nameof(NotifySingleInstance), args);
        SingleInstanceNotified?.Invoke(args);
    }

    /// <inheritdoc />
    public void Destroy()
    {
        _recorder.Record(nameof(Destroy));
        IsDestroyed = true;

        foreach (var window in _windows.Values)
        {
            window.Dispose();
        }

        SignalShutdown();
    }

    /// <inheritdoc />
    public void SetApplicationMenu(Menu? menu)
    {
        _recorder.Record(nameof(SetApplicationMenu), menu);
        ApplicationMenu = menu;
    }

    /// <inheritdoc />
    public uint GetCurrentWindowId()
    {
        _recorder.Record(nameof(GetCurrentWindowId));
        return Volatile.Read(ref _lastWindowId);
    }

    /// <inheritdoc />
    public void SetParent(IntPtr parent)
    {
        _recorder.Record(nameof(SetParent), parent);
        ParentHandle = parent;
    }

    /// <summary>
    /// 获取通过 <see cref="SetParent"/> 设置的父窗口句柄。
    /// </summary>
    public IntPtr ParentHandle { get; private set; }

    /// <inheritdoc />
    public void ShowAboutDialog(string name, string description, byte[]? icon)
    {
        _recorder.Record(nameof(ShowAboutDialog), name, description, icon);
        AboutDialogShownCount++;
    }

    /// <inheritdoc />
    public void SetIcon(byte[]? icon)
    {
        _recorder.Record(nameof(SetIcon), icon);
        IconBytes = icon;
    }

    /// <inheritdoc />
    public void On(uint id)
    {
        _recorder.Record(nameof(On), id);
        _handledEventIds.Enqueue(id);
    }

    /// <inheritdoc />
    public void DispatchOnMainThread(uint id)
    {
        _recorder.Record($"{nameof(DispatchOnMainThread)}(uint)", id);
        _handledEventIds.Enqueue(id);
    }

    /// <inheritdoc />
    public void Hide()
    {
        _recorder.Record(nameof(Hide));
        IsVisible = false;
    }

    /// <inheritdoc />
    public void Show()
    {
        _recorder.Record(nameof(Show));
        IsVisible = true;
    }

    /// <inheritdoc />
    public Screen? GetPrimaryScreen()
    {
        _recorder.Record(nameof(GetPrimaryScreen));
        var screens = Screens;
        if (screens.Length == 0)
        {
            return null;
        }

        return Array.Find(screens, s => s.IsPrimary) ?? screens[0];
    }

    /// <inheritdoc />
    public Screen[] GetScreens()
    {
        _recorder.Record(nameof(GetScreens));
        return [.. Screens];
    }

    /// <inheritdoc />
    public Dictionary<string, object?> GetFlags(ApplicationOptions options)
    {
        _recorder.Record(nameof(GetFlags));
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["platform"] = MockPlatformRegistrar.PlatformName,
            ["headless"] = true,
            ["appName"] = options.Name
        };
    }

    /// <inheritdoc />
    public bool IsOnMainThread()
    {
        _recorder.Record(nameof(IsOnMainThread));
        return OnMainThread;
    }

    /// <inheritdoc />
    public bool IsDarkMode()
    {
        _recorder.Record(nameof(IsDarkMode));
        return DarkMode;
    }

    /// <inheritdoc />
    public string GetAccentColor()
    {
        _recorder.Record(nameof(GetAccentColor));
        return AccentColor;
    }

    /// <inheritdoc />
    public void DispatchOnMainThread(Action action)
    {
        _recorder.Record($"{nameof(DispatchOnMainThread)}(Action)");

        // 无头环境不存在真实主线程消息循环，直接同步执行。
        action();
    }

    /// <inheritdoc />
    public void CreateWebviewWindow(uint id, WebviewWindowOptions options)
    {
        _recorder.Record(nameof(CreateWebviewWindow), id, options.Title);

        var window = new MockWebviewWindow(id, options, _recorder);
        _windows[id] = window;
        Volatile.Write(ref _lastWindowId, id);

        // 与真实平台实现一致：若原生 IPC 已启用，注册窗口以安装消息路由回调。
        // 这是 WailsTestHost 能够跑通完整 IPC 管线的关键一步。
        if (RegisterWindowsWithNativeIpc)
        {
            WailsApplication.Get()?.NativeIpcTransport?.RegisterWindow(id, window);
        }

        window.Show();
    }

    /// <summary>
    /// 按窗口 ID 获取 Mock 窗口。
    /// </summary>
    /// <param name="id">窗口 ID。</param>
    /// <param name="window">返回的 Mock 窗口。</param>
    /// <returns>找到窗口返回 true，否则返回 false。</returns>
    public bool TryGetWindow(uint id, out MockWebviewWindow? window)
    {
        return _windows.TryGetValue(id, out window);
    }

    /// <inheritdoc />
    public Task<int> ShowMessageDialog(string title, string message, DialogStyle style, string[] buttons)
    {
        _recorder.Record(nameof(ShowMessageDialog), title, message, style, buttons);
        return Task.FromResult(MessageDialogResult);
    }

    /// <inheritdoc />
    public Task<string?> OpenFileDialog(OpenFileDialogOptions options)
    {
        _recorder.Record(nameof(OpenFileDialog), options.Title);
        return Task.FromResult(OpenFileDialogResult);
    }

    /// <inheritdoc />
    public Task<string?> SaveFileDialog(SaveFileDialogOptions options)
    {
        _recorder.Record(nameof(SaveFileDialog), options.Title);
        return Task.FromResult(SaveFileDialogResult);
    }

    /// <inheritdoc />
    public Task<string[]?> OpenMultipleFilesDialog(OpenFileDialogOptions options)
    {
        _recorder.Record(nameof(OpenMultipleFilesDialog), options.Title);
        return Task.FromResult(OpenMultipleFilesDialogResult);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SignalShutdown();

        foreach (var window in _windows.Values)
        {
            window.Dispose();
        }

        _windows.Clear();
        _shutdownEvent.Dispose();
    }
}
