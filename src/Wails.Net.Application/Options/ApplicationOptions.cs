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
    /// 应用启动时执行的回调，可为 null。
    /// </summary>
    public Action? OnStartup { get; set; }

    /// <summary>
    /// 应用关闭时执行的回调，可为 null。
    /// </summary>
    public Action? OnShutdown { get; set; }

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
    public List<Capability> Capabilities { get; set; } = new();

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
