using Wails.Net.Application.Android;
using Wails.Net.Application.GuiContract.Tests;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Android.Tests;

/// <summary>
/// Android 平台的 <see cref="IWebviewWindowFixture"/> 实现。
/// <para>
/// 通过 <see cref="AndroidWebviewWindow"/> 创建基于 <c>Android.Webkit.WebView</c> 的窗口。
/// 由于单元测试运行在 .NET Android 工作负载下而非真实 Android 设备，
/// <c>AndroidPlatformApp.GetActivity</c> 返回 null，
/// <c>AndroidWebviewWindow.CreateWebView</c> 会回退到 <c>Application.Context</c> 路径，
/// 验证逻辑可执行但 WebView 不可见。
/// </para>
/// <para>
/// <see cref="HasRealGuiEnvironment"/> 始终返回 false：
/// 单元测试环境无真实 Activity 与 Surface，无法验证 L2 状态变化契约。
/// 仅 L1 通用契约（方法不抛异常、返回类型正确）会被执行。
/// </para>
/// <para>
/// <see cref="RunOnUiThread(Action)"/> 调用
/// <see cref="AndroidPlatformApp.DispatchOnMainThread(System.Action)"/>，
/// 在测试环境（无 MainLooper）下回退为同步执行。
/// </para>
/// </summary>
public sealed class AndroidWebviewWindowFixture : IWebviewWindowFixture
{
    /// <summary>
    /// 共享的 AndroidPlatformApp 实例。
    /// <para>
    /// AndroidPlatformApp 在构造时会读取 Looper.MainLooper 与当前线程 ID，
    /// 复用同一实例避免重复初始化与潜在的线程身份判断异常。
    /// </para>
    /// </summary>
    private static readonly AndroidPlatformApp _app = new(new ApplicationOptions { Name = "ContractTestApp" });

    /// <inheritdoc />
    public string PlatformName => "android";

    /// <inheritdoc />
    public bool HasRealGuiEnvironment => false;

    /// <inheritdoc />
    public IWebviewWindowImpl CreateWindow(uint id, WebviewWindowOptions options)
    {
        // AndroidWebviewWindow 构造函数仅缓存 options 中的几何参数与 URL，
        // 不立即创建 WebView（延迟到 Show() 时创建）。
        // 因此构造不会因缺少 Activity 而失败。
        return new AndroidWebviewWindow(id, options, _app);
    }

    /// <inheritdoc />
    public void DestroyWindow(IWebviewWindowImpl window)
    {
        try { window.Close(); }
        catch { /* 销毁阶段忽略异常，确保幂等 */ }
    }

    /// <inheritdoc />
    public void RunOnUiThread(Action action)
    {
        // 委托给 AndroidPlatformApp.DispatchOnMainThread。
        // 测试环境（无 MainLooper）下回退为同步执行。
        _app.DispatchOnMainThread(action);
    }

    /// <inheritdoc />
    public T RunOnUiThread<T>(Func<T> func)
    {
        T result = default!;
        _app.DispatchOnMainThread(() => result = func());
        return result;
    }
}

/// <summary>
/// Android 平台的契约测试执行器。
/// <para>
/// 继承 <see cref="WebviewWindowContractTests"/> 基类的全部契约测试方法，
/// 通过 <see cref="AndroidWebviewWindowFixture"/> 提供 Android 平台实例。
/// TUnit 会通过 <see cref="InheritsTestsAttribute"/> 自动发现并执行本类继承的所有 [Test] 方法。
/// </para>
/// </summary>
[NotInParallel]
[InheritsTests]
public sealed class AndroidWebviewWindowContractTests : WebviewWindowContractTests
{
    private static readonly AndroidWebviewWindowFixture _fixture = new();

    /// <inheritdoc />
    protected override IWebviewWindowFixture GetFixture() => _fixture;
}
