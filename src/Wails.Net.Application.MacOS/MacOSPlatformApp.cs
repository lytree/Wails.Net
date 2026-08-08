using Wails.Net.Application.Dialogs;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform.ServerMode;
using Wails.Net.Application.Screens;
using Menu = Wails.Net.Application.Menus.Menu;
using WailsApplication = Wails.Net.Application.Application;

namespace Wails.Net.Application.Platform;

/// <summary>
/// macOS 平台应用实现。
/// 对应 Wails v3 Go 版本 <c>application_darwin.go</c> + <c>mainthread_darwin.go</c>
/// + <c>screen_darwin.go</c> + <c>dialogs_darwin.go</c>。
/// <para>
/// 在 <c>net10.0-macos</c> 目标（<c>#if MACOS</c>）下使用 AppKit 完整实现：
/// NSApplication 主循环、NSMenu 应用菜单、NSScreen 屏幕信息、NSAlert/NSOpenPanel/NSSavePanel
/// 对话框、暗色模式与强调色、主线程调度、单实例（文件锁 + NSDistributedNotificationCenter）。
/// 非 macOS 目标保留 Server 降级骨架，保证任意宿主编译。
/// </para>
/// </summary>
public sealed class MacOSPlatformApp : IPlatformApp
{
    /// <summary>
    /// 应用名称。
    /// </summary>
    private readonly string _name;

    /// <summary>
    /// 应用配置选项。
    /// </summary>
    private readonly ApplicationOptions _options;

    /// <summary>
    /// 已创建的 Webview 窗口字典，按窗口 ID 索引。
    /// </summary>
    private readonly Dictionary<uint, MacOSWebviewWindow> _windows = new();

    /// <summary>
    /// 主线程 ID。
    /// </summary>
    private readonly int _mainThreadId = Environment.CurrentManagedThreadId;

    /// <summary>
    /// 单实例通知观察者 token（持有引用防止被 GC）。
    /// </summary>
    private IDisposable? _singleInstanceObserver;

    /// <summary>
    /// 单实例文件锁流。
    /// </summary>
    private FileStream? _singleInstanceLock;

    /// <summary>
    /// 单实例锁文件路径。
    /// </summary>
    private string? _singleInstanceLockPath;

#if MACOS
    /// <summary>
    /// 系统主题观察者（KVO effectiveAppearance），变化时触发 <c>wails:theme:changed</c> 事件。
    /// 参照 DevToys ThemeListener.SystemThemeObserver。
    /// </summary>
    private MacSystemThemeObserver? _themeObserver;
#endif

    /// <summary>
    /// Server 降级实现（非 macOS 目标使用）。
    /// </summary>
    private readonly ServerPlatformApp _stub;

    /// <summary>
    /// 构造 MacOSPlatformApp 实例。
    /// </summary>
    /// <param name="options">应用配置选项。</param>
    public MacOSPlatformApp(ApplicationOptions options)
    {
        _name = options.Name;
        _options = options;
        _stub = new ServerPlatformApp(options);
    }

    /// <inheritdoc />
    public string Name => _name;

    /// <inheritdoc />
    public PlatformCapabilities Capabilities
    {
        get
        {
#if MACOS
            return new PlatformCapabilities
            {
                HasNativeDrag = true,
                GtkVersion = 0, // macOS 无 GTK
                WebKitVersion = GetWebKitVersion(),
            };
#else
            return PlatformCapabilities.Default;
#endif
        }
    }

    /// <inheritdoc />
    public int Run()
    {
#if MACOS
        // 标准 macOS 程序入口（参照 DevToys Program.cs：Init → Delegate → Main）。
        AppKit.NSApplication.Init();
        var app = AppKit.NSApplication.SharedApplication;

        // 单实例通知监听（NSDistributedNotificationCenter）。
        if (_options.SingleInstance)
        {
            RegisterSingleInstanceListener();
        }

        // 系统主题变更监听（KVO effectiveAppearance，参照 DevToys ThemeListener）。
        _themeObserver = new MacSystemThemeObserver();

        // 应用级设置。
        var macOptions = _options.Mac;
        app.ActivationPolicy = (AppKit.NSApplicationActivationPolicy)(macOptions?.ActivationPolicy ?? 0);
        app.ActivateIgnoringOtherApps(true);

        // 屏幕缓存（首次访问时惰性枚举）。
        app.Run();
        _themeObserver?.Dispose();
        _themeObserver = null;
        ReleaseSingleInstanceResources();
        return 0;
#else
        return _stub.Run();
#endif
    }

    /// <inheritdoc />
    public bool AcquireSingleInstanceLock(string uniqueId)
    {
#if MACOS
        try
        {
            var tempDir = Foundation.NSTemporaryDirectory.TrimEnd('/') + "/";
            var lockPath = tempDir + SanitizeFileName(uniqueId) + ".lock";
            var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            _singleInstanceLock = stream;
            _singleInstanceLockPath = lockPath;
            return true;
        }
        catch (IOException)
        {
            // 文件已存在且被锁定：已有实例在运行。
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
#else
        return _stub.AcquireSingleInstanceLock(uniqueId);
#endif
    }

    /// <inheritdoc />
    public void NotifySingleInstance(string[] args)
    {
#if MACOS
        try
        {
            var message = System.Text.Json.JsonSerializer.Serialize(args);
            var center = Foundation.NSDistributedNotificationCenter.DefaultCenter;
            var uniqueId = _options.SingleInstanceUniqueID ?? _name;
            using var name = new Foundation.NSString(uniqueId);
            using var obj = new Foundation.NSString(message);
            // 沙盒应用无法通过 userInfo 传递数据，使用 object 参数。
            center.PostNotificationName(name, obj, null, true);
        }
        catch
        {
            // 通知失败时静默忽略（Application 会继续按单实例流程退出）。
        }
#else
        _stub.NotifySingleInstance(args);
#endif
    }

    /// <inheritdoc />
    public void Destroy()
    {
#if MACOS
        AppKit.NSApplication.SharedApplication.Terminate(null);
#else
        _stub.Destroy();
#endif
    }

    /// <inheritdoc />
    public void SetApplicationMenu(Menu? menu)
    {
#if MACOS
        if (menu is null)
        {
            // macOS 默认应用菜单（App/Edit/Window/Help）。
            menu = BuildDefaultApplicationMenu();
        }

        var macMenu = new MacOSMenu(menu);
        if (macMenu.NativeMenu is { } native)
        {
            AppKit.NSApplication.SharedApplication.MainMenu = native;
        }
#else
        // 非 macOS 目标：no-op 骨架。
#endif
    }

    /// <inheritdoc />
    public uint GetCurrentWindowId()
    {
#if MACOS
        var app = AppKit.NSApplication.SharedApplication;
        var keyWindow = app.KeyWindow ?? app.MainWindow;
        if (keyWindow is null)
        {
            return 0;
        }

        foreach (var (id, window) in _windows)
        {
            if (ReferenceEquals(window.NativeWindow, keyWindow))
            {
                return id;
            }
        }

        return 0;
#else
        return 0;
#endif
    }

    /// <inheritdoc />
    public void SetParent(IntPtr parent)
    {
        // macOS 无 SetParent 概念，no-op。
    }

    /// <inheritdoc />
    public void ShowAboutDialog(string name, string description, byte[]? icon)
    {
#if MACOS
        DispatchOnMainThreadSync(() =>
        {
            var alert = new AppKit.NSAlert
            {
                MessageText = name,
                InformativeText = description ?? string.Empty,
                AlertStyle = AppKit.NSAlertStyle.Informational,
            };
            if (icon is { Length: > 0 })
            {
                using var data = Foundation.NSData.FromArray(icon);
                var image = AppKit.NSImage.FromData(data);
                if (image is not null)
                {
                    alert.Icon = image;
                }
            }

            alert.RunModal();
        });
#else
        // 非 macOS 目标：no-op 骨架。
#endif
    }

    /// <inheritdoc />
    public void SetIcon(byte[]? icon)
    {
#if MACOS
        if (icon is null || icon.Length == 0)
        {
            return;
        }

        using var data = Foundation.NSData.FromArray(icon);
        var image = AppKit.NSImage.FromData(data);
        if (image is not null)
        {
            AppKit.NSApplication.SharedApplication.ApplicationIconImage = image;
        }
#else
        // 非 macOS 目标：no-op 骨架。
#endif
    }

    /// <inheritdoc />
    public void On(uint id)
    {
        // macOS 应用事件通过 NSDistributedNotificationCenter / NSWorkspace 通知监听，
        // 与 Wails v3 的事件注册表对应。当前仅处理主题变化（IsDarkMode 惰性查询）。
    }

    /// <inheritdoc />
    public void DispatchOnMainThread(uint id)
    {
#if MACOS
        DispatchOnMainThread(() =>
        {
            var app = WailsApplication.Get();
            app?.HandlePlatformEvent(id);
        });
#else
        _stub.DispatchOnMainThread(id);
#endif
    }

    /// <inheritdoc />
    public void Hide()
    {
#if MACOS
        AppKit.NSApplication.SharedApplication.Hide(null);
#else
        _stub.Hide();
#endif
    }

    /// <inheritdoc />
    public void Show()
    {
#if MACOS
        AppKit.NSApplication.SharedApplication.Unhide(null);
#else
        _stub.Show();
#endif
    }

    /// <inheritdoc />
    public Screen? GetPrimaryScreen()
    {
        var screens = GetScreens();
        return screens.FirstOrDefault(s => s.IsPrimary) ?? screens.FirstOrDefault();
    }

    /// <inheritdoc />
    public Screen[] GetScreens()
    {
#if MACOS
        return DispatchOnMainThreadSync(() => EnumerateScreens());
#else
        return _stub.GetScreens();
#endif
    }

    /// <inheritdoc />
    public Dictionary<string, object?> GetFlags(ApplicationOptions options) => new();

    /// <inheritdoc />
    public bool IsOnMainThread()
    {
#if MACOS
        return Foundation.NSThread.IsMain;
#else
        return Environment.CurrentManagedThreadId == _mainThreadId;
#endif
    }

    /// <inheritdoc />
    public bool IsDarkMode()
    {
#if MACOS
        try
        {
            // 参照 DevToys ThemeListener：EffectiveAppearance 含系统/应用级外观，
            // FindBestMatch 判定明暗，比 NSUserDefaults 的 AppleInterfaceStyle 更准确。
            var appearance = AppKit.NSApplication.SharedApplication.EffectiveAppearance;
            var bestMatch = appearance.FindBestMatch(new[] { AppKit.NSAppearance.NameAqua, AppKit.NSAppearance.NameDarkAqua });
            return bestMatch == AppKit.NSAppearance.NameDarkAqua;
        }
        catch
        {
            return false;
        }
#else
        return false;
#endif
    }

    /// <inheritdoc />
    public string GetAccentColor()
    {
#if MACOS
        try
        {
            var accent = AppKit.NSColor.ControlAccentColor;
            using var rgb = accent.ColorUsingColorSpace(AppKit.NSColorSpace.SRGBColorSpace) ?? accent;
            rgb.GetRgba(out var red, out var green, out var blue, out _);
            return $"rgb({(int)(red * 255)},{(int)(green * 255)},{(int)(blue * 255)})";
        }
        catch
        {
            return "rgb(0,122,255)";
        }
#else
        return string.Empty;
#endif
    }

    /// <inheritdoc />
    public void DispatchOnMainThread(Action action)
    {
#if MACOS
        if (action is null)
        {
            return;
        }

        if (Foundation.NSThread.IsMain)
        {
            action();
            return;
        }

        AppKit.NSApplication.SharedApplication.InvokeOnMainThread(action);
#else
        action();
#endif
    }

    /// <inheritdoc />
    public void CreateWebviewWindow(uint id, WebviewWindowOptions options)
    {
#if MACOS
        var window = new MacOSWebviewWindow(id, options, _options.Mac);
        _windows[id] = window;
        window.Create();
#else
        _stub.CreateWebviewWindow(id, options);
#endif
    }

    /// <inheritdoc />
    public Task<int> ShowMessageDialog(string title, string message, DialogStyle style, string[] buttons)
    {
#if MACOS
        if (buttons is null || buttons.Length == 0)
        {
            buttons = new[] { "OK" };
        }

        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        DispatchOnMainThread(() =>
        {
            var alert = new AppKit.NSAlert
            {
                MessageText = title,
                InformativeText = message ?? string.Empty,
                AlertStyle = style switch
                {
                    DialogStyle.Warning => AppKit.NSAlertStyle.Warning,
                    DialogStyle.Error => AppKit.NSAlertStyle.Critical,
                    _ => AppKit.NSAlertStyle.Informational,
                },
            };

            foreach (var button in buttons)
            {
                alert.AddButton(button);
            }

            // 默认按钮绑定回车，取消按钮绑定 Esc。
            if (buttons.Length > 0)
            {
                alert.Buttons[0].KeyEquivalent = "\r";
            }

            if (buttons.Length > 1)
            {
                alert.Buttons[^1].KeyEquivalent = "\u001b";
            }

            // 有父窗口时以 sheet 呈现，否则模态运行。
            var parent = GetKeyWindowForDialog();
            if (parent is not null)
            {
                alert.BeginSheet(parent, () => tcs.TrySetResult((int)alert.RunModal()));
            }
            else
            {
                var response = alert.RunModal();
                tcs.TrySetResult(GetButtonIndex(response));
            }
        });
        return tcs.Task;
#else
        return _stub.ShowMessageDialog(title, message, style, buttons);
#endif
    }

    /// <inheritdoc />
    public Task<string?> OpenFileDialog(OpenFileDialogOptions options)
    {
#if MACOS
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        DispatchOnMainThread(() =>
        {
            var panel = AppKit.NSOpenPanel.OpenPanel;
            ConfigureOpenPanel(panel, options);

            var parent = GetKeyWindowForDialog();
            if (parent is not null)
            {
                panel.BeginSheet(parent, result =>
                    tcs.TrySetResult(GetFirstSelectedPath(panel, result)));
            }
            else
            {
                panel.Begin(result => tcs.TrySetResult(GetFirstSelectedPath(panel, result)));
            }
        });
        return tcs.Task;
#else
        return _stub.OpenFileDialog(options);
#endif
    }

    /// <inheritdoc />
    public Task<string?> SaveFileDialog(SaveFileDialogOptions options)
    {
#if MACOS
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        DispatchOnMainThread(() =>
        {
            var panel = AppKit.NSSavePanel.SavePanel;
            ConfigureSavePanel(panel, options);

            var parent = GetKeyWindowForDialog();
            if (parent is not null)
            {
                panel.BeginSheet(parent, result =>
                    tcs.TrySetResult(result == (long)AppKit.NSModalResponse.OK ? panel.Url?.Path : null));
            }
            else
            {
                panel.Begin(result =>
                    tcs.TrySetResult(result == (long)AppKit.NSModalResponse.OK ? panel.Url?.Path : null));
            }
        });
        return tcs.Task;
#else
        return _stub.SaveFileDialog(options);
#endif
    }

    /// <inheritdoc />
    public Task<string[]?> OpenMultipleFilesDialog(OpenFileDialogOptions options)
    {
#if MACOS
        var tcs = new TaskCompletionSource<string[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        DispatchOnMainThread(() =>
        {
            var panel = AppKit.NSOpenPanel.OpenPanel;
            panel.AllowsMultipleSelection = true;
            ConfigureOpenPanel(panel, options);

            var parent = GetKeyWindowForDialog();
            if (parent is not null)
            {
                panel.BeginSheet(parent, result =>
                    tcs.TrySetResult(GetSelectedPaths(panel, result)));
            }
            else
            {
                panel.Begin(result => tcs.TrySetResult(GetSelectedPaths(panel, result)));
            }
        });
        return tcs.Task;
#else
        return _stub.OpenMultipleFilesDialog(options);
#endif
    }

#if MACOS
    /// <summary>
    /// 在主线程同步执行操作（若已在主线程则直接执行）。
    /// </summary>
    /// <param name="action">要执行的操作。</param>
    internal static void DispatchOnMainThreadSync(Action action)
    {
        if (action is null)
        {
            return;
        }

        if (Foundation.NSThread.IsMain)
        {
            action();
            return;
        }

        using var semaphore = new SemaphoreSlim(0, 1);
        AppKit.NSApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            try
            {
                action();
            }
            finally
            {
                semaphore.Release();
            }
        });
        semaphore.Wait();
    }

    /// <summary>
    /// 在主线程同步执行操作并返回值。
    /// </summary>
    /// <typeparam name="T">返回值类型。</typeparam>
    /// <param name="func">要执行的操作。</param>
    /// <returns>操作返回值。</returns>
    internal static T DispatchOnMainThreadSync<T>(Func<T> func)
    {
        if (Foundation.NSThread.IsMain)
        {
            return func();
        }

        T result = default!;
        DispatchOnMainThreadSync(() => result = func());
        return result;
    }

    /// <summary>
    /// 枚举所有屏幕（必须在主线程调用）。
    /// 对应 Wails v3 Go 版本 screen_darwin.go 的 getAllScreens + cScreenToScreen：
    /// NSScreen 为 bottom-left 原点，转换为 top-left 原点（主屏幕左上为 (0,0)），
    /// Physical* 字段 = 点值 × backingScaleFactor。
    /// </summary>
    /// <returns>屏幕数组。</returns>
    private static Screen[] EnumerateScreens()
    {
        var nsScreens = AppKit.NSScreen.Screens;
        if (nsScreens is null || nsScreens.Length == 0)
        {
            return Array.Empty<Screen>();
        }

        var primaryHeight = nsScreens[0].Frame.Height;
        var screens = new Screen[nsScreens.Length];
        for (var i = 0; i < nsScreens.Length; i++)
        {
            var ns = nsScreens[i];
            var frame = ns.Frame;
            var workArea = ns.VisibleFrame;
            var scale = (float)ns.BackingScaleFactor;

            // 屏幕 ID：CGDirectDisplayID 字符串（deviceDescription 的 NSScreenNumber）。
            var id = GetScreenId(ns);

            screens[i] = new Screen(
                id: id,
                name: ns.LocalizedName ?? string.Empty,
                x: (int)frame.X,
                y: (int)(primaryHeight - frame.Y - frame.Height),
                width: (int)frame.Width,
                height: (int)frame.Height,
                workAreaX: (int)workArea.X,
                workAreaY: (int)(primaryHeight - workArea.Y - workArea.Height),
                workAreaWidth: (int)workArea.Width,
                workAreaHeight: (int)workArea.Height,
                scaleFactor: scale,
                isPrimary: i == 0)
            {
                // Physical* = 点值 × scale（与 Wails cScreenToScreen 一致）。
                PhysicalX = (int)(frame.X * scale),
                PhysicalY = (int)((primaryHeight - frame.Y - frame.Height) * scale),
                PhysicalWidth = (int)(frame.Width * scale),
                PhysicalHeight = (int)(frame.Height * scale),
                PhysicalWorkAreaX = (int)(workArea.X * scale),
                PhysicalWorkAreaY = (int)((primaryHeight - workArea.Y - workArea.Height) * scale),
                PhysicalWorkAreaWidth = (int)(workArea.Width * scale),
                PhysicalWorkAreaHeight = (int)(workArea.Height * scale),
            };
        }

        return screens;
    }

    /// <summary>
    /// 从 NSScreen.deviceDescription 读取 CGDirectDisplayID 作为屏幕 ID。
    /// </summary>
    /// <param name="screen">NSScreen 实例。</param>
    /// <returns>显示 ID 字符串。</returns>
    private static string GetScreenId(AppKit.NSScreen screen)
    {
        try
        {
            var description = screen.DeviceDescription;
            if (description is not null
                && description.ObjectForKey(new Foundation.NSString("NSScreenNumber")) is Foundation.NSNumber number)
            {
                return number.UInt32Value.ToString();
            }
        }
        catch
        {
            // 读取失败回退。
        }

        return screen.LocalizedName ?? Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// 获取对话框父窗口（当前 KeyWindow）。
    /// </summary>
    /// <returns>NSWindow 实例，无则 null。</returns>
    private static AppKit.NSWindow? GetKeyWindowForDialog()
    {
        var app = AppKit.NSApplication.SharedApplication;
        return app.KeyWindow ?? app.MainWindow;
    }

    /// <summary>
    /// 将 NSModalResponse 转换为按钮索引。
    /// </summary>
    /// <param name="response">模态响应。</param>
    /// <returns>按钮索引（0 起）。</returns>
    private static int GetButtonIndex(long response)
    {
        return response switch
        {
            (long)AppKit.NSModalResponse.FirstButtonReturn => 0,
            (long)AppKit.NSModalResponse.SecondButtonReturn => 1,
            (long)AppKit.NSModalResponse.ThirdButtonReturn => 2,
            _ => 3,
        };
    }

    /// <summary>
    /// 配置 NSOpenPanel 选项。
    /// </summary>
    /// <param name="panel">打开面板。</param>
    /// <param name="options">选项。</param>
    private static void ConfigureOpenPanel(AppKit.NSOpenPanel panel, OpenFileDialogOptions options)
    {
        panel.CanChooseFiles = options.AllowFiles;
        panel.CanChooseDirectories = options.AllowDirectories;
        panel.CanCreateDirectories = true;
        panel.ShowsHiddenFiles = options.ShowHiddenFiles;

        if (!string.IsNullOrEmpty(options.Title))
        {
            panel.Message = options.Title;
        }

        if (!string.IsNullOrEmpty(options.Directory) && Directory.Exists(options.Directory))
        {
            panel.DirectoryUrl = Foundation.NSUrl.FromFilename(options.Directory);
        }

        if (options.Filters is { Length: > 0 })
        {
            ApplyFilters(panel, options.Filters);
        }
    }

    /// <summary>
    /// 配置 NSSavePanel 选项。
    /// </summary>
    /// <param name="panel">保存面板。</param>
    /// <param name="options">选项。</param>
    private static void ConfigureSavePanel(AppKit.NSSavePanel panel, SaveFileDialogOptions options)
    {
        panel.CanCreateDirectories = options.CreateDirectories;
        panel.ShowsHiddenFiles = options.ShowHiddenFiles;

        if (!string.IsNullOrEmpty(options.Title))
        {
            panel.Message = options.Title;
        }

        if (!string.IsNullOrEmpty(options.Directory) && Directory.Exists(options.Directory))
        {
            panel.DirectoryUrl = Foundation.NSUrl.FromFilename(options.Directory);
        }

        if (!string.IsNullOrEmpty(options.Filename))
        {
            panel.NameFieldStringValue = options.Filename;
        }

        if (options.Filters is { Length: > 0 })
        {
            ApplyFilters(panel, options.Filters);
        }
    }

    /// <summary>
    /// 应用文件过滤器（以 ';' 分隔扩展名，如 "*.png;*.jpg"）。
    /// </summary>
    /// <param name="panel">面板。</param>
    /// <param name="filters">过滤器数组。</param>
    private static void ApplyFilters(AppKit.NSSavePanel panel, string[] filters)
    {
        var extensions = new List<string>();
        foreach (var filter in filters)
        {
            foreach (var part in filter.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var ext = part.Trim().TrimStart('*', '.');
                if (!string.IsNullOrEmpty(ext))
                {
                    extensions.Add(ext);
                }
            }
        }

        if (extensions.Count > 0)
        {
            panel.AllowedFileTypes = extensions.ToArray();
        }
    }

    /// <summary>
    /// 从面板结果中提取首个选中路径。
    /// </summary>
    /// <param name="panel">打开面板。</param>
    /// <param name="result">模态结果。</param>
    /// <returns>路径，取消返回 null。</returns>
    private static string? GetFirstSelectedPath(AppKit.NSOpenPanel panel, long result)
    {
        if (result != (long)AppKit.NSModalResponse.OK)
        {
            return null;
        }

        var urls = panel.Urls;
        if (urls is { Length: > 0 })
        {
            return urls[0].Path;
        }

        return panel.Url?.Path;
    }

    /// <summary>
    /// 从面板结果中提取所有选中路径。
    /// </summary>
    /// <param name="panel">打开面板。</param>
    /// <param name="result">模态结果。</param>
    /// <returns>路径数组，取消返回 null。</returns>
    private static string[]? GetSelectedPaths(AppKit.NSOpenPanel panel, long result)
    {
        if (result != (long)AppKit.NSModalResponse.OK)
        {
            return null;
        }

        var urls = panel.Urls;
        if (urls is { Length: > 0 })
        {
            return urls.Select(u => u.Path ?? string.Empty)
                .Where(p => p.Length > 0)
                .ToArray();
        }

        return null;
    }

    /// <summary>
    /// 获取 WKWebView 版本字符串。
    /// </summary>
    /// <returns>版本字符串，未知时为空。</returns>
    private static string GetWebKitVersion()
    {
        // WKWebView 不暴露版本查询 API，返回空（与既有骨架一致）。
        return string.Empty;
    }

    /// <summary>
    /// 构建 macOS 默认应用菜单（App/Edit/Window/Help）。
    /// 对应 Wails v3 Go 版本 DefaultApplicationMenu。
    /// </summary>
    /// <returns>默认菜单。</returns>
    private static Menu BuildDefaultApplicationMenu()
    {
        var menu = new Menu();

        // App 菜单（应用名）。
        var appMenu = menu.AddSubmenu(_name);
        appMenu.AddRoleItem(MenuRole.About);
        appMenu.AddSeparator();
        appMenu.AddRoleItem(MenuRole.Hide);
        appMenu.AddRoleItem(MenuRole.HideOthers);
        appMenu.AddRoleItem(MenuRole.ShowAll);
        appMenu.AddSeparator();
        appMenu.AddRoleItem(MenuRole.Quit);

        // Edit 菜单。
        var editMenu = menu.AddSubmenu("Edit");
        editMenu.AddStandardEditMenu();

        // Window 菜单。
        var windowMenu = menu.AddSubmenu("Window");
        windowMenu.AddStandardWindowMenu();
        windowMenu.AddRoleItem(MenuRole.ToggleFullScreen);

        // Help 菜单。
        var helpMenu = menu.AddSubmenu("Help");
        helpMenu.AddStandardHelpMenu();

        return menu;
    }

    /// <summary>
    /// 注册单实例通知监听。
    /// </summary>
    private void RegisterSingleInstanceListener()
    {
        try
        {
            var center = Foundation.NSDistributedNotificationCenter.DefaultCenter;
            var uniqueId = _options.SingleInstanceUniqueID ?? _name;
            using var name = new Foundation.NSString(uniqueId);
            _singleInstanceObserver = center.AddObserver(name, notification =>
            {
                var message = notification.Object?.ToString();
                if (!string.IsNullOrEmpty(message))
                {
                    try
                    {
                        var args = System.Text.Json.JsonSerializer.Deserialize<string[]>(message);
                        if (args is not null)
                        {
                            WailsApplication.Get()?.RaiseSecondInstanceLaunched(args);
                        }
                    }
                    catch
                    {
                        // 消息解析失败时忽略。
                    }
                }
            });
        }
        catch
        {
            // 监听注册失败时忽略（单实例通知降级）。
        }
    }

    /// <summary>
    /// 清理单实例资源。
    /// </summary>
    private void ReleaseSingleInstanceResources()
    {
        try
        {
            _singleInstanceObserver?.Dispose();
            _singleInstanceObserver = null;
            _singleInstanceLock?.Dispose();
            _singleInstanceLock = null;
            if (_singleInstanceLockPath is not null)
            {
                File.Delete(_singleInstanceLockPath);
                _singleInstanceLockPath = null;
            }
        }
        catch
        {
            // 清理失败忽略。
        }
    }

    /// <summary>
    /// 将唯一 ID 转为安全文件名。
    /// </summary>
    /// <param name="uniqueId">唯一 ID。</param>
    /// <returns>安全文件名。</returns>
    private static string SanitizeFileName(string uniqueId)
    {
        var chars = uniqueId.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray();
        return new string(chars);
    }

    /// <summary>
    /// 系统主题观察者：通过 KVO 监听 <c>NSApplication.EffectiveAppearance</c>，
    /// 明暗模式切换时触发 <c>wails:theme:changed</c> 应用事件。
    /// 参照 DevToys ThemeListener.SystemThemeObserver。
    /// </summary>
    private sealed class MacSystemThemeObserver : Foundation.NSObject
    {
        /// <summary>
        /// 构造观察者并注册 KVO。
        /// </summary>
        internal MacSystemThemeObserver()
        {
            AppKit.NSApplication.SharedApplication.AddObserver(
                this,
                new Foundation.NSString("effectiveAppearance"),
                Foundation.NSKeyValueObservingOptions.New,
                IntPtr.Zero);
        }

        /// <summary>
        /// KVO 回调（由 AppKit 调用）。
        /// </summary>
        /// <param name="keyPath">被观察的键路径。</param>
        /// <param name="ofObject">被观察对象。</param>
        /// <param name="change">变更字典。</param>
        /// <param name="context">上下文。</param>
        [Foundation.Export("observeValueForKeyPath:ofObject:change:context:")]
        public void ObserveValue(
            Foundation.NSString keyPath,
            Foundation.NSObject ofObject,
            Foundation.NSDictionary change,
            IntPtr context)
        {
            if (keyPath == "effectiveAppearance")
            {
                WailsApplication.Get()?.HandlePlatformEvent((uint)Wails.Net.Events.ApplicationEventType.ThemeChanged);
            }
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            try
            {
                AppKit.NSApplication.SharedApplication.RemoveObserver(
                    this,
                    new Foundation.NSString("effectiveAppearance"));
            }
            catch
            {
                // 应用退出期间移除观察者失败时忽略。
            }

            base.Dispose(disposing);
        }
    }
#endif
}
