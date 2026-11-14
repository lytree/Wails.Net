using Wails.Net.Application.Dialogs;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Options;
using Wails.Net.Application.Screens;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Platform;

/// <summary>
/// 平台特定的应用接口，对应 Go 版 application.go 中的 platformApp。
/// </summary>
public interface IPlatformApp
{
    /// <summary>
    /// 获取应用名称。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 获取平台原生能力描述。
    /// 对应 Wails v3 Go 版本 <c>App.Capabilities()</c> 方法，返回
    /// <see cref="PlatformCapabilities"/> 结构描述当前平台支持的原生特性。
    /// 默认实现返回 <see cref="PlatformCapabilities.Default"/>，
    /// 由支持的平台覆盖（Windows / Linux / Android）。
    /// </summary>
    PlatformCapabilities Capabilities => PlatformCapabilities.Default;

    /// <summary>
    /// 启动平台应用主循环，阻塞直到主循环退出。
    /// </summary>
    /// <returns>进程退出码。</returns>
    int Run();

    /// <summary>
    /// 尝试获取单实例锁。返回 true 表示当前进程为首实例。
    /// 对应 Wails v3 Go 版本 application.go 中 SingleInstanceLock 的协调入口。
    /// </summary>
    /// <param name="uniqueId">用于标识应用的唯一 ID（通常为应用名称）。</param>
    /// <returns>成功获取锁（首实例）返回 true，否则返回 false。</returns>
    bool AcquireSingleInstanceLock(string uniqueId) => true;

    /// <summary>
    /// 通知已运行的前一个实例：有新的实例尝试启动（携带命令行参数）。
    /// </summary>
    /// <param name="args">新实例启动时传入的命令行参数。</param>
    void NotifySingleInstance(string[] args)
    {
        // 默认空实现，由平台特定实现覆盖。
    }

    /// <summary>
    /// 销毁平台应用。
    /// </summary>
    void Destroy();

    /// <summary>
    /// 异步销毁平台应用（R1-4）。
    /// <para>
    /// 默认实现同步调用 <see cref="Destroy"/> 后返回已完成的 <see cref="Task"/>，
    /// 由支持异步关闭的平台（如 Android 生命周期回调）覆盖以实现真正的优雅退出。
    /// </para>
    /// <para>
    /// 当宿主（如 <c>Microsoft.Extensions.Hosting</c>）通过 <c>IHostedService.StopAsync</c>
    /// 触发关闭时，会等待此方法的 <see cref="CancellationToken"/> 完成或被取消；
    /// 实现方应将 <paramref name="cancellationToken"/> 串联到内部异步清理路径。
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">取消令牌。宿主关闭超时或用户主动取消时触发。</param>
    /// <returns>销毁完成的任务。</returns>
    Task ShutdownAsync(CancellationToken cancellationToken)
    {
        Destroy();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 设置应用菜单。
    /// </summary>
    /// <param name="menu">应用菜单实例，可为 null。</param>
    void SetApplicationMenu(Menu? menu);

    /// <summary>
    /// 获取当前活动窗口 ID。
    /// </summary>
    /// <returns>当前活动窗口的 ID。</returns>
    uint GetCurrentWindowId();

    /// <summary>
    /// 将应用主窗口的父窗口设置为指定平台原生句柄。
    /// 主要用于将应用嵌入到外部宿主进程（如插件宿主、IDE 面板等）。
    /// 默认实现为 no-op，由支持的平台（如 Windows）覆盖。
    /// 对应 Wails v3 Go 版本 application.go 中的 SetParent 方法。
    /// </summary>
    /// <param name="parent">父窗口的平台原生句柄（如 Win32 HWND）。</param>
    void SetParent(IntPtr parent)
    {
        // 默认空实现，由平台特定实现覆盖。
    }

    /// <summary>
    /// 显示关于对话框。
    /// </summary>
    /// <param name="name">应用名称。</param>
    /// <param name="description">应用描述。</param>
    /// <param name="icon">应用图标字节数据，可为 null。</param>
    void ShowAboutDialog(string name, string description, byte[]? icon);

    /// <summary>
    /// 设置应用图标。
    /// </summary>
    /// <param name="icon">图标字节数据，可为 null。</param>
    void SetIcon(byte[]? icon);

    /// <summary>
    /// 处理平台事件。
    /// </summary>
    /// <param name="id">事件 ID。</param>
    void On(uint id);

    /// <summary>
    /// 在主线程分发事件。
    /// </summary>
    /// <param name="id">事件 ID。</param>
    void DispatchOnMainThread(uint id);

    /// <summary>
    /// 隐藏应用。
    /// </summary>
    void Hide();

    /// <summary>
    /// 显示应用。
    /// </summary>
    void Show();

    /// <summary>
    /// 获取主屏幕信息。
    /// </summary>
    /// <returns>主屏幕实例，可为 null。</returns>
    Screen? GetPrimaryScreen();

    /// <summary>
    /// 获取所有屏幕信息。
    /// </summary>
    /// <returns>屏幕数组。</returns>
    Screen[] GetScreens();

    /// <summary>
    /// 获取平台标志。
    /// </summary>
    /// <param name="options">应用选项。</param>
    /// <returns>平台标志字典。</returns>
    Dictionary<string, object?> GetFlags(ApplicationOptions options);

    /// <summary>
    /// 是否在主线程。
    /// </summary>
    /// <returns>如果在主线程则返回 true，否则返回 false。</returns>
    bool IsOnMainThread();

    /// <summary>
    /// 是否暗色模式。
    /// </summary>
    /// <returns>如果为暗色模式则返回 true，否则返回 false。</returns>
    bool IsDarkMode();

    /// <summary>
    /// 获取系统强调色。
    /// </summary>
    /// <returns>系统强调色字符串。</returns>
    string GetAccentColor();

    /// <summary>
    /// 在主线程执行操作。
    /// </summary>
    /// <param name="action">要执行的操作。</param>
    void DispatchOnMainThread(Action action);

    /// <summary>
    /// 平台是否支持原生消息通道（前端 <c>postMessage</c> → 后端，后端事件 → 前端原生投递）。
    /// <para>
    /// RH-WIN-02：窗口向 <see cref="Transport.INativeIpcTransport"/> 的注册与注销统一由
    /// <see cref="Managers.WindowManager"/> 依据此能力完成，平台实现不再自行注册。
    /// </para>
    /// <para>
    /// 默认 <c>false</c>：仅 Windows（WebView2 <c>WebMessageReceived</c>）与测试宿主（Mock）为真。
    /// Linux 的 <see cref="IWebviewWindowImpl.SetNativeMessageHandler"/> 为空操作、Android 走
    /// <c>JavascriptInterface</c> 独立桥，若误标为真，<c>EventIPCTransport</c> 会因"原生通道已接管"
    /// 而跳过 <c>ExecJS</c> 兜底，导致后端事件在前端丢失（RH-WIN-01 曾掩盖此缺陷）。
    /// </para>
    /// </summary>
    bool SupportsNativeMessageChannel => false;

    /// <summary>
    /// 窗口销毁通知（RH-WIN-02）。
    /// <para>
    /// <see cref="Managers.WindowManager"/> 在移除窗口记录后调用。
    /// RH-WIN-02 终态起平台层不再自持窗口表，本回调仅保留给需要做平台侧清理
    /// （关闭原生资源、取消注册托盘/快捷键关联等）的实现；枚举窗口请改用
    /// <see cref="AttachWindowAccessor"/> 注入的 <see cref="IPlatformWindowAccessor"/>。
    /// </para>
    /// <para>
    /// 默认空实现，不维护窗口缓存的平台无需覆盖。
    /// </para>
    /// </summary>
    /// <param name="windowId">已销毁的窗口 ID。</param>
    void OnWindowDestroyed(uint windowId)
    {
        // 默认空实现：平台若自持窗口缓存应覆盖以移除条目。
    }

    /// <summary>
    /// 注入窗口只读查询入口（RH-WIN-02 终态）。
    /// <para>
    /// 由 <see cref="Managers.WindowManager"/> 在构造与 <c>SetPlatformApp</c> 时调用，
    /// 使平台层无需自持窗口字典即可遍历/查找窗口，消除"平台 _windows 与 WindowManager 并存"。
    /// </para>
    /// <para>
    /// 默认空实现，仅供仍持有窗口缓存的兼容实现忽略。
    /// </para>
    /// </summary>
    /// <param name="accessor">窗口只读查询入口（实现方为 <see cref="Managers.WindowManager"/>）。</param>
    void AttachWindowAccessor(IPlatformWindowAccessor accessor)
    {
        // 默认空实现：不需要枚举窗口的平台可忽略。
    }

    /// <summary>
    /// 创建平台特定的 Webview 窗口。
    /// </summary>
    /// <param name="id">窗口 ID。</param>
    /// <param name="options">窗口选项。</param>
    /// <returns>创建的平台窗口实现；无法创建时返回 null（如 Server 模式）。</returns>
    IWebviewWindowImpl? CreateWebviewWindow(uint id, WebviewWindowOptions options);

    /// <summary>
    /// 异步显示消息对话框。
    /// </summary>
    /// <param name="title">对话框标题。</param>
    /// <param name="message">消息内容。</param>
    /// <param name="style">对话框样式。</param>
    /// <param name="buttons">按钮文本数组。</param>
    /// <returns>被点击按钮的索引。</returns>
    Task<int> ShowMessageDialog(string title, string message, DialogStyle style, string[] buttons);

    /// <summary>
    /// 异步显示消息对话框，支持自定义按钮标签、默认/取消按钮与选中回调。
    /// 默认实现回退到 <see cref="ShowMessageDialog(string, string, DialogStyle, string[])"/>，
    /// 并在对话框关闭后触发对应按钮的回调；平台实现可重写以提供原生自定义按钮（如 Windows TaskDialog）。
    /// </summary>
    /// <param name="options">消息对话框选项。</param>
    /// <returns>被点击按钮的索引；对话框被取消且未定义取消按钮时返回 -1。</returns>
    async Task<int> ShowMessageDialog(MessageDialogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var labels = options.GetButtonLabels();
        var index = await ShowMessageDialog(options.Title, options.Message, options.Style, labels).ConfigureAwait(false);
        options.InvokeButtonCallback(index);
        return index;
    }

    /// <summary>
    /// 异步打开文件对话框。
    /// </summary>
    /// <param name="options">打开文件对话框选项。</param>
    /// <returns>选中的文件路径，可为 null。</returns>
    Task<string?> OpenFileDialog(OpenFileDialogOptions options);

    /// <summary>
    /// 异步保存文件对话框。
    /// </summary>
    /// <param name="options">保存文件对话框选项。</param>
    /// <returns>保存的文件路径，可为 null。</returns>
    Task<string?> SaveFileDialog(SaveFileDialogOptions options);

    /// <summary>
    /// 异步打开多文件选择对话框。
    /// </summary>
    /// <param name="options">打开文件对话框选项。</param>
    /// <returns>选中的文件路径数组，可为 null。</returns>
    Task<string[]?> OpenMultipleFilesDialog(OpenFileDialogOptions options);
}
