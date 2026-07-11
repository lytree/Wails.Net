using System.CommandLine;
using System.Diagnostics;

namespace Wails.Net.Cli.Commands;

/// <summary>
/// clean 命令：清理项目的 bin/obj 目录。
/// 对应 Tauri v2 的 <c>tauri clean</c> 命令。
/// 递归删除项目根目录下所有 bin/ 和 obj/ 目录，可选清理 NuGet HTTP 缓存。
/// </summary>
internal sealed class CleanCommand : CliCommandBase
{
    /// <summary>
    /// 创建 clean 命令实例。
    /// </summary>
    /// <returns>配置好的命令。</returns>
    public static Command Create()
    {
        var projectOption = new Option<DirectoryInfo?>("--project");
        projectOption.Description = "项目根目录路径，默认使用当前目录";

        var nugetOption = new Option<bool>("--nuget");
        nugetOption.Description = "同时清理 NuGet HTTP 缓存";
        nugetOption.DefaultValueFactory = _ => false;

        var command = new Command("clean", "清理项目的 bin/obj 目录");
        command.Options.Add(projectOption);
        command.Options.Add(nugetOption);

        command.Action = AsyncAction.Create(async (parseResult, _) =>
        {
            var projectDir = parseResult.GetValue(projectOption);
            var cleanNuget = parseResult.GetValue(nugetOption);

            var cmd = new CleanCommand();
            return await cmd.ExecuteAsync(projectDir, cleanNuget);
        });

        return command;
    }

    /// <summary>
    /// 执行 clean 命令，清理 bin/obj 目录。
    /// </summary>
    /// <param name="projectDir">项目根目录。</param>
    /// <param name="cleanNuget">是否同时清理 NuGet 缓存。</param>
    /// <returns>退出码：0 表示成功，非零表示失败。</returns>
    private async Task<int> ExecuteAsync(DirectoryInfo? projectDir, bool cleanNuget)
    {
        var rootDir = ResolveRootDirectory(projectDir);
        if (rootDir is null)
        {
            Error("目录不存在，请通过 --project 指定有效路径");
            return 1;
        }

        Info($"清理 {rootDir.FullName} 下的 bin/obj 目录...");

        var deletedCount = CleanBinObjDirectories(rootDir);

        if (deletedCount > 0)
        {
            Success($"已清理 {deletedCount} 个 bin/obj 目录");
        }
        else
        {
            Info("未找到需要清理的 bin/obj 目录");
        }

        if (cleanNuget)
        {
            Info("清理 NuGet HTTP 缓存...");
            var nugetResult = await CleanNuGetCacheAsync();
            if (nugetResult)
            {
                Success("NuGet HTTP 缓存已清理");
            }
            else
            {
                Warn("NuGet HTTP 缓存清理失败（可能 dotnet 命令不可用）");
            }
        }

        return 0;
    }

    /// <summary>
    /// 递归删除指定目录下所有 bin/ 和 obj/ 目录。
    /// </summary>
    /// <param name="rootDir">根目录。</param>
    /// <returns>删除的目录数量。</returns>
    internal static int CleanBinObjDirectories(DirectoryInfo rootDir)
    {
        var count = 0;

        foreach (var dir in rootDir.GetDirectories("*", SearchOption.AllDirectories))
        {
            if (IsBinOrObj(dir))
            {
                try
                {
                    dir.Delete(recursive: true);
                    count++;
                }
                catch (IOException)
                {
                    // 文件被占用，跳过
                }
                catch (UnauthorizedAccessException)
                {
                    // 无权限，跳过
                }
            }
        }

        return count;
    }

    /// <summary>
    /// 判断目录是否为 bin 或 obj 目录。
    /// </summary>
    /// <param name="dir">目录信息。</param>
    /// <returns>是 bin/obj 返回 true。</returns>
    private static bool IsBinOrObj(DirectoryInfo dir)
    {
        return string.Equals(dir.Name, "bin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(dir.Name, "obj", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 解析项目根目录。
    /// </summary>
    /// <param name="projectDir">用户指定的目录。</param>
    /// <returns>目录信息，不存在则返回 null。</returns>
    private static DirectoryInfo? ResolveRootDirectory(DirectoryInfo? projectDir)
    {
        if (projectDir is not null)
        {
            return projectDir.Exists ? projectDir : null;
        }

        return new DirectoryInfo(Directory.GetCurrentDirectory());
    }

    /// <summary>
    /// 清理 NuGet HTTP 缓存。
    /// </summary>
    /// <returns>是否清理成功。</returns>
    private static async Task<bool> CleanNuGetCacheAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("nuget");
            psi.ArgumentList.Add("locals");
            psi.ArgumentList.Add("http-cache");
            psi.ArgumentList.Add("--clear");

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return false;
            }

            await proc.WaitForExitAsync();
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
