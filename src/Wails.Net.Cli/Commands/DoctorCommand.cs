using System.CommandLine;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Wails.Net.Cli.Commands;

/// <summary>
/// doctor 命令：诊断开发环境。
/// 对应 Wails v3 Go 版本 internal/doctor/doctor.go。
/// </summary>
internal sealed class DoctorCommand : CliCommandBase
{
    /// <summary>
    /// 创建 doctor 命令实例。
    /// </summary>
    /// <returns>配置好的命令。</returns>
    public static Command Create()
    {
        var command = new Command("doctor", "诊断开发环境（.NET SDK、WebView2、GTK4 等）");
        command.Action = AsyncAction.Create(async () =>
        {
            var cmd = new DoctorCommand();
            return await cmd.ExecuteAsync();
        });
        return command;
    }

    /// <summary>
    /// 执行 doctor 命令，逐项检查环境。
    /// </summary>
    /// <returns>退出码：0 表示全部通过，非零表示有缺失项。</returns>
    private async Task<int> ExecuteAsync()
    {
        Info("Wails.Net 环境诊断");
        Info("==================");
        Info(string.Empty);

        var checks = new List<DiagnosticResult>
        {
            CheckDotNetSdk(),
            CheckNodeJs(),
            CheckOsPlatform(),
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            checks.Add(CheckWebView2Runtime());
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            checks.Add(CheckGtk4());
            checks.Add(CheckWebKitGtk());
        }

        await Task.CompletedTask;

        var passed = 0;
        var failed = 0;
        foreach (var check in checks)
        {
            ReportCheck(check);
            if (check.Status == DiagnosticStatus.Pass)
            {
                passed++;
            }
            else
            {
                failed++;
            }
        }

        Info(string.Empty);
        Info($"诊断完成：{passed} 通过 / {failed} 失败 / {checks.Count} 总计");

        return failed == 0 ? 0 : 1;
    }

    private static void ReportCheck(DiagnosticResult result)
    {
        var mark = result.Status switch
        {
            DiagnosticStatus.Pass => "[OK]",
            DiagnosticStatus.Warn => "[WARN]",
            DiagnosticStatus.Fail => "[FAIL]",
            _ => "[?]",
        };

        Console.Write($"{mark,-8}");
        Console.WriteLine($"{result.Name,-30} {result.Message}");
    }

    private static DiagnosticResult CheckDotNetSdk()
    {
        var sdkPath = LocateExecutable("dotnet");
        if (sdkPath is null)
        {
            return new DiagnosticResult(
                "dotnet SDK",
                DiagnosticStatus.Fail,
                "未找到 dotnet 可执行文件，请安装 .NET 10 SDK");
        }

        var installedVersion = ReadDotNetVersion();
        return new DiagnosticResult(
            "dotnet SDK",
            DiagnosticStatus.Pass,
            $"已安装：v{installedVersion}");
    }

    private static DiagnosticResult CheckNodeJs()
    {
        var nodePath = LocateExecutable("node");
        if (nodePath is null)
        {
            return new DiagnosticResult(
                "Node.js",
                DiagnosticStatus.Warn,
                "未找到 node，前端构建功能将不可用");
        }

        var version = ReadExecutableVersion("node", "--version");
        return new DiagnosticResult(
            "Node.js",
            DiagnosticStatus.Pass,
            $"已安装：{version}");
    }

    private static DiagnosticResult CheckOsPlatform()
    {
        var os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "Windows"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                ? "Linux"
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                    ? "macOS"
                    : "未知";

        var arch = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
        var ver = Environment.OSVersion.Version.ToString();
        return new DiagnosticResult(
            "操作系统",
            DiagnosticStatus.Pass,
            $"{os} {arch} ({ver})");
    }

    private static DiagnosticResult CheckWebView2Runtime()
    {
        var edgePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft", "Edge", "Application", "msedge.exe");
        if (File.Exists(edgePath))
        {
            return new DiagnosticResult(
                "WebView2 Runtime",
                DiagnosticStatus.Pass,
                "Microsoft Edge 已安装，WebView2 运行时可用");
        }

        return new DiagnosticResult(
            "WebView2 Runtime",
            DiagnosticStatus.Warn,
            "未检测到 Microsoft Edge/WebView2 Runtime，请从 https://developer.microsoft.com/microsoft-edge/webview2/ 安装");
    }

    private static DiagnosticResult CheckGtk4()
    {
        var version = ReadExecutableVersion("pkg-config", "--modversion", "gtk4");
        if (string.IsNullOrEmpty(version))
        {
            return new DiagnosticResult(
                "GTK4",
                DiagnosticStatus.Fail,
                "未检测到 GTK4 开发库，请通过系统包管理器安装（例如 apt install libgtk-4-dev）");
        }

        return new DiagnosticResult(
            "GTK4",
            DiagnosticStatus.Pass,
            $"已安装：v{version}");
    }

    private static DiagnosticResult CheckWebKitGtk()
    {
        var version = ReadExecutableVersion("pkg-config", "--modversion", "webkitgtk-6.0");
        if (string.IsNullOrEmpty(version))
        {
            return new DiagnosticResult(
                "WebKitGTK-6.0",
                DiagnosticStatus.Fail,
                "未检测到 WebKitGTK-6.0 开发库");
        }

        return new DiagnosticResult(
            "WebKitGTK-6.0",
            DiagnosticStatus.Pass,
            $"已安装：v{version}");
    }

    private static string? LocateExecutable(string name)
    {
        var fileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? $"{name}.exe"
            : name;

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            return null;
        }

        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            var candidate = Path.Combine(dir, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string ReadDotNetVersion()
    {
        var raw = ReadExecutableVersion("dotnet", "--version");
        return string.IsNullOrEmpty(raw) ? Environment.Version.ToString() : raw.Trim();
    }

    private static string ReadExecutableVersion(string name, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = name,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in args)
            {
                psi.ArgumentList.Add(a);
            }

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return string.Empty;
            }

            // 异步读取 stderr 防止缓冲区满导致的死锁
            var stderrTask = proc.StandardError.ReadToEndAsync();
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(2000);
            if (!proc.HasExited)
            {
                try { proc.Kill(); } catch { /* 忽略 */ }
            }
            _ = stderrTask.Result;
            return output.Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    private enum DiagnosticStatus
    {
        Pass,
        Warn,
        Fail,
    }

    private sealed record DiagnosticResult(
        string Name,
        DiagnosticStatus Status,
        string Message);
}
