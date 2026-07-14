using Android.App;
using Android.Content;
using Android.Content.Res;
using Android.OS;
using Wails.Net.Application.Dialogs;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Options;
using Wails.Net.Application.Screens;
using Wails.Net.Application.Windows;
using Action = System.Action;
using Array = System.Array;
using Environment = System.Environment;
using Menu = Wails.Net.Application.Menus.Menu;
using Screen = Wails.Net.Application.Screens.Screen;
using WailsApplication = Wails.Net.Application.Application;

namespace Wails.Net.Application.Platform;

/// <summary>
/// Android 平台应用实现。
/// 对应 ADR-0002：基于 .NET Android SDK + Android.Webkit.WebView。
/// 通过 Android Looper/Handler 提供主线程分发，AlertDialog 提供对话框，
/// Resources.DisplayMetrics 提供屏幕信息。
/// Run() 不阻塞主线程（与 Win/Linux 不同），通过 ManualResetEventSlim 等待 Destroy() 触发关闭信号。
/// </summary>
public sealed class AndroidPlatformApp : IPlatformApp
{
    /// <summary>
    /// 应用名称。
    /// </summary>
    private readonly string _name;

    /// <summary>
    /// 主线程 ID，构造时记录。当 <see cref="_mainLooper"/> 不可用时作为回退判断依据。
    /// </summary>
    private readonly int _mainThreadId;

    /// <summary>
    /// 已创建的 Webview 窗口字典，按窗口 ID 索引。
    /// </summary>
    private readonly Dictionary<uint, AndroidWebviewWindow> _windows = new();

    /// <summary>
    /// 关闭信号，由 <see cref="Destroy"/> 设置，唤醒 <see cref="Run"/> 返回。
    /// </summary>
    private readonly ManualResetEventSlim _shutdownSignal = new(initialState: false);

    /// <summary>
    /// Android 主线程 Looper 引用，构造时通过 <c>Looper.MainLooper</c> 获取。
    /// 用于 <see cref="IsOnMainThread"/> 判断与 <see cref="DispatchOnMainThread(Action)"/> 分发。
    /// </summary>
    private readonly Looper? _mainLooper;

    /// <summary>
    /// 静态资源服务器引用，可为 null（未配置 AssetServer 时）。
    /// 创建 WebviewWindow 时注入到窗口实例，用于资源拦截。
    /// </summary>
    private Wails.Net.AssetServer.AssetServer? _assetServer;

    /// <summary>
    /// 当前 Activity 引用，由 <see cref="SetActivity"/> 注入。
    /// 用于 WebView 创建时附加到 Activity 视图层级（调用 <c>Activity.SetContentView</c>）。
    /// </summary>
    private Activity? _activity;

    /// <summary>
    /// 构造 AndroidPlatformApp 实例。
    /// </summary>
    /// <param name="options">应用配置选项。</param>
    public AndroidPlatformApp(ApplicationOptions options)
    {
        _name = options.Name;
        _mainThreadId = Environment.CurrentManagedThreadId;
        _mainLooper = Looper.MainLooper;

        // 从全局 Application 实例获取 AssetServer 引用（若已配置）。
        // Application 可能在构造 PlatformApp 之前已经配置了 AssetServer。
        _assetServer = WailsApplication.Get()?.AssetServer;
    }

    /// <summary>
    /// 设置静态资源服务器引用。
    /// 当 Application.AssetServer 在 PlatformApp 构造后才配置时调用此方法。
    /// </summary>
    /// <param name="assetServer">AssetServer 实例。</param>
    public void SetAssetServer(Wails.Net.AssetServer.AssetServer? assetServer)
    {
        _assetServer = assetServer;
    }

    /// <summary>
    /// 设置当前 Activity 引用。
    /// 在 Activity.OnCreate 中调用，使平台应用能将 WebView 附加到 Activity 视图层级。
    /// </summary>
    /// <param name="activity">当前 Activity 实例。</param>
    public void SetActivity(Activity activity)
    {
        _activity = activity;
    }

    /// <summary>
    /// 获取当前 Activity 引用。
    /// 供 <see cref="AndroidWebviewWindow"/> 创建 WebView 时使用，以附加到视图层级。
    /// </summary>
    /// <returns>当前 Activity；未设置时返回 null（测试场景回退）。</returns>
    internal Activity? GetActivity()
    {
        return _activity;
    }

    /// <inheritdoc />
    public string Name => _name;

    /// <inheritdoc />
    public int Run()
    {
        // 对应 ADR-0002 §7：Run() 不阻塞主线程（与 Win/Linux 不同）。
        // Android 主线程由 Looper 管理，不应阻塞；此方法在后台线程调用，
        // 通过 ManualResetEventSlim 等待 Destroy() 触发关闭信号后返回退出码。
        _shutdownSignal.Wait();
        return 0;
    }

    /// <inheritdoc />
    public bool AcquireSingleInstanceLock(string uniqueId)
    {
        // Android 无单实例概念（每个应用默认单进程），始终返回 true。
        return true;
    }

    /// <inheritdoc />
    public void Destroy()
    {
        // 设置关闭信号，唤醒 Run() 返回。
        _shutdownSignal.Set();
    }

    /// <inheritdoc />
    public void SetApplicationMenu(Menu? menu)
    {
        // Android 无应用菜单概念，no-op。
    }

    /// <inheritdoc />
    public uint GetCurrentWindowId()
    {
        // 返回第一个窗口的 ID（简化实现，Android 通常单窗口）。
        foreach (var id in _windows.Keys)
        {
            return id;
        }

        return 0;
    }

    /// <inheritdoc />
    public void ShowAboutDialog(string name, string description, byte[]? icon)
    {
        DispatchOnMainThread(() =>
        {
            var context = global::Android.App.Application.Context;
            if (context is null)
            {
                return;
            }

            // 使用 null 宽容操作符告知编译器 context 此处必非 null
            // （前面已通过 is null 提前返回）。编译器无法在 lambda 闭包中跟踪该状态。
            // .NET Android 绑定的 AlertDialog.Builder 流式方法返回值可空注解不完美，
            // 故使用 #pragma 禁用 CS8602 警告。
#pragma warning disable CS8602
            new AlertDialog.Builder(context!)
                .SetTitle(name)
                .SetMessage(description)
                .SetPositiveButton("确定", (s, e) => { })
                .Show();
#pragma warning restore CS8602
        });
    }

    /// <inheritdoc />
    public void SetIcon(byte[]? icon)
    {
        // Android 应用图标由 AndroidManifest.xml 配置，运行时不可修改，no-op。
    }

    /// <inheritdoc />
    public void On(uint id)
    {
        // 将平台事件分发到主线程并交由 Application.HandlePlatformEvent 处理。
        DispatchPlatformEventOnMainThread(id);
    }

    /// <inheritdoc />
    public void DispatchOnMainThread(uint id)
    {
        // 将事件 ID 分发到主线程执行。
        DispatchPlatformEventOnMainThread(id);
    }

    /// <summary>
    /// 将平台事件分发到 Android 主线程（Handler.Post），由 Application.HandlePlatformEvent 处理。
    /// 非 Android 环境下直接同步处理。
    /// </summary>
    /// <param name="id">平台事件 ID。</param>
    private void DispatchPlatformEventOnMainThread(uint id)
    {
        if (_mainLooper is null)
        {
            WailsApplication.Get()?.HandlePlatformEvent(id);
            return;
        }

        new Handler(_mainLooper).Post(() =>
        {
            WailsApplication.Get()?.HandlePlatformEvent(id);
        });
    }

    /// <inheritdoc />
    public void Hide()
    {
        // Android 应用由系统管理可见性，no-op。
    }

    /// <inheritdoc />
    public void Show()
    {
        // Android 应用由系统管理可见性，no-op。
    }

    /// <inheritdoc />
    public Screen? GetPrimaryScreen()
    {
        var context = global::Android.App.Application.Context;
        if (context is null)
        {
            return null;
        }

        var metrics = context.Resources?.DisplayMetrics;
        if (metrics is null)
        {
            return null;
        }

        return new Screen
        {
            Name = "Primary Display",
            Width = metrics.WidthPixels,
            Height = metrics.HeightPixels,
            WorkAreaWidth = metrics.WidthPixels,
            WorkAreaHeight = metrics.HeightPixels,
            ScaleFactor = metrics.Density,
            IsPrimary = true,
        };
    }

    /// <inheritdoc />
    public Screen[] GetScreens()
    {
        // Android 通常单屏，返回包含主屏幕的单元素数组。
        var primary = GetPrimaryScreen();
        if (primary is null)
        {
            return Array.Empty<Screen>();
        }

        return new[] { primary };
    }

    /// <inheritdoc />
    public Dictionary<string, object?> GetFlags(ApplicationOptions options)
    {
        return new Dictionary<string, object?>();
    }

    /// <inheritdoc />
    public bool IsOnMainThread()
    {
        // 对应 ADR-0002：使用 Looper.MyLooper() == Looper.MainLooper 判断主线程。
        // 当 MainLooper 不可用时（非 Android 环境），回退到线程 ID 比较。
        if (_mainLooper is not null)
        {
            return Looper.MyLooper() == _mainLooper;
        }

        return Environment.CurrentManagedThreadId == _mainThreadId;
    }

    /// <inheritdoc />
    public bool IsDarkMode()
    {
        var context = global::Android.App.Application.Context;
        if (context is null)
        {
            return false;
        }

        var uiMode = context.Resources?.Configuration?.UiMode ?? UiMode.NightUndefined;
        return (uiMode & UiMode.NightMask) == UiMode.NightYes;
    }

    /// <inheritdoc />
    public string GetAccentColor()
    {
        // Android 无统一强调色概念（各应用自定义），返回空字符串。
        return string.Empty;
    }

    /// <inheritdoc />
    public void DispatchOnMainThread(Action action)
    {
        if (_mainLooper is null)
        {
            // 非 Android 环境直接执行（测试环境兼容）。
            action();
            return;
        }

        // 使用 Handler.Post 将操作投递到 Android 主线程。
        new Handler(_mainLooper).Post(action);
    }

    /// <inheritdoc />
    public void CreateWebviewWindow(uint id, WebviewWindowOptions options)
    {
        var window = new AndroidWebviewWindow(id, options, this, _assetServer);
        _windows[id] = window;
        window.Show();
    }

    /// <inheritdoc />
    public async Task<int> ShowMessageDialog(string title, string message, DialogStyle style, string[] buttons)
    {
        if (buttons is null || buttons.Length == 0)
        {
            return 0;
        }

        var tcs = new TaskCompletionSource<int>();
        DispatchOnMainThread(() =>
        {
            var context = global::Android.App.Application.Context;
            if (context is null)
            {
                tcs.SetResult(0);
                return;
            }

            // 使用 null 宽容操作符告知编译器 context 此处必非 null
            // （前面已通过 is null 提前返回）。编译器无法在 lambda 闭包中跟踪该状态。
            var builder = new AlertDialog.Builder(context!)!
                .SetTitle(title)!
                .SetMessage(message)!;

            // Android AlertDialog 最多支持 3 个按钮（Positive/Negative/Neutral）。
            if (buttons.Length >= 1)
            {
                builder.SetPositiveButton(buttons[0], (s, e) => tcs.SetResult(0));
            }

            if (buttons.Length >= 2)
            {
                builder.SetNegativeButton(buttons[1], (s, e) => tcs.SetResult(1));
            }

            if (buttons.Length >= 3)
            {
                builder.SetNeutralButton(buttons[2], (s, e) => tcs.SetResult(2));
            }

            builder.Show();
        });

        return await tcs.Task.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<string?> OpenFileDialog(OpenFileDialogOptions options)
    {
        // Android Storage Access Framework 需要 Activity 上下文启动 Intent：
        //   var intent = new Intent(Intent.ActionOpenDocument);
        //   intent.SetType("*/*");
        //   intent.AddCategory(Intent.CategoryOpenable);
        //   if (options.CanChooseMultiple) intent.PutExtra(Intent.ExtraAllowMultiple, true);
        //   Activity.StartActivityForResult(intent, REQUEST_OPEN_FILE);
        // 然后在 Activity.OnActivityResult 中通过 TaskCompletionSource<string?> 完成回调。
        // 骨架实现无 Activity 引用，返回 null。完整实现需注入 Activity 引用。
        return Task.FromResult<string?>(null);
    }

    /// <inheritdoc />
    public Task<string?> SaveFileDialog(SaveFileDialogOptions options)
    {
        // 使用 Intent.ActionCreateDocument + Activity.StartActivityForResult。
        //   var intent = new Intent(Intent.ActionCreateDocument);
        //   intent.SetType("*/*");
        //   intent.AddCategory(Intent.CategoryOpenable);
        //   if (!string.IsNullOrEmpty(options.Filename)) intent.PutExtra(Intent.ExtraTitle, options.Filename);
        // 骨架实现无 Activity 引用，返回 null。
        return Task.FromResult<string?>(null);
    }

    /// <inheritdoc />
    public Task<string[]?> OpenMultipleFilesDialog(OpenFileDialogOptions options)
    {
        // 使用 Intent.ActionOpenDocument + Intent.ExtraAllowMultiple + Activity.StartActivityForResult。
        // 在 OnActivityResult 中解析 ClipData 获取多个 URI。
        // 骨架实现无 Activity 引用，返回 null。
        return Task.FromResult<string[]?>(null);
    }
}
