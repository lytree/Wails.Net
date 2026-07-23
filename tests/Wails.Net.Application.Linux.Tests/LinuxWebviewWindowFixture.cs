using Wails.Net.Application.GuiContract.Tests;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Linux.Tests;

/// <summary>
/// Linux 平台的 <see cref="IWebviewWindowFixture"/> 实现。
/// <para>
/// 通过 <see cref="LinuxWebviewWindow"/> 创建 GTK4 + WebKitGTK 窗口。
/// <see cref="LinuxWebviewWindow"/> 构造函数会自动调用
/// <see cref="LinuxPlatformApp.EnsureGtkInitialized"/> 完成 GTK 初始化。
/// </para>
/// <para>
/// <see cref="HasRealGuiEnvironment"/> 通过检测 <c>DISPLAY</c> 或 <c>WAYLAND_DISPLAY</c>
/// 环境变量判断是否具备真实 GUI 环境。无 GUI 环境时仅 L1 契约执行，L2 契约跳过。
/// </para>
/// <para>
/// <see cref="RunOnUiThread(Action)"/> 在当前线程同步执行。
/// LinuxPlatformApp 在非 Linux 环境下也采用同步回退策略，
/// 本 fixture 与之保持一致，避免引入额外的 GLib.MainContext 调度复杂度。
/// </para>
/// </summary>
public sealed class LinuxWebviewWindowFixture : IWebviewWindowFixture
{
    /// <inheritdoc />
    public string PlatformName => "linux";

    /// <inheritdoc />
    public bool HasRealGuiEnvironment =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) ||
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

    /// <inheritdoc />
    public IWebviewWindowImpl CreateWindow(uint id, WebviewWindowOptions options)
    {
        // LinuxWebviewWindow 构造函数内部会调用 EnsureGtkInitialized。
        // 若运行环境缺少 GTK4 原生库或 DISPLAY，构造会抛异常，
        // 由调用方（契约测试基类）通过 try/catch 捕获并标记失败。
        return new LinuxWebviewWindow(id, options);
    }

    /// <inheritdoc />
    public void DestroyWindow(IWebviewWindowImpl window)
    {
        try { window.Close(); }
        catch { /* 销毁阶段忽略异常，确保幂等 */ }
        if (window is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    /// <inheritdoc />
    public void RunOnUiThread(Action action)
    {
        // 简化策略：在当前线程同步执行。
        // 真实 GTK 主循环场景下应通过 GLib.Functions.IdleAdd 调度，
        // 但测试套件不启动 MainLoop，直接同步执行更简单且无死锁风险。
        action();
    }

    /// <inheritdoc />
    public T RunOnUiThread<T>(Func<T> func)
    {
        return func();
    }
}

/// <summary>
/// Linux 平台的契约测试执行器。
/// <para>
/// 继承 <see cref="WebviewWindowContractTests"/> 基类的全部契约测试方法，
/// 通过 <see cref="LinuxWebviewWindowFixture"/> 提供 Linux 平台实例。
/// TUnit 会通过 <see cref="InheritsTestsAttribute"/> 自动发现并执行本类继承的所有 [Test] 方法。
/// </para>
/// </summary>
[NotInParallel]
[InheritsTests]
public sealed class LinuxWebviewWindowContractTests : WebviewWindowContractTests
{
    private static readonly LinuxWebviewWindowFixture _fixture = new();

    /// <inheritdoc />
    protected override IWebviewWindowFixture GetFixture() => _fixture;
}
