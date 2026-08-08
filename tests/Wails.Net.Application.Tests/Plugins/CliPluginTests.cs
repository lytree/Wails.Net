using Wails.Net.Plugins.Cli;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TUnit.Core;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Plugins;

namespace Wails.Net.Application.Tests.Plugins;

/// <summary>
/// CliPlugin 的单元测试（TUnit）。
/// 对应 Tauri v2 cli 插件功能（运行时命令行参数解析）。
/// 验证命令注册、降级路径（DefaultCliParser 使用 Environment.GetCommandLineArgs）、自定义解析器注入。
/// </summary>
[NotInParallel]
public sealed class CliPluginTests
{
    private static (IPluginContext context, ServiceCollection services) CreatePluginContext()
    {
        var services = new ServiceCollection();
        var commands = new CommandRegistry();
        var config = new ConfigurationBuilder().Build();
        var loggerFactory = LoggerFactory.Create(_ => { });

        var context = Substitute.For<IPluginContext>();
        context.Services.Returns(services);
        context.Commands.Returns(commands);
        context.Configuration.Returns(config);
        context.LoggerFactory.Returns(loggerFactory);
        return (context, services);
    }

    private static ICommandContext CreateCommandContext(IServiceProvider serviceProvider)
    {
        var ctx = Substitute.For<ICommandContext>();
        ctx.Services.Returns(serviceProvider);
        ctx.WindowId.Returns((uint?)null);
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }

    private static object? InvokeCommand(CommandRegistry registry, string name, params object?[] args)
        => CommandTestHelper.Invoke(registry, name, args);

    // ---------------------------------------------------------------------
    // 基础测试
    // ---------------------------------------------------------------------

    [Test]
    public async Task Name_ReturnsCli()
    {
        var plugin = new CliPlugin();
        await Assert.That(plugin.Name).IsEqualTo("cli");
    }

    [Test]
    public async Task Configure_NullContext_ThrowsArgumentNullException()
    {
        var plugin = new CliPlugin();
        await Assert.That(() => plugin.Configure(null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task ConfigureServices_NullServices_ThrowsArgumentNullException()
    {
        var plugin = new CliPlugin();
        await Assert.That(() => plugin.ConfigureServices(null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task ConfigureServices_RegistersDefaultCliParser()
    {
        var plugin = new CliPlugin();
        var services = new ServiceCollection();

        plugin.ConfigureServices(services);

        var provider = services.BuildServiceProvider();
        var parser = provider.GetService<ICliParser>();
        await Assert.That(parser).IsNotNull();
        await Assert.That(parser).IsTypeOf<DefaultCliParser>();
    }

    [Test]
    public async Task Configure_RegistersGetMatchesCommand()
    {
        var plugin = new CliPlugin();
        var (context, services) = CreatePluginContext();
        plugin.ConfigureServices(services);

        plugin.Configure(context);

        await Assert.That(context.Commands.Find("cli.getMatches")).IsNotNull();
    }

    // ---------------------------------------------------------------------
    // 默认解析器测试（DefaultCliParser 使用 Environment.GetCommandLineArgs）
    // ---------------------------------------------------------------------

    [Test]
    public async Task GetMatches_WithDefaultParser_ReturnsCliMatches()
    {
        var plugin = new CliPlugin();
        var (context, services) = CreatePluginContext();
        plugin.ConfigureServices(services);
        plugin.Configure(context);
        var provider = services.BuildServiceProvider();
        var cmdCtx = CreateCommandContext(provider);

        // 默认 DefaultCliParser 使用 Environment.GetCommandLineArgs()，
        // 测试运行时会传入 dotnet test 参数，验证返回 CliMatches 实例即可
        var result = InvokeCommand(context.Commands, "cli.getMatches", cmdCtx);

        await Assert.That(result).IsNotNull();
        await Assert.That(result).IsTypeOf<CliMatches>();
    }

    // ---------------------------------------------------------------------
    // 自定义解析器注入测试
    // ---------------------------------------------------------------------

    [Test]
    public async Task GetMatches_WithCustomParser_ReturnsCustomResult()
    {
        var customMatches = new CliMatches
        {
            Args = new[] { "custom-arg" },
            Subcommand = "deploy",
            Options = new Dictionary<string, CliArgValue>
            {
                ["port"] = new() { Value = "8080", Occurrences = 1 },
            },
        };
        var customParser = new FakeCliParser { Matches = customMatches };
        var plugin = new CliPlugin();
        var (context, services) = CreatePluginContext();
        plugin.ConfigureServices(services);
        services.RemoveAll<ICliParser>();
        services.AddSingleton<ICliParser>(customParser);
        plugin.Configure(context);
        var provider = services.BuildServiceProvider();
        var cmdCtx = CreateCommandContext(provider);

        var result = (CliMatches)InvokeCommand(context.Commands, "cli.getMatches", cmdCtx)!;

        await Assert.That(customParser.ParseCalled).IsTrue();
        await Assert.That(result.Args).Contains("custom-arg");
        await Assert.That(result.Subcommand).IsEqualTo("deploy");
        await Assert.That(result.Options).ContainsKey("port");
        await Assert.That(result.Options["port"].Value).IsEqualTo("8080");
    }

    // ---------------------------------------------------------------------
    // DefaultCliParser 解析逻辑测试
    // ---------------------------------------------------------------------

    [Test]
    public async Task DefaultCliParser_Parse_NullDefinition_ThrowsArgumentNullException()
    {
        var parser = DefaultCliParser.Instance;
        await Assert.That(() => parser.Parse(null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task DefaultCliParser_Parse_EmptyDefinition_ReturnsEmptyMatches()
    {
        var parser = DefaultCliParser.Instance;

        // 注：DefaultCliParser.Parse 使用 Environment.GetCommandLineArgs()，
        // 测试运行时会传入 dotnet 参数。此处验证空定义下不抛异常并返回 CliMatches 实例。
        var result = parser.Parse(new CliDefinition());

        await Assert.That(result).IsNotNull();
        await Assert.That(result.Options).IsNotNull();
    }

    /// <summary>
    /// 用于测试的假 CLI 解析器，记录方法调用。
    /// </summary>
    private sealed class FakeCliParser : ICliParser
    {
        public CliMatches Matches { get; set; } = new();
        public bool ParseCalled { get; private set; }
        public CliDefinition? LastDefinition { get; private set; }

        public CliMatches Parse(CliDefinition definition)
        {
            ParseCalled = true;
            LastDefinition = definition;
            return Matches;
        }
    }
}
