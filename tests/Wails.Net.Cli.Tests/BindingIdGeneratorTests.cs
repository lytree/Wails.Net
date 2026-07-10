using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using Wails.Net.Application.Bindings;
using Wails.Net.Generator;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// 绑定 ID 生成器单元测试。
/// 验证 FNV-1a 32 位哈希算法的正确性和与运行时 BindingManager 的一致性。
/// </summary>
[NotInParallel]
public sealed class BindingIdGeneratorTests
{
    [Test]
    public async Task Generate_EmptyString_ReturnsOffsetBasis()
    {
        var hash = BindingIdGenerator.Generate(string.Empty);
        await Assert.That(hash).IsEqualTo(BindingIdGenerator.OffsetBasis);
        await Assert.That(hash).IsEqualTo(2166136261u);
    }

    [Test]
    public async Task Generate_SameString_ReturnsSameHash()
    {
        var hash1 = BindingIdGenerator.Generate("Wails.Net.Application.Tests.Service.GetName");
        var hash2 = BindingIdGenerator.Generate("Wails.Net.Application.Tests.Service.GetName");
        await Assert.That(hash1).IsEqualTo(hash2);
    }

    [Test]
    public async Task Generate_DifferentStrings_ReturnDifferentHashes()
    {
        var hash1 = BindingIdGenerator.Generate("methodA");
        var hash2 = BindingIdGenerator.Generate("methodB");
        await Assert.That(hash1).IsNotEqualTo(hash2);
    }

    [Test]
    public async Task Generate_MatchesRuntimeBindingManager_ForKnownFullName()
    {
        var fullName = "Wails.Net.Application.Tests.TestService.GetName";
        var generatorHash = BindingIdGenerator.Generate(fullName);
        var runtimeHash = BindingManager.FNV1aHash(fullName);
        await Assert.That(generatorHash).IsEqualTo(runtimeHash);
    }

    [Test]
    public async Task Generate_HandlesUnicodeCorrectly()
    {
        // Unicode 字符应通过 UTF-8 编码后参与计算
        var hash = BindingIdGenerator.Generate("方法名");
        var runtimeHash = BindingManager.FNV1aHash("方法名");
        await Assert.That(hash).IsEqualTo(runtimeHash);
        await Assert.That(hash).IsNotEqualTo(0u);
    }

    [Test]
    public async Task GetFullName_CombinesPartsCorrectly()
    {
        var fullName = BindingIdGenerator.GetFullName("My.Namespace", "MyClass", "MyMethod");
        await Assert.That(fullName).IsEqualTo("My.Namespace.MyClass.MyMethod");
    }

    [Test]
    public async Task GetFullName_WithEmptyNamespace_ReturnsClassAndMethod()
    {
        var fullName = BindingIdGenerator.GetFullName(string.Empty, "MyClass", "MyMethod");
        await Assert.That(fullName).IsEqualTo(".MyClass.MyMethod");
    }

    [Test]
    public async Task GenerateFromParts_EqualsGenerateOfFullName()
    {
        const string ns = "Wails.Net.Services";
        const string cls = "GreetingService";
        const string method = "Hello";

        var fromParts = BindingIdGenerator.GenerateFromParts(ns, cls, method);
        var fromFullName = BindingIdGenerator.Generate(BindingIdGenerator.GetFullName(ns, cls, method));

        await Assert.That(fromParts).IsEqualTo(fromFullName);
    }

    [Test]
    public async Task OffsetBasis_HasStandardValue()
    {
        var value = BindingIdGenerator.OffsetBasis;
        await Assert.That(value).IsEqualTo(2166136261u);
    }

    [Test]
    public async Task Prime_HasStandardValue()
    {
        var value = BindingIdGenerator.Prime;
        await Assert.That(value).IsEqualTo(16777619u);
    }
}
