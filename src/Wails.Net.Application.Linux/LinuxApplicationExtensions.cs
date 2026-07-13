using Wails.Net.Application.Managers;
using Wails.Net.Application.Platform;
using Wails.Net.Application.SystemTray;

namespace Wails.Net.Application;

/// <summary>
/// Linux 平台扩展方法，提供 UseLinux() 入口点以配置 Linux 平台应用。
/// </summary>
public static class LinuxApplicationExtensions
{
    /// <summary>
    /// 为应用配置 Linux 平台实现。
    /// 创建 LinuxPlatformApp 并注册对话框、屏幕和系统托盘相关服务。
    /// </summary>
    /// <param name="app">应用实例。</param>
    /// <returns>传入的应用实例，以支持链式调用。</returns>
    public static Application UseLinux(this Application app)
    {
        var platformApp = new LinuxPlatformApp(app.Options);
        app.SetPlatformApp(platformApp);

        // 注册对话框管理器服务，委托给 LinuxPlatformApp 的 GTK4 对话框实现。
        app.RegisterService(new DialogManager(platformApp));

        // 注册屏幕管理器服务，委托给 LinuxPlatformApp 的 Gdk.Display 屏幕实现。
        app.RegisterService(new ScreenManager(platformApp));

        // 注册系统托盘管理器，委托给 LinuxSystemTray 的 GTK4 模拟托盘实现。
        app.SystemTrayManager = new LinuxSystemTrayManager();

        // 注册快捷键绑定管理器，委托给 LinuxKeyBindingManager 的 GTK4 ShortcutController 实现。
        app.KeyBindingManager = new LinuxKeyBindingManager();

        return app;
    }

    /// <summary>
    /// Linux 平台系统托盘管理器实现。
    /// 对应 Go 版 application.go 中的 SystemTrayManager。
    /// 通过 LinuxSystemTray 创建和销毁托盘实例。
    /// </summary>
    private sealed class LinuxSystemTrayManager : ISystemTrayManager
    {
        /// <inheritdoc />
        public object CreateSystemTray(byte[] icon)
        {
            var tray = new LinuxSystemTray();
            tray.SetIcon(icon);
            tray.Show();
            return tray;
        }

        /// <inheritdoc />
        public void DestroySystemTray(object tray)
        {
            if (tray is LinuxSystemTray linuxTray)
            {
                linuxTray.Destroy();
            }
        }

        /// <inheritdoc />
        public void SetIcon(object tray, byte[]? iconData)
        {
            if (tray is LinuxSystemTray linuxTray && iconData is not null)
            {
                linuxTray.SetIcon(iconData);
            }
        }

        /// <inheritdoc />
        public void SetLabel(object tray, string label)
        {
            if (tray is LinuxSystemTray linuxTray)
            {
                linuxTray.SetLabel(label);
            }
        }

        /// <inheritdoc />
        public void SetMenu(object tray, Menu? menu)
        {
            if (tray is LinuxSystemTray linuxTray)
            {
                linuxTray.SetMenu(menu);
            }
        }

        /// <inheritdoc />
        public void SetTooltip(object tray, string tooltip)
        {
            if (tray is LinuxSystemTray linuxTray)
            {
                linuxTray.SetTooltip(tooltip);
            }
        }

        /// <inheritdoc />
        public void Show(object tray)
        {
            if (tray is LinuxSystemTray linuxTray)
            {
                linuxTray.Show();
            }
        }

        /// <inheritdoc />
        public void Hide(object tray)
        {
            if (tray is LinuxSystemTray linuxTray)
            {
                linuxTray.Hide();
            }
        }

        /// <inheritdoc />
        public bool IsVisible(object tray)
        {
            // Linux 托盘可见性由桌面环境管理，D-Bus 注册成功即视为可见
            return tray is LinuxSystemTray;
        }
    }
}
