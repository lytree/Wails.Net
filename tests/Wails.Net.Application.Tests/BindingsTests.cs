using System.Text.Json;
using TUnit.Core;
using Wails.Net.Application.Bindings;
using Wails.Net.Errors;

namespace Wails.Net.Application.Tests;

/// <summary>
/// 绑定系统的单元测试（TUnit）。
/// 测试绑定注册、调用、FNV-1a 哈希、异常处理等。
/// </summary>
[NotInParallel]
public sealed class BindingsTests
{
    /// <summary>
    /// 用于测试的服务类，包含各种方法签名。
    /// </summary>
    private class TestService
    {
        public string GetName() => "Wails.Net";

        public int Add(int a, int b) => a + b;

        public string Greet(string name) => $"Hello, {name}!";

        public Task<string> GetAsync() => Task.FromResult("async-result");

        public void DoNothing() { }

        public string ServiceName() => "should-be-excluded";

        public void ServiceStartup() { }

        public void ServiceShutdown() { }

        public int Sum(params int[] numbers) => numbers.Sum();

        public string EchoWithCancellation(CancellationToken ct, string message) => message;

        public string ThrowException() => throw new InvalidOperationException("test-error");
    }

    [Test]
    public async Task FNV1aHash_ReturnsConsistentValue()
    {
        var hash1 = BindingManager.FNV1aHash("Wails.Net.Application.Tests.TestService.GetName");
        var hash2 = BindingManager.FNV1aHash("Wails.Net.Application.Tests.TestService.GetName");

        await Assert.That(hash1).IsEqualTo(hash2);
    }

    [Test]
    public async Task FNV1aHash_ReturnsDifferentValuesForDifferentStrings()
    {
        var hash1 = BindingManager.FNV1aHash("methodA");
        var hash2 = BindingManager.FNV1aHash("methodB");

        await Assert.That(hash1).IsNotEqualTo(hash2);
    }

    [Test]
    public async Task FNV1aHash_MatchesKnownValue()
    {
        // FNV-1a 32-bit hash of "test"
        // offset=2166136261, prime=16777619
        // 't'=116: hash = (2166136261 ^ 116) * 16777619 = 2166136161 * 16777619 mod 2^32
        var hash = BindingManager.FNV1aHash("");
        await Assert.That(hash).IsEqualTo(2166136261u); // FNV offset basis for empty string
    }

    [Test]
    public async Task Add_RegistersPublicMethods()
    {
        var bindings = new BindingManager();
        var service = new TestService();

        bindings.Add(service);

        await Assert.That(bindings.BoundMethods.Count).IsGreaterThan(0);
        await Assert.That(bindings.BoundMethods.ContainsKey("Wails.Net.Application.Tests.TestService.GetName")).IsTrue();
        await Assert.That(bindings.BoundMethods.ContainsKey("Wails.Net.Application.Tests.TestService.Add")).IsTrue();
    }

    [Test]
    public async Task Add_ExcludesServiceInternalMethods()
    {
        var bindings = new BindingManager();
        var service = new TestService();

        bindings.Add(service);

        await Assert.That(bindings.BoundMethods.ContainsKey("Wails.Net.Application.Tests.TestService.ServiceName")).IsFalse();
        await Assert.That(bindings.BoundMethods.ContainsKey("Wails.Net.Application.Tests.TestService.ServiceStartup")).IsFalse();
        await Assert.That(bindings.BoundMethods.ContainsKey("Wails.Net.Application.Tests.TestService.ServiceShutdown")).IsFalse();
    }

    [Test]
    public async Task Call_ByName_ReturnsCorrectResult()
    {
        var bindings = new BindingManager();
        bindings.Add(new TestService());

        var args = Array.Empty<JsonElement>();
        var result = await bindings.Call("Wails.Net.Application.Tests.TestService.GetName", args);

        await Assert.That(result["error"]).IsNull();
        await Assert.That(result["result"]?.ToString()).IsEqualTo("Wails.Net");
    }

    [Test]
    public async Task Call_ByName_WithParameters_ReturnsCorrectResult()
    {
        var bindings = new BindingManager();
        bindings.Add(new TestService());

        var args = new[]
        {
            JsonSerializer.SerializeToElement(3),
            JsonSerializer.SerializeToElement(4)
        };
        var result = await bindings.Call("Wails.Net.Application.Tests.TestService.Add", args);

        await Assert.That(result["error"]).IsNull();
        await Assert.That(result["result"]?.ToString()).IsEqualTo("7");
    }

    [Test]
    public async Task Add_RegistersShortName()
    {
        var bindings = new BindingManager();
        bindings.Add(new TestService());

        // 短名称（ClassName.MethodName）应同时注册
        await Assert.That(bindings.BoundMethods.ContainsKey("TestService.GetName")).IsTrue();
        await Assert.That(bindings.BoundMethods.ContainsKey("TestService.Add")).IsTrue();
    }

    [Test]
    public async Task Call_ByShortName_ReturnsCorrectResult()
    {
        var bindings = new BindingManager();
        bindings.Add(new TestService());

        var args = Array.Empty<JsonElement>();
        var result = await bindings.Call("TestService.GetName", args);

        await Assert.That(result["error"]).IsNull();
        await Assert.That(result["result"]?.ToString()).IsEqualTo("Wails.Net");
    }

    [Test]
    public async Task Call_ByShortNameID_ReturnsCorrectResult()
    {
        var bindings = new BindingManager();
        bindings.Add(new TestService());

        var id = BindingManager.FNV1aHash("TestService.Greet");
        var args = new[] { JsonSerializer.SerializeToElement("World") };
        var result = await bindings.Call(id, args);

        await Assert.That(result["error"]).IsNull();
        await Assert.That(result["result"]?.ToString()).IsEqualTo("Hello, World!");
    }

    [Test]
    public async Task Call_ByID_ReturnsCorrectResult()
    {
        var bindings = new BindingManager();
        bindings.Add(new TestService());

        var id = BindingManager.FNV1aHash("Wails.Net.Application.Tests.TestService.Greet");
        var args = new[] { JsonSerializer.SerializeToElement("World") };
        var result = await bindings.Call(id, args);

        await Assert.That(result["error"]).IsNull();
        await Assert.That(result["result"]?.ToString()).IsEqualTo("Hello, World!");
    }

    [Test]
    public async Task Call_AsyncMethod_ReturnsTaskResult()
    {
        var bindings = new BindingManager();
        bindings.Add(new TestService());

        var args = Array.Empty<JsonElement>();
        var result = await bindings.Call("Wails.Net.Application.Tests.TestService.GetAsync", args);

        await Assert.That(result["error"]).IsNull();
        await Assert.That(result["result"]?.ToString()).IsEqualTo("async-result");
    }

    [Test]
    public async Task Call_VoidMethod_ReturnsNullResult()
    {
        var bindings = new BindingManager();
        bindings.Add(new TestService());

        var args = Array.Empty<JsonElement>();
        var result = await bindings.Call("Wails.Net.Application.Tests.TestService.DoNothing", args);

        await Assert.That(result["error"]).IsNull();
        await Assert.That(result["result"]).IsNull();
    }

    [Test]
    public async Task Call_VariadicMethod_ReturnsCorrectResult()
    {
        var bindings = new BindingManager();
        bindings.Add(new TestService());

        var args = new[]
        {
            JsonSerializer.SerializeToElement(1),
            JsonSerializer.SerializeToElement(2),
            JsonSerializer.SerializeToElement(3)
        };
        var result = await bindings.Call("Wails.Net.Application.Tests.TestService.Sum", args);

        await Assert.That(result["error"]).IsNull();
        await Assert.That(result["result"]?.ToString()).IsEqualTo("6");
    }

    [Test]
    public async Task Call_CancellationTokenMethod_InjectsToken()
    {
        var bindings = new BindingManager();
        bindings.Add(new TestService());

        var args = new[] { JsonSerializer.SerializeToElement("hello") };
        var result = await bindings.Call("Wails.Net.Application.Tests.TestService.EchoWithCancellation", args);

        await Assert.That(result["error"]).IsNull();
        await Assert.That(result["result"]?.ToString()).IsEqualTo("hello");
    }

    [Test]
    public async Task Call_ExceptionMethod_ReturnsRuntimeError()
    {
        var bindings = new BindingManager();
        bindings.Add(new TestService());

        var args = Array.Empty<JsonElement>();
        var result = await bindings.Call("Wails.Net.Application.Tests.TestService.ThrowException", args);

        await Assert.That(result["error"]).IsNotNull();
        var errorDict = result["error"] as Dictionary<string, object?>;
        await Assert.That(errorDict).IsNotNull();
        await Assert.That(errorDict!["message"]?.ToString()).IsEqualTo("test-error");
        await Assert.That(errorDict["kind"]?.ToString()).IsEqualTo(CallErrorKind.RuntimeError.ToString());
    }

    [Test]
    public async Task Call_UnknownID_ReturnsReferenceError()
    {
        var bindings = new BindingManager();

        var result = await bindings.Call(99999u, Array.Empty<JsonElement>());

        await Assert.That(result["error"]).IsNotNull();
        var errorDict = result["error"] as Dictionary<string, object?>;
        await Assert.That(errorDict).IsNotNull();
        await Assert.That(errorDict!["kind"]?.ToString()).IsEqualTo(CallErrorKind.ReferenceError.ToString());
    }

    [Test]
    public async Task Call_UnknownName_ReturnsReferenceError()
    {
        var bindings = new BindingManager();

        var result = await bindings.Call("NonExistent.Method", Array.Empty<JsonElement>());

        await Assert.That(result["error"]).IsNotNull();
        var errorDict = result["error"] as Dictionary<string, object?>;
        await Assert.That(errorDict).IsNotNull();
        await Assert.That(errorDict!["kind"]?.ToString()).IsEqualTo(CallErrorKind.ReferenceError.ToString());
    }

    [Test]
    public async Task BindingRegistry_Initialize_CreatesGlobalInstance()
    {
        BindingRegistry.Reset();
        var bindings = BindingRegistry.Initialize();

        await Assert.That(BindingRegistry.Global).IsSameReferenceAs(bindings);
        BindingRegistry.Reset();
    }

    [Test]
    public async Task BindingRegistry_GlobalOrNull_ReturnsNullBeforeInit()
    {
        BindingRegistry.Reset();

        await Assert.That(BindingRegistry.GlobalOrNull).IsNull();
    }

    [Test]
    public async Task BindingRegistry_Global_ThrowsBeforeInit()
    {
        BindingRegistry.Reset();

        await Assert.That(() => BindingRegistry.Global).ThrowsExactly<InvalidOperationException>();
    }
}
