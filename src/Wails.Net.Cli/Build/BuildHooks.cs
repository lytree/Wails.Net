using System.Diagnostics;

namespace Wails.Net.Cli.Build;

/// <summary>
/// 构建钩子执行结果。
/// </summary>
public sealed class HookResult
{
    /// <summary>是否成功执行（退出码为 0 视为成功）。</summary>
    public bool Success { get; init; }

    /// <summary>进程退出码；未执行钩子时为 null。</summary>
    public int? ExitCode { get; init; }

    /// <summary>错误消息（仅在执行失败时填充）。</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>钩子命令的标准输出与错误输出的合并文本。</summary>
    public string? Output { get; init; }

    /// <summary>是否跳过了钩子执行（命令为空或仅空白字符）。</summary>
    public bool Skipped { get; init; }
}

/// <summary>
/// 构建钩子执行器，负责在 shell 中运行 wails.json 配置的钩子命令。
/// 对应 Wails v3 Go 版本 internal/project/build.go 中的钩子执行逻辑。
/// 钩子命令字符串按平台选择解释器：
/// <list type="bullet">
/// <item>Windows：<c>cmd /c "&lt;command&gt;"</c></item>
/// <item>Linux/macOS：<c>sh -c "&lt;command&gt;"</c></item>
/// </list>
/// </summary>
public static class BuildHooks
{
    /// <summary>
    /// 异步执行指定的钩子命令。
    /// </summary>
    /// <param name="command">钩子命令字符串；为 null 或空白时跳过执行。</param>
    /// <param name="workingDirectory">命令执行的工作目录；为 null 时使用当前目录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>执行结果；命令为空时返回 <see cref="HookResult.Skipped"/> 为 true 的结果。</returns>
    public static async Task<HookResult> ExecuteAsync(
        string? command,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return new HookResult { Success = true, Skipped = true };
        }

        var (fileName, args) = BuildShellCommand(command);

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        try
        {
            using var proc = new Process { StartInfo = psi };
            proc.Start();

            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();

            await proc.WaitForExitAsync(cancellationToken);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var combined = string.IsNullOrEmpty(stderr) ? stdout : $"{stdout}\n{stderr}";

            return new HookResult
            {
                Success = proc.ExitCode == 0,
                ExitCode = proc.ExitCode,
                Output = combined,
                ErrorMessage = proc.ExitCode == 0 ? null : $"钩子命令退出码 {proc.ExitCode}",
            };
        }
        catch (OperationCanceledException)
        {
            return new HookResult { Success = false, ErrorMessage = "钩子执行被取消" };
        }
        catch (Exception ex)
        {
            return new HookResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// 根据当前平台构造 shell 调用命令。
    /// </summary>
    /// <param name="command">用户配置的钩子命令字符串。</param>
    /// <returns>(解释器路径, 参数) 元组。</returns>
    private static (string FileName, string Arguments) BuildShellCommand(string command)
    {
        if (OperatingSystem.IsWindows())
        {
            // cmd /c "command"
            return ("cmd.exe", $"/c \"{command}\"");
        }

        // sh -c "command"
        return ("sh", $"-c \"{command}\"");
    }
}
