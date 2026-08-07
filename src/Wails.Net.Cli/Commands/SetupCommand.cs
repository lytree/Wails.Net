using System.CommandLine;
using System.Runtime.InteropServices;

namespace Wails.Net.Cli.Commands;

/// <summary>
/// setup 命令：交互式环境设置向导。
/// 对应 Wails v3 Go 版本的 <c>wails3 setup</c>（PR #5601）。
/// <para>
/// 复用 <see cref="DoctorCommand.RunDiagnostics"/> 的环境诊断，
/// 逐项展示缺失依赖并给出分平台安装指引，引导用户完成环境准备。
/// 非交互环境（stdin 不可读）时退化为只读诊断摘要。
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
        var command = new Command("setup", "交互式环境设置向导（检测并引导安装 .NET SDK、WebView2、GTK4 等依赖）");
        command.Action = AsyncAction.Create(async () =>
        {
            var cmd = new SetupCommand();
            return await cmd.ExecuteAsync();
        });
        return command;
    }

    /// <summary>
    /// 执行 setup 命令。
    /// </summary>
    /// <returns>退出码：0 表示环境就绪，1 表示存在待修复的缺失项。</returns>
    private async Task<int> ExecuteAsync()
    {
        Info("Wails.Net 环境设置向导");
        Info("=======================");
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

        if (failed == 0)
        {
            Info(string.Empty);
            Success("环境已就绪，可以开始使用 wails new 创建项目。");
            return 0;
        }

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

        // 非交互环境不阻塞等待
        return 1;
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
