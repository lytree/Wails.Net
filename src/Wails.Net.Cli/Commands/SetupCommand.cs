using System.CommandLine;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Wails.Net.Cli.Setup;

namespace Wails.Net.Cli.Commands;

/// <summary>
/// setup 命令：交互式环境设置向导。
/// 对应 Wails v3 Go 版本的 <c>wails3 setup</c>（PR #5601）及其增强项
/// P2-22（beta.28 #6224「系统详情」+ beta.24「仅批准的包管理器命令」）。
/// <para>
/// 默认行为（无参）保持不变：打印系统详情、回放 <see cref="DoctorCommand.RunDiagnostics"/>
/// 的环境诊断，并给出分平台安装指引。新增 <c>--system</c> / <c>--json</c> 用于单独输出
/// 机器可读的系统详情；新增 <c>--install</c> / <c>--yes</c> 用于按白名单构造的包管理器
/// 命令安装缺失依赖（默认仅预览，执行前明确告知将要执行的命令）。
/// </para>
/// </summary>
internal sealed class SetupCommand : CliCommandBase
{
    /// <summary>
    /// 创建 setup 命令实例。
    /// </summary>
    /// <returns>配置好的命令。</returns>
    public static Command Create()
    {
        var jsonOption = new Option<bool>("--json")
        {
            Description = "以 JSON 输出机器可读的系统详情后退出（不执行诊断）",
        };

        var systemOption = new Option<bool>("--system")
        {
            Description = "以人类可读格式输出系统详情后退出（不执行诊断）",
        };

        var installOption = new Option<string?>("--install")
        {
            Description = "请求安装某个缺失依赖（命令由白名单构造，默认仅预览不执行）",
        };

        var yesOption = new Option<bool>("--yes")
        {
            Description = "与 --install 同用时实际执行已批准的包管理器命令",
        };

        var command = new Command("setup", "交互式环境设置向导（检测并引导安装 .NET SDK、WebView2、GTK4 等依赖）");
        command.Options.Add(jsonOption);
        command.Options.Add(systemOption);
        command.Options.Add(installOption);
        command.Options.Add(yesOption);

        command.Action = AsyncAction.Create(async (parseResult, _) =>
        {
            var cmd = new SetupCommand();
            return await cmd.ExecuteAsync(
                parseResult.GetValue(jsonOption),
                parseResult.GetValue(systemOption),
                parseResult.GetValue(installOption),
                parseResult.GetValue(yesOption));
        });

        return command;
    }

    /// <summary>
    /// 执行 setup 命令。
    /// </summary>
    /// <param name="asJson">是否以 JSON 输出系统详情后退出。</param>
    /// <param name="systemOnly">是否仅输出系统详情后退出。</param>
    /// <param name="install">请求安装的依赖名；null 表示不安装。</param>
    /// <param name="confirmed">是否以 <c>--yes</c> 确认实际执行安装命令。</param>
    /// <returns>退出码：0 表示环境就绪或操作成功，1 表示存在待修复的缺失项或安装失败。</returns>
    private async Task<int> ExecuteAsync(bool asJson, bool systemOnly, string? install, bool confirmed)
    {
        var system = SetupSystemInfoCollector.Collect();

        if (asJson)
        {
            Info(JsonSerializer.Serialize(system, SetupSystemInfoJsonContext.Default.SetupSystemInfo));
            return 0;
        }

        Info("Wails.Net 环境设置向导");
        Info("=======================");
        Info(string.Empty);

        // 系统详情（对应 Go initSystemInfo：向导启动时先采集并展示系统信息）
        PrintSystemDetails(system);

        if (systemOnly)
        {
            return 0;
        }

        Info(string.Empty);
        Info("正在检测开发环境依赖，请稍候...");
        Info(string.Empty);

        var checks = DoctorCommand.RunDiagnostics();
        await Task.CompletedTask;

        var failed = 0;
        var warned = 0;
        foreach (var check in checks)
        {
            var mark = check.Status switch
            {
                DoctorCommand.DiagnosticStatus.Pass => "[OK]",
                DoctorCommand.DiagnosticStatus.Warn => "[WARN]",
                _ => "[FAIL]",
            };

            Console.Write($"{mark,-8}");
            Console.WriteLine($"{check.Name,-30} {check.Message}");

            switch (check.Status)
            {
                case DoctorCommand.DiagnosticStatus.Pass:
                    break;
                case DoctorCommand.DiagnosticStatus.Warn:
                    warned++;
                    break;
                default:
                    failed++;
                    break;
            }
        }

        Info(string.Empty);
        Info($"检测完成：{checks.Count - failed - warned} 通过 / {warned} 警告 / {failed} 缺失");

        if (failed > 0)
        {
            // 展示缺失项的安装指引
            Info(string.Empty);
            Info("以下依赖缺失，请按平台指引安装：");
            Info(string.Empty);

            foreach (var check in checks.Where(c => c.Status == DoctorCommand.DiagnosticStatus.Fail))
            {
                Info($"  - {check.Name}");
                Info($"    {check.Message}");
                var hint = GetInstallHint(check.Name);
                if (!string.IsNullOrEmpty(hint))
                {
                    Info($"    安装：{hint}");
                }
            }

            Info(string.Empty);
            Info("安装完成后重新运行 wails setup 或 wails doctor 验证。");
        }

        if (install is not null)
        {
            return await HandleInstallAsync(install, confirmed);
        }

        if (failed == 0)
        {
            Info(string.Empty);
            Success("环境已就绪，可以开始使用 wails new 创建项目。");
            return 0;
        }

        return 1;
    }

    /// <summary>
    /// 输出人类可读的系统详情（与 <see cref="InfoCommand.PrintEnvironmentInfo"/> 风格一致）。
    /// </summary>
    /// <param name="system">系统详情快照。</param>
    private static void PrintSystemDetails(SetupSystemInfo system)
    {
        Info("系统详情");
        Info("--------");
        Info($"操作系统:      {system.OsName} {system.OsVersion}");
        Info($"平台/架构:     {system.Os} / {system.Arch}");
        Info($"Wails.Net:     {system.WailsVersion}");
        Info($".NET 运行时:   v{system.RuntimeVersion}");
        Info($".NET SDK:      {GetExtra(system, "dotnetSdk")}");
        Info($"主目录:        {system.HomeDir}");

        var git = FormatGitIdentity(system);
        if (git is not null)
        {
            Info($"Git 身份:      {git}");
        }

        if (system.Os == "windows")
        {
            Info($"WebView2:      {GetExtra(system, "webView2")}");
        }
        else if (system.Os == "linux")
        {
            Info($"GTK4:          {GetExtra(system, "gtk4")}");
            Info($"WebKitGTK:     {GetExtra(system, "webkitgtk-6.0")}");
        }

        Info($"CPU/内存:      {system.Details.Cpu} / {system.Details.Memory}");
    }

    /// <summary>
    /// 从系统详情的 extras 中读取指定键；缺失时返回 unknown。
    /// </summary>
    /// <param name="system">系统详情快照。</param>
    /// <param name="key">extras 键名。</param>
    /// <returns>键对应的值，缺失时为 unknown。</returns>
    private static string GetExtra(SetupSystemInfo system, string key)
        => system.Details.Extras.TryGetValue(key, out var value) ? value : "unknown";

    /// <summary>
    /// 格式化 Git 身份；两者均为 unknown 时返回 null（跳过输出）。
    /// </summary>
    /// <param name="system">系统详情快照。</param>
    /// <returns>形如 <c>Name &lt;email&gt;</c> 的字符串；无可用信息时返回 null。</returns>
    private static string? FormatGitIdentity(SetupSystemInfo system)
    {
        var hasName = system.GitName != "unknown";
        var hasEmail = system.GitEmail != "unknown";
        if (!hasName && !hasEmail)
        {
            return null;
        }

        if (hasName && hasEmail)
        {
            return $"{system.GitName} <{system.GitEmail}>";
        }

        return hasName ? system.GitName : system.GitEmail;
    }

    /// <summary>
    /// 处理 <c>--install</c> 请求：从白名单目录解析已批准命令，执行前明确告知用户，
    /// 仅在指定 <c>--yes</c> 时实际执行。
    /// </summary>
    /// <param name="dependencyName">依赖名。</param>
    /// <param name="confirmed">是否已确认执行。</param>
    /// <returns>退出码：0 表示预览成功或执行成功，1 表示不支持或执行失败。</returns>
    private static async Task<int> HandleInstallAsync(string dependencyName, bool confirmed)
    {
        Info(string.Empty);

        if (!SetupInstallerCatalog.TryResolve(dependencyName, out var parts))
        {
            Error($"当前平台不支持通过向导安装 '{dependencyName}'；" +
                  $"可用项：{FormatSupportedDependencies()}");
            return 1;
        }

        // 白名单校验：命令由常量目录构造，绝不接受用户字符串拼接执行。
        if (!SafeInstallerCommand.TryParse(parts, out var parsed))
        {
            Error($"内部错误：'{dependencyName}' 的安装命令未通过白名单校验，已拒绝执行");
            return 1;
        }

        Info($"将要执行（已批准的包管理器命令）：{parsed!.ToDisplayString()}");

        if (!confirmed)
        {
            Info("未指定 --yes，仅预览，未实际执行。");
            return 0;
        }

        return await RunApprovedCommandAsync(parsed!);
    }

    /// <summary>
    /// 列出当前平台可安装的依赖名（逗号分隔），空集返回 none。
    /// </summary>
    private static string FormatSupportedDependencies()
    {
        var names = SetupInstallerCatalog.SupportedDependencies.ToList();
        return names.Count == 0 ? "none" : string.Join(", ", names);
    }

    /// <summary>
    /// 执行已通过白名单校验的安装命令。
    /// 使用 <see cref="ProcessStartInfo.ArgumentList"/> 直接传递 argv，不经过任何 shell，
    /// 杜绝命令注入。
    /// </summary>
    /// <param name="command">已校验命令。</param>
    /// <returns>退出码：0 表示安装成功，1 表示失败。</returns>
    private static async Task<int> RunApprovedCommandAsync(SafeInstallCommand command)
    {
        Info("正在执行安装命令...");

        var psi = new ProcessStartInfo
        {
            FileName = command.Executable,
            UseShellExecute = false,
            CreateNoWindow = false,
        };
        foreach (var argument in command.ToArgv())
        {
            psi.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                Error("无法启动安装进程");
                return 1;
            }

            await process.WaitForExitAsync();
            Info($"安装命令退出码：{process.ExitCode}");
            return process.ExitCode == 0 ? 0 : 1;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Error($"安装命令执行失败：{ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// 获取缺失依赖的安装命令建议（分平台）。
    /// </summary>
    /// <param name="checkName">诊断项名称（与 DoctorCommand 检查项一致）。</param>
    /// <returns>安装命令建议；无建议返回空字符串。</returns>
    private static string GetInstallHint(string checkName)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return checkName switch
            {
                "dotnet SDK" => "https://dotnet.microsoft.com/download/dotnet/10.0",
                "Node.js" => "https://nodejs.org/ 或 winget install OpenJS.NodeJS.LTS",
                "pnpm" => "npm i -g pnpm",
                "WebView2 Runtime" => "https://developer.microsoft.com/microsoft-edge/webview2/",
                "NSIS" => "https://nsis.sourceforge.io/ 或 winget install NSIS.NSIS",
                _ => string.Empty,
            };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return checkName switch
            {
                "dotnet SDK" => "https://learn.microsoft.com/dotnet/core/install/linux",
                "Node.js" => "sudo apt install nodejs npm 或使用 nvm",
                "pnpm" => "sudo npm i -g pnpm 或 corepack enable",
                "GTK4" => "sudo apt install libgtk-4-dev",
                "WebKitGTK-6.0" => "sudo apt install libwebkitgtk-6.0-dev",
                "Linux 共享库" => "sudo apt install libgtk-4-1 libwebkitgtk-6.0-4 libglib2.0-0 libgio-2.0-0 libgdk-pixbuf-2.0-0",
                "D-Bus" => "sudo apt install dbus dbus-x11",
                _ => string.Empty,
            };
        }

        return string.Empty;
    }
}
