using System.Diagnostics;
using System.Text;

namespace Wails.Net.Cli.Mcp;

/// <summary>
/// MCP 子进程调用结果。
/// </summary>
/// <param name="ExitCode">退出码。</param>
/// <param name="Output">标准输出与标准错误合并后的有界内容。</param>
/// <param name="Truncated">输出是否被截断。</param>
internal sealed record McpSyncResult(int ExitCode, string Output, bool Truncated);

/// <summary>
/// 以当前 CLI 可执行文件启动子进程的辅助类。
/// 对应 Wails v3 Go 版本 <c>internal/commands/mcp.go</c> 的 <c>runSync</c> / <c>startJob</c>
/// （Go 通过 <c>os.Executable()</c> 递归调用自身）。
/// </summary>
internal static class McpSelfInvoker
{
    /// <summary>同步调用的输出字符上限（约 2 MiB）。</summary>
    public const int MaxOutputCharacters = 2 * 1024 * 1024;

    /// <summary>
    /// 构造以当前 CLI 自身为目标的进程启动信息。
    /// </summary>
    /// <param name="workingDirectory">子进程工作目录。</param>
    /// <param name="args">传给 CLI 的参数。</param>
    /// <returns>进程启动信息。</returns>
    /// <exception cref="InvalidOperationException">无法确定当前可执行文件。</exception>
    public static ProcessStartInfo CreateStartInfo(string workingDirectory, IReadOnlyList<string> args)
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable))
        {
            throw new InvalidOperationException("无法确定当前 CLI 可执行文件路径");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };

        // 与 Go 版本一致：子进程禁用彩色输出，并标记为 MCP 子进程
        startInfo.Environment["NO_COLOR"] = "1";
        startInfo.Environment["WAILS_MCP_CHILD"] = "1";

        // 通过 `dotnet run` / `dotnet exec` 启动时，ProcessPath 为 dotnet 主机，
        // 需要显式传入 CLI 程序集；独立 apphost / dotnet tool 无需该参数。
        if (IsDotNetHost(executable))
        {
            var assembly = Path.Combine(AppContext.BaseDirectory, "Wails.Net.Cli.dll");
            if (File.Exists(assembly))
            {
                startInfo.ArgumentList.Add(assembly);
            }
        }

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        return startInfo;
    }

    /// <summary>
    /// 同步运行子进程并返回有界输出。
    /// </summary>
    /// <param name="workingDirectory">工作目录。</param>
    /// <param name="args">CLI 参数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>调用结果。</returns>
    public static async Task<McpSyncResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = CreateStartInfo(workingDirectory, args),
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("无法启动 CLI 子进程");
        }

        var stdout = ReadCappedAsync(process.StandardOutput, cancellationToken);
        var stderr = ReadCappedAsync(process.StandardError, cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var (stdoutText, stdoutTruncated) = await stdout.ConfigureAwait(false);
        var (stderrText, stderrTruncated) = await stderr.ConfigureAwait(false);
        var combined = string.IsNullOrEmpty(stderrText) ? stdoutText : $"{stdoutText}\n{stderrText}";
        return new McpSyncResult(process.ExitCode, combined, stdoutTruncated || stderrTruncated);
    }

    /// <summary>
    /// 启动后台任务子进程，输出写入任务缓冲，结束后更新任务状态。
    /// </summary>
    /// <param name="job">任务实例。</param>
    /// <param name="args">CLI 参数。</param>
    public static void StartJob(McpJob job, IReadOnlyList<string> args)
    {
        var process = new Process
        {
            StartInfo = CreateStartInfo(job.WorkingDirectory, args),
        };

        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("无法启动 CLI 子进程");
        }

        job.Attach(process);
        _ = Task.Run(async () =>
        {
            var stdout = PumpAsync(process.StandardOutput, job, job.Token);
            var stderr = PumpAsync(process.StandardError, job, job.Token);
            try
            {
                await process.WaitForExitAsync(job.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 任务被停止：进程树已由 RequestStop 终止
            }
            catch (InvalidOperationException)
            {
                // 进程已释放
            }

            var exitCode = -1;
            try
            {
                exitCode = process.ExitCode;
            }
            catch (InvalidOperationException)
            {
                // 进程未启动或无退出码
            }

            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            job.Finish(job.Token.IsCancellationRequested, exitCode);
            process.Dispose();
        });
    }

    private static bool IsDotNetHost(string executable)
    {
        var name = Path.GetFileNameWithoutExtension(executable);
        return string.Equals(name, "dotnet", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<(string Text, bool Truncated)> ReadCappedAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        var buffer = new CappedTextBuffer(MaxOutputCharacters);
        var chars = new char[8192];
        int read;
        while ((read = await reader.ReadAsync(chars.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            buffer.Append(new string(chars, 0, read));
        }

        return (buffer.Text, buffer.Truncated);
    }

    private static async Task PumpAsync(StreamReader reader, McpJob job, CancellationToken cancellationToken)
    {
        var chars = new char[8192];
        try
        {
            int read;
            while ((read = await reader.ReadAsync(chars.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
            {
                job.Append(new string(chars, 0, read));
            }
        }
        catch (OperationCanceledException)
        {
            // 停止任务时管道关闭
        }
        catch (ObjectDisposedException)
        {
            // 进程已释放
        }
        catch (IOException)
        {
            // 管道中断
        }
    }
}
