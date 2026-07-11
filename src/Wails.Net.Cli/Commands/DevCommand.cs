using System.CommandLine;
using System.Diagnostics;

namespace Wails.Net.Cli.Commands;

/// <summary>
/// dev 命令：启动开发服务器与热更新。
/// 对应 Wails v3 Go 版本 cmd/wails3/dev.go。
/// 内部调用 dotnet watch 实现文件变更时自动重建与重启。
/// </summary>
internal sealed class DevCommand : CliCommandBase
{
    /// <summary>
    /// 创建 dev 命令实例。
    /// </summary>
    /// <returns>配置好的命令。</returns>
    public static Command Create()
    {
        var projectOption = new Option<FileInfo?>("--project");
        projectOption.Description = "项目文件路径（.csproj），默认使用当前目录的项目";

        var noHotReloadOption = new Option<bool>("--no-hot-reload");
        noHotReloadOption.Description = "禁用热更新，每次变更后完整重启";
        noHotReloadOption.DefaultValueFactory = _ => false;

        var verboseOption = new Option<bool>("--verbose");
        verboseOption.Description = "输出详细日志";
        verboseOption.DefaultValueFactory = _ => false;

        var command = new Command("dev", "启动开发服务器（热更新）");
        command.Options.Add(projectOption);
        command.Options.Add(noHotReloadOption);
        command.Options.Add(verboseOption);

        command.Action = AsyncAction.Create(async (parseResult, ct) =>
        {
            var project = parseResult.GetValue(projectOption);
            var noHotReload = parseResult.GetValue(noHotReloadOption);
            var verbose = parseResult.GetValue(verboseOption);

            var cmd = new DevCommand();
            return await cmd.ExecuteAsync(project, noHotReload, verbose, ct);
        });

        return command;
    }

    /// <summary>
    /// 执行 dev 命令。
    /// </summary>
    /// <param name="project">项目文件。</param>
    /// <param name="noHotReload">是否禁用热更新。</param>
    /// <param name="verbose">是否输出详细日志。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>退出码。</returns>
    private async Task<int> ExecuteAsync(
        FileInfo? project,
        bool noHotReload,
        bool verbose,
        CancellationToken cancellationToken)
    {
        var projectPath = ResolveProjectPath(project);
        if (projectPath is null)
        {
            Error("未找到项目文件，请通过 --project 指定，或在项目目录中运行");
            return 1;
        }

        Info($"启动开发模式：{projectPath.FullName}");
        Info(noHotReload ? "热更新已禁用" : "热更新已启用");
        if (verbose)
        {
            Info("详细日志模式");
        }

        var args = new List<string> { "watch", "--project", projectPath.FullName };

        if (noHotReload)
        {
            args.Add("--no-hot-reload");
        }

        if (verbose)
        {
            args.Add("--verbose");
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                UseShellExecute = false,
                CreateNoWindow = false,
            };
            foreach (var a in args)
            {
                psi.ArgumentList.Add(a);
            }

            using var proc = new Process { StartInfo = psi };
            proc.Start();
            await proc.WaitForExitAsync(cancellationToken);
            return proc.ExitCode;
        }
        catch (OperationCanceledException)
        {
            Warn("开发模式已停止");
            return 0;
        }
        catch (Exception ex)
        {
            Error($"启动 dotnet watch 失败：{ex.Message}");
            return 2;
        }
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
