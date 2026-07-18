using TUnit.Core;
using Wails.Net.Application.Browser;
using Wails.Net.Application.Managers;
using Wails.Net.Application.Platform.ServerMode;

namespace Wails.Net.Application.Tests.Browser;

/// <summary>
/// <see cref="ServerBrowserManager"/> 单元测试（TUnit）。
/// Server 模式为 no-op 桩：不打开浏览器，但应接受任何输入而不抛异常。
/// 验证与 <see cref="IBrowserManager"/> 接口契约的一致性。
/// </summary>
[NotInParallel]
public sealed class ServerBrowserManagerTests
{
    // ---------------------------------------------------------------------
    // OpenURL（no-op 桩）
    // ---------------------------------------------------------------------

    [Test]
    public async Task OpenURL_ValidUrl_DoesNotThrow()
    {
        var manager = new ServerBrowserManager();

        void Act() => manager.OpenURL("https://example.com/");

        await Assert.That(() => Act()).ThrowsNothing();
    }

    [Test]
    public async Task OpenURL_InvalidUrl_DoesNotThrow()
    {
        // Server 模式下不应因 URL 无效抛异常（仅静默验证）
        var manager = new ServerBrowserManager();

        void Act() => manager.OpenURL("javascript:alert(1)");

        await Assert.That(() => Act()).ThrowsNothing();
    }

    [Test]
    public async Task OpenURL_NullUrl_DoesNotThrow()
    {
        var manager = new ServerBrowserManager();

        void Act() => manager.OpenURL(null!);

        await Assert.That(() => Act()).ThrowsNothing();
    }

    [Test]
    public async Task OpenURL_EmptyUrl_DoesNotThrow()
    {
        var manager = new ServerBrowserManager();

        void Act() => manager.OpenURL(string.Empty);

        await Assert.That(() => Act()).ThrowsNothing();
    }

    // ---------------------------------------------------------------------
    // OpenURLInDefaultBrowser（与 OpenURL 行为一致）
    // ---------------------------------------------------------------------

    [Test]
    public async Task OpenURLInDefaultBrowser_ValidUrl_DoesNotThrow()
    {
        var manager = new ServerBrowserManager();

        void Act() => manager.OpenURLInDefaultBrowser("https://example.com/");

        await Assert.That(() => Act()).ThrowsNothing();
    }

    [Test]
    public async Task OpenURLInDefaultBrowser_InvalidUrl_DoesNotThrow()
    {
        var manager = new ServerBrowserManager();

        void Act() => manager.OpenURLInDefaultBrowser("file:///etc/passwd");

        await Assert.That(() => Act()).ThrowsNothing();
    }

    [Test]
    public async Task OpenURLInDefaultBrowser_NullUrl_DoesNotThrow()
    {
        var manager = new ServerBrowserManager();

        void Act() => manager.OpenURLInDefaultBrowser(null!);

        await Assert.That(() => Act()).ThrowsNothing();
    }

    // ---------------------------------------------------------------------
    // 接口契约
    // ---------------------------------------------------------------------

    [Test]
    public async Task ServerBrowserManager_ImplementsIBrowserManager()
    {
        var manager = new ServerBrowserManager();

        await Assert.That(manager).IsAssignableTo<IBrowserManager>();
    }

    [Test]
    public async Task OpenURL_AndOpenURLInDefaultBrowser_BehaviorEquivalent()
    {
        // Server 模式下两者均 no-op，调用顺序不应影响行为
        var manager = new ServerBrowserManager();

        void Open1() => manager.OpenURL("https://example.com/");
        void Open2() => manager.OpenURLInDefaultBrowser("https://example.com/");

        await Assert.That(() => Open1()).ThrowsNothing();
        await Assert.That(() => Open2()).ThrowsNothing();
    }
}
