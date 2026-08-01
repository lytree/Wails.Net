using System.CommandLine;
using System.Xml.Linq;
using Wails.Net.Cli.Build;
using Wails.Net.Cli.Config;

namespace Wails.Net.Cli.Commands;

/// <summary>
/// build 命令：编译 Wails.Net 项目。
/// 对应 Wails v3 Go 版本 cmd/wails3/build.go。
/// 支持从 wails.json 加载配置，并在构建前后执行钩子命令（beforeBuildCommand / afterBuildCommand）。
/// </summary>
internal sealed class BuildCommand : CliCommandBase
{
    /// <summary>
    /// 全平台构建时依次设置 <c>WailsNetPlatform</c> 的平台列表。
    /// </summary>
    private static readonly string[] AllPlatforms = { "windows", "linux", "android" };

    /// <summary>
    /// 创建 build 命令实例。
    /// </summary>
    /// <returns>配置好的命令。</returns>
    public static Command Create()
    {
        var projectOption = new Option<FileInfo?>("--project");
        projectOption.Description = "项目文件路径（.csproj），默认使用当前目录的项目";

        var configurationOption = new Option<string>("--configuration");
        configurationOption.Description = "构建配置（Debug 或 Release）";
        configurationOption.DefaultValueFactory = _ => "Release";

        var runtimeOption = new Option<string?>("--runtime");
        runtimeOption.Description = "目标运行时标识（如 win-x64、linux-x64）";

        var selfContainedOption = new Option<bool>("--self-contained");
        selfContainedOption.Description = "是否发布为自包含应用";
        selfContainedOption.DefaultValueFactory = _ => false;

        var skipHooksOption = new Option<bool>("--skip-hooks");
        skipHooksOption.Description = "跳过 wails.json 中的 beforeBuildCommand / afterBuildCommand 钩子";
        skipHooksOption.DefaultValueFactory = _ => false;

        var skipFrontendOption = new Option<bool>("--skip-frontend");
        skipFrontendOption.Description = "跳过前端构建（frontend.buildCommand / installCommand）";
        skipFrontendOption.DefaultValueFactory = _ => false;

        var allPlatformsOption = new Option<bool>("--all-platforms");
        allPlatformsOption.Description = "构建所有支持的平台（Windows/Linux/Android）。多 TFM 项目单次构建，单 TFM 项目分平台依次构建";
        allPlatformsOption.DefaultValueFactory = _ => false;

        var command = new Command("build", "编译 Wails.Net 项目");
        command.Options.Add(projectOption);
        command.Options.Add(configurationOption);
        command.Options.Add(runtimeOption);
        command.Options.Add(selfContainedOption);
        command.Options.Add(skipHooksOption);
        command.Options.Add(skipFrontendOption);
        command.Options.Add(allPlatformsOption);

        command.Action = AsyncAction.Create(async (parseResult, _) =>
        {
            var project = parseResult.GetValue(projectOption);
            var configuration = parseResult.GetValue(configurationOption) ?? "Release";
            var runtime = parseResult.GetValue(runtimeOption);
            var selfContained = parseResult.GetValue(selfContainedOption);
            var skipHooks = parseResult.GetValue(skipHooksOption);
            var skipFrontend = parseResult.GetValue(skipFrontendOption);
            var allPlatforms = parseResult.GetValue(allPlatformsOption);

            var cmd = new BuildCommand();
            return await cmd.ExecuteAsync(project, configuration, runtime, selfContained, skipHooks, skipFrontend, allPlatforms);
        });

        return command;
    }

    /// <summary>
    /// 执行 build 命令。
    /// </summary>
    /// <param name="project">项目文件。</param>
    /// <param name="configuration">构建配置。</param>
    /// <param name="runtime">运行时标识。</param>
    /// <param name="selfContained">是否自包含。</param>
    /// <param name="skipHooks">是否跳过构建钩子。</param>
    /// <param name="skipFrontend">是否跳过前端构建。</param>
    /// <param name="allPlatforms">是否构建所有平台。</param>
    /// <returns>退出码。</returns>
    private async Task<int> ExecuteAsync(
        FileInfo? project,
        string configuration,
        string? runtime,
        bool selfContained,
        bool skipHooks = false,
        bool skipFrontend = false,
        bool allPlatforms = false)
    {
        var projectPath = ResolveProjectPath(project);
        if (projectPath is null)
        {
            Error("未找到项目文件，请通过 --project 指定，或在项目目录中运行");
            return 1;
        }

        var workingDir = Path.GetDirectoryName(projectPath.FullName) ?? Directory.GetCurrentDirectory();

        // 加载 wails.json（若存在）
        var (config, configPath) = await ProjectConfig.FindAndLoadAsync(projectPath.FullName);
        if (config is not null)
        {
            Info($"加载配置：{configPath}");
        }

        // 执行前端构建（install + build）
        if (!skipFrontend && config?.Frontend is { } frontend)
        {
            var frontendDir = Path.Combine(workingDir, frontend.Dir);
            if (Directory.Exists(frontendDir))
            {
                if (!string.IsNullOrWhiteSpace(frontend.InstallCommand))
                {
                    Info($"安装前端依赖：{frontend.InstallCommand}");
                    var installResult = await BuildHooks.ExecuteAsync(frontend.InstallCommand, frontendDir);
                    if (!installResult.Success)
                    {
                        Error($"前端依赖安装失败：{installResult.ErrorMessage}");
                        if (!string.IsNullOrEmpty(installResult.Output))
                        {
                            Info(installResult.Output);
                        }
                        return 3;
                    }
                }

                if (!string.IsNullOrWhiteSpace(frontend.BuildCommand))
                {
                    Info($"构建前端：{frontend.BuildCommand}");
                    var frontendBuild = await BuildHooks.ExecuteAsync(frontend.BuildCommand, frontendDir);
                    if (!frontendBuild.Success)
                    {
                        Error($"前端构建失败：{frontendBuild.ErrorMessage}");
                        if (!string.IsNullOrEmpty(frontendBuild.Output))
                        {
                            Info(frontendBuild.Output);
                        }
                        return 3;
                    }
                }
            }
        }

        // 执行 beforeBuildCommand 钩子
        if (!skipHooks && !string.IsNullOrWhiteSpace(config?.BeforeBuildCommand))
        {
            Info($"执行 beforeBuildCommand：{config!.BeforeBuildCommand}");
            var beforeResult = await BuildHooks.ExecuteAsync(config.BeforeBuildCommand, workingDir);
            if (!beforeResult.Success)
            {
                Error($"beforeBuildCommand 失败：{beforeResult.ErrorMessage}");
                if (!string.IsNullOrEmpty(beforeResult.Output))
                {
                    Info(beforeResult.Output);
                }
                return 4;
            }
        }

        // 执行 dotnet build
        var builder = new ProjectBuilder();
        BuildResult? result;

        if (allPlatforms)
        {
            result = await BuildAllPlatformsAsync(builder, projectPath, configuration, runtime, selfContained);
        }
        else
        {
            result = await builder.BuildAsync(projectPath, configuration, runtime, selfContained);
        }

        if (!result.Success)
        {
            Error($"构建失败：{result.ErrorMessage}");
            if (!string.IsNullOrEmpty(result.BuildLog))
            {
                Info(result.BuildLog);
            }
            return 2;
        }

        // 执行 afterBuildCommand 钩子
        if (!skipHooks && !string.IsNullOrWhiteSpace(config?.AfterBuildCommand))
        {
            Info($"执行 afterBuildCommand：{config!.AfterBuildCommand}");
            var afterResult = await BuildHooks.ExecuteAsync(config.AfterBuildCommand, workingDir);
            if (!afterResult.Success)
            {
                Warn($"afterBuildCommand 失败：{afterResult.ErrorMessage}");
                if (!string.IsNullOrEmpty(afterResult.Output))
                {
                    Info(afterResult.Output);
                }
                // afterBuildCommand 失败不视为构建失败
            }
        }

        Success($"构建成功：{result.OutputPath}");
        return 0;
    }

    /// <summary>
    /// 全平台构建：优先检测多 TFM，单 TFM 则按 <see cref="AllPlatforms"/> 依次构建。
    /// </summary>
    /// <param name="builder">项目构建器。</param>
    /// <param name="projectPath">项目文件。</param>
    /// <param name="configuration">构建配置。</param>
    /// <param name="runtime">运行时标识（可空，全平台构建时通常不指定）。</param>
    /// <param name="selfContained">是否自包含。</param>
    /// <returns>聚合构建结果（任一失败则整体失败）。</returns>
    private async Task<BuildResult> BuildAllPlatformsAsync(
        ProjectBuilder builder,
        FileInfo projectPath,
        string configuration,
        string? runtime,
        bool selfContained)
    {
        var targetFrameworks = ParseTargetFrameworks(projectPath.FullName);

        // 多 TFM 项目：单次 dotnet build 即可，MSBuild 会为每个 TFM 分别构建
        if (targetFrameworks.Count >= 2)
        {
            Info($"检测到多 TFM 项目（{string.Join(", ", targetFrameworks)}），单次构建覆盖全平台");
            return await builder.BuildAsync(projectPath, configuration, runtime, selfContained);
        }

        // 单 TFM 项目：按平台依次构建，通过 WailsNetPlatform 属性切换平台
        Info($"项目为单 TFM（{(targetFrameworks.Count == 1 ? targetFrameworks[0] : "未指定")}），将分平台依次构建");
        var outputs = new List<string>();
        foreach (var platform in AllPlatforms)
        {
            Info($"----- 构建 {platform} -----");
            var props = new Dictionary<string, string>
            {
                ["WailsNetPlatform"] = platform,
            };
            var r = await builder.BuildAsync(projectPath, configuration, runtime, selfContained, props);
            if (!r.Success)
            {
                return r;
            }
            if (!string.IsNullOrEmpty(r.OutputPath))
            {
                outputs.Add($"[{platform}] {r.OutputPath}");
            }
        }

        return new BuildResult
        {
            Success = true,
            OutputPath = string.Join("; ", outputs),
        };
    }

    /// <summary>
    /// 从 .csproj 文件解析 TargetFrameworks（复数）/ TargetFramework（单数）。
    /// </summary>
    /// <param name="projectPath">.csproj 文件路径。</param>
    /// <returns>TFM 列表（空列表表示未找到）。</returns>
    private static List<string> ParseTargetFrameworks(string projectPath)
    {
        try
        {
            var doc = XDocument.Load(projectPath);
            var root = doc.Root;
            if (root is null) return new List<string>();

            var ns = root.GetDefaultNamespace();
            var propsGroup = root.Element(XName.Get("PropertyGroup", ns.NamespaceName));
            if (propsGroup is null) return new List<string>();

            // 优先读 TargetFrameworks（复数）
            var multi = propsGroup.Element(XName.Get("TargetFrameworks", ns.NamespaceName));
            if (multi is not null && !string.IsNullOrWhiteSpace(multi.Value))
            {
                return multi.Value
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
            }

            // 回退到 TargetFramework（单数）
            var single = propsGroup.Element(XName.Get("TargetFramework", ns.NamespaceName));
            if (single is not null && !string.IsNullOrWhiteSpace(single.Value))
            {
                return new List<string> { single.Value.Trim() };
            }
        }
        catch
        {
            // 解析失败按空列表处理，调用方会回退到单次构建
        }
        return new List<string>();
    }

    /// <summary>
    /// 解析项目文件路径。
    /// </summary>
    /// <param name="project">用户指定的项目文件。</param>
    /// <returns>项目文件路径，若未找到则返回 null。</returns>
    private static FileInfo? ResolveProjectPath(FileInfo? project)
    {
        if (project is not null)
        {
            return project.Exists ? project : null;
        }

        var currentDir = Directory.GetCurrentDirectory();
        var csprojFiles = Directory.GetFiles(currentDir, "*.csproj");
        if (csprojFiles.Length == 1)
        {
            return new FileInfo(csprojFiles[0]);
        }

        return null;
    }
}
