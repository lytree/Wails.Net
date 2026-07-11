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
            CheckGit(),
            CheckOsPlatform(),
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            checks.Add(CheckWebView2Runtime());
            checks.Add(CheckNsis());
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            checks.Add(CheckGtk4());
            checks.Add(CheckWebKitGtk());
            checks.Add(CheckLinuxSharedLibraries());
            checks.Add(CheckDBus());
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

    /// <summary>
    /// WebView2 Runtime 安装版本的最小要求。
    /// </summary>
    private const string WebView2MinVersion = "100.0.0.0";

    /// <summary>
    /// WebView2 Runtime 注册表键路径（HKLM\SOFTWARE\WOW6432Node 下，64 位系统）。
    /// {F3017226-FE2A-4295-8BDF-00C3A9A5E36C} 是 WebView2 Runtime 的产品 GUID。
    /// </summary>
    private const string WebView2RegKey = @"HKLM\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A5E36C}";

    /// <summary>
    /// WebView2 Runtime 注册表键路径（32 位系统或 per-user 安装）。
    /// </summary>
    private const string WebView2RegKeyUser = @"HKCU\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A5E36C}";

    private static DiagnosticResult CheckWebView2Runtime()
    {
        // 优先通过注册表检测 WebView2 Runtime 的安装版本（深度检测）。
        var regVersion = ReadWebView2RegistryVersion();
        if (!string.IsNullOrEmpty(regVersion))
        {
            var pass = IsVersionAtLeast(regVersion, WebView2MinVersion);
            return new DiagnosticResult(
                "WebView2 Runtime",
                pass ? DiagnosticStatus.Pass : DiagnosticStatus.Warn,
                $"已安装：v{regVersion}{(pass ? string.Empty : $"（低于推荐版本 {WebView2MinVersion}）")}");
        }

        // 回退：检测 Microsoft Edge 可执行文件（Edge 内含 WebView2 Runtime）。
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
            DiagnosticStatus.Fail,
            "未检测到 WebView2 Runtime，请从 https://developer.microsoft.com/microsoft-edge/webview2/ 安装");
    }

    /// <summary>
    /// 通过 reg query 读取注册表中 WebView2 Runtime 的安装版本。
    /// 对应 Go 版 doctor-ng 中的注册表检测逻辑。
    /// </summary>
    /// <returns>版本字符串（如 "120.0.2210.91"），未找到则返回 null。</returns>
    private static string? ReadWebView2RegistryVersion()
    {
        var version = ReadRegistryValue(WebView2RegKey, "pv");
        if (string.IsNullOrEmpty(version))
        {
            version = ReadRegistryValue(WebView2RegKeyUser, "pv");
        }

        return string.IsNullOrEmpty(version) ? null : version;
    }

    /// <summary>
    /// 通过 reg query 命令读取注册表字符串值。
    /// </summary>
    /// <param name="keyPath">注册表键路径（如 HKLM\SOFTWARE\...）。</param>
    /// <param name="valueName">值名称。</param>
    /// <returns>值字符串，失败返回 null。</returns>
    private static string? ReadRegistryValue(string keyPath, string valueName)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "reg",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("query");
            psi.ArgumentList.Add(keyPath);
            psi.ArgumentList.Add("/v");
            psi.ArgumentList.Add(valueName);

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return null;
            }

            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(2000);

            // 输出格式示例：
            //   pv    REG_SZ    120.0.2210.91
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.Contains("REG_SZ", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split(new[] { "REG_SZ" }, 2, StringSplitOptions.None);
                    if (parts.Length >= 2)
                    {
                        return parts[1].Trim();
                    }
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 比较版本字符串，判断是否大于等于最小要求版本。
    /// </summary>
    /// <param name="version">实际版本字符串。</param>
    /// <param name="minVersion">最小要求版本字符串。</param>
    /// <returns>实际版本 >= 最小版本时返回 true。</returns>
    internal static bool IsVersionAtLeast(string version, string minVersion)
    {
        if (Version.TryParse(version, out var actual) && Version.TryParse(minVersion, out var min))
        {
            return actual >= min;
        }

        return true; // 无法解析时放行
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

    /// <summary>
    /// 检测 Git 版本（用于项目脚手架和版本控制）。
    /// </summary>
    private static DiagnosticResult CheckGit()
    {
        var gitPath = LocateExecutable("git");
        if (gitPath is null)
        {
            return new DiagnosticResult(
                "Git",
                DiagnosticStatus.Warn,
                "未找到 git，项目脚手架功能将受限");
        }

        var version = ReadExecutableVersion("git", "--version");
        return new DiagnosticResult(
            "Git",
            DiagnosticStatus.Pass,
            $"已安装：{version}");
    }

    /// <summary>
    /// 检测 NSIS（Nullsoft Scriptable Install System，用于 Windows 打包 .exe 安装程序）。
    /// </summary>
    private static DiagnosticResult CheckNsis()
    {
        var nsisPath = LocateExecutable("makensis");
        if (nsisPath is null)
        {
            return new DiagnosticResult(
                "NSIS",
                DiagnosticStatus.Warn,
                "未找到 makensis，Windows .exe 安装程序打包将不可用（可选）");
        }

        var version = ReadExecutableVersion("makensis", "-version");
        return new DiagnosticResult(
            "NSIS",
            DiagnosticStatus.Pass,
            $"已安装：{version}");
    }

    /// <summary>
    /// 检测 Linux 共享库（深度依赖分析）。
    /// 通过 ldconfig -p 检查运行时所需的共享库是否已安装。
    /// 对应 Go 版 doctor-ng 中的 ldconfig 检测逻辑。
    /// </summary>
    private static DiagnosticResult CheckLinuxSharedLibraries()
    {
        var requiredLibs = new (string Name, string Pattern)[]
        {
            ("libgtk-4.so.1", "libgtk-4.so.1"),
            ("libwebkitgtk-6.0.so", "libwebkitgtk-6.0.so"),
            ("libglib-2.0.so.0", "libglib-2.0.so.0"),
            ("libgio-2.0.so.0", "libgio-2.0.so.0"),
            ("libgdk_pixbuf-2.0.so.0", "libgdk_pixbuf-2.0.so.0"),
        };

        var ldconfigOutput = ReadExecutableVersion("ldconfig", "-p");
        if (string.IsNullOrEmpty(ldconfigOutput))
        {
            return new DiagnosticResult(
                "Linux 共享库",
                DiagnosticStatus.Warn,
                "无法执行 ldconfig，跳过深度依赖分析");
        }

        var missing = new List<string>();
        var found = new List<string>();
        foreach (var (name, pattern) in requiredLibs)
        {
            if (ldconfigOutput.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                found.Add(name);
            }
            else
            {
                missing.Add(name);
            }
        }

        if (missing.Count > 0)
        {
            return new DiagnosticResult(
                "Linux 共享库",
                DiagnosticStatus.Fail,
                $"缺失：{string.Join(", ", missing)}");
        }

        return new DiagnosticResult(
            "Linux 共享库",
            DiagnosticStatus.Pass,
            $"已安装：{found.Count} 个核心库（GTK4、WebKitGTK、GLib、GIO、GdkPixbuf）");
    }

    /// <summary>
    /// 检测 D-Bus 会话总线可用性（Linux 桌面进程间通信基础）。
    /// </summary>
    private static DiagnosticResult CheckDBus()
    {
        var dbusPath = LocateExecutable("dbus-run-session");
        if (dbusPath is not null)
        {
            return new DiagnosticResult(
                "D-Bus",
                DiagnosticStatus.Pass,
                "dbus-run-session 已安装");
        }

        // 检查 dbus-send 是否可用（通常随 dbus 包安装）
        var sendPath = LocateExecutable("dbus-send");
        if (sendPath is not null)
        {
            return new DiagnosticResult(
                "D-Bus",
                DiagnosticStatus.Pass,
                "dbus-send 已安装");
        }

        return new DiagnosticResult(
            "D-Bus",
            DiagnosticStatus.Warn,
            "未检测到 D-Bus 工具，系统托盘和通知功能可能不可用");
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
