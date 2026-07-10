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

        // 简化：输出目录基于配置名推断
        var outputDir = Path.Combine(
            Path.GetDirectoryName(project.FullName) ?? string.Empty,
            "bin",
            configuration,
            "net10.0");

        return new BuildResult
        {
            Success = true,
            OutputPath = outputDir,
        };
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
