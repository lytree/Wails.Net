using Wails.Net.Application.Browser;
using Wails.Net.Application.Managers;
using Wails.Net.Application.Platform;

namespace Wails.Net.Application;

/// <summary>
/// Android 平台扩展方法，提供 UseAndroid() 入口点以配置 Android 平台应用。
/// 对应 Wails v3 Go 版本中各平台的 Init() 函数。
/// </summary>
public static class AndroidApplicationExtensions
{
    /// <summary>
    /// 为应用配置 Android 平台实现。
    /// 创建 AndroidPlatformApp 并注册对话框、屏幕和浏览器相关服务。
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

        return app;
    }
}
