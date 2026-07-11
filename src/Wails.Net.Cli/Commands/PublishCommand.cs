using System.CommandLine;
using Wails.Net.Cli.Build;

namespace Wails.Net.Cli.Commands;

/// <summary>
/// publish 命令：发布 Wails.Net 项目为可分发应用。
/// 对应 Wails v3 Go 版本 cmd/wails3/build.go 中的发布逻辑。
/// 调用 dotnet publish 生成可执行文件与资源。
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

        var runtimeOption = new Option<string?>("--runtime");
        runtimeOption.Description = "目标运行时标识（如 win-x64、linux-x64）";

        var selfContainedOption = new Option<bool>("--self-contained");
        selfContainedOption.Description = "是否发布为自包含应用（包含 .NET 运行时）";
        selfContainedOption.DefaultValueFactory = _ => false;

        var outputOption = new Option<DirectoryInfo?>("--output");
        outputOption.Description = "输出目录（默认为项目 bin/Release/publish）";

        var command = new Command("publish", "发布 Wails.Net 项目为可分发应用");
        command.Options.Add(projectOption);
        command.Options.Add(configurationOption);
        command.Options.Add(runtimeOption);
        command.Options.Add(selfContainedOption);
        command.Options.Add(outputOption);

        command.Action = AsyncAction.Create(async (parseResult, _) =>
        {
            var project = parseResult.GetValue(projectOption);
            var configuration = parseResult.GetValue(configurationOption) ?? "Release";
            var runtime = parseResult.GetValue(runtimeOption);
            var selfContained = parseResult.GetValue(selfContainedOption);
            var output = parseResult.GetValue(outputOption);

            var cmd = new PublishCommand();
            return await cmd.ExecuteAsync(project, configuration, runtime, selfContained, output);
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
    /// <returns>退出码。</returns>
    private async Task<int> ExecuteAsync(
        FileInfo? project,
        string configuration,
        string? runtime,
        bool selfContained,
        DirectoryInfo? output)
    {
        var projectPath = ResolveProjectPath(project);
        if (projectPath is null)
        {
            Error("未找到项目文件，请通过 --project 指定，或在项目目录中运行");
            return 1;
        }

        Info($"发布项目：{projectPath.FullName}");
        Info($"配置：{configuration}");
        if (!string.IsNullOrEmpty(runtime))
        {
            Info($"运行时：{runtime}");
        }
        Info($"自包含：{(selfContained ? "是" : "否")}");

        var builder = new ProjectBuilder();
        var result = await builder.PublishAsync(
            projectPath,
            configuration,
            runtime,
            selfContained,
            output?.FullName);

        if (!result.Success)
        {
            Error($"发布失败：{result.ErrorMessage}");
            if (!string.IsNullOrEmpty(result.BuildLog))
            {
                Info(result.BuildLog);
            }
            return 2;
        }

        Success($"发布成功：{result.OutputPath}");
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
        return csprojFiles.Length == 1 ? new FileInfo(csprojFiles[0]) : null;
    }
}
