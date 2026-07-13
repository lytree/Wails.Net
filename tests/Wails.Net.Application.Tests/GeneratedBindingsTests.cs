using System.Text.Json;
using TUnit.Core;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Commands;

namespace Wails.Net.Application.Tests;

/// <summary>
/// 源代码生成器调用器路径的端到端测试。
/// 验证 [Binding] / [Command] 特性标记的方法由源生成器生成强类型调用器，
/// 在 BindingManager.Call 中通过 GeneratedBindingRegistry 路径调用（而非反射）。
/// </summary>
[NotInParallel]
public sealed class GeneratedBindingsTests
{
    /// <summary>
    /// 用于测试源生成器路径的服务类。
    /// 方法标记 [Binding] 特性，源生成器会为其生成强类型调用器。
    /// </summary>
    public class GeneratedTestService
    {
        private int _counter;

        [Binding]
        public string GetName() => "Generated";

        [Binding]
        public int Add(int a, int b) => a + b;

        [Binding(Name = "custom.greet")]
        public string Greet(string name) => $"Hello, {name}!";

        [Binding]
        public int Increment() => ++_counter;

        [Binding]
        public Task<string> GetAsync() => Task.FromResult("async-generated");

        [Command("cmd.getValue")]
        public int GetValue() => _counter;

        [Command("cmd.reset")]
        public void Reset() => _counter = 0;
    }

    /// <summary>
    /// 验证源生成器已为 [Binding] 标记的方法注册调用器。
    /// </summary>
    [Test]
    public async Task GeneratedRegistry_HasBindingMethodsRegistered()
    {
        // 源生成器应在编译时为 [Binding] 方法生成调用器并注册到 GeneratedBindingRegistry。
        // 注意：生成器使用 Namespace.ClassName.MethodName 格式（不含父类名），
        // 与运行时 BindingManager.GetFullTypeName 一致（基于 type.Namespace + type.Name）。
        await Assert.That(GeneratedBindingRegistry.TryGetInvoker(
            "Wails.Net.Application.Tests.GeneratedTestService.GetName", out _)).IsTrue();
        await Assert.That(GeneratedBindingRegistry.TryGetInvoker(
            "GeneratedTestService.GetName", out _)).IsTrue();
        await Assert.That(GeneratedBindingRegistry.TryGetInvoker(
            "GeneratedTestService.Add", out _)).IsTrue();
    }

    /// <summary>
    /// 验证源生成器已为 [Binding(Name=...)] 标记的方法注册自定义名称。
    /// </summary>
    [Test]
    public async Task GeneratedRegistry_HasCustomNamedBindingRegistered()
    {
        await Assert.That(GeneratedBindingRegistry.TryGetInvoker("custom.greet", out _)).IsTrue();
    }

    /// <summary>
    /// 验证源生成器已为 [Command] 标记的方法注册调用器。
    /// </summary>
    [Test]
    public async Task GeneratedRegistry_HasCommandMethodsRegistered()
    {
        await Assert.That(GeneratedBindingRegistry.TryGetInvoker("cmd.getValue", out _)).IsTrue();
        await Assert.That(GeneratedBindingRegistry.TryGetInvoker("cmd.reset", out _)).IsTrue();
    }

    /// <summary>
    /// 验证通过生成器调用器路径调用无参数方法返回正确结果。
    /// </summary>
    [Test]
    public async Task Call_ByName_GeneratedPath_ReturnsCorrectResult()
    {
        var bindings = new BindingManager();
        bindings.Add(new GeneratedTestService());

        var result = await bindings.Call("GeneratedTestService.GetName", Array.Empty<JsonElement>());

        await Assert.That(result["error"]).IsNull();
        await Assert.That(result["result"]?.ToString()).IsEqualTo("Generated");
    }

    /// <summary>
    /// 验证通过生成器调用器路径调用带参数方法返回正确结果。
    /// </summary>
    [Test]
    public async Task Call_WithParameters_GeneratedPath_ReturnsCorrectResult()
    {
        var bindings = new BindingManager();
        bindings.Add(new GeneratedTestService());

        var args = new[]
        {
            JsonSerializer.SerializeToElement(10),
            JsonSerializer.SerializeToElement(20)
        };
        var result = await bindings.Call("GeneratedTestService.Add", args);

        await Assert.That(result["error"]).IsNull();
        await Assert.That(result["result"]?.ToString()).IsEqualTo("30");
    }

    /// <summary>
    /// 验证通过生成器调用器路径调用自定义命名方法返回正确结果。
    /// </summary>
    [Test]
    public async Task Call_CustomName_GeneratedPath_ReturnsCorrectResult()
    {
        var bindings = new BindingManager();
        bindings.Add(new GeneratedTestService());

        var args = new[] { JsonSerializer.SerializeToElement("World") };
        var result = await bindings.Call("custom.greet", args);

        await Assert.That(result["error"]).IsNull();
        await Assert.That(result["result"]?.ToString()).IsEqualTo("Hello, World!");
    }

    /// <summary>
    /// 验证通过生成器调用器路径调用 [Command] 标记的方法返回正确结果。
    /// </summary>
    [Test]
    public async Task Call_CommandName_GeneratedPath_ReturnsCorrectResult()
    {
        var bindings = new BindingManager();
        bindings.Add(new GeneratedTestService());

        // 先 Increment 两次，使计数器值为 2
        await bindings.Call("GeneratedTestService.Increment", Array.Empty<JsonElement>());
        await bindings.Call("GeneratedTestService.Increment", Array.Empty<JsonElement>());

        // 通过命令名调用 getValue
        var result = await bindings.Call("cmd.getValue", Array.Empty<JsonElement>());

        await Assert.That(result["error"]).IsNull();
        await Assert.That(result["result"]?.ToString()).IsEqualTo("2");
    }

    /// <summary>
    /// 验证通过生成器调用器路径调用异步方法返回正确结果。
    /// </summary>
    [Test]
    public async Task Call_AsyncMethod_GeneratedPath_ReturnsTaskResult()
    {
        var bindings = new BindingManager();
        bindings.Add(new GeneratedTestService());

        var result = await bindings.Call("GeneratedTestService.GetAsync", Array.Empty<JsonElement>());

        await Assert.That(result["error"]).IsNull();
        await Assert.That(result["result"]?.ToString()).IsEqualTo("async-generated");
    }

    /// <summary>
    /// 验证通过生成器调用器路径调用 void 方法返回 null。
    /// </summary>
    [Test]
    public async Task Call_VoidCommand_GeneratedPath_ReturnsNull()
    {
        var bindings = new BindingManager();
        bindings.Add(new GeneratedTestService());

        var result = await bindings.Call("cmd.reset", Array.Empty<JsonElement>());

        await Assert.That(result["error"]).IsNull();
        await Assert.That(result["result"]).IsNull();
    }

    /// <summary>
    /// 验证当实例未注册时返回 ReferenceError。
    /// </summary>
    [Test]
    public async Task Call_GeneratedMethod_NoInstanceRegistered_ReturnsReferenceError()
    {
        // 不注册实例，直接调用生成器路径的方法
        var bindings = new BindingManager();

        var result = await bindings.Call("GeneratedTestService.GetName", Array.Empty<JsonElement>());

        await Assert.That(result["error"]).IsNotNull();
    }
}
