using Wails.Net.Application.Browser;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Managers;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Platform;
using Wails.Net.Application.SystemTray;

namespace Wails.Net.Application;

/// <summary>
/// Linux 平台扩展方法，提供 UseLinux() 入口点以配置 Linux 平台应用。
/// </summary>
public static class LinuxApplicationExtensions
{
    /// <summary>
    /// 为桌面应用构建器配置 Linux 平台实现。
    /// </summary>
    /// <remarks>
    /// 调用此方法会强制加载 <c>Wails.Net.Application.Linux</c> 程序集，
    /// 触发 <c>[ModuleInitializer]</c> 自动注册 Linux 平台委托到 <see cref="PlatformFactory"/>，
    /// 然后委托给 <see cref="DesktopApplicationBuilder.UseAutoPlatform"/> 完成实际注册。
    /// <para>
    /// <b>注意</b>：自 <c>PlatformFactory.TryLoadPlatformAssembly</c> 引入后，
    /// <see cref="DesktopApplicationBuilder.UseAutoPlatform"/> 会通过
    /// <see cref="System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor"/>
    /// 显式触发模块初始化器，<b>不再需要</b>显式调用本方法来注册 Linux 平台。
    /// 本方法保留是为了向后兼容，以及允许在多个平台 TFM 同时编译时显式指定 Linux 平台。
    /// </para>
    /// </remarks>
    /// <param name="builder">桌面应用构建器。</param>
    /// <returns>构建器实例，以支持链式调用。</returns>
    public static DesktopApplicationBuilder UseLinux(this DesktopApplicationBuilder builder)
    {
        // 引用本程序集中的公共类型，强制 JIT 加载 Wails.Net.Application.Linux 程序集，
        // 触发 LinuxPlatformRegistrar.Register() 的 [ModuleInitializer] 调用，
        // 完成 PlatformFactory.RegisterPlatformApp("linux", ...) 注册。
        _ = typeof(LinuxApplicationExtensions);

        return builder.UseAutoPlatform();
    }

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

        // 注册浏览器管理器，委托给 LinuxBrowserManager 通过 xdg-open 打开默认浏览器。
        // 对应 Wails v3 internal/browser 包的 Linux 实现。
        app.BrowserManager = new LinuxBrowserManager();

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
        public ISystemTrayImpl CreateSystemTray(byte[] icon)
        {
            var tray = new LinuxSystemTray();
            tray.SetIcon(icon);
            tray.Show();
            return tray;
        }

        /// <inheritdoc />
        public void DestroySystemTray(ISystemTrayImpl tray)
        {
            tray.Destroy();
        }

        /// <inheritdoc />
        public void SetIcon(ISystemTrayImpl tray, byte[]? iconData)
        {
            if (iconData is not null)
            {
                tray.SetIcon(iconData);
            }
        }

        /// <inheritdoc />
        public void SetLabel(ISystemTrayImpl tray, string label)
        {
            tray.SetLabel(label);
        }

        /// <inheritdoc />
        public void SetMenu(ISystemTrayImpl tray, Menu? menu)
        {
            tray.SetMenu(menu);
        }

        /// <inheritdoc />
        public void SetTooltip(ISystemTrayImpl tray, string tooltip)
        {
            tray.SetTooltip(tooltip);
        }

        /// <inheritdoc />
        public void Show(ISystemTrayImpl tray)
        {
            tray.Show();
        }

        /// <inheritdoc />
        public void Hide(ISystemTrayImpl tray)
        {
            tray.Hide();
        }

        /// <inheritdoc />
        public bool IsVisible(ISystemTrayImpl tray)
        {
            // Linux 托盘可见性由桌面环境管理，D-Bus 注册成功即视为可见
            return tray is LinuxSystemTray;
        }
    }
}
