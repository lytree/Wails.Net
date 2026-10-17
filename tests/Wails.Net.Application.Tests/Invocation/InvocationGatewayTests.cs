using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Core;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Events;
using Wails.Net.Application.Invocation;
using Wails.Net.Application.Security;
using Wails.Net.Application.Options;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Tests.Invocation;

/// <summary>
/// InvocationGateway 的单元测试（TUnit）。
/// 验证 R2-1 融合审计要求的统一调用路径：Binding 命中、Command 回退、错误模型。
/// </summary>
[NotInParallel]
public sealed class InvocationGatewayTests
{
    /// <summary>
    /// 测试用绑定服务：含一个命中方法。
    /// <para>
    /// 类名须与本程序集内其他绑定类区分：源生成器会为 [Binding] 方法注册
    /// "ClassName.MethodName" 短名别名，同名类会导致别名冲突（WNET004）并使该别名不再注册。
    /// </para>
    /// </summary>
    public class GatewayTestService
    {
        [Binding]
        public string Echo(string value) => $"echo:{value}";

        [Binding]
        public int Add(int a, int b) => a + b;
    }

    private static JsonElement ArgOf(string json)
    {
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static BindingManager CreateBindings()
    {
        var b = new BindingManager();
        b.Add(new GatewayTestService());
        return b;
    }

    private static CommandDispatcher CreateDispatcher(CommandRegistry registry, IServiceProvider? services = null)
    {
        return new CommandDispatcher(
            registry,
            services ?? new ServiceCollection().BuildServiceProvider(),
            NullLogger<CommandDispatcher>.Instance);
    }

    /// <summary>
    /// 命中 Binding 时返回正确的 result，error 为 null。
    /// </summary>
    [Test]
    public async Task InvokeAsync_BindingHit_ReturnsResult()
    {
        var gateway = new InvocationGateway(CreateBindings(), null, null, null);
        var args = new[] { ArgOf("\"hello\"") };

        var result = await gateway.InvokeAsync(
            GeneratedBindingsMetadata.Methods.First(m => m.MethodName == "Echo" && m.ClassName == "GatewayTestService").FullName,
            args,
            null, null, CancellationToken.None);

        await Assert.That(result).IsNotNull();
        await Assert.That(result["error"]).IsNull();
        await Assert.That(result["result"]?.ToString()).IsEqualTo("echo:hello");
    }

    /// <summary>
    /// Binding 未命中但 Command 命中时返回 Command 结果。
    /// </summary>
    [Test]
    public async Task InvokeAsync_BindingMiss_CommandHit_ReturnsCommandResult()
    {
        var registry = new CommandRegistry();
        CompiledCommandInvoker invoker = (instance, parameters, ctx) =>
            Task.FromResult<object?>("cmd-result");
        registry.Register("counter.increment", invoker, Type.EmptyTypes, (CommandSecurity?)null);

        var gateway = new InvocationGateway(CreateBindings(), CreateDispatcher(registry), null, null);

        var result = await gateway.InvokeAsync(
            "counter.increment",
            Array.Empty<JsonElement>(),
            null, null, CancellationToken.None);

        await Assert.That(result).IsNotNull();
        await Assert.That(result["error"]).IsNull();
        await Assert.That(result["result"]?.ToString()).IsEqualTo("cmd-result");
    }

    /// <summary>
    /// Binding 与 Command 都未命中时返回 Binding 的 ReferenceError。
    /// </summary>
    [Test]
    public async Task InvokeAsync_BothMiss_ReturnsBindingReferenceError()
    {
        var registry = new CommandRegistry();
        var gateway = new InvocationGateway(CreateBindings(), CreateDispatcher(registry), null, null);

        var result = await gateway.InvokeAsync(
            "Unknown.Both.Miss",
            Array.Empty<JsonElement>(),
            null, null, CancellationToken.None);

        await Assert.That(result).IsNotNull();
        await Assert.That(result["result"]).IsNull();
        var error = result["error"] as Dictionary<string, object?>;
        await Assert.That(error).IsNotNull();
        await Assert.That(error!["kind"]?.ToString()).IsEqualTo("ReferenceError");
    }

    /// <summary>
    /// 未配置 CommandDispatcher 时仅走 Binding 路径，未命中返回 ReferenceError。
    /// </summary>
    [Test]
    public async Task InvokeAsync_NoCommandDispatcher_ReturnsBindingReferenceError()
    {
        var gateway = new InvocationGateway(CreateBindings(), null, null, null);

        var result = await gateway.InvokeAsync(
            "NonExistent.Method",
            Array.Empty<JsonElement>(),
            null, null, CancellationToken.None);

        await Assert.That(result).IsNotNull();
        await Assert.That(result["result"]).IsNull();
        var error = result["error"] as Dictionary<string, object?>;
        await Assert.That(error).IsNotNull();
        await Assert.That(error!["kind"]?.ToString()).IsEqualTo("ReferenceError");
    }

    /// <summary>
    /// 按 ID 调用 Binding：未知 ID 返回 ReferenceError。
    /// </summary>
    [Test]
    public async Task InvokeByIdAsync_UnknownId_ReturnsReferenceError()
    {
        var gateway = new InvocationGateway(CreateBindings(), null, null, null);

        var result = await gateway.InvokeByIdAsync(
            uint.MaxValue,
            Array.Empty<JsonElement>(),
            null, null, CancellationToken.None);

        await Assert.That(result).IsNotNull();
        await Assert.That(result["result"]).IsNull();
        var error = result["error"] as Dictionary<string, object?>;
        await Assert.That(error).IsNotNull();
        await Assert.That(error!["kind"]?.ToString()).IsEqualTo("ReferenceError");
    }

    /// <summary>
    /// 按 ID 调用 Binding：已知 ID 解析到方法全名并执行。
    /// </summary>
    [Test]
    public async Task InvokeByIdAsync_KnownId_DispatchesBinding()
    {
        var bindings = CreateBindings();
        var gateway = new InvocationGateway(bindings, null, null, null);

        var echoMethod = GeneratedBindingsMetadata.Methods.First(m => m.MethodName == "Echo" && m.ClassName == "GatewayTestService");
        var args = new[] { ArgOf("\"world\"") };

        var result = await gateway.InvokeByIdAsync(echoMethod.Id, args, null, null, CancellationToken.None);

        await Assert.That(result["error"]).IsNull();
        await Assert.That(result["result"]?.ToString()).IsEqualTo("echo:world");
    }

    /// <summary>
    /// Authorize 在 R2-1 阶段始终返回 Allow（R3 将接入完整授权）。
    /// </summary>
    [Test]
    public async Task Authorize_CurrentlyAlwaysAllow()
    {
        var gateway = new InvocationGateway(CreateBindings(), null, null, null);

        var auth = gateway.Authorize("any.method", new CommandContext(
            new ServiceCollection().BuildServiceProvider(),
            null, CancellationToken.None, null, null));

        await Assert.That(auth.IsAllowed).IsTrue();
    }

    /// <summary>
    /// SetWindowLookup 注册后 InvokeAsync 调用 CommandDispatcher 时 WindowName 可被解析。
    /// </summary>
    [Test]
    public async Task SetWindowLookup_RegistersWindowLookup_CanBeInvoked()
    {
        var registry = new CommandRegistry();
        CompiledCommandInvoker invoker = (instance, parameters, ctx) =>
        {
            // 通过 ctx.WindowName 验证窗口名查找已注入
            return Task.FromResult<object?>(ctx?.WindowName ?? "no-window-name");
        };
        registry.Register("test.echo", invoker, Type.EmptyTypes, (CommandSecurity?)null);

        var gateway = new InvocationGateway(CreateBindings(), CreateDispatcher(registry), null, new ServiceCollection().BuildServiceProvider());
        gateway.SetWindowLookup(_ => new WebviewWindow(1u, "MyWindow", new WebviewWindowOptions { Name = "MyWindow" }));

        var result = await gateway.InvokeAsync(
            "test.echo",
            Array.Empty<JsonElement>(),
            1u, null, CancellationToken.None);

        await Assert.That(result["error"]).IsNull();
        await Assert.That(result["result"]?.ToString()).IsEqualTo("MyWindow");
    }
}
