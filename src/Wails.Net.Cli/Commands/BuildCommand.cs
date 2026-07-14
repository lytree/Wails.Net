using System.CommandLine;
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

        var command = new Command("build", "编译 Wails.Net 项目");
        command.Options.Add(projectOption);
        command.Options.Add(configurationOption);
        command.Options.Add(runtimeOption);
        command.Options.Add(selfContainedOption);
        command.Options.Add(skipHooksOption);
        command.Options.Add(skipFrontendOption);

        command.Action = AsyncAction.Create(async (parseResult, _) =>
        {
            var project = parseResult.GetValue(projectOption);
            var configuration = parseResult.GetValue(configurationOption) ?? "Release";
            var runtime = parseResult.GetValue(runtimeOption);
            var selfContained = parseResult.GetValue(selfContainedOption);
            var skipHooks = parseResult.GetValue(skipHooksOption);
            var skipFrontend = parseResult.GetValue(skipFrontendOption);

            var cmd = new BuildCommand();
            return await cmd.ExecuteAsync(project, configuration, runtime, selfContained, skipHooks, skipFrontend);
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
    /// <returns>退出码。</returns>
    private async Task<int> ExecuteAsync(
        FileInfo? project,
        string configuration,
        string? runtime,
        bool selfContained,
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
        var result = await builder.BuildAsync(projectPath, configuration, runtime, selfContained);

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
