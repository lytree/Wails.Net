using System.Text.RegularExpressions;
using System.Threading;
using TUnit.Core;
using Wails.Net.Application.Dialogs;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;

namespace Wails.Net.Application.Windows.Tests;

/// <summary>
/// WindowsPlatformApp 的单元测试（TUnit）。
/// 测试线程身份判断、主题、强调色、标志位、对话框、屏幕信息等平台属性。
/// 注意：此类测试线程身份和 GUI 操作，因此不并行执行。
/// </summary>
[NotInParallel]
public sealed class WindowsPlatformAppTests
{
    /// <summary>
    /// 在 STA 线程上执行指定操作，并等待完成。
    /// WinForms 对话框需要 STA 线程。
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
    public async Task Constructor_SetsName()
    {
        // 安排与操作
        var app = new WindowsPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 断言
        await Assert.That(app.Name).IsEqualTo("TestApp");
    }

    [Test]
    public async Task IsOnMainThread_ReturnsTrue_WhenCalledFromSameThread()
    {
        // 安排：在当前线程构造
        var app = new WindowsPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作与断言：在相同线程调用应返回 true
        await Assert.That(app.IsOnMainThread()).IsTrue();
    }

    [Test]
    public async Task IsOnMainThread_ReturnsFalse_WhenCalledFromDifferentThread()
    {
        // 安排：在当前线程构造
        var app = new WindowsPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作：在不同线程调用
        var result = await Task.Run(() => app.IsOnMainThread());

        // 断言：不同线程应返回 false
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsDarkMode_ReturnsBool()
    {
        // 安排
        var app = new WindowsPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作与断言：返回布尔值（取决于系统主题设置）
        await Assert.That(app.IsDarkMode()).IsTypeOf<bool>();
    }

    [Test]
    public async Task GetAccentColor_ReturnsValidHexColor()
    {
        // 安排
        var app = new WindowsPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作
        var color = app.GetAccentColor();

        // 断言：匹配 #RRGGBB 格式
        await Assert.That(Regex.IsMatch(color, @"^#[0-9A-Fa-f]{6}$")).IsTrue();
    }

    [Test]
    public async Task GetFlags_ReturnsEmptyDictionary()
    {
        // 安排
        var app = new WindowsPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作
        var flags = app.GetFlags(new ApplicationOptions());

        // 断言：返回空字典
        await Assert.That(flags.Count).IsEqualTo(0);
    }

    [Test]
    public async Task GetPrimaryScreen_ReturnsNonNull()
    {
        // 安排
        var app = new WindowsPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作
        var screen = app.GetPrimaryScreen();

        // 断言：返回非 null 的主屏幕信息
        await Assert.That(screen).IsNotNull();
    }

    [Test]
    public async Task GetScreens_ReturnsNonEmptyArray()
    {
        // 安排
        var app = new WindowsPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作
        var screens = app.GetScreens();

        // 断言：至少返回一个屏幕
        await Assert.That(screens.Length).IsGreaterThan(0);
    }

    [Test]
    public async Task Run_ExitsGracefully_WhenDestroyCalled()
    {
        // 安排
        var app = new WindowsPlatformApp(new ApplicationOptions { Name = "TestApp" });
        Exception? caught = null;
        var completed = false;

        // 操作：在 STA 线程上启动消息循环，用 WinForms Timer 在同一线程调用 Destroy()
        // PostQuitMessage 投递到调用线程的消息队列，因此必须在同一线程调用
        var thread = new Thread(() =>
        {
            try
            {
                using var timer = new System.Windows.Forms.Timer { Interval = 200 };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    app.Destroy();
                };
                timer.Start();
                app.Run();
                completed = true;
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(10000);

        // 断言：消息循环正常退出，无异常
        await Assert.That(caught).IsNull();
        await Assert.That(completed).IsTrue();
    }

    [Test]
    public async Task Destroy_DoesNotThrow()
    {
        // 安排
        var app = new WindowsPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作与断言：Destroy 调用 PostQuitMessage 不抛出异常
        await Assert.That(() => app.Destroy()).ThrowsNothing();
    }

    [Test]
    public async Task ShowMessageDialog_ReturnsValidIndex()
    {
        // 安排
        var app = new WindowsPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作：在 STA 线程上显示消息对话框，用 Timer 自动发送回车键关闭
        var result = RunOnSTAThread<int>(() =>
        {
            using var timer = new System.Windows.Forms.Timer { Interval = 500 };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                System.Windows.Forms.SendKeys.SendWait("{ENTER}");
            };
            timer.Start();

            var task = app.ShowMessageDialog("标题", "消息", DialogStyle.Info, new[] { "确定" });
            return task.GetAwaiter().GetResult();
        });

        // 断言：返回有效的按钮索引（>= 0）
        await Assert.That(result).IsGreaterThanOrEqualTo(0);
    }

    [Test]
    public async Task OpenFileDialog_ReturnsNull_WhenCancelled()
    {
        // 安排
        var app = new WindowsPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作：在 STA 线程上打开文件对话框，用 Timer 自动发送 Esc 键取消
        var result = RunOnSTAThread<string?>(() =>
        {
            using var timer = new System.Windows.Forms.Timer { Interval = 500 };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                System.Windows.Forms.SendKeys.SendWait("{ESC}");
            };
            timer.Start();

            var task = app.OpenFileDialog(new OpenFileDialogOptions { Title = "测试" });
            return task.GetAwaiter().GetResult();
        });

        // 断言：取消时返回 null
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task SaveFileDialog_ReturnsNull_WhenCancelled()
    {
        // 安排
        var app = new WindowsPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作：在 STA 线程上打开保存对话框，用 Timer 自动发送 Esc 键取消
        var result = RunOnSTAThread<string?>(() =>
        {
            using var timer = new System.Windows.Forms.Timer { Interval = 500 };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                System.Windows.Forms.SendKeys.SendWait("{ESC}");
            };
            timer.Start();

            var task = app.SaveFileDialog(new SaveFileDialogOptions { Title = "测试" });
            return task.GetAwaiter().GetResult();
        });

        // 断言：取消时返回 null
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task OpenMultipleFilesDialog_ReturnsNull_WhenCancelled()
    {
        // 安排
        var app = new WindowsPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作：在 STA 线程上打开多文件对话框，用 Timer 自动发送 Esc 键取消
        var result = RunOnSTAThread<string[]?>(() =>
        {
            using var timer = new System.Windows.Forms.Timer { Interval = 500 };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                System.Windows.Forms.SendKeys.SendWait("{ESC}");
            };
            timer.Start();

            var task = app.OpenMultipleFilesDialog(new OpenFileDialogOptions { Title = "测试" });
            return task.GetAwaiter().GetResult();
        });

        // 断言：取消时返回 null
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ShowAboutDialog_DoesNotThrow()
    {
        // 安排
        var app = new WindowsPlatformApp(new ApplicationOptions
        {
            Name = "TestApp",
            Description = "测试应用",
            Version = "1.0.0"
        });

        // 操作与断言：在 STA 线程上显示关于对话框，用 Timer 自动关闭
        await Assert.That(() => RunOnSTAThread(() =>
        {
            using var timer = new System.Windows.Forms.Timer { Interval = 500 };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                System.Windows.Forms.SendKeys.SendWait("{ENTER}");
            };
            timer.Start();
            app.ShowAboutDialog("TestApp", "测试应用", null);
        })).ThrowsNothing();
    }

    [Test]
    public async Task SetIcon_DoesNotThrow_WithNullIcon()
    {
        // 安排
        var app = new WindowsPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作与断言：SetIcon 接受 null 不抛出异常
        await Assert.That(() => app.SetIcon(null)).ThrowsNothing();
    }

    [Test]
    public async Task Hide_DoesNotThrow_WithNoWindows()
    {
        // 安排
        var app = new WindowsPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作与断言：无窗口时 Hide 不抛出异常
        await Assert.That(() => app.Hide()).ThrowsNothing();
    }

    [Test]
    public async Task Show_DoesNotThrow_WithNoWindows()
    {
        // 安排
        var app = new WindowsPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作与断言：无窗口时 Show 不抛出异常
        await Assert.That(() => app.Show()).ThrowsNothing();
    }

    [Test]
    public async Task GetCurrentWindowId_ReturnsZero_WithNoWindows()
    {
        // 安排
        var app = new WindowsPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作
        var id = app.GetCurrentWindowId();

        // 断言：无窗口时返回 0
        await Assert.That(id).IsEqualTo((uint)0);
    }
}
