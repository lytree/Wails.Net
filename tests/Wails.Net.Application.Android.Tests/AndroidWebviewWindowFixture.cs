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
/// 说明：本测试工程以普通 TUnit 宿主的形态运行在 <c>net10.0-android36.0</c>，
/// 未声明 <c>ApplicationId</c>/<c>AndroidApplication</c>，也没有 <c>MainActivity</c> 与
/// <c>AndroidManifest.xml</c>，因此进程内不存在可承载 <c>SurfaceView</c> 的 Activity
/// （<c>AndroidPlatformApp.GetActivity()</c> 恒为 null）。
/// </para>
/// <para>
/// 因此本执行器不具备真实 GUI 能力：<see cref="AndroidWebviewWindow"/> 构造仅缓存参数，
/// 真正创建 WebView 需在 <c>Show()</c> 时附加到 Activity 视图层级；缺少 Activity 时只可能回退到
/// <c>Application.Context</c>，无法产生可见窗口，故 L2/L3 以及依赖窗口实例的 L1 契约均不可执行。
/// </para>
/// <para>
/// 基类 <c>CreateWindow</c> 在
/// <see cref="HasRealGuiEnvironment"/> 为 false 时抛 <c>SkipTestException</c>，
/// 所以契约用例中仅"创建窗口前 <c>if (!HasRealGuiEnvironment) return;</c> 早退"的少量用例会真实执行，
/// 其余全部标记为跳过——这是能力边界，不是验证通过。
/// </para>
/// <para>
/// 设备端（真实 Activity + WebView + IPC）验证由
/// <c>tests/Wails.Net.Android.E2E/run-android-e2e.fsx</c> 覆盖；补齐可部署测试宿主属后续工作，
/// 决策理由见 <c>docs/development/wails-v3-beta-comparison.md</c> §12.5。
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

    /// <summary>
    /// 当前执行器是否具备真实 GUI 环境。
    /// <para>
    /// 恒为 <c>false</c>：测试宿主没有 Activity/Manifest，无法创建可见 WebView。
    /// 此值必须保持 false，不能用 true 伪装成可执行，否则契约测试会在缺少 Activity 时失败或挂起。
    /// </para>
    /// </summary>
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
/// <para>
/// 契约测试针对 Android 原生 WebView 窗口，属于 GUI 相关测试，默认禁用
/// （设置环境变量 <c>WAILS_RUN_GUI_TESTS=1</c> 后启用）。
/// </para>
/// </summary>
[NotInParallel]
[InheritsTests]
[RequiresGuiEnvironment]
public sealed class AndroidWebviewWindowContractTests : WebviewWindowContractTests
{
    private static readonly AndroidWebviewWindowFixture _fixture = new();

    /// <inheritdoc />
    protected override IWebviewWindowFixture GetFixture() => _fixture;
}
