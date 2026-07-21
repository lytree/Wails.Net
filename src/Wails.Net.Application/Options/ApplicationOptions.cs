using Wails.Net.Application.Security;

namespace Wails.Net.Application.Options;

/// <summary>
/// 应用配置选项，对应 Wails v3 中的 application_options.go。
/// </summary>
public class ApplicationOptions
{
    /// <summary>
    /// 应用名称。
    /// </summary>
    public string Name { get; set; } = "MyApp";

    /// <summary>
    /// 应用描述。
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// 应用版本号。
    /// </summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// 应用程序 Bundle ID（包标识符），用于平台特定注册和单实例协调。
    /// 对应 Wails v3 Go 版本 application_options.go 中的 BundleID 字段。
    /// 形如 <c>com.company.appname</c>，未设置时回退到 <see cref="Name"/>。
    /// </summary>
    public string? BundleID { get; set; }

    /// <summary>
    /// 应用图标字节数据，可为 null。
    /// </summary>
    public byte[]? Icon { get; set; }

    /// <summary>
    /// 是否以 Server（无界面）模式运行。
    /// </summary>
    public bool Headless { get; set; } = false;

    /// <summary>
    /// 关闭窗口时是否隐藏而非退出。
    /// </summary>
    public bool HideOnClose { get; set; } = false;

    /// <summary>
    /// 启动时是否隐藏窗口。
    /// </summary>
    public bool HideOnStart { get; set; } = false;

    /// <summary>
    /// 是否启用单实例模式。
    /// </summary>
    public bool SingleInstance { get; set; } = false;

    /// <summary>
    /// 是否禁用最后一个窗口关闭时退出应用。
    /// </summary>
    public bool DisableQuitOnLastWindowClosed { get; set; } = false;

    /// <summary>
    /// 最后一个窗口关闭时是否弹出退出确认对话框。
    /// <para>
    /// 为 true 时，平台在最后一个窗口收到关闭请求后会先弹出原生确认对话框
    /// （如 GTK AlertDialog / Win32 MessageBox），用户确认后才退出应用；
    /// 用户取消则阻止窗口关闭，应用继续运行。
    /// </para>
    /// <para>
    /// 默认为 false：直接退出，不弹确认框。
    /// 与 <see cref="ShouldQuit"/> 的区别：ShouldQuit 是同步回调，适用于"有未保存数据"等
    /// 编程化判断；本选项触发原生 UI 对话框，适用于"用户确认退出"场景。
    /// </para>
    /// </summary>
    public bool ShowExitConfirmationDialog { get; set; } = false;

    /// <summary>
    /// 退出确认对话框的标题，仅在 <see cref="ShowExitConfirmationDialog"/> 为 true 时使用。
    /// 默认为 "确认退出"。
    /// </summary>
    public string ExitDialogTitle { get; set; } = "确认退出";

    /// <summary>
    /// 退出确认对话框的消息内容，仅在 <see cref="ShowExitConfirmationDialog"/> 为 true 时使用。
    /// 默认为 "确定要退出应用吗？"。
    /// </summary>
    public string ExitDialogMessage { get; set; } = "确定要退出应用吗？";

    /// <summary>
    /// 应用启动时执行的回调，可为 null。
    /// </summary>
    public Action? OnStartup { get; set; }

    /// <summary>
    /// 应用关闭时执行的回调，可为 null。
    /// 对应 Wails v3 Go 版本 <c>Options.OnShutdown</c>：在所有关闭任务（<see cref="ShutdownTasks"/>）中首先执行。
    /// </summary>
    public Action? OnShutdown { get; set; }

    /// <summary>
    /// 应用完全关闭后执行的回调，可为 null。
    /// <para>
    /// 对应 Wails v3 Go 版本 <c>Options.PostShutdown</c>：
    /// 在 <see cref="Application.Shutdown"/> 方法的最末尾、所有清理工作完成之后调用。
    /// 与 <see cref="OnShutdown"/> 的区别：
    /// <list type="bullet">
    /// <item><c>OnShutdown</c> 在关闭流程开始时调用，此时窗口、服务、传输层等仍可访问。</item>
    /// <item><c>PostShutdown</c> 在关闭流程结束后调用，此时所有资源已释放、平台已销毁。</item>
    /// </list>
    /// </para>
    /// <para>
    /// 典型用途：刷新日志、释放外部资源（如文件句柄、命名管道）、上报关闭指标等。
    /// </para>
    /// </summary>
    public Action? PostShutdown { get; set; }

    /// <summary>
    /// 决定应用是否应该退出的回调，可为 null。返回 true 表示允许退出；返回 false 表示阻止退出。
    /// <para>
    /// 对应 Wails v3 Go 版本 <c>Options.ShouldQuit</c>：
    /// 由平台信号处理器在收到退出信号（如最后一个窗口关闭、系统关机、Ctrl+C）时调用。
    /// 若返回 false，平台将不触发关闭流程，应用继续运行。
    /// </para>
    /// <para>
    /// 默认行为（为 null 时）：始终返回 true，即允许退出。
    /// </para>
    /// <para>
    /// 典型用途：未保存数据提示、下载任务进行中阻止退出、托盘最小化而非退出等。
    /// </para>
    /// </summary>
    public Func<bool>? ShouldQuit { get; set; }

    /// <summary>
    /// 服务列表。
    /// </summary>
    public List<object> Services { get; set; } = new();

    /// <summary>
    /// 平台标志字典。
    /// </summary>
    public Dictionary<string, object?> Flags { get; set; } = new();

    /// <summary>
    /// 是否启用默认上下文菜单。
    /// </summary>
    public bool EnableDefaultContextMenu { get; set; } = true;

    /// <summary>
    /// 是否启用拖放功能。
    /// </summary>
    public bool DragAndDrop { get; set; } = true;

    /// <summary>
    /// 要绑定的类型全名列表。
    /// </summary>
    public List<string> Bind { get; set; } = new();

    /// <summary>
    /// 获取或设置资源服务器配置。
    /// 对应 Wails v3 Go 版本 application_options.go 中的 AssetServer 配置。
    /// </summary>
    public AssetOptions? AssetServer { get; set; }

    /// <summary>
    /// 获取或设置应用的能力声明列表，用于权限控制。
    /// 对应 Tauri v2 的 Capabilities 配置。
    /// </summary>
    public List<Security.Capability> Capabilities { get; set; } = new();

    /// <summary>
    /// 关闭时执行的任务列表。
    /// </summary>
    public List<Action> ShutdownTasks { get; set; } = new();

    /// <summary>
    /// 全局快捷键绑定字典，键为快捷键描述，值为回调。
    /// </summary>
    public IReadOnlyDictionary<string, Action>? KeyBindings { get; set; }

    /// <summary>
    /// 错误处理回调，可为 null。
    /// </summary>
    public Action<Exception>? ErrorHandler { get; set; }

    /// <summary>
    /// 警告处理回调，可为 null。
    /// </summary>
    public Action<string>? WarningHandler { get; set; }

    /// <summary>
    /// 致命错误处理回调，可为 null。
    /// </summary>
    public Action<Exception>? FatalErrorHandler { get; set; }

    /// <summary>
    /// 启动前执行的回调，可为 null。
    /// </summary>
    public Action? OnBeforeStart { get; set; }

    /// <summary>
    /// 启动后执行的回调，可为 null。
    /// </summary>
    public Action? OnAfterStart { get; set; }

    /// <summary>
    /// 自定义打开浏览器 URL 的函数，返回是否已处理。
    /// </summary>
    public Func<string, bool>? BrowserOpenURLFunc { get; set; }

    /// <summary>
    /// 任务栏缩略图图标字节数据，可为 null。
    /// </summary>
    public byte[]? ThumbnailIcon { get; set; }

    /// <summary>
    /// 应用级托盘图标字节数据，可为 null。
    /// </summary>
    public byte[]? TrayIcon { get; set; }

    /// <summary>
    /// 应用级是否无边框。
    /// </summary>
    public bool Frameless { get; set; } = false;

    /// <summary>
    /// 应用级背景色 RGBA 元组，可为 null 表示使用默认值。
    /// </summary>
    public (byte R, byte G, byte B, byte A)? BackgroundColour { get; set; }

    /// <summary>
    /// 单实例锁标识，可为 null。
    /// </summary>
    public string? SingleInstanceLock { get; set; }

    /// <summary>
    /// 单实例唯一 ID，可为 null。
    /// </summary>
    public string? SingleInstanceUniqueID { get; set; }

    /// <summary>
    /// 单实例模式下检测到已有实例运行时的退出码，默认 1。
    /// </summary>
    public int SingleInstanceExitCode { get; set; } = 1;

    /// <summary>
    /// 是否禁用右键菜单。
    /// </summary>
    public bool DisableContextMenu { get; set; } = false;

    /// <summary>
    /// 是否禁用拖放功能。
    /// </summary>
    public bool DisableDragAndDrop { get; set; } = false;

    /// <summary>
    /// 获取或设置内容安全策略（CSP）配置。
    /// 对应 Tauri v2 的 CSP 安全配置。
    /// </summary>
    public Wails.Net.Application.Security.CspOptions? Csp { get; set; }

    /// <summary>
    /// 获取或设置允许的外部 URL 白名单。
    /// </summary>
    public Wails.Net.Application.Security.UrlWhitelist? AllowedUrls { get; set; }
}
