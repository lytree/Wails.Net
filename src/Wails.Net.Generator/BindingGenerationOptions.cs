namespace Wails.Net.Generator;

/// <summary>
/// 绑定代码生成选项，配置生成行为。
/// </summary>
public sealed class BindingGenerationOptions
{
    /// <summary>
    /// 输出目录路径。
    /// </summary>
    public string OutputDirectory { get; set; } = "src/wails";

    /// <summary>
    /// 类型定义文件名（.d.ts）。
    /// </summary>
    public string DefinitionsFileName { get; set; } = "bindings.d.ts";

    /// <summary>
    /// 调用封装文件名（.ts）。
    /// </summary>
    public string CallerFileName { get; set; } = "bindings.ts";

    /// <summary>
    /// 绑定 ID 映射文件名。
    /// </summary>
    public string IdMapFileName { get; set; } = "binding-ids.ts";

    /// <summary>
    /// 事件类型定义文件名。
    /// </summary>
    public string EventsFileName { get; set; } = "events.ts";

    /// <summary>
    /// 已知事件名称常量文件名。
    /// </summary>
    public string KnownEventsFileName { get; set; } = "known-events.ts";

    /// <summary>
    /// 是否生成类型定义文件。
    /// </summary>
    public bool GenerateDefinitions { get; set; } = true;

    /// <summary>
    /// 是否生成调用封装文件。
    /// </summary>
    public bool GenerateCaller { get; set; } = true;

    /// <summary>
    /// 是否生成绑定 ID 映射文件。
    /// </summary>
    public bool GenerateIdMap { get; set; } = true;

    /// <summary>
    /// 是否生成事件类型文件。
    /// </summary>
    public bool GenerateEvents { get; set; } = true;

    /// <summary>
    /// 是否生成已知事件名称常量文件。
    /// </summary>
    public bool GenerateKnownEvents { get; set; } = true;
}
