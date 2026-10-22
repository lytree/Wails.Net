using System.Text.Json.Nodes;

namespace Wails.Net.Cli.Mcp;

/// <summary>
/// MCP 工具定义。
/// </summary>
/// <param name="Name">工具名。</param>
/// <param name="Description">工具描述。</param>
/// <param name="InputSchema">入参 JSON Schema。</param>
/// <param name="ReadOnly">是否只读。</param>
/// <param name="OpenWorld">是否访问允许根目录之外的世界。</param>
/// <param name="Destructive">是否具有破坏性。</param>
internal sealed record McpToolDefinition(
    string Name,
    string Description,
    JsonObject InputSchema,
    bool ReadOnly,
    bool OpenWorld,
    bool Destructive)
{
    /// <summary>
    /// 转换为 <c>tools/list</c> 返回条目。
    /// </summary>
    /// <returns>工具描述对象。</returns>
    public JsonObject ToJson() => new()
    {
        ["name"] = Name,
        ["description"] = Description,
        ["inputSchema"] = InputSchema.DeepClone(),
        ["annotations"] = new JsonObject
        {
            ["readOnlyHint"] = ReadOnly,
            ["openWorldHint"] = OpenWorld,
            ["destructiveHint"] = Destructive,
        },
    };
}

/// <summary>
/// CLI MCP 工具目录。
/// 对应 Wails v3 Go 版本 <c>internal/commands/mcp.go</c> 的 <c>registerTools</c>；
/// 工具名与语义保持一致，命令映射改为 Wails.Net CLI（<c>new</c> / <c>build</c> / <c>dev</c> / <c>generate</c>）。
/// </summary>
internal static class McpToolCatalog
{
    /// <summary>工具：项目检查（只读）。</summary>
    public const string Inspect = "wails_project_inspect";

    /// <summary>工具：初始化新项目（需令牌）。</summary>
    public const string NewProject = "wails_project_new";

    /// <summary>工具：环境诊断（只读）。</summary>
    public const string Doctor = "wails_project_doctor";

    /// <summary>工具：启动构建任务（需令牌）。</summary>
    public const string Build = "wails_project_build";

    /// <summary>工具：启动开发模式任务（需令牌）。</summary>
    public const string DevStart = "wails_project_dev_start";

    /// <summary>工具：启动 TypeScript 绑定生成任务（需令牌）。</summary>
    public const string GenerateBindings = "wails_project_generate_bindings";

    /// <summary>工具：查询任务状态（只读）。</summary>
    public const string JobStatus = "wails_job_status";

    /// <summary>工具：停止任务（需令牌）。</summary>
    public const string JobStop = "wails_job_stop";

    private const string TokenProperty =
        "\"token\":{\"type\":\"string\",\"description\":\"server instructions 中的会话令牌\"}";

    private const string PathProperty =
        "\"path\":{\"type\":\"string\",\"description\":\"相对允许根目录的项目路径；默认根目录\"}";

    private const string ArgsProperty =
        "\"args\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"追加的 CLI 参数（路径参数必须位于允许根目录内）\"}";

    private const string JobIdProperty =
        "\"jobId\":{\"type\":\"string\",\"description\":\"进程控制工具返回的任务 ID\"}";

    private const string NameProperty =
        "\"name\":{\"type\":\"string\",\"description\":\"项目名称（同时作为目录名）\"}";

    private const string DirectoryProperty =
        "\"directory\":{\"type\":\"string\",\"description\":\"项目创建目录（相对允许根目录）\"}";

    private const string TemplateProperty =
        "\"template\":{\"type\":\"string\",\"description\":\"前端模板名称；默认 vanilla-ts\"}";

    private const string AllowExternalProperty =
        "\"allowExternal\":{\"type\":\"boolean\",\"description\":\"显式允许远程模板\"}";

    /// <summary>全部工具（顺序与 Go 版本基本一致）。</summary>
    public static IReadOnlyList<McpToolDefinition> All { get; } =
    [
        new(
            Inspect,
            "检查允许根目录内的 Wails.Net 项目，不修改任何文件。",
            ObjectSchema(string.Empty, PathProperty),
            ReadOnly: true,
            OpenWorld: false,
            Destructive: false),
        new(
            NewProject,
            "在允许根目录内初始化新的 Wails.Net 项目。远程模板属于外部输入，需要显式批准。",
            ObjectSchema(
                "\"token\",\"name\"",
                TokenProperty,
                NameProperty,
                DirectoryProperty,
                TemplateProperty,
                AllowExternalProperty),
            ReadOnly: false,
            OpenWorld: true,
            Destructive: false),
        new(
            Doctor,
            "运行 Wails.Net 环境诊断并返回机器可读 JSON。",
            ObjectSchema(string.Empty),
            ReadOnly: true,
            OpenWorld: false,
            Destructive: false),
        new(
            Build,
            "在允许根目录内启动构建任务，返回任务 ID 供状态查询。",
            ObjectSchema("\"token\"", TokenProperty, PathProperty, ArgsProperty),
            ReadOnly: false,
            OpenWorld: false,
            Destructive: false),
        new(
            DevStart,
            "在允许根目录内启动开发模式任务，返回任务 ID 供状态查询。",
            ObjectSchema("\"token\"", TokenProperty, PathProperty, ArgsProperty),
            ReadOnly: false,
            OpenWorld: false,
            Destructive: false),
        new(
            GenerateBindings,
            "在允许根目录内启动 TypeScript 绑定生成任务，返回任务 ID 供状态查询。",
            ObjectSchema("\"token\"", TokenProperty, PathProperty, ArgsProperty),
            ReadOnly: false,
            OpenWorld: false,
            Destructive: false),
        new(
            JobStatus,
            "读取 MCP 任务的运行状态与有界初始输出。",
            ObjectSchema("\"jobId\"", JobIdProperty),
            ReadOnly: true,
            OpenWorld: false,
            Destructive: false),
        new(
            JobStop,
            "停止由本服务器启动的 MCP 任务。",
            ObjectSchema("\"token\",\"jobId\"", TokenProperty, JobIdProperty),
            ReadOnly: false,
            OpenWorld: false,
            Destructive: false),
    ];

    private static JsonObject ObjectSchema(string required, params string[] properties)
    {
        var json = "{\"type\":\"object\",\"properties\":{" + string.Join(",", properties) + "}";
        if (!string.IsNullOrEmpty(required))
        {
            json += ",\"required\":[" + required + "]";
        }

        json += ",\"additionalProperties\":false}";
        return (JsonObject)JsonNode.Parse(json)!;
    }
}
