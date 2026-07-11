using System.Threading;
using TUnit.Core;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;

namespace Wails.Net.Application.Windows.Tests;

/// <summary>
/// Win32WebviewWindow 的单元测试（TUnit）。
/// 测试 Win32 原生窗口创建、属性访问、基本窗口操作。
/// 注意：Win32 窗口创建需要 STA 线程，因此不并行执行。
/// </summary>
[NotInParallel]
public sealed class Win32WebviewWindowTests
{
    /// <summary>
    /// 在 STA 线程上执行指定操作，并等待完成。
    /// Win32 窗口创建需要 STA 线程。
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

    [Test]
    public async Task Constructor_CreatesWindow_WithValidId()
    {
        // 安排与操作：在 STA 线程创建窗口
        var window = RunOnSTAThread(() => new Win32WebviewWindow(42, new WebviewWindowOptions
        {
            Title = "测试窗口",
            Width = 640,
            Height = 480
        }));

        // 断言：ID 正确
        await Assert.That(window.Id).IsEqualTo((uint)42);

        // 清理
        RunOnSTAThread(() => window.Dispose());
    }

    [Test]
    public async Task IsClosed_ReturnsFalse_AfterConstruction()
    {
        // 安排与操作：在 STA 线程创建窗口
        var window = RunOnSTAThread(() => new Win32WebviewWindow(1, new WebviewWindowOptions
        {
            Title = "测试窗口"
        }));

        // 断言：刚创建时未关闭
        await Assert.That(window.IsClosed).IsFalse();

        // 清理
        RunOnSTAThread(() => window.Dispose());
    }

    [Test]
    public async Task IsClosed_ReturnsTrue_AfterClose()
    {
        // 安排：在 STA 线程创建窗口
        var window = RunOnSTAThread(() => new Win32WebviewWindow(1, new WebviewWindowOptions
        {
            Title = "测试窗口"
        }));

        // 操作：关闭窗口
        RunOnSTAThread(() => window.Close());

        // 断言：已关闭
        await Assert.That(window.IsClosed).IsTrue();
    }

    [Test]
    public async Task SetTitle_DoesNotThrow()
    {
        // 安排：在 STA 线程创建窗口
        var window = RunOnSTAThread(() => new Win32WebviewWindow(1, new WebviewWindowOptions
        {
            Title = "原标题"
        }));

        // 操作与断言：设置标题不抛出异常
        await Assert.That(() => RunOnSTAThread(() => window.SetTitle("新标题"))).ThrowsNothing();

        // 清理
        RunOnSTAThread(() => window.Dispose());
    }

    [Test]
    public async Task Show_DoesNotThrow()
    {
        // 安排：在 STA 线程创建窗口
        var window = RunOnSTAThread(() => new Win32WebviewWindow(1, new WebviewWindowOptions
        {
            Title = "测试窗口"
        }));

        // 操作与断言：显示窗口不抛出异常
        await Assert.That(() => RunOnSTAThread(() => window.Show())).ThrowsNothing();

        // 清理
        RunOnSTAThread(() => window.Dispose());
    }

    [Test]
    public async Task Hide_DoesNotThrow()
    {
        // 安排：在 STA 线程创建窗口
        var window = RunOnSTAThread(() => new Win32WebviewWindow(1, new WebviewWindowOptions
        {
            Title = "测试窗口"
        }));

        // 操作与断言：隐藏窗口不抛出异常
        await Assert.That(() => RunOnSTAThread(() => window.Hide())).ThrowsNothing();

        // 清理
        RunOnSTAThread(() => window.Dispose());
    }

    [Test]
    public async Task SetSize_DoesNotThrow()
    {
        // 安排：在 STA 线程创建窗口
        var window = RunOnSTAThread(() => new Win32WebviewWindow(1, new WebviewWindowOptions
        {
            Title = "测试窗口",
            Width = 400,
            Height = 300
        }));

        // 操作与断言：设置窗口大小不抛出异常
        await Assert.That(() => RunOnSTAThread(() => window.SetSize(800, 600))).ThrowsNothing();

        // 清理
        RunOnSTAThread(() => window.Dispose());
    }

    [Test]
    public async Task SetPosition_DoesNotThrow()
    {
        // 安排：在 STA 线程创建窗口
        var window = RunOnSTAThread(() => new Win32WebviewWindow(1, new WebviewWindowOptions
        {
            Title = "测试窗口"
        }));

        // 操作与断言：设置窗口位置不抛出异常
        await Assert.That(() => RunOnSTAThread(() => window.SetPosition(100, 100))).ThrowsNothing();

        // 清理
        RunOnSTAThread(() => window.Dispose());
    }

    [Test]
    public async Task Maximise_DoesNotThrow()
    {
        // 安排：在 STA 线程创建窗口
        var window = RunOnSTAThread(() => new Win32WebviewWindow(1, new WebviewWindowOptions
        {
            Title = "测试窗口"
        }));

        // 操作与断言：最大化不抛出异常
        await Assert.That(() => RunOnSTAThread(() => window.Maximise())).ThrowsNothing();

        // 清理
        RunOnSTAThread(() => window.Dispose());
    }

    [Test]
    public async Task Minimise_DoesNotThrow()
    {
        // 安排：在 STA 线程创建窗口
        var window = RunOnSTAThread(() => new Win32WebviewWindow(1, new WebviewWindowOptions
        {
            Title = "测试窗口"
        }));

        // 操作与断言：最小化不抛出异常
        await Assert.That(() => RunOnSTAThread(() => window.Minimise())).ThrowsNothing();

        // 清理
        RunOnSTAThread(() => window.Dispose());
    }

    [Test]
    public async Task Centre_DoesNotThrow()
    {
        // 安排：在 STA 线程创建窗口
        var window = RunOnSTAThread(() => new Win32WebviewWindow(1, new WebviewWindowOptions
        {
            Title = "测试窗口"
        }));

        // 操作与断言：居中窗口不抛出异常
        await Assert.That(() => RunOnSTAThread(() => window.Centre())).ThrowsNothing();

        // 清理
        RunOnSTAThread(() => window.Dispose());
    }

    [Test]
    public async Task GetSize_ReturnsValidValues_AfterShow()
    {
        // 安排：在 STA 线程创建窗口并显示
        var window = RunOnSTAThread(() =>
        {
            var w = new Win32WebviewWindow(1, new WebviewWindowOptions
            {
                Title = "测试窗口",
                Width = 640,
                Height = 480
            });
            w.Show();
            return w;
        });

        // 操作：获取窗口大小
        var (width, height) = RunOnSTAThread(() => window.GetSize());

        // 断言：返回正值（窗口已显示，Win32 窗口尺寸有效）
        await Assert.That(width).IsGreaterThan(0);
        await Assert.That(height).IsGreaterThan(0);

        // 清理
        RunOnSTAThread(() => window.Dispose());
    }

    [Test]
    public async Task GetPosition_ReturnsValidValues_AfterConstruction()
    {
        // 安排：在 STA 线程创建窗口
        var window = RunOnSTAThread(() => new Win32WebviewWindow(1, new WebviewWindowOptions
        {
            Title = "测试窗口"
        }));

        // 操作：获取窗口位置
        var (x, y) = RunOnSTAThread(() => window.GetPosition());

        // 断言：返回有效坐标（非负，在屏幕范围内）
        await Assert.That(x).IsGreaterThanOrEqualTo(0);
        await Assert.That(y).IsGreaterThanOrEqualTo(0);

        // 清理
        RunOnSTAThread(() => window.Dispose());
    }

    [Test]
    public async Task IsVisible_ReturnsBool()
    {
        // 安排：在 STA 线程创建窗口
        var window = RunOnSTAThread(() => new Win32WebviewWindow(1, new WebviewWindowOptions
        {
            Title = "测试窗口"
        }));

        // 操作与断言：返回布尔值
        await Assert.That(window.IsVisible()).IsTypeOf<bool>();

        // 清理
        RunOnSTAThread(() => window.Dispose());
    }

    [Test]
    public async Task SetAlwaysOnTop_DoesNotThrow()
    {
        // 安排：在 STA 线程创建窗口
        var window = RunOnSTAThread(() => new Win32WebviewWindow(1, new WebviewWindowOptions
        {
            Title = "测试窗口"
        }));

        // 操作与断言：设置置顶不抛出异常
        await Assert.That(() => RunOnSTAThread(() => window.SetAlwaysOnTop(true))).ThrowsNothing();

        // 清理
        RunOnSTAThread(() => window.Dispose());
    }

    [Test]
    public async Task SetEnabled_DoesNotThrow()
    {
        // 安排：在 STA 线程创建窗口
        var window = RunOnSTAThread(() => new Win32WebviewWindow(1, new WebviewWindowOptions
        {
            Title = "测试窗口"
        }));

        // 操作与断言：设置启用状态不抛出异常
        await Assert.That(() => RunOnSTAThread(() => window.SetEnabled(false))).ThrowsNothing();

        // 清理
        RunOnSTAThread(() => window.Dispose());
    }

    [Test]
    public async Task Dispose_CanBeCalledMultipleTimes()
    {
        // 安排：在 STA 线程创建窗口
        var window = RunOnSTAThread(() => new Win32WebviewWindow(1, new WebviewWindowOptions
        {
            Title = "测试窗口"
        }));

        // 操作与断言：多次 Dispose 不抛出异常
        await Assert.That(() =>
        {
            RunOnSTAThread(() => window.Dispose());
            RunOnSTAThread(() => window.Dispose());
        }).ThrowsNothing();
    }
}
