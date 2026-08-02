using Wails.Net.Application.GuiContract.Tests;
using Wails.Net.Application.Options;
using Wails.Net.Application.Windows;
using Wails.Net.Testing.Platform;

namespace Wails.Net.Testing.Tests;

/// <summary>
/// Mock 平台的 <see cref="IWebviewWindowFixture"/> 实现，使 <see cref="MockWebviewWindow"/>
/// 作为第 4 个平台参与 <see cref="WebviewWindowContractTests"/> 跨平台一致性契约测试。
/// <para>
/// 与 Windows / Linux / Android 三平台夹具的核心区别：Mock 窗口是内存态测试替身，
/// <see cref="HasRealGuiEnvironment"/> 返回 <c>true</c> —— 即 L1（不抛异常）与 L2（Set → Get
/// 往返一致）全部契约都应通过。这样"必须有真实 GUI"的窗口契约测试得以在 CI 中稳定跑通，
/// 无需 xvfb / DISPLAY / Android 模拟器。对标 Tauri v2 的 MockRuntime 窗口替身。
/// </para>
/// <para>
/// <see cref="RunOnUiThread"/> 直接同步执行：Mock 窗口不依赖任何真实 UI 线程/消息循环，
/// 所有状态变更都在内存中完成，同步执行既简单又无死锁风险。
/// </para>
/// </summary>
public sealed class MockWebviewWindowFixture : IWebviewWindowFixture
{
    /// <inheritdoc />
    public string PlatformName => "mock";

    /// <inheritdoc />
    /// <remarks>
    /// Mock 窗口是内存态测试替身，<b>始终具备可观察的真实状态</b>，因此返回 true：
    /// 所有 L1 与 L2 契约都必须通过（区别于 Android 夹具恒为 false 仅跑 L1）。
    /// 这正是把 Mock 接入 GuiContract 作为第 4 平台的价值所在。
    /// </remarks>
    public bool HasRealGuiEnvironment => true;

    /// <inheritdoc />
    public IWebviewWindowImpl CreateWindow(uint id, WebviewWindowOptions options)
    {
        // 直接构造内存态 Mock 窗口；不依赖 MockPlatformApp，纯窗口契约测试可独立运行。
        return new MockWebviewWindow(id, options);
    }

    /// <inheritdoc />
    public void DestroyWindow(IWebviewWindowImpl window)
    {
        if (window is MockWebviewWindow mockWindow)
        {
            mockWindow.Dispose();
            return;
        }

        if (window is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    /// <inheritdoc />
    public void RunOnUiThread(Action action) => action();

    /// <inheritdoc />
    public T RunOnUiThread<T>(Func<T> func) => func();
}

/// <summary>
/// Mock 平台的契约测试执行器。
/// <para>
/// 继承 <see cref="WebviewWindowContractTests"/> 基类的全部契约测试方法，
/// 通过 <see cref="MockWebviewWindowFixture"/> 提供 Mock 平台实例。
/// TUnit 通过 <see cref="InheritsTestsAttribute"/> 自动发现并执行本类继承的所有 [Test] 方法。
/// </para>
/// </summary>
[NotInParallel]
[InheritsTests]
public sealed class MockWebviewWindowContractTests : WebviewWindowContractTests
{
    private static readonly MockWebviewWindowFixture _fixture = new();

    /// <inheritdoc />
    protected override IWebviewWindowFixture GetFixture() => _fixture;
}
