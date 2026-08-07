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
    /// <param name="streamOutput">
    /// 是否边执行边把标准输出 / 错误逐行转发到控制台。
    /// 长耗时命令（如 <c>pnpm install</c>、<c>vite build</c>）应开启，避免长时间无输出。
    /// 关闭时沿用一次性读取，命令结束后由调用方自行打印 <see cref="HookResult.Output"/>。
    /// </param>
    /// <returns>执行结果；命令为空时返回 <see cref="HookResult.Skipped"/> 为 true 的结果。</returns>
    public static async Task<HookResult> ExecuteAsync(
        string? command,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default,
        bool streamOutput = false)
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

            return streamOutput
                ? await RunStreamingAsync(proc, cancellationToken)
                : await RunBufferedAsync(proc, cancellationToken);
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
    /// 一次性读取模式：命令结束后返回完整输出（历史行为）。
    /// </summary>
    /// <param name="proc">已配置好 StartInfo 的进程对象。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>执行结果。</returns>
    private static async Task<HookResult> RunBufferedAsync(Process proc, CancellationToken cancellationToken)
    {
        proc.Start();

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = proc.StandardError.ReadToEndAsync(cancellationToken);

        await proc.WaitForExitAsync(cancellationToken);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var combined = string.IsNullOrEmpty(stderr) ? stdout : $"{stdout}\n{stderr}";

        return BuildResult(proc.ExitCode, combined);
    }

    /// <summary>
    /// 流式模式：逐行转发到控制台，同时累积完整输出供调用方复用。
    /// </summary>
    /// <param name="proc">已配置好 StartInfo 的进程对象。</param>
    /// <param name="cancellationToken">取消令牌（触发时终止整个子进程树）。</param>
    /// <returns>执行结果。</returns>
    private static async Task<HookResult> RunStreamingAsync(Process proc, CancellationToken cancellationToken)
    {
        var buffer = new System.Text.StringBuilder();
        var sync = new object();

        void Append(string line, bool isError)
        {
            lock (sync)
            {
                buffer.AppendLine(line);
                if (isError)
                {
                    Console.Error.WriteLine(line);
                }
                else
                {
                    Console.WriteLine(line);
                }
            }
        }

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                Append(e.Data, isError: false);
            }
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                Append(e.Data, isError: true);
            }
        };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        using var cancelReg = cancellationToken.Register(() => TryKill(proc));

        try
        {
            await proc.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(proc);
            throw;
        }

        string output;
        lock (sync)
        {
            output = buffer.ToString();
        }

        return BuildResult(proc.ExitCode, output);
    }

    private static HookResult BuildResult(int exitCode, string output) => new()
    {
        Success = exitCode == 0,
        ExitCode = exitCode,
        Output = output,
        ErrorMessage = exitCode == 0 ? null : $"钩子命令退出码 {exitCode}",
    };

    private static void TryKill(Process proc)
    {
        try
        {
            if (!proc.HasExited)
            {
                proc.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            /* 进程可能已退出 */
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
