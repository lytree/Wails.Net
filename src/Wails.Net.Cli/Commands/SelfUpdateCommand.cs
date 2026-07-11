using System.CommandLine;
using System.Diagnostics;

namespace Wails.Net.Cli.Commands;

/// <summary>
/// update 命令：CLI 自更新。
/// 对应 Wails v3 的 <c>wails3 update</c> 命令。
/// 通过 <c>dotnet tool update -g Wails.Net.Cli</c> 更新 CLI 工具。
/// </summary>
internal sealed class SelfUpdateCommand : CliCommandBase
{
    /// <summary>
    /// CLI 工具的 NuGet 包标识。
    /// </summary>
    private const string ToolPackageId = "Wails.Net.Cli";

    /// <summary>
    /// 创建 update 命令实例。
    /// </summary>
    /// <returns>配置好的命令。</returns>
    public static Command Create()
    {
        var versionOption = new Option<string?>("--version", "-v")
        {
            Description = "指定要更新的版本（未指定时使用最新版本）",
        };

        var command = new Command("update", "更新 Wails.Net CLI 到最新版本");
        command.Options.Add(versionOption);

        command.Action = AsyncAction.Create(async (parseResult, _) =>
        {
            var version = parseResult.GetValue(versionOption);
            var cmd = new SelfUpdateCommand();
            return await cmd.ExecuteAsync(version);
        });

        return command;
    }

    /// <summary>
    /// 执行 CLI 自更新。
    /// </summary>
    /// <param name="version">指定版本（可选）。</param>
    /// <returns>退出码。</returns>
    internal async Task<int> ExecuteAsync(string? version)
    {
        var currentVersion = VersionCommand.GetCliVersion();
        Info($"当前版本: v{currentVersion}");

        var args = new List<string> { "tool", "update", "-g", ToolPackageId };

        if (!string.IsNullOrEmpty(version))
        {
            args.Add("--version");
            args.Add(version);
        }

        Info($"正在更新 {ToolPackageId}...");
        var (exitCode, output) = await RunDotnetAsync(args);

        if (exitCode != 0)
        {
            Error($"更新失败：dotnet tool update 退出码 {exitCode}");
            if (!string.IsNullOrEmpty(output))
            {
                Info(output);
            }
            return 2;
        }

        Success("更新完成");
        if (!string.IsNullOrEmpty(output))
        {
            Info(output);
        }

        return 0;
    }

    /// <summary>
    /// 运行 dotnet 命令并捕获输出。
    /// </summary>
    /// <param name="args">参数列表。</param>
    /// <returns>(退出码, 标准输出+错误输出)。</returns>
    private static async Task<(int ExitCode, string Output)> RunDotnetAsync(IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        try
        {
            using var proc = new Process { StartInfo = psi };
            proc.Start();

            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var combined = string.IsNullOrEmpty(stderr) ? stdout : $"{stdout}\n{stderr}";
            return (proc.ExitCode, combined);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
