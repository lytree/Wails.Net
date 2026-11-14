using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System.Windows.Forms;
using Wails.Net.Application.Dialogs;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Windows;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.WindowsAndMessaging;
using Menu = Wails.Net.Application.Menus.Menu;
using Screen = Wails.Net.Application.Screens.Screen;
using WinFormsMessageBox = System.Windows.Forms.MessageBox;
using WinFormsMessageBoxButtons = System.Windows.Forms.MessageBoxButtons;
using WinFormsMessageBoxDefaultButton = System.Windows.Forms.MessageBoxDefaultButton;
using WinFormsMessageBoxIcon = System.Windows.Forms.MessageBoxIcon;
using WinFormsOpenFileDialog = System.Windows.Forms.OpenFileDialog;
using WinFormsSaveFileDialog = System.Windows.Forms.SaveFileDialog;
using WinFormsScreen = System.Windows.Forms.Screen;

namespace Wails.Net.Application.Platform;

/// <summary>
/// Windows 平台应用实现，对应 Go 版 application.go 中的 Windows platformApp。
/// 通过注册表读取系统主题与强调色，使用 Win32 消息循环驱动应用主线程。
/// 支持 DPI 感知、暗色模式、应用菜单、图标、单实例锁与主线程事件分发。
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
    /// WM_QUIT 消息常量（0x0012），用于消息循环退出判断。
    /// </summary>
    private const uint WmQuit = 0x0012;

    /// <summary>
    /// WM_APP 基值（0x8000），用于自定义窗口消息。
    /// </summary>
    internal const uint WmApp = 0x8000;

    /// <summary>
    /// 单实例通知消息：WM_APP + 1。
    /// </summary>
    internal const uint WmAppSingleInstance = WmApp + 1;

    /// <summary>
    /// 主线程 Action 执行消息：WM_APP + 2。
    /// </summary>
    internal const uint WmAppDispatchAction = WmApp + 2;

    /// <summary>
    /// 平台事件分发消息：WM_APP + 3，wParam 携带事件 ID。
    /// </summary>
    internal const uint WmAppPlatformEvent = WmApp + 3;

    /// <summary>
    /// ERROR_ALREADY_EXISTS（183）：CreateMutex 创建已存在的命名互斥体时 GetLastError 返回此值。
    /// </summary>
    private const int ErrorAlreadyExists = 183;

    /// <summary>
    /// WM_SETICON（0x0080）：设置窗口图标消息。
    /// </summary>
    private const uint WmSetIcon = 0x0080;

    /// <summary>
    /// ICON_BIG（1）：大图标参数。
    /// </summary>
    private const nint IconBig = 1;

    /// <summary>
    /// ICON_SMALL（0）：小图标参数。
    /// </summary>
    private const nint IconSmall = 0;

    /// <summary>
    /// 应用名称。
    /// </summary>
    private readonly string _name;

    /// <summary>
    /// 主线程托管 ID，用于 IsOnMainThread 判断。
    /// 构造时记录捕获的线程 ID；<see cref="Run"/> 启动时重新记录，因为 UI 线程可能不是构造时的线程。
    /// </summary>
    private int _mainThreadId;

    /// <summary>
    /// 应用描述，用于关于对话框。
    /// </summary>
    private readonly string _description;

    /// <summary>
    /// 应用版本号，用于关于对话框。
    /// </summary>
    private readonly string _version;

    /// <summary>
    /// Windows 平台特定选项，全局生效于所有窗口共享的 WebView2 环境。
    /// 在构造时从 <see cref="ApplicationOptions.Windows"/> 读取，传入 <see cref="Win32WebviewWindow"/>。
    /// </summary>
    private readonly WindowsOptions? _windowsOptions;

    /// <summary>
    /// 窗口只读查询入口（RH-WIN-02 终态）。
    /// 由 <see cref="Wails.Net.Application.Managers.WindowManager"/> 注入，替代此前的自持窗口字典；
    /// 唯一事实源是 <see cref="Wails.Net.Application.Managers.WindowManager"/>。
    /// </summary>
    private IPlatformWindowAccessor? _windowAccessor;

    /// <summary>
    /// 枚举本平台创建的窗口（按窗口 ID 升序）。
    /// <para>
    /// RH-WIN-02 终态：不再持有 <c>Dictionary&lt;uint, Win32WebviewWindow&gt;</c>，
    /// 改为查询 <see cref="Wails.Net.Application.Managers.WindowManager"/> 并做类型判断取回平台实现（零反射）。
    /// 创建过程中的窗口 <see cref="WebviewWindow.Impl"/> 尚未回填，不会出现在结果中。
    /// </para>
    /// </summary>
    private IEnumerable<Win32WebviewWindow> EnumerateWindows()
        => EnumerateWindowsCore(_windowAccessor);

    /// <summary>
    /// 从查询入口枚举 <see cref="Win32WebviewWindow"/> 实现（按 ID 升序，保证行为确定）。
    /// </summary>
    private static IEnumerable<Win32WebviewWindow> EnumerateWindowsCore(IPlatformWindowAccessor? accessor)
    {
        if (accessor is null)
        {
            yield break;
        }

        var snapshot = accessor.EnumeratePlatformWindows();
        foreach (var impl in snapshot)
        {
            if (impl is Win32WebviewWindow window)
            {
                yield return window;
            }
        }
    }

    /// <summary>
    /// 主线程待执行 Action 队列，由 DispatchOnMainThread(Action) 投递，WndProc 消费。
    /// </summary>
    private readonly ConcurrentQueue<Action> _actionQueue = new();

    /// <summary>
    /// 单实例命名互斥体句柄，保持存活以持有锁。
    /// </summary>
    private SafeFileHandle? _singleInstanceMutex;

    /// <summary>
    /// 当前应用实例（静态引用），供 WndProc 回调访问队列。
    /// </summary>
    private static WindowsPlatformApp? s_current;

    /// <summary>
    /// 当前应用菜单的 Win32 实现，可为 null。
    /// </summary>
    private Win32Menu? _applicationMenu;

    /// <summary>
    /// 应用级图标句柄缓存：持有最近一次 <see cref="SetIcon"/> 的 HICON，
    /// 替换时释放旧句柄，应用销毁时释放当前句柄（对应上游 #6502 的 <c>currentIcon</c>）。
    /// 新窗口复用该句柄，避免逐窗口重复加载。
    /// </summary>
    private readonly Win32AppIconCache _appIconCache = new();

    /// <summary>
    /// 系统级事件监听器，监听电源模式变化和网络连通性变化。
    /// </summary>
    private SystemEventWatcher? _systemEventWatcher;

    /// <summary>
    /// 平台层顶层隐藏消息窗口，用于接收系统广播（如 <c>WM_SETTINGCHANGE</c>）并在
    /// 无 webview 窗口时也能发出主题变化事件（P1-10 补全）。
    /// </summary>
    private PlatformMessageWindow? _platformMessageWindow;

    /// <summary>
    /// 构造 WindowsPlatformApp 实例。
    /// 在构造时设置进程 DPI 感知，确保后续窗口按高 DPI 正确缩放。
    /// </summary>
    /// <param name="options">应用配置选项。</param>
    public WindowsPlatformApp(ApplicationOptions options)
    {
        _name = options.Name;
        _description = options.Description;
        _version = options.Version;
        _windowsOptions = options.Windows;
        s_current = this;

        // 记录构造线程为主线程 ID，供 IsOnMainThread 判断使用。
        // Run() 未启动时以构造线程为准（与 Linux / macOS 实现一致），
        // 避免 _mainThreadId 保持默认值 0 导致 IsOnMainThread 恒返回 false。
        _mainThreadId = Environment.CurrentManagedThreadId;

        // 设置进程 DPI 感知，必须在创建任何窗口之前完成。
        SetDpiAwareness();

        // 初始化 uxtheme 应用级暗色（SetPreferredAppMode），必须在创建窗口前完成。
        // 对应 Wails v3 theme.go init()。
        Win32Theme.Initialize();
    }

    /// <inheritdoc />
    public string Name => _name;

    /// <inheritdoc />
    /// <remarks>
    /// Windows 通过 OLE Drag-Drop 提供原生拖放支持；WebView2 内核为 Edge Chromium，
    /// 版本号通过 <c>CoreWebView2Environment.Options</c> 或注册表获取。
    /// </remarks>
    public PlatformCapabilities Capabilities { get; } = new()
    {
        HasNativeDrag = true,
        GtkVersion = 0,
        WebKitVersion = string.Empty, // WebView2 非 WebKit，留空
    };

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
        // 使用 WinForms Screen 获取主屏幕信息并转换为 Wails.Net Screen。
        var primary = WinFormsScreen.PrimaryScreen;
        return primary is null ? null : ToScreen(primary, isPrimary: true);
    }

    /// <inheritdoc />
    public Screen[] GetScreens()
    {
        // 枚举所有屏幕并转换为 Wails.Net Screen 数组。
        var allScreens = WinFormsScreen.AllScreens;
        if (allScreens is null || allScreens.Length == 0)
        {
            return Array.Empty<Screen>();
        }

        var result = new Screen[allScreens.Length];
        for (var i = 0; i < allScreens.Length; i++)
        {
            result[i] = ToScreen(allScreens[i], isPrimary: allScreens[i].Primary);
        }

        return result;
    }

    /// <inheritdoc />
    public Dictionary<string, object?> GetFlags(ApplicationOptions options)
    {
        // 返回 Windows 平台特定标志，供应用层查询。
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["WinClass"] = Win32WebviewWindow.WindowClassName,
            ["SingleInstance"] = options.SingleInstance,
            ["Platform"] = "windows",
        };
    }

    /// <inheritdoc />
    public int Run()
    {
        // 在 UI 线程启动时记录线程 ID（可能不是构造时的线程，如通过 STA 线程运行时）。
        _mainThreadId = Environment.CurrentManagedThreadId;

        // 启动系统级事件监听器（电源模式变化、网络连通性变化）。
        _systemEventWatcher = new SystemEventWatcher();
        _systemEventWatcher.Start();

        // P1-10 补全：创建平台层顶层隐藏消息窗口。WM_SETTINGCHANGE 等系统消息只广播给
        // 顶层窗口，而托盘（message-only）与 webview 窗口并非始终存在；本窗口保证主题变化
        // 事件在任何情况下都能发出（托盘据此切换 light/dark 图标）。
        // 对应 Wails v3 application_windows.go 中 mainThreadWindow 的 WM_SETTINGCHANGE 处理。
        // 必须在消息循环前、于本 UI 线程创建。
        _platformMessageWindow = new PlatformMessageWindow();
        _platformMessageWindow.Create();

        // 标准 Win32 消息循环：GetMessage 阻塞等待消息，TranslateMessage 转换键盘消息，
        // DispatchMessage 分发到窗口过程。GetMessage 返回 0 表示收到 WM_QUIT，退出循环。
        // 线程级热键消息（WM_HOTKEY，hwnd == NULL）不通过 DispatchMessage 分发，
        // 需在此直接处理，转发到 KeyBindingManager。
        const uint WmHotkey = 0x0312;

        while ((int)PInvoke.GetMessage(out var msg, default, 0, 0) > 0)
        {
            // 处理线程级热键消息（hwnd 为 NULL 的 WM_HOTKEY）。
            if (msg.hwnd.IsNull && msg.message == WmHotkey)
            {
                Application.Get()?.KeyBindingManager?.HandleHotKey((int)msg.wParam.Value);
                continue;
            }

            PInvoke.TranslateMessage(in msg);
            PInvoke.DispatchMessage(in msg);
        }

        return 0;
    }

    /// <inheritdoc />
    public void Destroy()
    {
        // 停止系统级事件监听器。
        _systemEventWatcher?.Dispose();
        _systemEventWatcher = null;

        // 销毁平台层消息窗口。
        _platformMessageWindow?.Dispose();
        _platformMessageWindow = null;

        // 销毁应用菜单。
        _applicationMenu?.Destroy();
        _applicationMenu = null;

        // 释放应用级图标句柄（对齐上游 destroy() 中的 DestroyIcon(currentIcon)）。
        _appIconCache.Dispose();

        // 投递 WM_QUIT 消息以退出消息循环。
        PInvoke.PostQuitMessage(0);
    }

    /// <inheritdoc />
    public void SetApplicationMenu(Menu? menu)
    {
        // 销毁旧菜单。
        if (_applicationMenu is not null)
        {
            _applicationMenu.Destroy();
            _applicationMenu = null;
        }

        if (menu is null)
        {
            // 移除所有窗口的菜单。
            foreach (var window in EnumerateWindows())
            {
                window.ClearMenu();
            }

            return;
        }

        // 创建 Win32 菜单实现并构建原生菜单。
        _applicationMenu = new Win32Menu(menu);
        _applicationMenu.Build();

        // 将菜单设置到所有已创建窗口。
        foreach (var window in EnumerateWindows())
        {
            window.SetMenuHandle(_applicationMenu.Hmenu);
        }
    }

    /// <inheritdoc />
    public uint GetCurrentWindowId()
    {
        // 优先返回前台窗口对应的窗口 ID。
        var foreground = PInvoke.GetForegroundWindow();
        if (!foreground.IsNull)
        {
            foreach (var window in EnumerateWindows())
            {
                if (window.Hwnd == foreground)
                {
                    return window.Id;
                }
            }
        }

        // 回退：返回第一个窗口 ID 或 0。
        foreach (var window in EnumerateWindows())
        {
            return window.Id;
        }

        return 0;
    }

    /// <inheritdoc />
    public void ShowAboutDialog(string name, string description, byte[]? icon)
    {
        // 使用 WinForms MessageBox 显示关于信息。
        var message = string.IsNullOrEmpty(description)
            ? $"{name} 版本 {_version}"
            : $"{name} 版本 {_version}\n\n{description}";
        WinFormsMessageBox.Show(message, $"关于 {name}",
            WinFormsMessageBoxButtons.OK,
            WinFormsMessageBoxIcon.Information);
    }

    /// <inheritdoc />
    public void SetIcon(byte[]? icon)
    {
        // 加载并缓存 HICON；同时释放被替换的旧句柄（对齐上游 #6502 的 setIcon）。
        // 加载失败或字节为空时保留当前图标，不改变已生效的窗口图标。
        var hicon = _appIconCache.Set(icon);
        if (hicon.IsNull)
        {
            return;
        }

        // 将图标应用到所有已创建窗口。
        ApplyIconToWindows(hicon);
    }

    /// <inheritdoc />
    public void On(uint id)
    {
        // 将平台事件 ID 投递到主线程消息循环，由 WndProc 处理。
        var hwnd = GetMainThreadHwnd();
        if (hwnd.IsNull)
        {
            // 无窗口时直接处理。
            Application.Get()?.HandlePlatformEvent(id);
            return;
        }

        PInvoke.PostMessage(hwnd, WmAppPlatformEvent, (WPARAM)(nuint)id, default);
    }

    /// <inheritdoc />
    public void DispatchOnMainThread(uint id)
    {
        // 同 On，通过 PostMessageW 投递到主线程。
        On(id);
    }

    /// <inheritdoc />
    public void Hide()
    {
        // 遍历所有窗口并隐藏。
        foreach (var window in EnumerateWindows())
        {
            window.Hide();
        }
    }

    /// <inheritdoc />
    public void Show()
    {
        // 遍历所有窗口并显示。
        foreach (var window in EnumerateWindows())
        {
            window.Show();
        }
    }

    /// <inheritdoc />
    public void DispatchOnMainThread(Action action)
    {
        if (action is null)
        {
            return;
        }

        // 如果已在主线程，直接执行。
        if (IsOnMainThread())
        {
            action();
            return;
        }

        // 将 Action 入队，通过 PostMessageW 投递 WM_APP+2 到主线程消息循环。
        _actionQueue.Enqueue(action);
        var hwnd = GetMainThreadHwnd();
        if (hwnd.IsNull)
        {
            // 无窗口时回退：直接执行（无法跨线程投递）。
            try
            {
                action();
            }
            catch
            {
                // 忽略回退执行中的异常
            }

            return;
        }

        PInvoke.PostMessage(hwnd, WmAppDispatchAction, default, default);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Windows 通过 WebView2 <c>WebMessageReceived</c>（接收）与 <c>PostWebMessageAsJson</c>（投递）
    /// 提供原生消息通道，回调由 <see cref="Win32WebviewWindow.SetNativeMessageHandler"/> 安装，
    /// 因此声明为支持。RH-WIN-02 起 <c>WindowManager</c> 据此决定是否注册到 NativeIpcTransport。
    /// </remarks>
    public bool SupportsNativeMessageChannel => true;

    /// <inheritdoc />
    public void AttachWindowAccessor(IPlatformWindowAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        _windowAccessor = accessor;
    }

    /// <inheritdoc />
    public IWebviewWindowImpl? CreateWebviewWindow(uint id, WebviewWindowOptions options)
    {
        // 创建 Win32WebviewWindow 实例。窗口登记由 WindowManager 完成（RH-WIN-02 终态），
        // 平台侧不再持有窗口字典。
        // 将应用级 WindowsOptions 传入窗口，用于配置 WebView2 环境（用户数据目录、浏览器路径等）。
        var window = new Win32WebviewWindow(id, options, _windowsOptions);

        // 应用暗色模式。
        ApplyDarkModeToWindow(window.Hwnd, IsDarkMode());

        // 应用菜单（若已设置）。
        if (_applicationMenu is not null)
        {
            window.SetMenuHandle(_applicationMenu.Hmenu);
        }

        // 禁用图标：先加 WS_EX_DLGMODALFRAME 让标题栏/任务栏/Alt+Tab 不显示图标
        // （对齐上游 windowsWebviewWindow.run() 中的 disableIcon()）。
        if (_windowsOptions?.DisableIcon == true)
        {
            window.DisableWindowIcon();
        }

        // 应用最近一次 SetIcon 的图标（复用缓存句柄，对齐上游 run() 中 currentIcon 的套用逻辑）。
        var hicon = _appIconCache.Current;
        if (!hicon.IsNull)
        {
            ApplyIcon(window, hicon);
        }

        // P0-C2：不再在此注册 NativeIpcTransport。
        // RH-WIN-02 起窗口注册/注销统一由 WindowManager 依据
        // IPlatformApp.SupportsNativeMessageChannel 完成（本类声明为 true），
        // 避免平台层回退到 Application.Get() 静态定位器。

        // 显示窗口。Win32 CreateWindowEx 默认创建不可见窗口（无 WS_VISIBLE 样式），
        // 必须显式调用 ShowWindow 才能显示。对应 Wails v3 Go 版本中
        // window.Show() 的调用。
        window.Show();

        return window;
    }

    /// <inheritdoc />
    public Task<int> ShowMessageDialog(string title, string message, DialogStyle style, string[] buttons)
    {
        return Task.FromResult(ShowMessageBox(title, message, style, buttons));
    }

    /// <inheritdoc />
    /// <remarks>
    /// 询问样式且提供按钮时优先使用 TaskDialogIndirect，以显示自定义按钮标签与默认/取消语义；
    /// comctl32 v6 不可用（无 manifest）或调用失败时回退 MessageBox，并按标签/位置映射结果后触发回调。
    /// 对应 Wails v3 Go 版本 dialogs_windows.go 中 <c>windowsDialog.show</c> 的分派逻辑。
    /// </remarks>
    public Task<int> ShowMessageDialog(MessageDialogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Style == DialogStyle.Question && options.Buttons.Count > 0)
        {
            if (Win32TaskDialog.TryShow(options, out var taskDialogIndex))
            {
                options.InvokeButtonCallback(taskDialogIndex);
                return Task.FromResult(taskDialogIndex);
            }

            // 回退：MessageBox 无法渲染自定义标签，按 Go messageBoxButtonFor 的标签/位置规则映射。
            var fallbackButtons = options.Buttons.Count >= 2
                ? WinFormsMessageBoxButtons.YesNo
                : WinFormsMessageBoxButtons.OK;
            // 对应 Go calculateMessageDialogFlags：第二个按钮为默认时使用 MB_DEFBUTTON2。
            var fallbackDefault = options.Buttons.Count >= 2 && options.Buttons[1].IsDefault
                ? WinFormsMessageBoxDefaultButton.Button2
                : WinFormsMessageBoxDefaultButton.Button1;
            var fallbackResult = WinFormsMessageBox.Show(
                options.Message, options.Title, fallbackButtons, WinFormsMessageBoxIcon.Question, fallbackDefault);
            var fallbackId = DialogResultToMessageBoxId(fallbackResult);
            var fallbackIndex = Win32TaskDialog.ResolveMessageBoxButtonIndex(options.Buttons, fallbackId);
            if (fallbackIndex < 0 && fallbackId == 1)
            {
                // 单按钮对话框点击“确定”时按位置回退到第一个按钮。
                fallbackIndex = 0;
            }

            options.InvokeButtonCallback(fallbackIndex);
            return Task.FromResult(fallbackIndex);
        }

        var index = ShowMessageBox(options.Title, options.Message, options.Style, options.GetButtonLabels());
        options.InvokeButtonCallback(index);
        return Task.FromResult(index);
    }

    /// <summary>
    /// 使用 WinForms MessageBox 显示消息对话框并返回按钮索引。
    /// </summary>
    /// <param name="title">对话框标题。</param>
    /// <param name="message">消息内容。</param>
    /// <param name="style">对话框样式。</param>
    /// <param name="buttons">按钮文本数组（仅用于决定标准按钮数量）。</param>
    /// <returns>被点击按钮的索引。</returns>
    private static int ShowMessageBox(string title, string message, DialogStyle style, string[] buttons)
    {
        // 根据 DialogStyle 映射 MessageBoxIcon。
        var icon = style switch
        {
            DialogStyle.Info => WinFormsMessageBoxIcon.Information,
            DialogStyle.Warning => WinFormsMessageBoxIcon.Warning,
            DialogStyle.Error => WinFormsMessageBoxIcon.Error,
            DialogStyle.Question => WinFormsMessageBoxIcon.Question,
            _ => WinFormsMessageBoxIcon.None
        };

        // 根据按钮数量映射 MessageBoxButtons。
        var buttonFlags = buttons.Length switch
        {
            0 or 1 => WinFormsMessageBoxButtons.OK,
            2 => WinFormsMessageBoxButtons.OKCancel,
            3 => WinFormsMessageBoxButtons.YesNoCancel,
            _ => WinFormsMessageBoxButtons.OK
        };

        var result = WinFormsMessageBox.Show(message, title, buttonFlags, icon);

        // 将 DialogResult 映射回按钮索引。
        return MapDialogResultToIndex(result);
    }

    /// <summary>
    /// 将 MessageBox 的 <see cref="DialogResult"/> 映射为按钮索引。
    /// </summary>
    /// <param name="result">MessageBox 返回值。</param>
    /// <returns>按钮索引。</returns>
    private static int MapDialogResultToIndex(DialogResult result)
    {
        return result switch
        {
            DialogResult.OK or DialogResult.Yes => 0,
            DialogResult.Cancel or DialogResult.No => 1,
            DialogResult.Abort or DialogResult.Retry => 2,
            DialogResult.Ignore => 3,
            _ => 0
        };
    }

    /// <summary>
    /// 将 MessageBox 的 <see cref="DialogResult"/> 映射为标准 MessageBox 返回 ID，
    /// 供 <see cref="Win32TaskDialog.ResolveMessageBoxButtonIndex"/> 按 Go 语义匹配按钮。
    /// </summary>
    /// <param name="result">MessageBox 返回值。</param>
    /// <returns>MessageBox 标准返回 ID（IDOK=1、IDCANCEL=2、IDYES=6、IDNO=7 等）。</returns>
    private static int DialogResultToMessageBoxId(DialogResult result)
    {
        return result switch
        {
            DialogResult.OK => 1,
            DialogResult.Cancel => 2,
            DialogResult.Abort => 3,
            DialogResult.Retry => 4,
            DialogResult.Ignore => 5,
            DialogResult.Yes => 6,
            DialogResult.No => 7,
            DialogResult.TryAgain => 10,
            DialogResult.Continue => 11,
            _ => 0
        };
    }

    /// <inheritdoc />
    public Task<string?> OpenFileDialog(OpenFileDialogOptions options)
    {
        // 使用 WinForms OpenFileDialog 选择单个文件。
        using var dialog = new WinFormsOpenFileDialog
        {
            Multiselect = false,
            Title = options.Title ?? string.Empty,
            InitialDirectory = options.Directory ?? string.Empty,
            Filter = BuildFilter(options.Filters)
        };

        var result = dialog.ShowDialog();
        var path = result == DialogResult.OK && !string.IsNullOrEmpty(dialog.FileName)
            ? dialog.FileName
            : null;

        return Task.FromResult(path);
    }

    /// <inheritdoc />
    public Task<string?> SaveFileDialog(SaveFileDialogOptions options)
    {
        // 使用 WinForms SaveFileDialog 选择保存路径。
        using var dialog = new WinFormsSaveFileDialog
        {
            Title = options.Title ?? string.Empty,
            InitialDirectory = options.Directory ?? string.Empty,
            FileName = options.Filename ?? string.Empty,
            Filter = BuildFilter(options.Filters)
        };

        var result = dialog.ShowDialog();
        var path = result == DialogResult.OK && !string.IsNullOrEmpty(dialog.FileName)
            ? dialog.FileName
            : null;

        return Task.FromResult(path);
    }

    /// <inheritdoc />
    public Task<string[]?> OpenMultipleFilesDialog(OpenFileDialogOptions options)
    {
        // 使用 WinForms OpenFileDialog 选择多个文件。
        using var dialog = new WinFormsOpenFileDialog
        {
            Multiselect = true,
            Title = options.Title ?? string.Empty,
            InitialDirectory = options.Directory ?? string.Empty,
            Filter = BuildFilter(options.Filters)
        };

        var result = dialog.ShowDialog();
        string[]? files = result == DialogResult.OK && dialog.FileNames.Length > 0
            ? dialog.FileNames
            : null;

        return Task.FromResult(files);
    }

    /// <summary>
    /// 尝试获取单实例锁。使用命名互斥体实现：若互斥体已存在则表示已有实例运行。
    /// 对应 Wails v3 Go 版本 application_windows.go 中 SingleInstanceLock 实现。
    /// </summary>
    /// <param name="uniqueId">用于标识应用的唯一 ID。</param>
    /// <returns>成功获取锁（首实例）返回 true，否则返回 false。</returns>
    public bool AcquireSingleInstanceLock(string uniqueId)
    {
        if (string.IsNullOrEmpty(uniqueId))
        {
            return true;
        }

        try
        {
            var mutexName = "Global\\" + uniqueId;
            var mutex = PInvoke.CreateMutex(null, false, mutexName);
            var lastError = Marshal.GetLastWin32Error();
            if (lastError == ErrorAlreadyExists)
            {
                // 互斥体已存在：已有实例运行，释放当前句柄并返回 false。
                mutex?.Dispose();
                return false;
            }

            _singleInstanceMutex = mutex;
            return true;
        }
        catch
        {
            // 互斥体创建失败时视作首实例（保守策略）。
            return true;
        }
    }

    /// <summary>
    /// 通知已运行的前一个实例：有新实例尝试启动。
    /// 对应 Wails v3 Go 版本 application_single_instance.go 中的 notifySingleInstance 函数。
    /// 将命令行参数序列化为 JSON 写入临时文件，再通过 PostMessage 通知已运行实例，
    /// 已运行实例的 WndProc 收到 WM_APP+1 后读取该文件并分发事件。
    /// </summary>
    /// <param name="args">新实例启动时传入的命令行参数。</param>
    public void NotifySingleInstance(string[] args)
    {
        try
        {
            // 将命令行参数序列化为 JSON 写入临时文件，供已运行实例读取。
            var argsFile = GetSingleInstanceArgsFilePath();
            var json = System.Text.Json.JsonSerializer.Serialize(args);
            File.WriteAllText(argsFile, json);

            // 广播单实例通知消息到所有顶层窗口，已运行实例的 WndProc 将读取临时文件。
            PInvoke.PostMessage(default, WmAppSingleInstance, default, default);
        }
        catch
        {
            // 通知失败时忽略
        }
    }

    /// <summary>
    /// 获取单实例通知参数临时文件路径。
    /// 文件名基于应用名称，确保新旧实例使用相同路径。
    /// </summary>
    /// <returns>临时文件路径。</returns>
    private string GetSingleInstanceArgsFilePath()
    {
        return Path.Combine(Path.GetTempPath(), $"wailsnet_{_name}_single_instance_args.json");
    }

    /// <summary>
    /// 处理单实例通知：读取临时文件中的命令行参数并分发到应用事件系统。
    /// 由 WndProc 在收到 WM_APP+1（WmAppSingleInstance）时调用。
    /// 对应 Wails v3 Go 版本中已运行实例收到通知后读取 args 并触发事件的逻辑。
    /// </summary>
    internal static void HandleSingleInstanceNotification()
    {
        var app = s_current;
        if (app is null)
        {
            return;
        }

        try
        {
            var argsFile = app.GetSingleInstanceArgsFilePath();
            if (!File.Exists(argsFile))
            {
                return;
            }

            var json = File.ReadAllText(argsFile);
            var args = System.Text.Json.JsonSerializer.Deserialize<string[]>(json);
            if (args is not null)
            {
                // 通过 Application 集中入口分发 SecondInstanceLaunched 事件并触发用户回调。
                // 对应主题 D：替换魔法字符串为 KnownEvents.SecondInstanceLaunched 常量。
                Application.Get()?.RaiseSecondInstanceLaunched(args);
            }

            // 读取完成后删除临时文件。
            try
            {
                File.Delete(argsFile);
            }
            catch
            {
                // 临时文件删除失败时忽略
            }
        }
        catch
        {
            // 读取或解析失败时忽略
        }
    }

    /// <summary>
    /// 排空主线程待执行 Action 队列。由 WndProc 在收到 WM_APP+2 时调用。
    /// </summary>
    internal static void DrainActionQueue()
    {
        var app = s_current;
        if (app is null)
        {
            return;
        }

        while (app._actionQueue.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch
            {
                // 单个 Action 异常不应中断后续执行
            }
        }
    }

    /// <summary>
    /// 获取主线程窗口句柄（第一个窗口），用于 PostMessage 投递。
    /// </summary>
    /// <returns>主窗口句柄，若无窗口则返回 NULL。</returns>
    private HWND GetMainThreadHwnd()
    {
        foreach (var window in EnumerateWindows())
        {
            if (!window.Hwnd.IsNull)
            {
                return window.Hwnd;
            }
        }

        return default;
    }

    /// <summary>
    /// 设置进程 DPI 感知：优先 Per-Monitor V2（Windows 10 1703+），回退 SetProcessDPIAware。
    /// </summary>
    private static void SetDpiAwareness()
    {
        try
        {
            // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 句柄值为 (IntPtr)(-4)。
            // CsWin32 生成 DPI_AWARENESS_CONTEXT 类型（位于 Windows.Win32.UI.HiDpi 命名空间）。
            var ctx = new DPI_AWARENESS_CONTEXT((IntPtr)(-4));
            if (PInvoke.SetProcessDpiAwarenessContext(ctx))
            {
                return;
            }
        }
        catch (EntryPointNotFoundException)
        {
            // 旧版本 Windows 不支持 SetProcessDpiAwarenessContext，回退。
        }
        catch (TypeLoadException)
        {
            // 类型加载失败时回退。
        }
        catch
        {
            // 其他异常时回退。
        }

        try
        {
            PInvoke.SetProcessDPIAware();
        }
        catch
        {
            // 忽略 DPI 设置失败
        }
    }

    /// <summary>
    /// 将暗色模式应用到指定窗口（DWM 标题栏 + 原生菜单主题）。
    /// 委托给 <see cref="Win32Theme.SetTheme"/>，对应 Wails v3 theme.go SetTheme。
    /// </summary>
    /// <param name="hwnd">窗口句柄。</param>
    /// <param name="dark">是否暗色模式。</param>
    internal static void ApplyDarkModeToWindow(HWND hwnd, bool dark)
    {
        if (hwnd.IsNull)
        {
            return;
        }

        Win32Theme.SetTheme(hwnd, dark);
    }

    /// <summary>
    /// 将图标字节数据加载为 HICON。写入临时 .ico 文件后通过 LoadImage 加载。
    /// </summary>
    /// <param name="iconBytes">图标字节数据。</param>
    /// <returns>HICON 句柄，加载失败返回 NULL。</returns>
    internal static HICON LoadIconFromBytes(byte[] iconBytes)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"wailsnet_icon_{Guid.NewGuid():N}.ico");
        try
        {
            File.WriteAllBytes(tempPath, iconBytes);
            var handle = PInvoke.LoadImage(
                default,
                tempPath,
                (GDI_IMAGE_TYPE)1, // IMAGE_ICON = 1
                0,
                0,
                IMAGE_FLAGS.LR_LOADFROMFILE | IMAGE_FLAGS.LR_DEFAULTSIZE);
            var iconPtr = handle.DangerousGetHandle();
            GC.SuppressFinalize(handle);
            return (HICON)iconPtr;
        }
        catch
        {
            return default;
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // 临时文件清理失败时忽略
            }
        }
    }

    /// <summary>
    /// 将图标应用到所有已创建窗口。
    /// </summary>
    private void ApplyIconToWindows(HICON hicon)
    {
        if (hicon.IsNull)
        {
            return;
        }

        foreach (var window in EnumerateWindows())
        {
            ApplyIcon(window, hicon);
        }
    }

    /// <summary>
    /// 将图标句柄应用到单个窗口。
    /// <para>
    /// 对应上游 <c>windowsApp.applyIcon</c>：<see cref="WindowsOptions.DisableIcon"/> 为 true 时，
    /// 临时清除 <c>WS_SYSMENU</c> 再设置图标，避免 Windows 把图标重新加回标题栏。
    /// </para>
    /// </summary>
    /// <param name="window">目标窗口。</param>
    /// <param name="hicon">图标句柄（由 <see cref="_appIconCache"/> 持有，窗口不得释放）。</param>
    private void ApplyIcon(Win32WebviewWindow window, HICON hicon)
    {
        if (_windowsOptions?.DisableIcon == true)
        {
            window.SetIconHandleWithoutSystemMenu(hicon);
            return;
        }

        window.SetIconHandle(hicon);
    }

    /// <summary>
    /// 销毁图标句柄。作为 <see cref="Win32AppIconCache"/> 的默认释放器。
    /// 对应上游 <c>w32.DestroyIcon</c>。
    /// </summary>
    /// <param name="icon">要销毁的图标句柄。</param>
    internal static void DestroyIconHandle(HICON icon)
    {
        if (icon.IsNull)
        {
            return;
        }

        PInvoke.DestroyIcon(icon);
    }

    /// <summary>
    /// 将 Wails 文件过滤器数组转换为 WinForms Filter 字符串。
    /// </summary>
    /// <param name="filters">过滤器数组，每项格式为 "描述|扩展名" 或直接为扩展名如 "*.txt"。</param>
    /// <returns>WinForms Filter 字符串，例如 "文本文件|*.txt|所有文件|*.*"。</returns>
    private static string BuildFilter(string[]? filters)
    {
        if (filters is null || filters.Length == 0)
        {
            return "所有文件|*.*";
        }

        var parts = new List<string>(filters.Length);
        foreach (var filter in filters)
        {
            if (string.IsNullOrEmpty(filter))
            {
                continue;
            }

            // 若过滤器已包含 "|" 则直接使用，否则按扩展名生成描述。
            if (filter.Contains('|'))
            {
                parts.Add(filter);
            }
            else
            {
                parts.Add($"{filter}|{filter}");
            }
        }

        return parts.Count == 0 ? "所有文件|*.*" : string.Join("|", parts);
    }

    /// <summary>
    /// 将 WinForms Screen 转换为 Wails.Net Screen。
    /// </summary>
    /// <param name="screen">WinForms Screen 实例。</param>
    /// <param name="isPrimary">是否为主屏幕。</param>
    /// <returns>Wails.Net Screen 实例。</returns>
    private static Screen ToScreen(WinFormsScreen screen, bool isPrimary)
    {
        var bounds = screen.Bounds;
        var workArea = screen.WorkingArea;

        // 使用 GetDpiForSystem 计算缩放比例，失败时回退 1.0。
        var scaleFactor = GetSystemScaleFactor();

        // Windows 使用 DeviceName 作为屏幕唯一标识符，对应 Wails v3 Screen.ID。
        // WinForms Screen 未直接暴露旋转角度，此处保持 0。
        return new Screen(
            id: screen.DeviceName,
            name: screen.DeviceName,
            x: bounds.X,
            y: bounds.Y,
            width: bounds.Width,
            height: bounds.Height,
            workAreaX: workArea.X,
            workAreaY: workArea.Y,
            workAreaWidth: workArea.Width,
            workAreaHeight: workArea.Height,
            scaleFactor: scaleFactor,
            isPrimary: isPrimary,
            rotation: 0);
    }

    /// <summary>
    /// 获取系统 DPI 缩放比例（96 DPI = 1.0）。
    /// </summary>
    /// <returns>缩放比例。</returns>
    private static float GetSystemScaleFactor()
    {
        try
        {
            var dpi = PInvoke.GetDpiForSystem();
            return dpi > 0 ? dpi / 96.0f : 1.0f;
        }
        catch
        {
            return 1.0f;
        }
    }
}
