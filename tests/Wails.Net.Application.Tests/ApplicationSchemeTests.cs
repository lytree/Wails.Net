using TUnit.Core;
using Wails.Net.Application.Options;

namespace Wails.Net.Application.Tests;

/// <summary>
/// Application 自定义 URI scheme 注册 API 的单元测试（TUnit）。
/// 对应主题 F-5：添加 Application.RegisterScheme 公共 API。
/// 注意：Application 构造函数会设置静态全局实例，因此此类中的测试不并行执行。
/// </summary>
[NotInParallel]
public sealed class ApplicationSchemeTests
{
    /// <summary>
    /// 测试用的最小 AssetServer 子类，便于注册到 scheme。
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
    public async Task RegisterScheme_WithExplicitAssetServer_RegistersAndCanBeRetrieved()
    {
        var app = new Application(new ApplicationOptions());
        var server = new StubAssetServer();

        app.RegisterScheme("myapp", server);

        await Assert.That(app.IsSchemeRegistered("myapp")).IsTrue();
        await Assert.That(app.TryGetSchemeAssetServer("myapp", out var retrieved)).IsTrue();
        await Assert.That(retrieved).IsSameReferenceAs(server);
    }

    [Test]
    public async Task RegisterScheme_UsesApplicationAssetServer_WhenNotProvided()
    {
        var app = new Application(new ApplicationOptions());
        var server = new StubAssetServer();
        app.AssetServer = server;

        app.RegisterScheme("default");

        await Assert.That(app.TryGetSchemeAssetServer("default", out var retrieved)).IsTrue();
        await Assert.That(retrieved).IsSameReferenceAs(server);
    }

    [Test]
    public async Task RegisterScheme_WithoutAssetServer_ThrowsInvalidOperationException()
    {
        var app = new Application(new ApplicationOptions());

        await Assert.That(() => app.RegisterScheme("orphan")).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task RegisterScheme_NullScheme_ThrowsArgumentNullException()
    {
        var app = new Application(new ApplicationOptions());
        var server = new StubAssetServer();

        await Assert.That(() => app.RegisterScheme(null!, server)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task RegisterScheme_EmptyScheme_ThrowsArgumentException()
    {
        var app = new Application(new ApplicationOptions());
        var server = new StubAssetServer();

        await Assert.That(() => app.RegisterScheme("", server)).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task RegisterScheme_OverwritesExistingRegistration()
    {
        var app = new Application(new ApplicationOptions());
        var first = new StubAssetServer();
        var second = new StubAssetServer();

        app.RegisterScheme("dup", first);
        app.RegisterScheme("dup", second);

        await Assert.That(app.TryGetSchemeAssetServer("dup", out var retrieved)).IsTrue();
        await Assert.That(retrieved).IsSameReferenceAs(second);
    }

    [Test]
    public async Task RegisterScheme_IsCaseInsensitive()
    {
        var app = new Application(new ApplicationOptions());
        var server = new StubAssetServer();

        app.RegisterScheme("MyApp", server);

        await Assert.That(app.IsSchemeRegistered("myapp")).IsTrue();
        await Assert.That(app.IsSchemeRegistered("MYAPP")).IsTrue();
        await Assert.That(app.TryGetSchemeAssetServer("myapp", out _)).IsTrue();
        await Assert.That(app.TryGetSchemeAssetServer("MyApp", out _)).IsTrue();
    }

    [Test]
    public async Task UnregisterScheme_ExistingScheme_ReturnsTrueAndRemoves()
    {
        var app = new Application(new ApplicationOptions());
        var server = new StubAssetServer();
        app.RegisterScheme("temp", server);

        var result = app.UnregisterScheme("temp");

        await Assert.That(result).IsTrue();
        await Assert.That(app.IsSchemeRegistered("temp")).IsFalse();
    }

    [Test]
    public async Task UnregisterScheme_NonExistingScheme_ReturnsFalse()
    {
        var app = new Application(new ApplicationOptions());

        var result = app.UnregisterScheme("nonexistent");

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task UnregisterScheme_EmptyScheme_ReturnsFalse()
    {
        var app = new Application(new ApplicationOptions());

        var result = app.UnregisterScheme("");

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsSchemeRegistered_NotRegistered_ReturnsFalse()
    {
        var app = new Application(new ApplicationOptions());

        await Assert.That(app.IsSchemeRegistered("unknown")).IsFalse();
    }

    [Test]
    public async Task IsSchemeRegistered_EmptyScheme_ReturnsFalse()
    {
        var app = new Application(new ApplicationOptions());

        await Assert.That(app.IsSchemeRegistered("")).IsFalse();
    }

    [Test]
    public async Task TryGetSchemeAssetServer_NotRegistered_ReturnsFalse()
    {
        var app = new Application(new ApplicationOptions());

        var result = app.TryGetSchemeAssetServer("missing", out var retrieved);

        await Assert.That(result).IsFalse();
        await Assert.That(retrieved).IsNull();
    }

    [Test]
    public async Task TryGetSchemeAssetServer_EmptyScheme_ReturnsFalse()
    {
        var app = new Application(new ApplicationOptions());

        var result = app.TryGetSchemeAssetServer("", out var retrieved);

        await Assert.That(result).IsFalse();
        await Assert.That(retrieved).IsNull();
    }

    [Test]
    public async Task RegisteredSchemes_EmptyInitially_ReturnsEmptyList()
    {
        var app = new Application(new ApplicationOptions());

        await Assert.That(app.RegisteredSchemes.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RegisteredSchemes_AfterMultipleRegistrations_ReturnsAllSchemes()
    {
        var app = new Application(new ApplicationOptions());
        var server = new StubAssetServer();

        app.RegisterScheme("alpha", server);
        app.RegisterScheme("beta", server);
        app.RegisterScheme("gamma", server);

        await Assert.That(app.RegisteredSchemes.Count).IsEqualTo(3);
        await Assert.That(app.RegisteredSchemes).Contains("alpha");
        await Assert.That(app.RegisteredSchemes).Contains("beta");
        await Assert.That(app.RegisteredSchemes).Contains("gamma");
    }

    [Test]
    public async Task RegisteredSchemes_AfterUnregister_ReturnsRemainingSchemes()
    {
        var app = new Application(new ApplicationOptions());
        var server = new StubAssetServer();
        app.RegisterScheme("keep", server);
        app.RegisterScheme("remove", server);

        app.UnregisterScheme("remove");

        await Assert.That(app.RegisteredSchemes.Count).IsEqualTo(1);
        await Assert.That(app.RegisteredSchemes).Contains("keep");
    }
}
