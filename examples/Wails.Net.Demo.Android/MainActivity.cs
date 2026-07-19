using Android.App;
using Android.Content;
using Android.OS;
using Wails.Net.Application;
using Wails.Net.Application.Android;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;
using Wails.Net.Demo.Android.Services;
using WailsApplication = Wails.Net.Application.Application;

namespace Wails.Net.Demo.Android;

/// <summary>
/// Android Demo 应用的主 Activity 入口。
/// 对应 ADR-0002 §3：单 Activity + WebView 模型。
/// 在 <see cref="OnCreate"/> 中初始化 Wails.Net Application，
/// 使用自定义 scheme（http://wails.localhost/）通过 <see cref="AndroidAssetServer" /> 加载 assets 目录资源。
/// 应用主循环在后台线程运行，避免阻塞 Android UI 线程。
/// <para>
/// 生命周期回调通过 <see cref="AndroidPlatformApp"/> 转发为 Wails 平台事件
/// （对应 Wails v3 <c>events_common_android.go</c> 的 7 个事件映射）：
/// <list type="bullet">
///   <item><see cref="OnCreate"/> → <c>Android.ActivityCreated</c> → <c>Common.ApplicationStarted</c></item>
///   <item><see cref="OnStart"/> → <c>Android.ActivityStarted</c></item>
///   <item><see cref="OnResume"/> → <c>Android.ActivityResumed</c></item>
///   <item><see cref="OnPause"/> → <c>Android.ActivityPaused</c></item>
///   <item><see cref="OnStop"/> → <c>Android.ActivityStopped</c></item>
///   <item><see cref="OnDestroy"/> → <c>Android.ActivityDestroyed</c></item>
///   <item><see cref="OnLowMemory"/> → <c>Android.ApplicationLowMemory</c> → <c>Common.LowMemory</c></item>
/// </list>
/// </para>
/// </summary>
[Activity(Label = "Wails.Net Demo", MainLauncher = true)]
public class MainActivity : Activity
{
    /// <summary>
    /// Wails.Net 应用实例。
    /// </summary>
    private WailsApplication? _app;

    /// <summary>
    /// Android 平台应用实例引用，用于生命周期事件转发。
    /// </summary>
    private AndroidPlatformApp? _platformApp;

    /// <summary>
    /// 后台应用线程，运行 Application.Run() 阻塞主循环。
    /// </summary>
    private Thread? _appThread;

    /// <summary>
    /// Activity 创建时调用，初始化 Wails.Net 应用并启动主循环。
    /// </summary>
    /// <param name="savedInstanceState">保存的实例状态，首次启动为 null。</param>
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // 在后台线程启动 Wails.Net Application，避免阻塞 Android UI 线程。
        _appThread = new Thread(StartWailsApp)
        {
            IsBackground = true,
            Name = "Wails.Net.AppThread",
        };
        _appThread.Start();
    }

    /// <summary>
    /// 启动 Wails.Net 应用主循环。
    /// 此方法在后台线程调用，Application.Run() 会阻塞直到应用退出。
    /// </summary>
    private void StartWailsApp()
    {
        // 创建应用选项
        var options = new ApplicationOptions
        {
            Name = "Wails.Net Demo",
            SingleInstance = false,
        };

        // 应用启动后创建主窗口
        // 使用自定义 scheme http://wails.localhost/ 加载前端资源，
        // 由 WailsWebViewClient 拦截请求并通过 AndroidAssetServer 从 assets 目录提供。
        options.OnAfterStart = () =>
        {
            _app?.CreateWebviewWindow(new WebviewWindowOptions
            {
                Name = "main",
                Title = "Wails.Net Demo - Android",
                Width = 1080,
                Height = 1920,
                URL = "http://wails.localhost/",
            });
        };

        // 创建应用实例
        _app = new WailsApplication(options);

        // 配置 Android 平台实现：
        //   - 创建 AndroidPlatformApp 并注册到 Application
        //   - 注册 DialogManager / ScreenManager / AndroidBrowserManager
        //   - 注册移动平台实现（AndroidHaptics / AndroidBiometric / AndroidNfc / AndroidBarcodeScanner）
        //   - 通过 OnBeforeStart hook 注册 AndroidRuntimePlugin（提供 device.info / toast.show 命令）
        // 对应 Wails v3 Go 版本 application.go 中的 Init() 函数。
        _app.UseAndroid();

        // 创建 Android 平台资源服务器，从 APK assets 目录读取前端资源。
        // Assets 子路径为空字符串，表示直接从 assets 根目录读取。
        var assetServer = new AndroidAssetServer(Assets, assetPrefix: string.Empty);

        // 注入 AssetServer 与 Activity 引用到 AndroidPlatformApp
        if (_app.PlatformApp is AndroidPlatformApp androidApp)
        {
            _platformApp = androidApp;
            androidApp.SetAssetServer(assetServer);
            androidApp.SetActivity(this);
        }

        // 注册绑定服务到 Application（公共方法通过反射暴露给前端）
        _app.RegisterService(new GreetingService());

        // 进入应用主循环（阻塞直到 Quit 被调用）
        _app.Run();
    }

    /// <summary>
    /// Activity 启动时调用，转发 <c>Android.ActivityStarted</c> 平台事件。
    /// </summary>
    protected override void OnStart()
    {
        base.OnStart();
        _platformApp?.OnActivityStarted();
    }

    /// <summary>
    /// Activity 恢复时调用，转发 <c>Android.ActivityResumed</c> 平台事件。
    /// </summary>
    protected override void OnResume()
    {
        base.OnResume();
        _platformApp?.OnActivityResumed();
    }

    /// <summary>
    /// Activity 暂停时调用，转发 <c>Android.ActivityPaused</c> 平台事件。
    /// </summary>
    protected override void OnPause()
    {
        _platformApp?.OnActivityPaused();
        base.OnPause();
    }

    /// <summary>
    /// Activity 停止时调用，转发 <c>Android.ActivityStopped</c> 平台事件。
    /// </summary>
    protected override void OnStop()
    {
        _platformApp?.OnActivityStopped();
        base.OnStop();
    }

    /// <summary>
    /// Activity 销毁时调用，转发 <c>Android.ActivityDestroyed</c> 平台事件并关闭 Wails.Net 应用。
    /// </summary>
    protected override void OnDestroy()
    {
        _platformApp?.OnActivityDestroyed();
        // 触发应用关闭信号，唤醒 Run() 返回
        _app?.Quit();
        _app = null;
        _platformApp = null;
        base.OnDestroy();
    }

    /// <summary>
    /// 系统内存不足时调用，转发 <c>Android.ApplicationLowMemory</c> 平台事件
    /// （映射为 <c>Common.LowMemory</c> 公共事件）。
    /// </summary>
    public override void OnLowMemory()
    {
        base.OnLowMemory();
        _platformApp?.OnLowMemory();
    }

    /// <summary>
    /// Activity 结果回调，转发给 <see cref="AndroidPlatformApp.HandleActivityResult"/>
    /// 以完成 SAF（Storage Access Framework）文件对话框的 TaskCompletionSource。
    /// <para>
    /// 当 SAF 文件对话框（<c>OpenFileDialog</c> / <c>SaveFileDialog</c> / <c>OpenMultipleFilesDialog</c>）
    /// 通过 <c>StartActivityForResult</c> 启动后，用户选择文件或取消操作均会触发此回调。
    /// </para>
    /// </summary>
    /// <param name="requestCode">请求码，由 SAF 对话框启动时传入（0x1001~0x1003）。</param>
    /// <param name="resultCode">结果码（<see cref="Result.Ok"/> 或 <see cref="Result.Canceled"/>）。</param>
    /// <param name="data">返回的 Intent，包含选中的文件 URI。</param>
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        _platformApp?.HandleActivityResult(requestCode, resultCode, data);
    }
}
