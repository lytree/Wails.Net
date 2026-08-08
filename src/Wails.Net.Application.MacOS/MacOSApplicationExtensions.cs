using Wails.Net.Application.Browser;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Managers;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Platform;
using Wails.Net.Application.SystemTray;

namespace Wails.Net.Application;

/// <summary>
/// macOS 平台扩展方法，提供 <c>UseMacOS()</c> 入口点以配置 macOS 平台应用。
/// 对应 Wails v3 Go 版本中各平台的 <c>Init()</c> 函数。
/// </summary>
public static class MacOSApplicationExtensions
{
    /// <summary>
    /// 为桌面应用构建器配置 macOS 平台实现。
    /// </summary>
    /// <remarks>
    /// 调用此方法会强制加载 <c>Wails.Net.Application.MacOS</c> 程序集，
    /// 触发 <c>[ModuleInitializer]</c> 自动注册 macOS 平台委托到 <see cref="PlatformFactory"/>，
    /// 然后委托给 <see cref="DesktopApplicationBuilder.UseAutoPlatform"/> 完成实际注册。
    /// <para>
    /// <b>注意</b>：自 <c>PlatformFactory.TryLoadPlatformAssembly</c> 引入后，
    /// <see cref="DesktopApplicationBuilder.UseAutoPlatform"/> 会通过
    /// <see cref="System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor"/>
    /// 显式触发模块初始化器，<b>不再需要</b>显式调用本方法来注册 macOS 平台。
    /// 本方法保留是为了向后兼容，以及允许在多个平台 TFM 同时编译时显式指定 macOS 平台。
    /// </para>
    /// </remarks>
    /// <param name="builder">桌面应用构建器。</param>
    /// <returns>构建器实例，以支持链式调用。</returns>
    public static DesktopApplicationBuilder UseMacOS(this DesktopApplicationBuilder builder)
    {
        // 引用本程序集中的公共类型，强制 JIT 加载 Wails.Net.Application.MacOS 程序集，
        // 触发 MacOSPlatformRegistrar.Register() 的 [ModuleInitializer] 调用，
        // 完成 PlatformFactory.RegisterPlatformApp("macos", ...) 注册。
        _ = typeof(MacOSApplicationExtensions);

        return builder.UseAutoPlatform();
    }

    /// <summary>
    /// 为应用配置 macOS 平台实现。
    /// 创建 MacOSPlatformApp 并注册对话框、屏幕和系统托盘相关服务。
    /// </summary>
    /// <param name="app">应用实例。</param>
    /// <returns>传入的应用实例，以支持链式调用。</returns>
    public static Application UseMacOS(this Application app)
    {
        var platformApp = new MacOSPlatformApp(app.Options);
        app.SetPlatformApp(platformApp);

        // 注册对话框管理器服务，委托给 MacOSPlatformApp 的 AppKit 对话框实现。
        app.RegisterService(new DialogManager(platformApp));

        // 注册屏幕管理器服务，委托给 MacOSPlatformApp 的 NSScreen 屏幕实现。
        app.RegisterService(new ScreenManager(platformApp));

        // 注册系统托盘管理器，委托给 MacOSSystemTray 的 NSStatusItem 实现。
        app.SystemTrayManager = new MacOSSystemTrayManager();

        // 注册快捷键绑定管理器，委托给 MacOSKeyBindingManager 的 Carbon RegisterEventHotKey 实现。
        app.KeyBindingManager = new MacOSKeyBindingManager();

        // 注册浏览器管理器，委托给 MacOSBrowserManager 通过 NSWorkspace 打开默认浏览器。
        // 对应 Wails v3 internal/browser 包的 macOS 实现。
        app.BrowserManager = new MacOSBrowserManager();

        return app;
    }

    /// <summary>
    /// macOS 平台系统托盘管理器实现。
    /// 对应 Go 版 application.go 中的 SystemTrayManager。
    /// 通过 MacOSSystemTray 创建和销毁托盘实例。
    /// </summary>
    private sealed class MacOSSystemTrayManager : ISystemTrayManager
    {
        /// <summary>
        /// 托盘 ID 计数器。
        /// </summary>
        private int _nextTrayId;

        /// <inheritdoc />
        public ISystemTrayImpl CreateSystemTray(byte[] icon)
        {
            var tray = new MacOSSystemTray((uint)Interlocked.Increment(ref _nextTrayId));
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
            return tray is MacOSSystemTray;
        }
    }
}
