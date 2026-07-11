using System.CommandLine;
using System.Diagnostics;

namespace Wails.Net.Cli.Commands;

/// <summary>
/// platform 命令：管理 .NET 目标平台和工作负载。
/// 对应 Tauri v2 的 <c>tauri platform</c> 命令。
/// 封装 <c>dotnet --info</c>、<c>dotnet workload list/install/uninstall</c>。
/// </summary>
internal sealed class PlatformCommand : CliCommandBase
{
    /// <summary>
    /// 支持的目标平台 RID 列表。
    /// </summary>
    private static readonly string[] SupportedRids =
    [
        "win-x64",
        "win-x86",
        "win-arm64",
        "linux-x64",
        "linux-arm64",
        "linux-musl-x64",
        "linux-musl-arm64",
    ];

    /// <summary>
    /// 创建 platform 命令实例。
    /// </summary>
    /// <returns>配置好的命令。</returns>
    public static Command Create()
    {
        var command = new Command("platform", "管理目标平台和工作负载");

        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateAddCommand());
        command.Subcommands.Add(CreateRemoveCommand());

        return command;
    }

    /// <summary>
    /// 创建 platform list 子命令。
    /// </summary>
    /// <returns>list 子命令。</returns>
    private static Command CreateListCommand()
    {
        var command = new Command("list", "列出已安装的工作负载和可用平台");
        command.Action = AsyncAction.Create(async (_, _) =>
        {
            var cmd = new PlatformCommand();
            return await cmd.ListAsync();
        });
        return command;
    }

    /// <summary>
    /// 创建 platform add 子命令。
    /// </summary>
    /// <returns>add 子命令。</returns>
    private static Command CreateAddCommand()
    {
        var ridArgument = new Argument<string>("rid")
        {
            Description = "目标平台 RID（如 win-x64、linux-x64）",
        };

        var command = new Command("add", "安装指定平台的工作负载");
        command.Arguments.Add(ridArgument);
        command.Action = AsyncAction.Create(async (parseResult, _) =>
        {
            var rid = parseResult.GetValue(ridArgument);
            var cmd = new PlatformCommand();
            return await cmd.AddAsync(rid!);
        });
        return command;
    }

    /// <summary>
    /// 创建 platform remove 子命令。
    /// </summary>
    /// <returns>remove 子命令。</returns>
    private static Command CreateRemoveCommand()
    {
        var ridArgument = new Argument<string>("rid")
        {
            Description = "目标平台 RID",
        };

        var command = new Command("remove", "卸载指定平台的工作负载");
        command.Arguments.Add(ridArgument);
        command.Action = AsyncAction.Create(async (parseResult, _) =>
        {
            var rid = parseResult.GetValue(ridArgument);
            var cmd = new PlatformCommand();
            return await cmd.RemoveAsync(rid!);
        });
        return command;
    }

    /// <summary>
    /// 列出已安装的工作负载和可用平台。
    /// </summary>
    /// <returns>退出码。</returns>
    internal async Task<int> ListAsync()
    {
        Info("可用目标平台 RID：");
        foreach (var rid in SupportedRids)
        {
            Info($"  - {rid}");
        }

        Info(string.Empty);
        Info("已安装的 .NET 工作负载：");

        var (exitCode, output) = await RunDotnetAsync(["workload", "list"]);
        if (exitCode != 0)
        {
            Warn("无法获取工作负载列表。");
            return 0;
        }

        if (!string.IsNullOrEmpty(output))
        {
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                Info($"  {line.Trim()}");
            }
        }

        return 0;
    }

    /// <summary>
    /// 安装指定平台的工作负载。
    /// </summary>
    /// <param name="rid">目标平台 RID。</param>
    /// <returns>退出码。</returns>
    internal async Task<int> AddAsync(string rid)
    {
        if (!IsSupportedRid(rid))
        {
            Error($"不支持的平台 RID：{rid}");
            Info($"支持的 RID：{string.Join(", ", SupportedRids)}");
            return 1;
        }

        Info($"为平台 {rid} 安装工作负载...");
        var workload = MapRidToWorkload(rid);
        if (workload is null)
        {
            Warn($"平台 {rid} 无需额外工作负载。");
            return 0;
        }

        var (exitCode, output) = await RunDotnetAsync(["workload", "install", workload]);
        if (exitCode != 0)
        {
            Error($"安装工作负载失败：{workload}");
            if (!string.IsNullOrEmpty(output))
            {
                Info(output);
            }
            return 2;
        }

        Success($"工作负载 {workload} 已安装");
        return 0;
    }

    /// <summary>
    /// 卸载指定平台的工作负载。
    /// </summary>
    /// <param name="rid">目标平台 RID。</param>
    /// <returns>退出码。</returns>
    internal async Task<int> RemoveAsync(string rid)
    {
        if (!IsSupportedRid(rid))
        {
            Error($"不支持的平台 RID：{rid}");
            Info($"支持的 RID：{string.Join(", ", SupportedRids)}");
            return 1;
        }

        var workload = MapRidToWorkload(rid);
        if (workload is null)
        {
            Warn($"平台 {rid} 无需额外工作负载。");
            return 0;
        }

        Info($"从平台 {rid} 卸载工作负载 {workload}...");
        var (exitCode, output) = await RunDotnetAsync(["workload", "uninstall", workload]);
        if (exitCode != 0)
        {
            Error($"卸载工作负载失败：{workload}");
            if (!string.IsNullOrEmpty(output))
            {
                Info(output);
            }
            return 2;
        }

        Success($"工作负载 {workload} 已卸载");
        return 0;
    }

    /// <summary>
    /// 检查 RID 是否受支持。
    /// </summary>
    /// <param name="rid">平台 RID。</param>
    /// <returns>受支持返回 true。</returns>
    internal static bool IsSupportedRid(string rid)
    {
        return Array.Exists(SupportedRids, r => string.Equals(r, rid, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 将 RID 映射到对应的 .NET 工作负载名称。
    /// </summary>
    /// <param name="rid">平台 RID。</param>
    /// <returns>工作负载名称，若无需额外工作负载则返回 null。</returns>
    internal static string? MapRidToWorkload(string rid)
    {
        // Windows 桌面应用需要 maui-windows 工作负载（用于 WPF/WinForms）
        if (rid.StartsWith("win-", StringComparison.OrdinalIgnoreCase))
        {
            return "maui-windows";
        }

        // Linux 平台无需额外工作负载
        return null;
    }

    /// <summary>
    /// 获取支持的 RID 列表。
    /// </summary>
    /// <returns>RID 数组。</returns>
    internal static string[] GetSupportedRids() => SupportedRids;

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
