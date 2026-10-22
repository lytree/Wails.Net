using System.CommandLine;
using System.Net;
using Wails.Net.Cli.Mcp;

namespace Wails.Net.Cli.Commands;

/// <summary>
/// mcp 命令：启动本地项目 MCP 服务器，供智能体辅助项目管理。
/// 对应 Wails v3 Go 版本 <c>internal/commands/mcp.go</c> 的 <c>MCP</c>（beta.9 PR #5896 +
/// beta.19 PR #6092 origin/token 硬化）。
/// </summary>
/// <remarks>
/// 传输方式自动选择：由智能体启动（无终端）时使用 stdio，交互式运行时使用 loopback Streamable HTTP；
/// 亦可用 <c>--stdio</c> / <c>--http</c> 显式指定。stdio 模式下标准输出专用于 JSON-RPC，
/// 所有诊断信息均写往标准错误。
/// </remarks>
internal sealed class McpCommand : CliCommandBase
{
    /// <summary>
    /// 创建 mcp 命令实例。
    /// </summary>
    /// <returns>配置好的命令。</returns>
    public static Command Create()
    {
        var tokenOption = new Option<string?>("--token");
        tokenOption.Description = "会话令牌（默认读取 WAILS_MCP_TOKEN，未设置时随机生成）";

        var rootOption = new Option<string?>("--root");
        rootOption.Description = "允许的项目根目录（默认为当前目录）";

        var httpOption = new Option<bool>("--http");
        httpOption.Description = "使用 loopback Streamable HTTP，而非自动选择传输方式";

        var stdioOption = new Option<bool>("--stdio");
        stdioOption.Description = "使用 stdio，而非自动选择传输方式";

        var portOption = new Option<int>("--port");
        portOption.Description = "HTTP 端口（0 表示自动选择空闲的 loopback 端口）";
        portOption.DefaultValueFactory = _ => 0;

        var command = new Command("mcp", "启动本地 MCP 服务器（stdio 或 loopback Streamable HTTP）");
        command.Options.Add(tokenOption);
        command.Options.Add(rootOption);
        command.Options.Add(httpOption);
        command.Options.Add(stdioOption);
        command.Options.Add(portOption);

        command.Action = AsyncAction.Create(async (parseResult, cancellationToken) =>
        {
            var options = new McpOptions(
                parseResult.GetValue(rootOption),
                parseResult.GetValue(tokenOption),
                parseResult.GetValue(httpOption),
                parseResult.GetValue(stdioOption),
                parseResult.GetValue(portOption));
            return await ExecuteAsync(options, cancellationToken);
        });

        return command;
    }

    private static async Task<int> ExecuteAsync(McpOptions options, CancellationToken cancellationToken)
    {
        if (options.Http && options.Stdio)
        {
            Console.Error.WriteLine("[错误] --http 与 --stdio 只能二选一");
            return 1;
        }

        McpRootPolicy policy;
        try
        {
            policy = McpRootPolicy.Create(options.Root);
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine($"[错误] {ex.Message}");
            return 1;
        }

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var server = new McpServer(options, policy, lifetime.Token);

        var useHttp = options.Http
            || (!options.Stdio && !Console.IsInputRedirected && !Console.IsOutputRedirected);

        try
        {
            return useHttp
                ? await McpHttpTransport.RunAsync(server, options.Port, lifetime.Token)
                : await McpStdioTransport.RunAsync(server, lifetime.Token);
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpListenerException or IOException)
        {
            Console.Error.WriteLine($"[错误] {ex.Message}");
            return 1;
        }
    }
}
