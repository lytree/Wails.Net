using Wails.Net.Application.Browser;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Managers;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Platform;
using Wails.Net.Application.SystemTray;
using Menu = Wails.Net.Application.Menus.Menu;

namespace Wails.Net.Application;

/// <summary>
/// Windows 平台扩展方法，提供 UseWindows() 入口点以配置 Windows 平台应用。
/// </summary>
public static class WindowsApplicationExtensions
{
    /// <summary>
    /// 为桌面应用构建器配置 Windows 平台实现。
    /// </summary>
    /// <remarks>
    /// 调用此方法会强制加载 <c>Wails.Net.Application.Windows</c> 程序集，
    /// 触发 <c>[ModuleInitializer]</c> 自动注册 Windows 平台委托到 <see cref="PlatformFactory"/>，
    /// 然后委托给 <see cref="DesktopApplicationBuilder.UseAutoPlatform"/> 完成实际注册。
    /// <para>
    /// <b>注意</b>：自 <c>PlatformFactory.TryLoadPlatformAssembly</c> 引入后，
    /// <see cref="DesktopApplicationBuilder.UseAutoPlatform"/> 会通过
    /// <see cref="System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor"/>
    /// 显式触发模块初始化器，<b>不再需要</b>显式调用本方法来注册 Windows 平台。
    /// 本方法保留是为了向后兼容，以及允许在多个平台 TFM 同时编译时显式指定 Windows 平台。
    /// </para>
    /// </remarks>
    /// <param name="builder">桌面应用构建器。</param>
    /// <returns>构建器实例，以支持链式调用。</returns>
    public static DesktopApplicationBuilder UseWindows(this DesktopApplicationBuilder builder)
    {
        // 引用本程序集中的公共类型，强制 JIT 加载 Wails.Net.Application.Windows 程序集，
        // 触发 WindowsPlatformRegistrar.Register() 的 [ModuleInitializer] 调用，
        // 完成 PlatformFactory.RegisterPlatformApp("windows", ...) 注册。
        _ = typeof(WindowsApplicationExtensions);

        return builder.UseAutoPlatform();
    }

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

        // 注册快捷键绑定管理器，委托给 Win32KeyBindingManager 的 RegisterHotKey 实现。
        app.KeyBindingManager = new Win32KeyBindingManager();

        // 注册浏览器管理器，委托给 WindowsBrowserManager 通过 ShellExecuteW 打开默认浏览器。
        // 对应 Wails v3 internal/browser 包的 Windows 实现。
        app.BrowserManager = new WindowsBrowserManager();

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
        public ISystemTrayImpl CreateSystemTray(byte[] icon)
        {
            var tray = new Win32SystemTray();
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
            return tray is Win32SystemTray win32Tray && win32Tray.IsVisible;
        }
    }
}
