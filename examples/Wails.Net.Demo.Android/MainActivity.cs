using Android.App;
using Android.OS;
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
/// </summary>
[Activity(Label = "Wails.Net Demo", MainLauncher = true)]
public class MainActivity : Activity
{
    /// <summary>
    /// Wails.Net 应用实例。
    /// </summary>
    private WailsApplication? _app;

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

        // 创建应用实例（PlatformFactory 会通过反射自动加载 AndroidPlatformApp）
        _app = new WailsApplication(options);

        // 创建 Android 平台资源服务器，从 APK assets 目录读取前端资源。
        // Assets 子路径为空字符串，表示直接从 assets 根目录读取。
        var assetServer = new AndroidAssetServer(Assets, assetPrefix: string.Empty);

        // 注入 AssetServer 到 AndroidPlatformApp
        if (_app.PlatformApp is AndroidPlatformApp androidApp)
        {
            androidApp.SetAssetServer(assetServer);
            androidApp.SetActivity(this);
        }

        // 注册绑定服务到 Application（公共方法通过反射暴露给前端）
        _app.RegisterService(new GreetingService());

        // 进入应用主循环（阻塞直到 Quit 被调用）
        _app.Run();
    }

    /// <summary>
    /// Activity 销毁时调用，关闭 Wails.Net 应用并清理资源。
    /// </summary>
    protected override void OnDestroy()
    {
        // 触发应用关闭信号，唤醒 Run() 返回
        _app?.Quit();
        _app = null;
        base.OnDestroy();
    }
}
