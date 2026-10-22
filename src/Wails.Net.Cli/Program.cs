using System.CommandLine;
using System.Text;
using Wails.Net.Cli.Commands;

namespace Wails.Net.Cli;

/// <summary>
/// Wails.Net CLI 入口点。
/// 对应 Wails v3 Go 版本 cmd/wails3/main.go。
/// </summary>
public static class Program
{
    /// <summary>
    /// 程序入口方法。
    /// </summary>
    /// <param name="args">命令行参数。</param>
    /// <returns>进程退出码：0 表示成功，非零表示失败。</returns>
    public static async Task<int> Main(string[] args)
    {
        EnsureUtf8WhenRedirected();

        var rootCommand = BuildRootCommand();
        var parseResult = rootCommand.Parse(args);
        return await parseResult.InvokeAsync();
    }

    /// <summary>
    /// 标准输出被重定向（管道 / 子进程采集）时统一使用 UTF-8，避免 Windows 代码页导致
    /// 中文乱码（MCP 子进程输出经此路径回传给服务器）。
    /// </summary>
    private static void EnsureUtf8WhenRedirected()
    {
        if (!Console.IsOutputRedirected)
        {
            return;
        }

        try
        {
            Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }
        catch (IOException)
        {
            // 无控制台或平台不支持时保持默认编码
        }
    }

    /// <summary>
    /// 构建根命令及其子命令树。
    /// </summary>
    /// <returns>配置好的根命令。</returns>
    private static RootCommand BuildRootCommand()
    {
        var root = new RootCommand("Wails.Net CLI - .NET 10 桌面应用开发工具链");
        root.Subcommands.Add(GenerateCommand.Create());
        root.Subcommands.Add(DoctorCommand.Create());
        root.Subcommands.Add(McpCommand.Create());
        root.Subcommands.Add(SetupCommand.Create());
        root.Subcommands.Add(NewCommand.Create());
        root.Subcommands.Add(CheckCommand.Create());
        root.Subcommands.Add(CapabilityCommand.Create());
        root.Subcommands.Add(BuildCommand.Create());
        root.Subcommands.Add(DevCommand.Create());
        root.Subcommands.Add(PublishCommand.Create());
        root.Subcommands.Add(PackCommand.Create());
        root.Subcommands.Add(PluginCommand.Create());
        root.Subcommands.Add(VersionCommand.Create());
        root.Subcommands.Add(CleanCommand.Create());
        root.Subcommands.Add(InfoCommand.Create());
        root.Subcommands.Add(IconCommand.Create());
        root.Subcommands.Add(SignerCommand.Create());
        root.Subcommands.Add(PlatformCommand.Create());
        root.Subcommands.Add(SelfUpdateCommand.Create());
        root.Subcommands.Add(DeployCommand.Create());
        return root;
    }
}
