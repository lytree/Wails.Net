using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wails.Net.Cli.Config;

/// <summary>
/// wails.json 项目配置文件模型。
/// 对应 Wails v3 Go 版本 internal/project/project.go 中的 ProjectConfig 结构，
/// 同时融合 Tauri v2 的 tauri.conf.json 字段命名风格。
/// 字段采用 camelCase（与 ProjectScaffolder 生成的 wails.json 一致）。
/// </summary>
public sealed class ProjectConfig
{
    /// <summary>
    /// JSON 序列化选项，使用 camelCase 命名策略并允许尾部逗号与注释。
    /// </summary>
    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>项目名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>项目版本号，形如 <c>0.1.0</c>。</summary>
    public string Version { get; set; } = "0.1.0";

    /// <summary>创建项目时使用的模板名称。</summary>
    public string? Template { get; set; }

    /// <summary>项目作者信息。</summary>
    public AuthorInfo? Author { get; set; }

    /// <summary>前端配置。</summary>
    public FrontendConfig Frontend { get; set; } = new();

    /// <summary>绑定代码生成配置。</summary>
    public BindingsConfig Bindings { get; set; } = new();

    /// <summary>
    /// 资源目录路径（前端构建产物所在目录），对应 Wails v3 的 <c>assetdir</c>。
    /// </summary>
    public string? AssetDir { get; set; }

    /// <summary>
    /// 输出可执行文件名（不含扩展名），对应 Wails v3 的 <c>outputfilename</c>。
    /// </summary>
    public string? OutputFilename { get; set; }

    /// <summary>
    /// Wails JS 运行时输出目录，对应 Wails v3 的 <c>wailsjsdir</c>。
    /// </summary>
    public string? WailsJsDir { get; set; }

    /// <summary>
    /// 构建前执行的钩子命令（在 dotnet build 之前运行）。
    /// 对应 Wails v3 的 <c>beforeBuildCommand</c>。
    /// </summary>
    public string? BeforeBuildCommand { get; set; }

    /// <summary>
    /// 构建后执行的钩子命令（在 dotnet build 成功之后运行）。
    /// 对应 Wails v3 的 <c>afterBuildCommand</c>。
    /// </summary>
    public string? AfterBuildCommand { get; set; }

    /// <summary>
    /// 开发模式启动前执行的钩子命令（在 dotnet watch 之前运行）。
    /// 对应 Wails v3 的 <c>beforeDevCommand</c>。
    /// </summary>
    public string? BeforeDevCommand { get; set; }

    /// <summary>
    /// 开发模式结束后执行的钩子命令（在 dotnet watch 退出之后运行）。
    /// 对应 Wails v3 的 <c>afterDevCommand</c>。
    /// </summary>
    public string? AfterDevCommand { get; set; }

    /// <summary>
    /// 从指定路径加载 wails.json 配置文件。
    /// </summary>
    /// <param name="configPath">wails.json 文件完整路径。</param>
    /// <returns>反序列化后的 <see cref="ProjectConfig"/> 实例；若文件不存在则返回 null。</returns>
    /// <exception cref="JsonException">文件内容不是合法的 wails.json 格式时抛出。</exception>
    public static async Task<ProjectConfig?> LoadFromFileAsync(string configPath)
    {
        if (!File.Exists(configPath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(configPath);
        return JsonSerializer.Deserialize<ProjectConfig>(json, SerializerOptions);
    }

    /// <summary>
    /// 在指定项目目录或其祖先目录中查找 wails.json 并加载。
    /// 查找顺序：项目文件所在目录 → 当前工作目录。
    /// </summary>
    /// <param name="projectPath">项目文件（.csproj）路径或目录路径；为 null 时使用当前工作目录。</param>
    /// <returns>加载的 <see cref="ProjectConfig"/> 实例与配置文件路径的元组；未找到时返回 (null, null)。</returns>
    public static async Task<(ProjectConfig? Config, string? ConfigPath)> FindAndLoadAsync(string? projectPath)
    {
        var configPath = FindConfigPath(projectPath);
        if (configPath is null)
        {
            return (null, null);
        }

        var config = await LoadFromFileAsync(configPath);
        return (config, configPath);
    }

    /// <summary>
    /// 在指定项目目录或其祖先目录中查找 wails.json 文件路径。
    /// </summary>
    /// <param name="projectPath">项目文件（.csproj）路径或目录路径；为 null 时使用当前工作目录。</param>
    /// <returns>wails.json 文件完整路径；未找到时返回 null。</returns>
    public static string? FindConfigPath(string? projectPath)
    {
        var searchDirs = new List<string>();

        if (!string.IsNullOrEmpty(projectPath))
        {
            if (File.Exists(projectPath))
            {
                searchDirs.Add(Path.GetDirectoryName(projectPath)!);
            }
            else if (Directory.Exists(projectPath))
            {
                searchDirs.Add(projectPath);
            }
        }

        searchDirs.Add(Directory.GetCurrentDirectory());

        foreach (var dir in searchDirs.Where(d => !string.IsNullOrEmpty(d)).Distinct())
        {
            var candidate = Path.Combine(dir, "wails.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}

/// <summary>
/// 作者信息。
/// 对应 Wails v3 wails.json 中的 author 节。
/// </summary>
public sealed class AuthorInfo
{
    /// <summary>作者名称。</summary>
    public string? Name { get; set; }

    /// <summary>作者邮箱。</summary>
    public string? Email { get; set; }
}

/// <summary>
/// 前端配置。
/// 对应 Wails v3 wails.json 中的 frontend 节。
/// </summary>
public sealed class FrontendConfig
{
    /// <summary>前端源码目录，默认 <c>frontend</c>。</summary>
    public string Dir { get; set; } = "frontend";

    /// <summary>
    /// 前端开发服务器 URL（开发模式下由 dev 命令使用）。
    /// 对应 Wails v3 的 <c>frontend:dev:serverUrl</c>。
    /// </summary>
    public string? DevServerUrl { get; set; }

    /// <summary>
    /// 前端构建命令（如 <c>npm run build</c>），由 build 命令在 dotnet build 前调用。
    /// 对应 Wails v3 的 <c>frontend:build</c>。
    /// </summary>
    public string? BuildCommand { get; set; }

    /// <summary>
    /// 前端依赖安装命令（如 <c>npm install</c>），首次构建或显式触发时调用。
    /// 对应 Wails v3 的 <c>frontend:install</c>。
    /// </summary>
    public string? InstallCommand { get; set; }

    /// <summary>
    /// 前端构建产物输出目录（相对路径），对应 Wails v3 的 <c>outputDir</c>。
    /// </summary>
    public string? OutputDir { get; set; }
}

/// <summary>
/// 绑定代码生成配置。
/// 对应 Wails v3 wails.json 中的 bindings 节。
/// </summary>
public sealed class BindingsConfig
{
    /// <summary>绑定的 TypeScript 输出目录，默认 <c>frontend/src/wails</c>。</summary>
    public string OutputDir { get; set; } = "frontend/src/wails";
}
