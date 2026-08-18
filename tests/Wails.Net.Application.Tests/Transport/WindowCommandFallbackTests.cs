using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TUnit.Core;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Events;
using Wails.Net.Application.Options;
using Wails.Net.Application.Transport;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Tests.Transport;

/// <summary>
/// window.* 消息的插件命令路径测试（审计 §3.5 / R0-5）。
/// <para>
/// 验证：当 CommandDispatcher 已配置时，window.* 消息不再回退到硬编码
/// <c>DispatchWindowAction</c>（无权限校验路径），未命中插件命令时返回
/// PermissionDenied 错误响应。仅 CommandDispatcher 未配置（向后兼容测试场景）
/// 时才保留硬编码回退。
/// </para>
/// </summary>
[NotInParallel]
public sealed class WindowCommandFallbackTests
{
    private static Message CreateWindowMessage(string action, uint? windowId = 1, string? title = "Hello")
    {
        var payloadJson = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["title"] = title,
        });
        return new Message
        {
            Id = "win-1",
            Type = $"window.{action}",
            Payload = JsonSerializer.Deserialize<JsonElement>(payloadJson),
            WindowId = windowId,
        };
    }

    /// <summary>
    /// 创建带 CommandDispatcher 的 MessageProcessor（无 WindowPlugin 命令注册）。
    /// </summary>
    private static MessageProcessor CreateProcessorWithEmptyDispatcher(WebviewWindow? window = null)
    {
        var bindings = new BindingManager();
        var events = new EventProcessor();
        var registry = new CommandRegistry();
        var services = new ServiceCollection().BuildServiceProvider();
        var dispatcher = new CommandDispatcher(registry, services,
            NullLogger<CommandDispatcher>.Instance);

        Func<uint, WebviewWindow?> windowLookup = window is not null ? (_ => window) : null!;
        return new MessageProcessor(bindings, events, windowLookup, dispatcher, services);
    }

    /// <summary>
    /// 创建带 CommandDispatcher 且注册了 window.setTitle 插件命令的 MessageProcessor。
    /// </summary>
    private static MessageProcessor CreateProcessorWithWindowPluginSetTitle(out IWebviewWindowImpl impl)
    {
        var bindings = new BindingManager();
        var events = new EventProcessor();
        var window = new WebviewWindow(1u, "W1", new WebviewWindowOptions { Name = "W1" });
        impl = Substitute.For<IWebviewWindowImpl>();
        window.Impl = impl;

        var registry = new CommandRegistry();
        CompiledCommandInvoker invoker = (instance, parameters, ctx) =>
        {
            var title = parameters.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString() ?? string.Empty
                : string.Empty;
            var targetId = ctx?.WindowId ?? 1u;
            return Task.FromResult<object?>($"set:{targetId}:{title}");
        };
        registry.Register("window.setTitle", invoker, new[] { typeof(string) }, (CommandSecurity?)null);

        var services = new ServiceCollection().BuildServiceProvider();
        var dispatcher = new CommandDispatcher(registry, services,
            NullLogger<CommandDispatcher>.Instance);

        Func<uint, WebviewWindow?> windowLookup = _ => window;
        return new MessageProcessor(bindings, events, windowLookup, dispatcher, services);
    }

    /// <summary>
    /// R0-5：当 CommandDispatcher 已配置且未注册 window.setTitle 命令时，
    /// 返回 PermissionDenied 错误响应，不再静默调用 DispatchWindowAction。
    /// </summary>
    [Test]
    public async Task ProcessAsync_WindowSetTitle_NoPluginCommand_ReturnsPermissionDenied()
    {
        var processor = CreateProcessorWithEmptyDispatcher();
        var message = CreateWindowMessage("setTitle", windowId: 1, title: "test");

        var response = await processor.ProcessAsync(message);

        await Assert.That(response).IsNotNull();
        await Assert.That(response!.Type).IsEqualTo(MessageProcessor.MessageTypes.Error);
        await Assert.That(response.Result).IsNotNull();
        // error 字段是 CallError.ToJson() 返回的字典（message/cause/kind）
        var errorDict = response.Result!["error"] as System.Collections.Generic.Dictionary<string, object?>;
        await Assert.That(errorDict).IsNotNull();
        await Assert.That(errorDict!["message"]?.ToString()).Contains("window.setTitle");
        await Assert.That(errorDict["kind"]?.ToString()).IsEqualTo("ReferenceError");
    }

    /// <summary>
    /// R0-5：当 CommandDispatcher 已配置且注册了 window.setTitle 插件命令时，
    /// 走插件命令路径成功，不回退到硬编码 DispatchWindowAction。
    /// </summary>
    [Test]
    public async Task ProcessAsync_WindowSetTitle_PluginCommandRegistered_SucceedsViaPlugin()
    {
        var processor = CreateProcessorWithWindowPluginSetTitle(out _);
        var message = CreateWindowMessage("setTitle", windowId: 1, title: "HelloPlugin");

        var response = await processor.ProcessAsync(message);

        await Assert.That(response).IsNotNull();
        await Assert.That(response!.Type).IsEqualTo(MessageProcessor.MessageTypes.Response);
        await Assert.That(response.Result).IsNotNull();
        var resultStr = response.Result!["result"]?.ToString() ?? string.Empty;
        await Assert.That(resultStr).IsEqualTo("set:1:HelloPlugin");
    }

    /// <summary>
    /// R0-5 向后兼容：当 CommandDispatcher 未配置时（遗留/测试场景），
    /// 保留硬编码 DispatchWindowAction 回退（_windowLookup 路径）。
    /// </summary>
    [Test]
    public async Task ProcessAsync_WindowSetTitle_NoDispatcher_FallsBackToHardcoded()
    {
        var bindings = new BindingManager();
        var events = new EventProcessor();
        var window = new WebviewWindow(1u, "W1", new WebviewWindowOptions { Name = "W1" });
        var impl = Substitute.For<IWebviewWindowImpl>();
        window.Impl = impl;

        Func<uint, WebviewWindow?> windowLookup = _ => window;
        var processor = new MessageProcessor(bindings, events, windowLookup, commands: null, services: null);

        var message = CreateWindowMessage("setTitle", windowId: 1, title: "legacy");

        var response = await processor.ProcessAsync(message);

        await Assert.That(response).IsNotNull();
        await Assert.That(response!.Type).IsEqualTo(MessageProcessor.MessageTypes.Response);
        impl.Received(1).SetTitle("legacy");
    }
}
