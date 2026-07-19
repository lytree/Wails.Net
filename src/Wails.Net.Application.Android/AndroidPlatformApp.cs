using System.Collections.Concurrent;
using Android.App;
using Android.Content;
using Android.Content.Res;
using Android.OS;
using Wails.Net.Application.Android;
using Wails.Net.Application.Android.Mobile;
using Wails.Net.Application.Dialogs;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Options;
using Wails.Net.Application.Screens;
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
    /// 构造时记录的调用方线程 ID。
    /// 注意：构造函数在后台线程（StartWailsApp）执行，此 ID 不是 Android UI 线程 ID，
    /// 仅用于非 Android 环境（如单元测试）回退判断主线程，与 <see cref="_mainLooper"/> 配合使用。
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
    /// 也用于 SAF 文件对话框启动 <c>StartActivityForResult</c>。
    /// </summary>
    private Activity? _activity;

    /// <summary>
    /// SAF（Storage Access Framework）文件对话框请求码基址。
    /// 使用 0x1000+ 范围避免与 Android 系统/其他库的请求码冲突。
    /// </summary>
    private const int RequestOpenFile = 0x1001;

    /// <summary>SAF 保存文件对话框请求码。</summary>
    private const int RequestSaveFile = 0x1002;

    /// <summary>SAF 多选文件对话框请求码。</summary>
    private const int RequestOpenMultipleFiles = 0x1003;

    /// <summary>
    /// 待处理的 Activity 结果回调注册表，按请求码索引。
    /// SAF 文件对话框启动 <c>StartActivityForResult</c> 时注册回调，
    /// <see cref="HandleActivityResult"/> 在 <c>Activity.OnActivityResult</c> 中查找并完成对应的 TaskCompletionSource。
    /// 使用 <see cref="ConcurrentDictionary{TKey, TValue}"/> 保证注册（任意线程）与回调（主线程）的线程安全。
    /// </summary>
    private readonly ConcurrentDictionary<int, Action<Result, Intent?>> _pendingActivityResultCallbacks = new();

    /// <summary>
    /// Android 震动反馈实现，由 <c>UseAndroid</c> 扩展方法注入。
    /// 对应 Wails v3 messageprocessor_android.go 中的 <c>androidHapticsVibrate</c>。
    /// </summary>
    public AndroidHaptics? MobileHaptics { get; set; }

    /// <summary>
    /// Android 生物识别实现，由 <c>UseAndroid</c> 扩展方法注入。
    /// 对应 Tauri v2 <c>@tauri-apps/plugin-biometric</c> 的 Android 后端。
    /// </summary>
    public AndroidBiometric? MobileBiometric { get; set; }

    /// <summary>
    /// Android NFC 实现，由 <c>UseAndroid</c> 扩展方法注入。
    /// 对应 Tauri v2 <c>@tauri-apps/plugin-nfc</c> 的 Android 后端。
    /// </summary>
    public AndroidNfc? MobileNfc { get; set; }

    /// <summary>
    /// Android 条码扫描实现，由 <c>UseAndroid</c> 扩展方法注入。
    /// 对应 Tauri v2 <c>@tauri-apps/plugin-barcode-scanner</c> 的 Android 后端。
    /// </summary>
    public AndroidBarcodeScanner? MobileBarcodeScanner { get; set; }

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

    /// <summary>
    /// 处理 <c>Activity.OnActivityResult</c> 回调，根据 <paramref name="requestCode"/> 路由到对应的 SAF 文件对话框回调。
    /// 在 <c>MainActivity.OnActivityResult</c> 中调用此方法，将结果转发给正在等待的 <c>TaskCompletionSource</c>。
    /// <para>
    /// 若 <paramref name="requestCode"/> 未在注册表中找到对应回调（如未启动 SAF 对话框或已被取消），
    /// 此方法为 no-op，不抛异常。
    /// </para>
    /// </summary>
    /// <param name="requestCode">请求码，由 <see cref="OpenFileDialog"/> / <see cref="SaveFileDialog"/> / <see cref="OpenMultipleFilesDialog"/> 启动时传入。</param>
    /// <param name="resultCode">Activity 结果码（<see cref="Result.Ok"/> / <see cref="Result.Canceled"/>）。</param>
    /// <param name="data">返回的 Intent，包含选中的文件 URI（<c>Intent.Data</c> 或 <c>Intent.ClipData</c>）。</param>
    public void HandleActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (_pendingActivityResultCallbacks.TryRemove(requestCode, out var callback))
        {
            callback(resultCode, data);
        }
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
        // 对应 Wails v3 events_common_android.go 中的 setupCommonEvents 转发：
        // 若传入 Android 平台事件 ID（如 ActivityCreated=1267）且存在 Common 事件映射，
        // 则转发为公共事件 ID；否则按原 ID 分发。
        var commonEvent = AndroidPlatformEvents.MapToCommonEvent(id);
        var dispatchId = commonEvent is not null ? (uint)commonEvent.Value : id;
        DispatchPlatformEventOnMainThread(dispatchId);
    }

    /// <inheritdoc />
    public void DispatchOnMainThread(uint id)
    {
        // 将事件 ID 分发到主线程执行。
        DispatchPlatformEventOnMainThread(id);
    }

    // ---------------------------------------------------------------------
    // Activity 生命周期集成（对应 Wails v3 events_common_android.go 中
    // 由 Activity JNI 回调触发的 Android.* 事件，转发到 Common 事件）
    // ---------------------------------------------------------------------

    /// <summary>
    /// 在 Activity.OnCreate 中调用，触发 <c>Android.ActivityCreated</c> 平台事件，
    /// 经 <see cref="AndroidPlatformEvents"/> 映射为 <c>Common.ApplicationStarted</c> 公共事件。
    /// 对应 Wails v3 <c>androidApp.onActivityCreate</c>。
    /// </summary>
    /// <param name="activity">当前 Activity 实例。</param>
    public void OnActivityCreated(Activity activity)
    {
        SetActivity(activity);
        On(AndroidPlatformEvents.ActivityCreated);
    }

    /// <summary>
    /// 在 Activity.OnStart 中调用，触发 <c>Android.ActivityStarted</c> 平台事件。
    /// 该事件未映射到公共事件，仅按 Android 专属事件 ID 分发。
    /// </summary>
    public void OnActivityStarted()
    {
        DispatchPlatformEventOnMainThread(AndroidPlatformEvents.ActivityStarted);
    }

    /// <summary>
    /// 在 Activity.OnResume 中调用，触发 <c>Android.ActivityResumed</c> 平台事件。
    /// </summary>
    public void OnActivityResumed()
    {
        DispatchPlatformEventOnMainThread(AndroidPlatformEvents.ActivityResumed);
    }

    /// <summary>
    /// 在 Activity.OnPause 中调用，触发 <c>Android.ActivityPaused</c> 平台事件。
    /// </summary>
    public void OnActivityPaused()
    {
        DispatchPlatformEventOnMainThread(AndroidPlatformEvents.ActivityPaused);
    }

    /// <summary>
    /// 在 Activity.OnStop 中调用，触发 <c>Android.ActivityStopped</c> 平台事件。
    /// </summary>
    public void OnActivityStopped()
    {
        DispatchPlatformEventOnMainThread(AndroidPlatformEvents.ActivityStopped);
    }

    /// <summary>
    /// 在 Activity.OnDestroy 中调用，触发 <c>Android.ActivityDestroyed</c> 平台事件并清理 Activity 引用。
    /// </summary>
    public void OnActivityDestroyed()
    {
        DispatchPlatformEventOnMainThread(AndroidPlatformEvents.ActivityDestroyed);
        _activity = null;
    }

    /// <summary>
    /// 在 Activity.onLowMemory 中调用，触发 <c>Android.ApplicationLowMemory</c> 平台事件，
    /// 经 <see cref="AndroidPlatformEvents"/> 映射为 <c>Common.LowMemory</c> 公共事件。
    /// 对应 Wails v3 <c>androidApp.onLowMemory</c>。
    /// </summary>
    public void OnLowMemory()
    {
        On(AndroidPlatformEvents.ApplicationLowMemory);
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
        // 注：构造函数在后台线程（StartWailsApp）执行，故 _mainThreadId 实际不是 UI 线程 ID，
        // 仅作为非 Android 环境的回退判断（测试场景）。
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
    /// <remarks>
    /// Android 平台通过 Storage Access Framework（SAF）实现文件选择：
    /// 使用 <c>Intent.ActionOpenDocument</c> 启动系统文件选择器，通过 <c>Activity.StartActivityForResult</c>
    /// 等待用户选择，在 <c>OnActivityResult</c> 中通过 <c>TaskCompletionSource</c> 完成回调。
    /// 返回的字符串为内容 URI（如 <c>content://com.android.providers.documents/document/abc</c>），
    /// 前端可通过 <c>ContentResolver.OpenInputStream</c> 读取。
    /// <para>
    /// 非 Android 环境（单元测试）或未注入 Activity 时返回 null。
    /// </para>
    /// </remarks>
    public Task<string?> OpenFileDialog(OpenFileDialogOptions options)
    {
        var activity = _activity;
        if (activity is null)
        {
            // 非 Android 环境或未注入 Activity：返回 null（测试场景降级）
            return Task.FromResult<string?>(null);
        }

        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        // 注册回调：在 OnActivityResult 中根据结果码完成 TaskCompletionSource
        _pendingActivityResultCallbacks[RequestOpenFile] = (resultCode, data) =>
        {
            if (resultCode == Result.Ok && data?.Data is not null)
            {
                tcs.TrySetResult(data.Data.ToString());
            }
            else
            {
                tcs.TrySetResult(null);
            }
        };

        // 在主线程启动 SAF Intent（StartActivityForResult 必须在 UI 线程调用）
        DispatchOnMainThread(() =>
        {
            try
            {
                var intent = new Intent(Intent.ActionOpenDocument);
                intent.AddCategory(Intent.CategoryOpenable);
                intent.SetType(BuildMimeType(options.Filters));
                // 单选模式：显式设置 ExtraAllowMultiple=false
                intent.PutExtra(Intent.ExtraAllowMultiple, false);
                activity.StartActivityForResult(intent, RequestOpenFile);
            }
            catch (Exception)
            {
                // 启动失败（如无 Activity 处理该 Intent）：移除回调并完成 Task
                _pendingActivityResultCallbacks.TryRemove(RequestOpenFile, out _);
                tcs.TrySetResult(null);
            }
        });

        return tcs.Task;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Android 平台通过 SAF 的 <c>Intent.ActionCreateDocument</c> 实现保存文件对话框。
    /// 用户选择目标位置后返回内容 URI，前端可通过 <c>ContentResolver.OpenOutputStream</c> 写入。
    /// <para>
    /// 非 Android 环境（单元测试）或未注入 Activity 时返回 null。
    /// </para>
    /// </remarks>
    public Task<string?> SaveFileDialog(SaveFileDialogOptions options)
    {
        var activity = _activity;
        if (activity is null)
        {
            return Task.FromResult<string?>(null);
        }

        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        _pendingActivityResultCallbacks[RequestSaveFile] = (resultCode, data) =>
        {
            if (resultCode == Result.Ok && data?.Data is not null)
            {
                tcs.TrySetResult(data.Data.ToString());
            }
            else
            {
                tcs.TrySetResult(null);
            }
        };

        DispatchOnMainThread(() =>
        {
            try
            {
                var intent = new Intent(Intent.ActionCreateDocument);
                intent.AddCategory(Intent.CategoryOpenable);
                intent.SetType(BuildMimeType(options.Filters));
                // 设置建议文件名（作为对话框标题栏的默认输入）
                if (!string.IsNullOrEmpty(options.Filename))
                {
                    intent.PutExtra(Intent.ExtraTitle, options.Filename);
                }
                activity.StartActivityForResult(intent, RequestSaveFile);
            }
            catch (Exception)
            {
                _pendingActivityResultCallbacks.TryRemove(RequestSaveFile, out _);
                tcs.TrySetResult(null);
            }
        });

        return tcs.Task;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Android 平台通过 SAF 的 <c>Intent.ActionOpenDocument</c> + <c>Intent.ExtraAllowMultiple=true</c>
    /// 实现多选文件对话框。在 <c>OnActivityResult</c> 中解析 <c>Intent.ClipData</c> 获取多个 URI。
    /// <para>
    /// 非 Android 环境（单元测试）或未注入 Activity 时返回 null。
    /// </para>
    /// </remarks>
    public Task<string[]?> OpenMultipleFilesDialog(OpenFileDialogOptions options)
    {
        var activity = _activity;
        if (activity is null)
        {
            return Task.FromResult<string[]?>(null);
        }

        var tcs = new TaskCompletionSource<string[]?>(TaskCreationOptions.RunContinuationsAsynchronously);

        _pendingActivityResultCallbacks[RequestOpenMultipleFiles] = (resultCode, data) =>
        {
            if (resultCode != Result.Ok || data is null)
            {
                tcs.TrySetResult(null);
                return;
            }

            // 多选模式下，URI 通过 ClipData 返回（Intent.Data 仅包含第一项）
            var uris = new List<string>();
            var clipData = data.ClipData;
            if (clipData is not null)
            {
                for (int i = 0; i < clipData.ItemCount; i++)
                {
                    var item = clipData.GetItemAt(i);
                    if (item?.Uri is not null)
                    {
                        uris.Add(item.Uri.ToString()!);
                    }
                }
            }
            else if (data.Data is not null)
            {
                // 某些设备在用户只选一个文件时仍走 Data 路径
                uris.Add(data.Data.ToString()!);
            }

            tcs.TrySetResult(uris.Count > 0 ? uris.ToArray() : null);
        };

        DispatchOnMainThread(() =>
        {
            try
            {
                var intent = new Intent(Intent.ActionOpenDocument);
                intent.AddCategory(Intent.CategoryOpenable);
                intent.SetType(BuildMimeType(options.Filters));
                intent.PutExtra(Intent.ExtraAllowMultiple, true);
                activity.StartActivityForResult(intent, RequestOpenMultipleFiles);
            }
            catch (Exception)
            {
                _pendingActivityResultCallbacks.TryRemove(RequestOpenMultipleFiles, out _);
                tcs.TrySetResult(null);
            }
        });

        return tcs.Task;
    }

    /// <summary>
    /// 根据文件过滤器数组构建 SAF MIME 类型。
    /// Wails 过滤器格式为 <c>"描述 (*.ext1;*.ext2)"</c>，SAF 的 <c>SetType</c> 仅接受单一 MIME 类型。
    /// <para>
    /// 转换规则（保守实现，与 Wails v3 Android 行为一致）：
    /// <list type="bullet">
    ///   <item>过滤器为 null/空数组 → <c>"*/*"</c>（所有文件）。</item>
    ///   <item>无法识别的扩展名 → <c>"*/*"</c>。</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="filters">文件过滤器数组，可为 null。</param>
    /// <returns>SAF 兼容的 MIME 类型字符串。</returns>
    private static string BuildMimeType(string[]? filters)
    {
        if (filters is null || filters.Length == 0)
        {
            return "*/*";
        }

        // 简化实现：SAF SetType 仅支持单一 MIME 类型，多过滤器场景使用 */*
        // 完整的 MIME 推断需要扩展名映射表，此处保持与 Wails v3 一致的保守行为
        return "*/*";
    }
}
