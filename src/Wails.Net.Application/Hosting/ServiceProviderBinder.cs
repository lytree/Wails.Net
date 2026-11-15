using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wails.Net.Application.Managers;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Transport;

namespace Wails.Net.Application.Hosting;

/// <summary>
/// DI 容器到 <see cref="Wails.Net.Application.Application"/> 的服务绑定器（RH-HOST-02）。
/// <para>
/// 从 <see cref="Wails.Net.Application.Application.InitializeFromServiceProvider"/> 整体抽出：
/// 该方法此前内联 110 余行"从 DI 取服务 → 写入 Application 字段"，属装配噪声而非聚合根职责。
/// 迁移只搬位置、不改语义：
/// <list type="bullet">
/// <item>只读管理器（Bindings / Events）在构造期已注入，此处不再处理；</item>
/// <item>平台相关管理器（WindowManager / DialogManager / ScreenManager）走字段槽，
/// 因为 DI 工厂无法感知 <see cref="Wails.Net.Application.Application"/> 运行状态，
/// 需要回挂 <c>SetApplication</c> 与 <c>AttachRunningState</c>；</item>
/// <item>平台特定管理器（剪贴板 / 菜单 / 托盘 / 快捷键 / 浏览器 / 自启动 / 环境）走公共 Setter，
/// 保持原有"用户显式设置优先"的覆盖语义。</item>
/// </list>
/// </para>
/// <para>
/// 事件监听器装配（<c>EventIPCTransport</c> 兜底）不在本类，已收敛进
/// <see cref="Wails.Net.Application.Transport.IIpcTransportStrategy"/>（RH-IPC-02 第 4 点）。
/// </para>
/// </summary>
internal static class ServiceProviderBinder
{
    /// <summary>
    /// 把 DI 容器中已注册的服务绑定到指定应用实例。
    /// 已由用户显式设置的值优先（<c>Logger</c> 等），未注册的服务保持 null。
    /// </summary>
    /// <param name="application">目标应用实例。</param>
    /// <param name="serviceProvider">DI 容器。</param>
    internal static void Bind(Wails.Net.Application.Application application, IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        // 保存 DI 容器引用，供 MessageProcessor 创建带 WindowId 的命令上下文。
        application.ServiceProviderSlot = serviceProvider;

        // 自动从 DI 容器获取 ILogger<Application>，若用户未显式设置则使用 DI 注册的实例。
        if (application.Logger is null)
        {
            application.Logger = serviceProvider.GetService<ILogger<Wails.Net.Application.Application>>();
        }

        // 自动从 DI 容器获取 IHostApplicationLifetime，用于在 Run/Shutdown 中触发标准生命周期事件。
        application.LifetimeSlot = serviceProvider.GetService<IHostApplicationLifetime>();

        BindPlatformManagers(application, serviceProvider);
        BindPlatformSpecificManagers(application, serviceProvider);

        // 从 DI 容器获取 PluginManager 并收集已注册的插件实例。
        // 对应主题 B：插件生命周期对齐。PluginManager 由 UsePlugin 注册为单例，
        // RegisterFromServices 从 IEnumerable<IPlugin> 收集所有插件实例。
        application.PluginManagerSlot = serviceProvider.GetService<PluginManager>();
        application.PluginManagerSlot?.RegisterFromServices();

        // IPC 传输策略与事件监听器装配（RH-IPC-02 第 4 点）：
        // 策略可被 DI 覆盖（AddSingleton<IIpcTransportStrategy, ...>），未注册时保持默认策略；
        // 监听器内容与顺序由策略决定（Was: 此处硬编码 "DI 监听器 + EventIPC 兜底"）。
        application.IpcStrategySlot = serviceProvider.GetService<IIpcTransportStrategy>();
        application.WireIpcEventListeners(serviceProvider);
    }

    /// <summary>
    /// 绑定平台依赖的管理器（非只读字段，可从 DI 替换）。
    /// </summary>
    private static void BindPlatformManagers(
        Wails.Net.Application.Application application,
        IServiceProvider serviceProvider)
    {
        var windowManager = serviceProvider.GetService<WindowManager>();
        if (windowManager is not null)
        {
            application.WindowManagerSlot = windowManager;

            // R1-3：DI 注入的 WindowManager 已存在，需同步注入 Application 引用，
            // 避免其内部回退到 Application.Get() 静态访问。
            windowManager.SetApplication(application);
        }

        var dialogManager = serviceProvider.GetService<DialogManager>();
        if (dialogManager is not null)
        {
            // DI 工厂无法感知 Application 运行状态，这里补挂运行前拦截判定（v3.1.0 #6390）。
            dialogManager.AttachRunningState(() => application.IsRunning);
            application.DialogManagerSlot = dialogManager;
        }

        var screenManager = serviceProvider.GetService<ScreenManager>();
        if (screenManager is not null)
        {
            application.ScreenManagerSlot = screenManager;
        }
    }

    /// <summary>
    /// 绑定通过公共 Setter 注入的平台特定管理器（由平台代码注册到 DI）。
    /// 未注册时保持既有值，不覆盖用户显式设置。
    /// </summary>
    private static void BindPlatformSpecificManagers(
        Wails.Net.Application.Application application,
        IServiceProvider serviceProvider)
    {
        if (serviceProvider.GetService<IClipboardManager>() is { } clipboardManager)
        {
            application.ClipboardManager = clipboardManager;
        }

        if (serviceProvider.GetService<IMenuManager>() is { } menuManager)
        {
            application.MenuManager = menuManager;
        }

        if (serviceProvider.GetService<ISystemTrayManager>() is { } systemTrayManager)
        {
            application.SystemTrayManager = systemTrayManager;
        }

        if (serviceProvider.GetService<IKeyBindingManager>() is { } keyBindingManager)
        {
            application.KeyBindingManager = keyBindingManager;
        }

        if (serviceProvider.GetService<IBrowserManager>() is { } browserManager)
        {
            application.BrowserManager = browserManager;
        }

        if (serviceProvider.GetService<IAutostartManager>() is { } autostartManager)
        {
            application.AutostartManager = autostartManager;
        }

        if (serviceProvider.GetService<IEnvironmentManager>() is { } environmentManager)
        {
            application.EnvironmentManager = environmentManager;
        }
    }
}
