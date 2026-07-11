using System.CommandLine;
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
        var rootCommand = BuildRootCommand();
        var parseResult = rootCommand.Parse(args);
        return await parseResult.InvokeAsync();
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
        root.Subcommands.Add(NewCommand.Create());
        root.Subcommands.Add(BuildCommand.Create());
        root.Subcommands.Add(DevCommand.Create());
        root.Subcommands.Add(PublishCommand.Create());
        root.Subcommands.Add(PluginCommand.Create());
        return root;
    }
}
