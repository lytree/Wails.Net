using Wails.Net.Application.Plugins;
using WailsApplication = Wails.Net.Application.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wails.Net.Application.Commands;

namespace Wails.Net.Plugins.Cli;

/// <summary>
/// 运行时 CLI 插件，在应用启动时解析命令行参数并暴露给前端。
/// 对应 Tauri v2 的 <c>@tauri-apps/plugin-cli</c>。
/// <para>
/// 与 Wails.Net.Cli（构建时工具）不同，本插件在应用运行时解析 <c>Environment.GetCommandLineArgs()</c>，
/// 通过 <c>cli.getMatches</c> 命令将解析结果暴露给前端，前端可读取 <c>--port 8080</c> 等参数。
/// </para>
/// <para>
/// 命令行定义通过 <see cref="CliDefinition"/> 配置，可在 <c>appsettings.json</c> 的 <c>Wails.Net:Cli</c> 节点
/// 声明参数与子命令。未配置时仅暴露原始参数数组。
/// </para>
/// </summary>
public class CliPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "cli";

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。
    /// 注册 <see cref="ICliParser"/> 的默认实现 <see cref="DefaultCliParser"/>。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ICliParser, DefaultCliParser>();
    }

    /// <summary>
    /// 配置插件，注册运行时 CLI 相关命令。
    /// 命令名采用 <c>cli.&lt;action&gt;</c> 格式，对齐 Tauri v2 命名约定。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Permissions.RegisterPermissionSet("cli:default", "运行时 CLI 默认权限集",
            "cli:allow-get-matches");
        context.Permissions.DeclarePermission("cli:allow-get-matches", "允许读取命令行参数解析结果");

        // 从配置加载 CLI 定义（Wails.Net:Cli 节点）
        var definition = context.Configuration.GetSection("Wails.Net:Cli").Get<CliDefinition>() ?? new CliDefinition();

        var commands = context.Commands;

        // 获取命令行解析结果（对应 Tauri cli.getMatches）
        commands.MapCommand("cli.getMatches",
            (Func<ICommandContext, CliMatches>)(ctx => ResolveParser(ctx).Parse(definition)));
    }

    /// <summary>
    /// 从命令上下文的服务容器解析 <see cref="ICliParser"/>。
    /// 若未注册则返回 <see cref="DefaultCliParser"/> 单例，保证命令不会因缺失实现而抛异常。
    /// </summary>
    /// <param name="ctx">命令上下文。</param>
    /// <returns>CLI 解析器实例。</returns>
    private static ICliParser ResolveParser(ICommandContext ctx)
    {
        return ctx.Services.GetService(typeof(ICliParser)) as ICliParser
            ?? DefaultCliParser.Instance;
    }
}

/// <summary>
/// CLI 解析器抽象接口。
/// </summary>
public interface ICliParser
{
    /// <summary>
    /// 根据给定的 CLI 定义解析当前进程的命令行参数。
    /// </summary>
    /// <param name="definition">CLI 定义（参数与子命令声明）。</param>
    /// <returns>解析结果。</returns>
    CliMatches Parse(CliDefinition definition);
}

/// <summary>
/// 默认 CLI 解析器，使用 <see cref="Environment.GetCommandLineArgs"/> 获取参数。
/// 对应 Tauri v2 cli 插件的 Rust 后端解析逻辑。
/// <para>
/// 解析规则：
/// <list type="bullet">
/// <item><c>--name value</c> 或 <c>--name=value</c>：长选项</item>
/// <item><c>-n value</c> 或 <c>-n=value</c>：短选项</item>
/// <item><c>--flag</c>：布尔标志（值为 "true"）</item>
/// <item>非选项参数：收集到 <c>args</c> 数组</item>
/// <item>子命令：第一个非选项参数匹配 <see cref="CliDefinition.Subcommands"/> 时作为子命令名</item>
/// </list>
/// </para>
/// </summary>
public sealed class DefaultCliParser : ICliParser
{
    /// <summary>单例实例，避免重复分配。</summary>
    public static readonly DefaultCliParser Instance = new();

    /// <inheritdoc />
    public CliMatches Parse(CliDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        // Environment.GetCommandLineArgs()[0] 通常是可执行文件路径，跳过
        var rawArgs = Environment.GetCommandLineArgs();
        var args = rawArgs.Length > 1 ? rawArgs[1..] : Array.Empty<string>();

        var matches = new CliMatches
        {
            Args = args.Where(a => !a.StartsWith('-')).ToArray(),
        };

        var options = new Dictionary<string, CliArgValue>(StringComparer.Ordinal);
        var i = 0;

        // 解析子命令：第一个非选项参数若匹配定义中的子命令则消费
        if (args.Length > 0 && !args[0].StartsWith('-') && definition.Subcommands.Count > 0)
        {
            if (definition.Subcommands.TryGetValue(args[0], out var subcommand))
            {
                matches.Subcommand = args[0];
                i = 1;
                // 子命令的参数沿用相同的解析逻辑
                ParseOptions(args, ref i, options, subcommand);
            }
        }

        // 解析顶层参数（未匹配子命令或子命令后剩余参数）
        ParseOptions(args, ref i, options, definition);

        matches.Options = options;
        return matches;
    }

    /// <summary>
    /// 解析选项参数到 <paramref name="options"/> 字典。
    /// </summary>
    /// <param name="args">原始参数数组。</param>
    /// <param name="i">当前索引（引用传递）。</param>
    /// <param name="options">选项字典（写入目标）。</param>
    /// <param name="definition">参数定义（提供长/短名映射与默认值）。</param>
    private static void ParseOptions(string[] args, ref int i, Dictionary<string, CliArgValue> options, CliDefinitionBase definition)
    {
        while (i < args.Length)
        {
            var arg = args[i];

            if (arg.StartsWith("--"))
            {
                // 长选项：--name 或 --name=value
                var eqIndex = arg.IndexOf('=', StringComparison.Ordinal);
                string name;
                string? inlineValue = null;

                if (eqIndex >= 0)
                {
                    name = arg[2..eqIndex];
                    inlineValue = arg[(eqIndex + 1)..];
                }
                else
                {
                    name = arg[2..];
                }

                // 查找参数定义（若声明）
                var argDef = definition.FindArgument(name);
                var takesValue = argDef?.TakesValue ?? inlineValue is null && !IsNextValue(args, i);

                if (takesValue)
                {
                    string value;
                    if (inlineValue is not null)
                    {
                        value = inlineValue;
                    }
                    else if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                    {
                        value = args[++i];
                    }
                    else
                    {
                        value = "true"; // 布尔标志
                    }

                    options[name] = new CliArgValue { Value = value, Occurrences = 1 };
                }
                else
                {
                    // 布尔标志
                    options[name] = new CliArgValue
                    {
                        Value = inlineValue ?? "true",
                        Occurrences = 1,
                    };
                }
            }
            else if (arg.StartsWith('-') && arg.Length > 1)
            {
                // 短选项：-n 或 -n=value 或 -nvalue
                var rest = arg[1..];
                var eqIndex = rest.IndexOf('=', StringComparison.Ordinal);
                string shortName;
                string? inlineValue = null;

                if (eqIndex >= 0)
                {
                    shortName = rest[..eqIndex];
                    inlineValue = rest[(eqIndex + 1)..];
                }
                else
                {
                    shortName = rest[0].ToString();
                    if (rest.Length > 1)
                    {
                        inlineValue = rest[1..];
                    }
                }

                // 查找长名映射
                var longName = definition.ResolveShortName(shortName) ?? shortName;
                options[longName] = new CliArgValue
                {
                    Value = inlineValue ?? "true",
                    Occurrences = 1,
                };
            }
            // 非选项参数已在顶层收集到 Args，此处跳过

            i++;
        }
    }

    /// <summary>
    /// 检查下一个参数是否可作为当前选项的值（非选项格式）。
    /// </summary>
    private static bool IsNextValue(string[] args, int currentIndex)
    {
        return currentIndex + 1 < args.Length && !args[currentIndex + 1].StartsWith('-');
    }
}

/// <summary>
/// CLI 定义，声明应用支持的参数与子命令。
/// 对应 Tauri v2 <c>tauri.conf.json</c> 的 <c>cli</c> 配置节。
/// </summary>
public sealed class CliDefinition : CliDefinitionBase
{
    /// <summary>子命令定义，按子命令名索引。</summary>
    public Dictionary<string, CliDefinitionBase> Subcommands { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// CLI 定义基类，包含参数声明。
/// </summary>
public abstract class CliDefinitionBase
{
    /// <summary>参数定义列表。</summary>
    public List<CliArgDefinition> Args { get; set; } = new();

    /// <summary>
    /// 按长名查找参数定义。
    /// </summary>
    /// <param name="name">参数长名。</param>
    /// <returns>参数定义；未找到返回 null。</returns>
    public CliArgDefinition? FindArgument(string name)
    {
        return Args.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.Ordinal));
    }

    /// <summary>
    /// 按短名将短名解析为长名。
    /// </summary>
    /// <param name="shortName">短名（单个字符）。</param>
    /// <returns>对应的长名；未定义返回 null。</returns>
    public string? ResolveShortName(string shortName)
    {
        return Args.FirstOrDefault(a => string.Equals(a.ShortName, shortName, StringComparison.Ordinal))?.Name;
    }
}

/// <summary>
/// CLI 参数定义。
/// </summary>
public sealed class CliArgDefinition
{
    /// <summary>参数长名（如 "port"）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>参数短名（如 "p"）；可为空。</summary>
    public string? ShortName { get; set; }

    /// <summary>是否接受值参数（false 表示布尔标志）。</summary>
    public bool TakesValue { get; set; } = true;

    /// <summary>默认值（参数未提供时使用）。</summary>
    public string? DefaultValue { get; set; }

    /// <summary>参数描述。</summary>
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// CLI 解析结果。
/// </summary>
public sealed class CliMatches
{
    /// <summary>非选项参数数组（位置参数）。</summary>
    public string[] Args { get; set; } = Array.Empty<string>();

    /// <summary>选项字典，按参数长名索引。</summary>
    public Dictionary<string, CliArgValue> Options { get; set; } = new(StringComparer.Ordinal);

    /// <summary>匹配的子命令名；未匹配子命令时为 null。</summary>
    public string? Subcommand { get; set; }
}

/// <summary>
/// CLI 参数值。
/// </summary>
public sealed class CliArgValue
{
    /// <summary>参数值（布尔标志为 "true"）。</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>参数出现的次数。</summary>
    public int Occurrences { get; set; }
}
