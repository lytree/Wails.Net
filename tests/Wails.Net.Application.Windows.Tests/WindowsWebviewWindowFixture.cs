using System.Threading;
using Wails.Net.Application.GuiContract.Tests;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;

namespace Wails.Net.Application.Windows.Tests;

/// <summary>
/// Windows 平台的 <see cref="IWebviewWindowFixture"/> 实现。
/// <para>
/// 通过 STA 线程承载 Win32 窗口创建与操作（Win32 窗口要求单线程单元），
/// 保证 <see cref="Win32WebviewWindow"/> 的窗口过程在正确的线程上下文中执行。
/// </para>
/// <para>
/// 与 <see cref="Win32WebviewWindowTests"/> 保持一致：每次窗口操作在独立的 STA 线程上执行，
/// 构造函数内部 fire-and-forget 的 WebView2 初始化在测试断言时不阻塞。
/// </para>
/// <para>
/// 本 fixture 假设运行环境具备真实 GUI（Windows 桌面会话），
/// 因此 <see cref="HasRealGuiEnvironment"/> 返回 true，L1/L2 契约均会执行。
/// </para>
/// </summary>
public sealed class WindowsWebviewWindowFixture : IWebviewWindowFixture
{
    /// <inheritdoc />
    public string PlatformName => "windows";

    /// <inheritdoc />
    public bool HasRealGuiEnvironment => OperatingSystem.IsWindows();

    /// <inheritdoc />
    public IWebviewWindowImpl CreateWindow(uint id, WebviewWindowOptions options)
    {
        // Win32 窗口必须在 STA 线程上创建，否则窗口类注册与消息循环无法正常工作。
        return RunOnSTAThread(() => new Win32WebviewWindow(id, options));
    }

    /// <inheritdoc />
    public void DestroyWindow(IWebviewWindowImpl window)
    {
        // 关闭与释放也必须在 STA 线程上执行，避免跨线程访问 HWND。
        RunOnSTAThread(() =>
        {
            try { window.Close(); }
            catch { /* 销毁阶段忽略异常，确保幂等 */ }
            if (window is IDisposable disposable)
            {
                disposable.Dispose();
            }
        });
    }

    /// <inheritdoc />
    public void RunOnUiThread(Action action)
    {
        RunOnSTAThread(action);
    }

    /// <inheritdoc />
    public T RunOnUiThread<T>(Func<T> func)
    {
        return RunOnSTAThread(func);
    }

    /// <summary>
    /// 在 STA 线程上执行指定操作并等待完成。
    /// <para>
    /// Win32 窗口创建、销毁与窗口过程调用均要求在 STA 线程上执行，
    /// 否则 <c>CreateWindowEx</c> 等 API 会失败或消息循环无法工作。
    /// </para>
    /// </summary>
    /// <param name="action">要执行的操作。</param>
    private static void RunOnSTAThread(Action action)
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { caught = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (caught is not null) throw caught;
    }

    /// <summary>
    /// 在 STA 线程上执行指定函数并返回结果。
    /// </summary>
    /// <typeparam name="T">返回类型。</typeparam>
    /// <param name="func">要执行的函数。</param>
    /// <returns>函数返回值。</returns>
    private static T RunOnSTAThread<T>(Func<T> func)
    {
        T result = default!;
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try { result = func(); }
            catch (Exception ex) { caught = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (caught is not null) throw caught;
        return result;
    }
}

/// <summary>
/// Windows 平台的契约测试执行器。
/// <para>
/// 继承 <see cref="WebviewWindowContractTests"/> 基类的全部契约测试方法，
/// 通过 <see cref="WindowsWebviewWindowFixture"/> 提供 Windows 平台实例。
/// TUnit 会通过 <see cref="InheritsTestsAttribute"/> 自动发现并执行本类继承的所有 [Test] 方法。
/// </para>
/// </summary>
[NotInParallel]
[InheritsTests]
public sealed class WindowsWebviewWindowContractTests : WebviewWindowContractTests
{
    private static readonly WindowsWebviewWindowFixture _fixture = new();

    /// <inheritdoc />
    protected override IWebviewWindowFixture GetFixture() => _fixture;
}
