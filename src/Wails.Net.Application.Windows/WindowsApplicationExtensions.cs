using Wails.Net.Application.Managers;
using Wails.Net.Application.Platform;
using Wails.Net.Application.SystemTray;

namespace Wails.Net.Application;

/// <summary>
/// Windows 平台扩展方法，提供 UseWindows() 入口点以配置 Windows 平台应用。
/// </summary>
public static class WindowsApplicationExtensions
{
    /// <summary>
    /// 为应用配置 Windows 平台实现。
    /// 创建 WindowsPlatformApp 并注册对话框、屏幕和系统托盘相关服务。
    /// </summary>
    /// <param name="app">应用实例。</param>
    /// <returns>传入的应用实例，以支持链式调用。</returns>
    public static Application UseWindows(this Application app)
    {
        var platformApp = new WindowsPlatformApp(app.Options);
        app.SetPlatformApp(platformApp);

        // 注册对话框管理器服务，委托给 WindowsPlatformApp 的 WinForms 对话框实现。
        app.RegisterService(new DialogManager(platformApp));

        // 注册屏幕管理器服务，委托给 WindowsPlatformApp 的 WinForms Screen 实现。
        app.RegisterService(new ScreenManager(platformApp));

        // 注册系统托盘管理器，委托给 Win32SystemTray 的 Shell_NotifyIconW 实现。
        app.SystemTrayManager = new WindowsSystemTrayManager();

        return app;
    }

    /// <summary>
    /// Windows 平台系统托盘管理器实现。
    /// 对应 Go 版 application.go 中的 SystemTrayManager。
    /// 通过 Win32SystemTray 创建和销毁托盘实例。
    /// </summary>
    private sealed class WindowsSystemTrayManager : ISystemTrayManager
    {
        /// <inheritdoc />
        public object CreateSystemTray(byte[] icon)
        {
            var tray = new Win32SystemTray();
            tray.SetIcon(icon);
            tray.Show();
            return tray;
        }

        /// <inheritdoc />
        public void DestroySystemTray(object tray)
        {
            if (tray is Win32SystemTray win32Tray)
            {
                win32Tray.Destroy();
            }
        }
    }
}
