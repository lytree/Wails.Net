using TUnit.Core;
using Wails.Net.Application.Schemes;

namespace Wails.Net.Application.Tests.Schemes;

/// <summary>
/// <see cref="CustomSchemeRegistry"/> 的单元测试（TUnit）。
/// 对应 RH-HOST-02：scheme 表从 <see cref="Application"/> 抽出后，语义（不区分大小写、
/// 空 scheme 返回 false、显式异常类型）必须与抽取前一致。
/// </summary>
public sealed class CustomSchemeRegistryTests
{
    /// <summary>
    /// 测试用的最小 AssetServer 子类。
    /// </summary>
    private sealed class StubAssetServer : Wails.Net.AssetServer.AssetServer
    {
        public StubAssetServer()
            : base(new Wails.Net.AssetServer.AssetOptions { Handler = "stub" })
        {
        }

        protected override byte[]? ReadAssetCore(string path) => null;
    }

    [Test]
    public async Task Register_ThenTryGet_ReturnsSameInstance()
    {
        var registry = new CustomSchemeRegistry();
        var server = new StubAssetServer();

        registry.Register("myapp", server);

        await Assert.That(registry.IsRegistered("myapp")).IsTrue();
        await Assert.That(registry.TryGetAssetServer("myapp", out var retrieved)).IsTrue();
        await Assert.That(retrieved).IsSameReferenceAs(server);
    }

    [Test]
    public async Task Scheme_IsCaseInsensitive()
    {
        var registry = new CustomSchemeRegistry();
        var server = new StubAssetServer();

        registry.Register("MyApp", server);

        await Assert.That(registry.IsRegistered("myapp")).IsTrue();
        await Assert.That(registry.TryGetAssetServer("MYAPP", out var retrieved)).IsTrue();
        await Assert.That(retrieved).IsSameReferenceAs(server);
    }

    [Test]
    public async Task Register_SameSchemeTwice_Overwrites()
    {
        var registry = new CustomSchemeRegistry();
        var first = new StubAssetServer();
        var second = new StubAssetServer();

        registry.Register("myapp", first);
        registry.Register("myapp", second);

        await Assert.That(registry.TryGetAssetServer("myapp", out var retrieved)).IsTrue();
        await Assert.That(retrieved).IsSameReferenceAs(second);
        await Assert.That(registry.RegisteredSchemes.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Register_NullAssetServer_Throws()
    {
        var registry = new CustomSchemeRegistry();

        await Assert.That(() => registry.Register("myapp", null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Register_EmptyScheme_Throws()
    {
        var registry = new CustomSchemeRegistry();

        await Assert.That(() => registry.Register(string.Empty, new StubAssetServer()))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Unregister_RemovesEntry_AndReturnsFalseForUnknown()
    {
        var registry = new CustomSchemeRegistry();
        registry.Register("myapp", new StubAssetServer());

        await Assert.That(registry.Unregister("myapp")).IsTrue();
        await Assert.That(registry.Unregister("myapp")).IsFalse();
        await Assert.That(registry.Unregister(string.Empty)).IsFalse();
        await Assert.That(registry.IsRegistered("myapp")).IsFalse();
    }

    [Test]
    public async Task TryGetAssetServer_EmptyScheme_ReturnsFalse()
    {
        var registry = new CustomSchemeRegistry();

        await Assert.That(registry.TryGetAssetServer(string.Empty, out var server)).IsFalse();
        await Assert.That(server).IsNull();
    }

    [Test]
    public async Task RegisteredSchemes_IsSnapshot_NotLiveView()
    {
        var registry = new CustomSchemeRegistry();
        registry.Register("a", new StubAssetServer());

        var snapshot = registry.RegisteredSchemes;
        registry.Register("b", new StubAssetServer());

        await Assert.That(snapshot.Count).IsEqualTo(1);
        await Assert.That(registry.RegisteredSchemes.Count).IsEqualTo(2);
    }
}
