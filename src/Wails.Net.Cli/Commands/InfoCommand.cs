using System.CommandLine;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace Wails.Net.Cli.Commands;

/// <summary>
/// info 命令：显示项目和环境的详细诊断信息。
/// 对应 Tauri v2 的 <c>tauri info</c> 命令。
/// 输出项目信息（目标框架、包引用、项目引用）和环境信息（操作系统、SDK 版本、WebView2 版本）。
/// </summary>
internal sealed class InfoCommand : CliCommandBase
{
    /// <summary>
    /// 创建 info 命令实例。
    /// </summary>
    /// <returns>配置好的命令。</returns>
    public static Command Create()
    {
        var projectOption = new Option<FileInfo?>("--project");
        projectOption.Description = "项目文件路径（.csproj），默认使用当前目录的项目";

        var command = new Command("info", "显示项目和环境的诊断信息");
        command.Options.Add(projectOption);

        command.Action = AsyncAction.Create(async (parseResult, _) =>
        {
            var project = parseResult.GetValue(projectOption);
            var cmd = new InfoCommand();
            return await cmd.ExecuteAsync(project);
        });

        return command;
    }

    /// <summary>
    /// 执行 info 命令，输出项目与环境信息。
    /// </summary>
    /// <param name="project">项目文件。</param>
    /// <returns>退出码：0 表示成功。</returns>
    private async Task<int> ExecuteAsync(FileInfo? project)
    {
        Info("Wails.Net 项目信息");
        Info("==================");
        Info(string.Empty);

        var projectPath = ResolveProjectPath(project);

        if (projectPath is not null)
        {
            PrintProjectInfo(projectPath);
        }
        else
        {
            Warn("未找到项目文件，跳过项目信息");
        }

        Info(string.Empty);
        Info("环境信息");
        Info("--------");
        PrintEnvironmentInfo();

        await Task.CompletedTask;
        return 0;
    }

    /// <summary>
    /// 输出项目信息（目标框架、包引用、项目引用）。
    /// </summary>
    /// <param name="projectPath">项目文件路径。</param>
    internal static void PrintProjectInfo(FileInfo projectPath)
    {
        try
        {
            var doc = XDocument.Load(projectPath.FullName);
            Info($"项目: {projectPath.Name}");

            var targetFramework = ExtractElement(doc, "TargetFramework");
            var targetFrameworks = ExtractElement(doc, "TargetFrameworks");

            if (!string.IsNullOrEmpty(targetFramework))
            {
                Info($"目标框架: {targetFramework}");
            }
            else if (!string.IsNullOrEmpty(targetFrameworks))
            {
                Info($"目标框架: {targetFrameworks}（多目标）");
            }

            var packageRefs = doc.Descendants("PackageReference")
                .Select(e => new
                {
                    Include = e.Attribute("Include")?.Value ?? string.Empty,
                    Version = e.Attribute("Version")?.Value,
                })
                .Where(p => !string.IsNullOrEmpty(p.Include))
                .OrderBy(p => p.Include)
                .ToList();

            if (packageRefs.Count > 0)
            {
                Info($"NuGet 包引用 ({packageRefs.Count}):");
                foreach (var pkg in packageRefs)
                {
                    var versionStr = pkg.Version is not null ? $" [v{pkg.Version}]" : string.Empty;
                    Info($"  - {pkg.Include}{versionStr}");
                }
            }

            var projectRefs = doc.Descendants("ProjectReference")
                .Select(e => e.Attribute("Include")?.Value ?? string.Empty)
                .Where(p => !string.IsNullOrEmpty(p))
                .OrderBy(p => p)
                .ToList();

            if (projectRefs.Count > 0)
            {
                Info($"项目引用 ({projectRefs.Count}):");
                foreach (var pr in projectRefs)
                {
                    var name = Path.GetFileNameWithoutExtension(pr);
                    Info($"  - {name}");
                }
            }
        }
        catch (Exception ex)
        {
            Error($"读取项目文件失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 输出环境信息（操作系统、SDK 版本、WebView2 版本）。
    /// </summary>
    internal static void PrintEnvironmentInfo()
    {
        var os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "Windows"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                ? "Linux"
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                    ? "macOS"
                    : "未知";

        Info($"操作系统:      {os} {Environment.OSVersion.Version}");
        Info($"OS 架构:       {RuntimeInformation.OSArchitecture}");
        Info($"进程架构:      {RuntimeInformation.ProcessArchitecture}");
        Info($".NET 运行时:   v{Environment.Version}");
        Info($".NET SDK 版本: {ReadDotNetVersion()}");
        Info($"机器名:        {Environment.MachineName}");
        Info($"工作目录:      {Environment.CurrentDirectory}");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var webview2 = ReadWebView2Version();
            if (webview2 is not null)
            {
                Info($"WebView2:      v{webview2}");
            }
            else
            {
                Info("WebView2:      未检测到");
            }
        }
    }

    /// <summary>
    /// 解析项目文件路径。
    /// </summary>
    /// <param name="project">用户指定的项目文件。</param>
    /// <returns>项目文件路径，未找到返回 null。</returns>
    private static FileInfo? ResolveProjectPath(FileInfo? project)
    {
        if (project is not null)
        {
            return project.Exists ? project : null;
        }

        var currentDir = Directory.GetCurrentDirectory();
        var csprojFiles = Directory.GetFiles(currentDir, "*.csproj");
        return csprojFiles.Length == 1 ? new FileInfo(csprojFiles[0]) : null;
    }

    /// <summary>
    /// 从 XDocument 中提取指定元素值。
    /// </summary>
    /// <param name="doc">XML 文档。</param>
    /// <param name="elementName">元素名称。</param>
    /// <returns>元素值，不存在返回 null。</returns>
    private static string? ExtractElement(XDocument doc, string elementName)
    {
        return doc.Descendants(elementName).FirstOrDefault()?.Value;
    }

    /// <summary>
    /// 读取 .NET SDK 版本。
    /// </summary>
    /// <returns>SDK 版本字符串。</returns>
    private static string ReadDotNetVersion()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("--version");

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return "未知";
            }

            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(2000);
            return string.IsNullOrEmpty(output) ? "未知" : output.Trim();
        }
        catch
        {
            return "未知";
        }
    }

    /// <summary>
    /// 读取 WebView2 Runtime 版本（仅 Windows）。
    /// 通过注册表 reg query 读取。
    /// </summary>
    /// <returns>WebView2 版本字符串，未检测到返回 null。</returns>
    private static string? ReadWebView2Version()
    {
        var regKey = @"HKLM\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A5E36C}";
        var regKeyUser = @"HKCU\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A5E36C}";

        var version = ReadRegistryValue(regKey, "pv");
        if (string.IsNullOrEmpty(version))
        {
            version = ReadRegistryValue(regKeyUser, "pv");
        }

        return string.IsNullOrEmpty(version) ? null : version;
    }

    /// <summary>
    /// 通过 reg query 读取注册表字符串值。
    /// </summary>
    /// <param name="keyPath">注册表键路径。</param>
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
}
