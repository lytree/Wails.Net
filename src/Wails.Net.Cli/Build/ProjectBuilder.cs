using System.Diagnostics;

namespace Wails.Net.Cli.Build;

/// <summary>
/// 构建结果。
/// </summary>
public sealed class BuildResult
{
    /// <summary>是否成功。</summary>
    public bool Success { get; set; }

    /// <summary>错误消息。</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>构建输出程序集路径。</summary>
    public string? OutputPath { get; set; }

    /// <summary>构建日志（仅失败时填充）。</summary>
    public string? BuildLog { get; set; }
}

/// <summary>
/// 项目构建器，封装 dotnet build/publish 调用。
/// 对应 Wails v3 Go 版本 internal/project/build.go。
/// </summary>
public sealed class ProjectBuilder
{
    /// <summary>
    /// 构建指定项目。
    /// </summary>
    /// <param name="project">项目文件。</param>
    /// <param name="configuration">构建配置（Debug/Release）。</param>
    /// <param name="runtime">运行时标识（可空）。</param>
    /// <param name="selfContained">是否自包含。</param>
    /// <returns>构建结果。</returns>
    public async Task<BuildResult> BuildAsync(
        FileInfo project,
        string configuration,
        string? runtime,
        bool selfContained)
    {
        var args = new List<string>
        {
            "build",
            project.FullName,
            "-c",
            configuration,
        };

        if (!string.IsNullOrEmpty(runtime))
        {
            args.Add("-r");
            args.Add(runtime);
        }

        if (selfContained)
        {
            args.Add("--self-contained");
        }

        var (exitCode, output) = await RunDotnetAsync(args);

        if (exitCode != 0)
        {
            return new BuildResult
            {
                Success = false,
                ErrorMessage = $"dotnet build 退出码 {exitCode}",
                BuildLog = output,
            };
        }

        // 从构建输出中解析实际输出目录，避免硬编码 TFM
        var outputDir = ParseOutputDirectory(output)
            ?? Path.Combine(
                Path.GetDirectoryName(project.FullName) ?? string.Empty,
                "bin",
                configuration);

        return new BuildResult
        {
            Success = true,
            OutputPath = outputDir,
        };
    }

    /// <summary>
    /// 从 dotnet build 输出中解析输出目录路径。
    /// </summary>
    /// <param name="buildOutput">dotnet build 的标准输出。</param>
    /// <returns>输出目录路径，若无法解析则返回 null。</returns>
    private static string? ParseOutputDirectory(string buildOutput)
    {
        // dotnet build 输出格式：path/to/bin/Debug/net10.0/ -> /path/to/bin/Debug/net10.0/
        var lines = buildOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            // 匹配 " -> " 后的路径
            var arrowIndex = trimmed.IndexOf("->", StringComparison.Ordinal);
            if (arrowIndex >= 0)
            {
                var path = trimmed[(arrowIndex + 2)..].Trim();
                if (!string.IsNullOrEmpty(path))
                {
                    return path;
                }
            }
        }
        return null;
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
