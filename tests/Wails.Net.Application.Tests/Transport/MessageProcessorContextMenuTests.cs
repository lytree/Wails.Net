using System.Text.Json;
using NSubstitute;
using TUnit.Core;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Events;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Options;
using Wails.Net.Application.Transport;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Tests.Transport;

/// <summary>
/// <see cref="MessageProcessor.ProcessContextMenu"/> 路径的单元测试（P1-4）。
/// 覆盖：
/// <list type="bullet">
/// <item>常规上下文菜单消息：触发 OpenContextMenu(ContextMenuData) 调用。</item>
/// <item>无 windowLookup / 无 WindowId / 无 Impl 时的退化行为。</item>
/// <item>wails:contextmenu 事件广播钩子。</item>
/// <item>OpenContextMenu 抛出异常时不应导致消息处理崩溃（应通过 wails:error 事件暴露错误）。</item>
/// <item>兼容旧前端 ContextId 字段格式。</item>
/// </list>
/// </summary>
[NotInParallel]
public sealed class MessageProcessorContextMenuTests
{
    /// <summary>
    /// 创建带 windowLookup 的 MessageProcessor，lookup 返回指定窗口（Impl 已设置）。
    /// </summary>
    private static (MessageProcessor processor, WebviewWindow window, IWebviewWindowImpl impl, EventProcessor events) CreateWithWindow(uint windowId = 1)
    {
        var bindings = new BindingManager();
        var events = new EventProcessor();
        var window = new WebviewWindow(windowId, $"W{windowId}", new WebviewWindowOptions { Name = $"W{windowId}" });
        var impl = Substitute.For<IWebviewWindowImpl>();
        window.Impl = impl;

        MessageProcessor processor = new(bindings, events, _ => window);
        return (processor, window, impl, events);
    }

    /// <summary>
    /// 构造 contextmenu 消息。
    /// </summary>
    private static Message CreateContextMenuMessage(string id, int x, int y, string? data = null, uint? windowId = 1, string? contextId = null)
    {
        var payloadObj = new Dictionary<string, object?> { ["id"] = id, ["x"] = x, ["y"] = y };
        if (data is not null)
        {
            payloadObj["data"] = data;
        }
        if (contextId is not null)
        {
            payloadObj["contextId"] = contextId;
        }
        var payloadJson = JsonSerializer.Serialize(payloadObj);
        return new Message
        {
            Id = "req-1",
            Type = MessageProcessor.MessageTypes.ContextMenu,
            Payload = JsonSerializer.Deserialize<JsonElement>(payloadJson),
            WindowId = windowId
        };
    }

    /// <summary>
    /// 标准上下文菜单消息：windowId 命中已注册窗口，应调用 OpenContextMenu(ContextMenuData)。
    /// </summary>
    [Test]
    public async Task ProcessContextMenu_ValidMessage_InvokesOpenContextMenuWithData()
    {
        // 安排
        var (processor, _, impl, _) = CreateWithWindow(windowId: 42);
        ContextMenuData? captured = null;
        // 使用 When..Do 捕获 ContextMenuData 参数
        impl.When(x => x.OpenContextMenu(Arg.Any<ContextMenuData>()))
            .Do(callInfo => captured = callInfo.Arg<ContextMenuData>());

        var message = CreateContextMenuMessage(id: "edit-menu", x: 120, y: 88, data: "row-42", windowId: 42);

        // 操作
        await processor.ProcessAsync(message);

        // 断言
        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.Id).IsEqualTo("edit-menu");
        await Assert.That(captured.X).IsEqualTo(120);
        await Assert.That(captured.Y).IsEqualTo(88);
        await Assert.That(captured.Data).IsEqualTo("row-42");
    }

    /// <summary>
    /// 无 windowLookup 时（构造函数未提供）应不调用 OpenContextMenu，
    /// 但仍广播 wails:contextmenu 事件。
    /// </summary>
    [Test]
    public async Task ProcessContextMenu_NoWindowLookup_BroadcastsEventWithoutInvokingImpl()
    {
        // 安排
        var bindings = new BindingManager();
        var events = new EventProcessor();
        var processor = new MessageProcessor(bindings, events);

        object? emittedData = null;
        events.On("wails:contextmenu", data => emittedData = data);

        var message = CreateContextMenuMessage(id: "cm", x: 0, y: 0, windowId: 1);

        // 操作
        await processor.ProcessAsync(message);

        // 断言：事件被广播
        await Assert.That(emittedData).IsNotNull();
    }

    /// <summary>
    /// 消息未携带 windowId 时不应调用 OpenContextMenu，但仍广播事件。
    /// </summary>
    [Test]
    public async Task ProcessContextMenu_NullWindowId_DoesNotInvokeImpl()
    {
        // 安排
        var (processor, _, impl, _) = CreateWithWindow(windowId: 1);

        var message = CreateContextMenuMessage(id: "cm", x: 10, y: 10, windowId: null);

        // 操作
        await processor.ProcessAsync(message);

        // 断言：Impl 上的 OpenContextMenu 不应被调用
        impl.DidNotReceive().OpenContextMenu(Arg.Any<ContextMenuData>());
    }

    /// <summary>
    /// windowLookup 找不到窗口时应安全降级（不抛异常，不调用 Impl）。
    /// </summary>
    [Test]
    public async Task ProcessContextMenu_UnknownWindowId_DoesNotThrow()
    {
        // 安排
        var bindings = new BindingManager();
        var events = new EventProcessor();
        MessageProcessor processor = new(bindings, events, _ => null);

        var message = CreateContextMenuMessage(id: "cm", x: 10, y: 10, windowId: 999);

        // 操作 + 断言
        await Assert.That(() => processor.ProcessAsync(message)).ThrowsNothing();
    }

    /// <summary>
    /// 窗口的 Impl 为 null 时应安全降级（不抛异常）。
    /// </summary>
    [Test]
    public async Task ProcessContextMenu_NullImpl_DoesNotThrow()
    {
        // 安排
        var bindings = new BindingManager();
        var events = new EventProcessor();
        var window = new WebviewWindow(7, "W7", new WebviewWindowOptions { Name = "W7" });
        // window.Impl 保持 null
        MessageProcessor processor = new(bindings, events, _ => window);

        var message = CreateContextMenuMessage(id: "cm", x: 0, y: 0, windowId: 7);

        // 操作 + 断言
        await Assert.That(() => processor.ProcessAsync(message)).ThrowsNothing();
    }

    /// <summary>
    /// OpenContextMenu 抛出异常时，处理器应吞掉异常并通过 wails:error 事件暴露错误，
    /// 不应导致 ProcessAsync 抛出。
    /// </summary>
    [Test]
    public async Task ProcessContextMenu_ImplThrows_EmitsWailsErrorAndDoesNotThrow()
    {
        // 安排
        var (processor, _, impl, events) = CreateWithWindow(windowId: 3);
        impl.When(x => x.OpenContextMenu(Arg.Any<ContextMenuData>()))
            .Do(_ => throw new InvalidOperationException("native menu explosion"));

        object? errorData = null;
        events.On("wails:error", data => errorData = data);

        var message = CreateContextMenuMessage(id: "cm", x: 1, y: 2, windowId: 3);

        // 操作 + 断言
        await Assert.That(() => processor.ProcessAsync(message)).ThrowsNothing();

        // 等待事件传播（事件 On 是同步分发，应已触发）
        await Assert.That(errorData).IsNotNull();
    }

    /// <summary>
    /// 兼容旧前端格式：Id 为空但 ContextId 存在时，应使用 ContextId 作为菜单 ID。
    /// </summary>
    [Test]
    public async Task ProcessContextMenu_LegacyContextIdFallback_UsesContextIdAsMenuId()
    {
        // 安排
        var (processor, _, impl, _) = CreateWithWindow(windowId: 1);
        ContextMenuData? captured = null;
        impl.When(x => x.OpenContextMenu(Arg.Any<ContextMenuData>()))
            .Do(callInfo => captured = callInfo.Arg<ContextMenuData>());

        // 构造 Id 为空、ContextId 存在的载荷
        var payloadJson = """{"id":"","x":50,"y":60,"contextId":"legacy-menu"}""";
        var message = new Message
        {
            Id = "req-legacy",
            Type = MessageProcessor.MessageTypes.ContextMenu,
            Payload = JsonSerializer.Deserialize<JsonElement>(payloadJson),
            WindowId = 1
        };

        // 操作
        await processor.ProcessAsync(message);

        // 断言
        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.Id).IsEqualTo("legacy-menu");
    }

    /// <summary>
    /// Id 优先于 ContextId：当两个字段都存在时，应使用 Id。
    /// </summary>
    [Test]
    public async Task ProcessContextMenu_BothIdAndContextId_PrefersId()
    {
        // 安排
        var (processor, _, impl, _) = CreateWithWindow(windowId: 1);
        ContextMenuData? captured = null;
        impl.When(x => x.OpenContextMenu(Arg.Any<ContextMenuData>()))
            .Do(callInfo => captured = callInfo.Arg<ContextMenuData>());

        var message = CreateContextMenuMessage(id: "primary", x: 1, y: 2, contextId: "legacy", windowId: 1);

        // 操作
        await processor.ProcessAsync(message);

        // 断言
        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.Id).IsEqualTo("primary");
    }

    /// <summary>
    /// 空载荷应被安全处理（反序列化失败不抛出）。
    /// </summary>
    [Test]
    public async Task ProcessContextMenu_EmptyPayload_ReturnsNullWithoutThrowing()
    {
        // 安排
        var (processor, _, impl, _) = CreateWithWindow(windowId: 1);

        var message = new Message
        {
            Id = "req-empty",
            Type = MessageProcessor.MessageTypes.ContextMenu,
            Payload = JsonSerializer.Deserialize<JsonElement>("null"),
            WindowId = 1
        };

        // 操作 + 断言
        await Assert.That(() => processor.ProcessAsync(message)).ThrowsNothing();
        impl.DidNotReceive().OpenContextMenu(Arg.Any<ContextMenuData>());
    }

    /// <summary>
    /// ProcessContextMenu 应始终返回 null（无响应消息）。
    /// </summary>
    [Test]
    public async Task ProcessContextMenu_AlwaysReturnsNullResponse()
    {
        // 安排
        var (processor, _, _, _) = CreateWithWindow(windowId: 1);
        var message = CreateContextMenuMessage(id: "cm", x: 10, y: 20, windowId: 1);

        // 操作
        var response = await processor.ProcessAsync(message);

        // 断言
        await Assert.That(response).IsNull();
    }
}
