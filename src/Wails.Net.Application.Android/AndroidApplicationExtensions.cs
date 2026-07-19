using Wails.Net.Application.Android.Mobile;
using Wails.Net.Application.Browser;
using Wails.Net.Application.Managers;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Plugins.Mobile;

namespace Wails.Net.Application;

/// <summary>
/// Android 平台扩展方法，提供 UseAndroid() 入口点以配置 Android 平台应用。
/// 对应 Wails v3 Go 版本中各平台的 Init() 函数。
/// </summary>
public static class AndroidApplicationExtensions
{
    /// <summary>
    /// 为应用配置 Android 平台实现。
    /// 创建 AndroidPlatformApp 并注册对话框、屏幕、浏览器、移动平台实现等服务。
    /// <para>
    /// 移动平台实现（<see cref="AndroidHaptics"/> / <see cref="AndroidBiometric"/> /
    /// <see cref="AndroidNfc"/> / <see cref="AndroidBarcodeScanner"/>）注册到
    /// <see cref="Application.Services"/>，供相应移动插件通过 DI 解析覆盖降级实现。
    /// </para>
    /// <para>
    /// <see cref="AndroidRuntimePlugin"/> 通过 <see cref="Application.OnBeforeStart"/>
    /// hook 注入，提供 <c>device.info</c> 与 <c>toast.show</c> 命令（对应 Wails v3
    /// <c>messageprocessor_android.go</c> 中的 AndroidDeviceInfo / AndroidToast 方法）。
    /// </para>
    /// </summary>
    /// <param name="app">应用实例。</param>
    /// <returns>传入的应用实例，以支持链式调用。</returns>
    public static Application UseAndroid(this Application app)
    {
        var platformApp = new AndroidPlatformApp(app.Options);
        app.SetPlatformApp(platformApp);

        // 注册对话框管理器服务，委托给 AndroidPlatformApp 的 AlertDialog 实现。
        app.RegisterService(new DialogManager(platformApp));

        // 注册屏幕管理器服务，委托给 AndroidPlatformApp 的 DisplayManager 实现。
        app.RegisterService(new ScreenManager(platformApp));

        // 注册浏览器管理器，委托给 AndroidBrowserManager 通过 Intent.ActionView 打开默认浏览器。
        // 对应 Wails v3 internal/browser 包的 Android 实现。
        app.BrowserManager = new AndroidBrowserManager();

        // ---------------------------------------------------------------------
        // 移动平台实现注册（对齐 Wails v3 messageprocessor_android.go 与 Tauri v2 移动插件后端）
        // ---------------------------------------------------------------------
        // 注意：移动插件（HapticsPlugin / BiometricPlugin / NfcPlugin / BarcodeScannerPlugin）
        // 通过 TryAddSingleton 注册降级 NullImpl。此处提前将 Android 实现注册到
        // Application.Services，使后续插件 ConfigureServices 调用时 TryAddSingleton 跳过降级实现。
        // 由于 Application.Services 是 ServiceRegistry（非 DI 容器），此处通过
        // AndroidPlatformApp 的 MobileImpls 属性暂存，供 AndroidRuntimePlugin 注入使用。
        platformApp.MobileHaptics = new AndroidHaptics();
        platformApp.MobileBiometric = new AndroidBiometric();
        platformApp.MobileNfc = new AndroidNfc();
        platformApp.MobileBarcodeScanner = new AndroidBarcodeScanner();

        // ---------------------------------------------------------------------
        // Android 运行时插件注册（对齐 Wails v3 messageprocessor_android.go）
        // ---------------------------------------------------------------------
        // AndroidRuntimePlugin 提供 device.info / toast.show 命令，通过 Application
        // 的 OnBeforeStart hook 在 Run 启动前注册到 CommandRegistry（若已注入 CommandDispatcher）。
        var runtimePlugin = new AndroidRuntimePlugin();
        app.OnBeforeStart(application =>
        {
            ConfigureAndroidRuntimePlugin(application, runtimePlugin);
        });

        return app;
    }

    /// <summary>
    /// 配置 AndroidRuntimePlugin，注册其命令到 <see cref="Application.CommandDispatcher"/>。
    /// 在 <see cref="Application.Run"/> 启动前通过 OnBeforeStart hook 调用。
    /// <para>
    /// 若 <see cref="Application.CommandDispatcher"/> 未注入（手动 new Application 模式），
    /// 则通过 <see cref="Application.RegisterService(object)"/> 将插件实例注册为绑定服务，
    /// 其公共方法 <c>GetDeviceInfoJson</c> / <c>ShowToast</c> 仍可通过绑定系统调用。
    /// </para>
    /// </summary>
    /// <param name="application">应用实例。</param>
    /// <param name="plugin">Android 运行时插件实例。</param>
    private static void ConfigureAndroidRuntimePlugin(Application application, AndroidRuntimePlugin plugin)
    {
        // 将插件实例注册为绑定服务，前端可通过标准的绑定调用机制
        // 直接调用 GetDeviceInfoJson / ShowToast 公共方法。
        // 对应 Wails v3 messageprocessor_android.go 中通过 object ID 12 路由的两个方法。
        application.RegisterService(plugin);
    }
}
