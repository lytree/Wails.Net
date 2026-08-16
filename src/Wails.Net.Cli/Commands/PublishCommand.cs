using System.CommandLine;
using Wails.Net.Cli.Build;
using Wails.Net.Cli.Config;

namespace Wails.Net.Cli.Commands;

/// <summary>
/// publish 命令：发布 Wails.Net 项目为可分发应用。
/// 对应 Wails v3 Go 版本 cmd/wails3/build.go 中的发布逻辑。
/// 调用 dotnet publish 生成可执行文件与资源，
/// 并支持从 wails.json 加载 beforeBuildCommand / afterBuildCommand 钩子。
/// </summary>
internal sealed class PublishCommand : CliCommandBase
{
    /// <summary>
    /// 创建 publish 命令实例。
    /// </summary>
    /// <returns>配置好的命令。</returns>
    public static Command Create()
    {
        var projectOption = new Option<FileInfo?>("--project");
        projectOption.Description = "项目文件路径（.csproj），默认使用当前目录的项目";

        var configurationOption = new Option<string>("--configuration");
        configurationOption.Description = "构建配置（Debug 或 Release）";
        configurationOption.DefaultValueFactory = _ => "Release";

        // SDK-4：--target 为统一入口（替代/兼容 --runtime），二者为同一选项的别名。
        // RID 经 Wails.Net.Sdk 的 RID→平台映射（SDK-1）自动切换平台引用。
        var runtimeOption = new Option<string?>("--target");
        runtimeOption.Description = "目标运行时标识（RID，如 win-x64、linux-x64、android-arm64）。SDK 依据 RID 前缀自动选择平台引用。兼容别名：--runtime";
        runtimeOption.Aliases.Add("--runtime");

        var selfContainedOption = new Option<bool>("--self-contained");
        selfContainedOption.Description = "是否发布为自包含应用（包含 .NET 运行时）";
        selfContainedOption.DefaultValueFactory = _ => false;

        var outputOption = new Option<DirectoryInfo?>("--output");
        outputOption.Description = "输出目录（默认为项目 bin/Release/publish）";

        // SDK-4：--all-targets 基于 RID 的多目标发布。
        var allTargetsOption = new Option<bool>("--all-targets");
        allTargetsOption.Description = "发布所有目标 RID（win-x64、linux-x64、android-arm64；macOS 主机额外加入 osx-*）。产物按 RID 分目录输出";
        allTargetsOption.DefaultValueFactory = _ => false;

        var skipHooksOption = new Option<bool>("--skip-hooks");
        skipHooksOption.Description = "跳过 wails.json 中的 beforeBuildCommand / afterBuildCommand 钩子";
        skipHooksOption.DefaultValueFactory = _ => false;

        var skipFrontendOption = new Option<bool>("--skip-frontend");
        skipFrontendOption.Description = "跳过前端构建（frontend.buildCommand / installCommand）";
        skipFrontendOption.DefaultValueFactory = _ => false;

        var command = new Command("publish", "发布 Wails.Net 项目为可分发应用");
        command.Options.Add(projectOption);
        command.Options.Add(configurationOption);
        command.Options.Add(runtimeOption);
        command.Options.Add(selfContainedOption);
        command.Options.Add(outputOption);
        command.Options.Add(allTargetsOption);
        command.Options.Add(skipHooksOption);
        command.Options.Add(skipFrontendOption);

        command.Action = AsyncAction.Create(async (parseResult, _) =>
        {
            var project = parseResult.GetValue(projectOption);
            var configuration = parseResult.GetValue(configurationOption) ?? "Release";
            var runtime = parseResult.GetValue(runtimeOption);
            var selfContained = parseResult.GetValue(selfContainedOption);
            var output = parseResult.GetValue(outputOption);
            var allTargets = parseResult.GetValue(allTargetsOption);
            var skipHooks = parseResult.GetValue(skipHooksOption);
            var skipFrontend = parseResult.GetValue(skipFrontendOption);

            var cmd = new PublishCommand();
            return await cmd.ExecuteAsync(project, configuration, runtime, selfContained, output, allTargets, skipHooks, skipFrontend);
        });

        return command;
    }

    /// <summary>
    /// 执行 publish 命令。
    /// </summary>
    /// <param name="project">项目文件。</param>
    /// <param name="configuration">构建配置。</param>
    /// <param name="runtime">运行时标识。</param>
    /// <param name="selfContained">是否自包含。</param>
    /// <param name="output">输出目录。</param>
    /// <param name="allTargets">是否发布所有目标 RID（SDK-4）。</param>
    /// <param name="skipHooks">是否跳过构建钩子。</param>
    /// <param name="skipFrontend">是否跳过前端构建。</param>
    /// <returns>退出码。</returns>
    private async Task<int> ExecuteAsync(
        FileInfo? project,
        string configuration,
        string? runtime,
        bool selfContained,
        DirectoryInfo? output,
        bool allTargets = false,
        bool skipHooks = false,
        bool skipFrontend = false)
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

        Info($"发布项目：{projectPath.FullName}");
        Info($"配置：{configuration}");
        if (!string.IsNullOrEmpty(runtime))
        {
            Info($"运行时：{runtime}");
        }
        Info($"自包含：{(selfContained ? "是" : "否")}");
        if (allTargets)
        {
            Info($"全目标发布（RID）：{string.Join(", ", TargetDefinitions.GetAllTargets())}");
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

        var builder = new ProjectBuilder();
        BuildResult result;
        if (allTargets)
        {
            var outputBase = output?.FullName
                ?? Path.Combine(Path.GetDirectoryName(projectPath.FullName) ?? string.Empty, "bin", configuration, "publish-all");
            result = await PublishAllTargetsAsync(builder, projectPath, configuration, selfContained, outputBase);
        }
        else
        {
            result = await builder.PublishAsync(
                projectPath,
                configuration,
                runtime,
                selfContained,
                output?.FullName);
        }

        if (!result.Success)
        {
            Error($"发布失败：{result.ErrorMessage}");
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
                // afterBuildCommand 失败不视为发布失败
            }
        }

        Success($"发布成功：{result.OutputPath}");
        return 0;
    }

    /// <summary>
    /// 全目标 RID 发布（SDK-4）：循环 <see cref="TargetDefinitions.GetAllTargets"/>，
    /// 每 RID 作为运行时标识传给 dotnet publish，产物按 RID 分目录输出到 <paramref name="outputBase"/>/{rid}。
    /// </summary>
    /// <param name="builder">项目构建器。</param>
    /// <param name="projectPath">项目文件。</param>
    /// <param name="configuration">发布配置。</param>
    /// <param name="selfContained">是否自包含。</param>
    /// <param name="outputBase">输出根目录，每 RID 产物输出到其下级子目录。</param>
    /// <returns>聚合发布结果（任一失败则整体失败）。</returns>
    private async Task<BuildResult> PublishAllTargetsAsync(
        ProjectBuilder builder,
        FileInfo projectPath,
        string configuration,
        bool selfContained,
        string outputBase)
    {
        var targets = TargetDefinitions.GetAllTargets();
        Info($"--all-targets：发布 {targets.Length} 个目标 RID：{string.Join(", ", targets)}");
        var outputs = new List<string>();

        foreach (var rid in targets)
        {
            Info($"----- 发布 {rid} -----");
            var ridOut = Path.Combine(outputBase, rid);
            var r = await builder.PublishAsync(projectPath, configuration, rid, selfContained, ridOut);
            if (!r.Success)
            {
                return r;
            }
            if (!string.IsNullOrEmpty(r.OutputPath))
            {
                outputs.Add($"[{rid}] {r.OutputPath}");
            }
        }

        return new BuildResult
        {
            Success = true,
            OutputPath = string.Join("; ", outputs),
        };
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
        return csprojFiles.Length == 1 ? new FileInfo(csprojFiles[0]) : null;
    }
}
