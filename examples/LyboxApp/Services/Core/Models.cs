namespace LyboxApp.Services.Core;

/// <summary>
/// 插件清单（对应 LYBox 的 plugin.json 元数据）。
/// 由每个插件在 ConfigureServices 中以单例形式注册，插件管理页据此展示。
/// </summary>
public class PluginManifest
{
    /// <summary>插件唯一标识（UUID）。</summary>
    public string Id { get; set; } = "";

    /// <summary>插件显示名称。</summary>
    public string Name { get; set; } = "";

    /// <summary>作者。</summary>
    public string Author { get; set; } = "";

    /// <summary>版本号。</summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>描述。</summary>
    public string Description { get; set; } = "";

    /// <summary>分类（System / Demo / 等）。</summary>
    public string Category { get; set; } = "Demo";

    /// <summary>主路由（可选）。</summary>
    public string? Route { get; set; }
}

/// <summary>
/// 导航项。插件在 ConfigureServices 中注册，侧边栏据此渲染（仅启用插件可见）。
/// </summary>
public class NavItem
{
    /// <summary>路由 Key（与前端页面注册表对应）。</summary>
    public string Key { get; set; } = "";

    /// <summary>i18n 标题 Key。</summary>
    public string TitleKey { get; set; } = "";

    /// <summary>图标名（前端映射为 SVG/emoji）。</summary>
    public string Icon { get; set; } = "circle";

    /// <summary>排序（越小越靠前）。</summary>
    public int Order { get; set; } = 100;

    /// <summary>父级 Key（可选，用于分组）。</summary>
    public string? ParentKey { get; set; }

    /// <summary>所属插件 Id。</summary>
    public string PluginId { get; set; } = "";
}

/// <summary>
/// 任务信息（用于任务托盘 / 任务页）。
/// </summary>
public class TaskInfo
{
    /// <summary>任务 Id。</summary>
    public string Id { get; set; } = "";

    /// <summary>任务名称。</summary>
    public string Name { get; set; } = "";

    /// <summary>状态：running / done / failed / canceled。</summary>
    public string Status { get; set; } = "running";

    /// <summary>进度（0-100）。</summary>
    public double Progress { get; set; }

    /// <summary>详情文本。</summary>
    public string? Detail { get; set; }

    /// <summary>开始时间。</summary>
    public DateTime StartedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// 插件视图模型（插件管理页使用，合并启用状态）。
/// </summary>
public class PluginView
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "Demo";
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// 语言信息。
/// </summary>
public class LanguageInfo
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>
/// 设置数据传输对象（前端保存时回传）。
/// 采用基础类型，避免 JSON 命名策略导致的属性名不匹配。
/// </summary>
public class SettingsDto
{
    public string Language { get; set; } = "zh-CN";
    public string Theme { get; set; } = "light";
    public Dictionary<string, bool> PluginEnabled { get; set; } = new();
}
