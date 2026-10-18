using System.Diagnostics;

namespace Wails.Net.Cli.Build;

/// <summary>
/// CLI 内部进程执行辅助：运行外部工具并捕获标准输出与标准错误。
/// 供 MSIX 打包（MakeAppx/signtool）与 macOS 输出探测（xattr）等场景复用。
/// </summary>
internal static class CliProcess
{
    /// <summary>
    /// 运行外部进程并捕获输出。
    /// </summary>
    /// <param name="fileName">可执行文件路径。</param>
    /// <param name="arguments">参数列表。</param>
    /// <returns>(退出码, 标准输出 + 标准错误)；进程启动失败时返回 (-1, 异常消息)。</returns>
    public static async Task<(int ExitCode, string Output)> RunAsync(string fileName, params string[] arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        try
        {
            using var process = new Process { StartInfo = psi };
            process.Start();

            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var stdout = await standardOutput;
            var stderr = await standardError;
            var combined = string.IsNullOrEmpty(stderr) ? stdout : $"{stdout}{Environment.NewLine}{stderr}";
            return (process.ExitCode, combined);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
