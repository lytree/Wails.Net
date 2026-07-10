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
    /// 资源服务器配置，暂用 object 类型。
    /// </summary>
    public object? AssetServer { get; set; }

    /// <summary>
    /// 关闭时执行的任务列表。
    /// </summary>
    public List<Action> ShutdownTasks { get; set; } = new();
}
