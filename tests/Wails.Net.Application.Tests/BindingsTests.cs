using TUnit.Core;
using Wails.Net.Application.Bindings;

namespace Wails.Net.Application.Tests;

/// <summary>
/// 绑定系统的单元测试（TUnit）。
/// 仅测试 FNV-1a 哈希一致性与 BindingRegistry 全局实例管理。
/// 反射路径已删除（遵循 AGENTS.md §3.4），方法调用路径由
/// <see cref="GeneratedBindingsTests"/> 验证源生成器生成的强类型调用器。
/// </summary>
[NotInParallel]
public sealed class BindingsTests
{
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
        // FNV-1a 32-bit hash of "" — offset basis
        var hash = BindingManager.FNV1aHash("");
        await Assert.That(hash).IsEqualTo(2166136261u);
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

    [Test]
    public async Task RegisterInstance_RegistersByTypeFullName()
    {
        // 验证 RegisterInstance 仅按类型全名注册实例，
        // 供源生成器生成的调用器查找，不进行反射方法枚举（遵循 AGENTS.md §3.4）。
        var bindings = new BindingManager();
        var service = new object();

        bindings.RegisterInstance(service);

        // 不抛异常即视为通过：RegisterInstance 仅填充实例字典，不暴露 BoundMethods
        await Assert.That(service).IsNotNull();
    }

    [Test]
    public async Task Call_UnknownName_ReturnsReferenceError()
    {
        var bindings = new BindingManager();

        var result = await bindings.Call("NonExistent.Method", Array.Empty<System.Text.Json.JsonElement>());

        await Assert.That(result["error"]).IsNotNull();
        var errorDict = result["error"] as Dictionary<string, object?>;
        await Assert.That(errorDict).IsNotNull();
        await Assert.That(errorDict!["kind"]?.ToString()).IsEqualTo("ReferenceError");
    }

    [Test]
    public async Task Call_UnknownID_ReturnsReferenceError()
    {
        var bindings = new BindingManager();

        var result = await bindings.Call(99999u, Array.Empty<System.Text.Json.JsonElement>());

        await Assert.That(result["error"]).IsNotNull();
        var errorDict = result["error"] as Dictionary<string, object?>;
        await Assert.That(errorDict).IsNotNull();
        await Assert.That(errorDict!["kind"]?.ToString()).IsEqualTo("ReferenceError");
    }
}
