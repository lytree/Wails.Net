using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TUnit.Core;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Plugins.BuiltIn;

namespace Wails.Net.Application.Tests;

/// <summary>
/// OpenerPlugin 单元测试（TUnit）。
/// 验证协议白名单、URL 模式白名单、命令注册和目标程序白名单。
/// </summary>
[NotInParallel]
public sealed class OpenerPluginTests
{
    /// <summary>
    /// 创建模拟的 <see cref="IPluginContext"/>。
    /// </summary>
    private static IPluginContext CreatePluginContext()
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
        return context;
    }

    /// <summary>
    /// 通过命令注册表调用命令。
    /// </summary>
    private static object? InvokeCommand(CommandRegistry registry, string name, params object?[] args)
    {
        var entry = registry.Find(name);
        if (entry is null)
        {
            throw new InvalidOperationException($"命令未找到: {name}");
        }
        return entry.Method.Invoke(entry.Instance, args);
    }

    /// <summary>
    /// 通过命令注册表调用返回 bool 的命令。
    /// </summary>
    private static bool InvokeBool(CommandRegistry registry, string name, params object?[] args)
    {
        return InvokeCommand(registry, name, args) is bool b && b;
    }

    /// <summary>
    /// 通过命令注册表调用返回 string? 的命令。
    /// </summary>
    private static string? InvokeString(CommandRegistry registry, string name, params object?[] args)
    {
        return InvokeCommand(registry, name, args) as string;
    }

    // ---------------------------------------------------------------------
    // Name 和 ConfigureServices
    // ---------------------------------------------------------------------

    [Test]
    public async Task Name_ReturnsOpener()
    {
        var plugin = new OpenerPlugin();
        await Assert.That(plugin.Name).IsEqualTo("opener");
    }

    [Test]
    public async Task ConfigureServices_DoesNotThrow()
    {
        var plugin = new OpenerPlugin();
        var services = new ServiceCollection();
        plugin.ConfigureServices(services);
        await Assert.That(services.Count).IsEqualTo(0);
    }

    // ---------------------------------------------------------------------
    // Configure 命令注册
    // ---------------------------------------------------------------------

    [Test]
    public async Task Configure_RegistersAllCommands()
    {
        var plugin = new OpenerPlugin();
        var context = CreatePluginContext();

        plugin.Configure(context);

        var commands = context.Commands.GetCommandNames().ToList();
        await Assert.That(commands).Contains("opener.openUrl");
        await Assert.That(commands).Contains("opener.openPath");
        await Assert.That(commands).Contains("opener.revealInFolder");
        await Assert.That(commands).Contains("opener.isUrlAllowed");
        await Assert.That(commands).Contains("opener.verifyUrl");
    }

    // ---------------------------------------------------------------------
    // IsUrlAllowed
    // ---------------------------------------------------------------------

    [Test]
    public async Task IsUrlAllowed_ValidHttps_ReturnsTrue()
    {
        var plugin = new OpenerPlugin();
        var result = plugin.IsUrlAllowed("https://example.com");
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsUrlAllowed_ValidHttp_ReturnsTrue()
    {
        var plugin = new OpenerPlugin();
        var result = plugin.IsUrlAllowed("http://example.com");
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsUrlAllowed_ValidMailto_ReturnsTrue()
    {
        var plugin = new OpenerPlugin();
        var result = plugin.IsUrlAllowed("mailto:test@example.com");
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsUrlAllowed_FileProtocol_BlockedByDefault()
    {
        var plugin = new OpenerPlugin();
        var result = plugin.IsUrlAllowed("file:///etc/passwd");
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsUrlAllowed_JavascriptProtocol_BlockedByDefault()
    {
        var plugin = new OpenerPlugin();
        var result = plugin.IsUrlAllowed("javascript:alert(1)");
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsUrlAllowed_EmptyUrl_ReturnsFalse()
    {
        var plugin = new OpenerPlugin();
        var result = plugin.IsUrlAllowed("");
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsUrlAllowed_InvalidUrl_ReturnsFalse()
    {
        var plugin = new OpenerPlugin();
        var result = plugin.IsUrlAllowed("not-a-url");
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsUrlAllowed_UrlPatternWhitelist_MatchingUrl_ReturnsTrue()
    {
        var plugin = new OpenerPlugin();
        plugin.AddAllowedUrlPattern("https://*.example.com");

        var result = plugin.IsUrlAllowed("https://sub.example.com");
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsUrlAllowed_UrlPatternWhitelist_NonMatchingUrl_ReturnsFalse()
    {
        var plugin = new OpenerPlugin();
        plugin.AddAllowedUrlPattern("https://*.example.com");

        var result = plugin.IsUrlAllowed("https://other.com");
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsUrlAllowed_CustomScheme_WithCustomAllowedSchemes()
    {
        var plugin = new OpenerPlugin("http", "https", "custom");
        var result = plugin.IsUrlAllowed("custom://test");
        await Assert.That(result).IsTrue();
    }

    // ---------------------------------------------------------------------
    // VerifyUrl
    // ---------------------------------------------------------------------

    [Test]
    public async Task VerifyUrl_ValidUrl_ReturnsNull()
    {
        var plugin = new OpenerPlugin();
        var result = plugin.VerifyUrl("https://example.com");
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task VerifyUrl_EmptyUrl_ReturnsError()
    {
        var plugin = new OpenerPlugin();
        var result = plugin.VerifyUrl("");
        await Assert.That(result).IsEqualTo("URL 不能为空");
    }

    [Test]
    public async Task VerifyUrl_InvalidUrl_ReturnsError()
    {
        var plugin = new OpenerPlugin();
        var result = plugin.VerifyUrl("not-a-url");
        await Assert.That(result).Contains("URL 格式无效");
    }

    [Test]
    public async Task VerifyUrl_BlockedScheme_ReturnsError()
    {
        var plugin = new OpenerPlugin();
        var result = plugin.VerifyUrl("file:///etc/passwd");
        await Assert.That(result).Contains("不在允许列表中");
    }

    [Test]
    public async Task VerifyUrl_NotMatchingPattern_ReturnsError()
    {
        var plugin = new OpenerPlugin();
        plugin.AddAllowedUrlPattern("https://*.example.com");
        var result = plugin.VerifyUrl("https://other.com");
        await Assert.That(result).Contains("不匹配任何允许的模式");
    }

    // ---------------------------------------------------------------------
    // 命令调用
    // ---------------------------------------------------------------------

    [Test]
    public async Task Command_IsUrlAllowed_ValidHttps_ReturnsTrue()
    {
        var plugin = new OpenerPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);

        var result = InvokeBool(context.Commands, "opener.isUrlAllowed", "https://example.com");
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Command_IsUrlAllowed_FileProtocol_ReturnsFalse()
    {
        var plugin = new OpenerPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);

        var result = InvokeBool(context.Commands, "opener.isUrlAllowed", "file:///etc/passwd");
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task Command_VerifyUrl_ValidUrl_ReturnsNull()
    {
        var plugin = new OpenerPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);

        var result = InvokeString(context.Commands, "opener.verifyUrl", "https://example.com");
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Command_VerifyUrl_InvalidUrl_ReturnsError()
    {
        var plugin = new OpenerPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);

        var result = InvokeString(context.Commands, "opener.verifyUrl", "invalid");
        await Assert.That(result).Contains("URL 格式无效");
    }

    // ---------------------------------------------------------------------
    // AddAllowedProgram
    // ---------------------------------------------------------------------

    [Test]
    public async Task AddAllowedProgram_ValidProgram_AddsToSet()
    {
        var plugin = new OpenerPlugin();

        // 验证不抛异常
        await Assert.That(() => plugin.AddAllowedProgram("chrome")).ThrowsNothing();
    }

    [Test]
    public async Task AddAllowedProgram_EmptyProgram_Throws()
    {
        var plugin = new OpenerPlugin();
        await Assert.That(() => plugin.AddAllowedProgram("")).ThrowsExactly<ArgumentException>();
    }

    // ---------------------------------------------------------------------
    // OpenUrl/OpenPath（不实际启动进程的边界测试）
    // ---------------------------------------------------------------------

    [Test]
    public async Task OpenUrl_BlockedUrl_ReturnsFalse()
    {
        var plugin = new OpenerPlugin();
        var result = plugin.OpenUrl("file:///etc/passwd", null);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task OpenUrl_EmptyUrl_ReturnsFalse()
    {
        var plugin = new OpenerPlugin();
        var result = plugin.OpenUrl("", null);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task OpenPath_EmptyPath_ReturnsFalse()
    {
        var plugin = new OpenerPlugin();
        var result = plugin.OpenPath("", null);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task OpenPath_NonExistentFile_ReturnsFalse()
    {
        var plugin = new OpenerPlugin();
        var result = plugin.OpenPath("/path/that/does/not/exist", null);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task OpenUrl_WithTargetNotAllowed_ReturnsFalse()
    {
        var plugin = new OpenerPlugin();
        plugin.AddAllowedProgram("chrome");

        // target 'firefox' 不在白名单中
        var result = plugin.OpenUrl("https://example.com", "firefox");
        await Assert.That(result).IsFalse();
    }

    // ---------------------------------------------------------------------
    // RevealInFolder
    // ---------------------------------------------------------------------

    [Test]
    public async Task RevealInFolder_EmptyPath_DoesNotThrow()
    {
        var plugin = new OpenerPlugin();

        // 应静默处理不抛异常
        await Assert.That(() => plugin.RevealInFolder("")).ThrowsNothing();
    }

    [Test]
    public async Task RevealInFolder_NonExistentPath_DoesNotThrow()
    {
        var plugin = new OpenerPlugin();

        // 应静默处理不抛异常
        await Assert.That(() => plugin.RevealInFolder("/path/that/does/not/exist")).ThrowsNothing();
    }
}
