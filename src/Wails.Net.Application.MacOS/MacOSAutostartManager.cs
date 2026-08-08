using System.Diagnostics;
using System.Text;
using Wails.Net.Application.Managers;

namespace Wails.Net.Application.Managers;

/// <summary>
/// macOS 自启动管理器实现（LaunchAgent plist 方案）。
/// 对应 Wails v3 Go 版本 autostart_darwin.go 的 LaunchAgent 路径：
/// 在 <c>~/Library/LaunchAgents/{label}.plist</c> 写入 plist 并通过
/// <c>launchctl bootstrap/bootout</c> 立即生效。
/// <para>
/// 该方案不要求应用以 .app bundle 运行（SMAppService 需要 bundle + macOS 13+），
/// 因此开发阶段的未打包二进制同样支持自启动。
/// </para>
/// </summary>
public sealed class MacOSAutostartManager : IAutostartManager
{
    /// <summary>
    /// 应用名称，用作 plist 标签与文件名。
    /// </summary>
    private readonly string _appName;

    /// <summary>
    /// 构造 MacOSAutostartManager 实例。
    /// </summary>
    /// <param name="appName">应用名称。</param>
    public MacOSAutostartManager(string appName)
    {
        _appName = appName;
    }

    /// <summary>
    /// 获取 LaunchAgents 目录路径（~/Library/LaunchAgents）。
    /// </summary>
    /// <returns>目录路径。</returns>
    private static string GetLaunchAgentsDirectory()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "LaunchAgents");

    /// <summary>
    /// 获取 plist 文件完整路径。
    /// </summary>
    /// <returns>plist 路径。</returns>
    private string GetPlistPath() => Path.Combine(GetLaunchAgentsDirectory(), $"{GetLabel()}.plist");

    /// <summary>
    /// 获取 LaunchAgent 标签。
    /// 对应 Wails v3 Go 版本 defaultLabel：优先 bundle id，否则 <c>wails.autostart.&lt;slug&gt;</c>。
    /// </summary>
    /// <returns>标签。</returns>
    private string GetLabel()
    {
        var slug = new string(_appName.Where(char.IsLetterOrDigit).ToArray());
        return string.IsNullOrEmpty(slug) ? "wails.autostart.app" : $"wails.autostart.{slug}";
    }

    /// <inheritdoc />
    public bool IsEnabled()
    {
        // LaunchAgent 机制仅存在于 macOS；其他平台（测试/CI）直接返回 false。
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        try
        {
            var path = GetPlistPath();
            if (!File.Exists(path))
            {
                return false;
            }

            // 校验 plist 的 ProgramArguments 首元素指向当前可执行文件，避免误判其他应用条目。
            var content = File.ReadAllText(path);
            return content.Contains(Environment.ProcessPath ?? string.Empty, StringComparison.Ordinal)
                && content.Contains("RunAtLoad", StringComparison.Ordinal);
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public void Enable()
    {
        // 非 macOS 平台（测试/CI）不执行任何写入。
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        try
        {
            var dir = GetLaunchAgentsDirectory();
            Directory.CreateDirectory(dir);

            var path = GetPlistPath();
            var exe = Environment.ProcessPath ?? string.Empty;
            if (string.IsNullOrEmpty(exe))
            {
                return;
            }

            var plist = BuildPlist(GetLabel(), exe);
            File.WriteAllText(path, plist, new UTF8Encoding(false));

            // 立即加载到当前 GUI 会话（尽力而为，失败不影响下次登录自启）。
            RunLaunchctl("bootstrap", path);
        }
        catch (UnauthorizedAccessException)
        {
            // 文件系统访问失败时静默忽略。
        }
        catch (IOException)
        {
            // 文件系统访问失败时静默忽略。
        }
    }

    /// <inheritdoc />
    public void Disable()
    {
        // 非 macOS 平台（测试/CI）不执行任何操作。
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        try
        {
            var path = GetPlistPath();
            RunLaunchctl("bootout", path);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // 文件系统访问失败时静默忽略。
        }
        catch (IOException)
        {
            // 文件系统访问失败时静默忽略。
        }
    }

    /// <summary>
    /// 执行 launchctl 子命令（bootstrap/bootout），尽力而为。
    /// 对应 Wails v3 Go 版本 launchctlBootstrap/launchctlBootout。
    /// </summary>
    /// <param name="subcommand">子命令。</param>
    /// <param name="plistPath">plist 路径。</param>
    private static void RunLaunchctl(string subcommand, string plistPath)
    {
        try
        {
            var uid = Environment.GetEnvironmentVariable("UID");
            var target = string.IsNullOrEmpty(uid) ? "gui/501" : $"gui/{uid}";
            var psi = new ProcessStartInfo
            {
                FileName = "/bin/launchctl",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            psi.ArgumentList.Add(subcommand);
            psi.ArgumentList.Add(target);
            psi.ArgumentList.Add(plistPath);

            using var proc = Process.Start(psi);
            proc?.WaitForExit(2000);
        }
        catch (Exception)
        {
            // launchctl 调用失败静默忽略（plist 仍会在下次登录时生效）。
        }
    }

    /// <summary>
    /// 构造 LaunchAgent plist 内容（RunAtLoad + KeepAlive=false）。
    /// 对应 Wails v3 Go 版本 launchAgentPlist。
    /// </summary>
    /// <param name="label">标签。</param>
    /// <param name="exe">可执行文件路径。</param>
    /// <returns>plist XML 字符串。</returns>
    private static string BuildPlist(string label, string exe)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">");
        sb.AppendLine("<plist version=\"1.0\">");
        sb.AppendLine("  <dict>");
        sb.AppendLine("    <key>Label</key>");
        sb.AppendLine($"    <string>{EscapeXml(label)}</string>");
        sb.AppendLine("    <key>ProgramArguments</key>");
        sb.AppendLine("    <array>");
        sb.AppendLine($"      <string>{EscapeXml(exe)}</string>");
        sb.AppendLine("    </array>");
        sb.AppendLine("    <key>RunAtLoad</key>");
        sb.AppendLine("    <true/>");
        sb.AppendLine("    <key>KeepAlive</key>");
        sb.AppendLine("    <false/>");
        sb.AppendLine("  </dict>");
        sb.AppendLine("</plist>");
        return sb.ToString();
    }

    /// <summary>
    /// 转义 XML 特殊字符。
    /// </summary>
    /// <param name="value">原始字符串。</param>
    /// <returns>转义后字符串。</returns>
    private static string EscapeXml(string value)
        => value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
}
