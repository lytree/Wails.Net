using System.Threading;
using TUnit.Core;
using Wails.Net.Application.Clipboard;

namespace Wails.Net.Application.Windows.Tests;

/// <summary>
/// WindowsClipboard 的单元测试（TUnit）。
/// 剪贴板操作需要 STA 线程，通过辅助方法在 STA 线程上执行。
/// 注意：剪贴板是系统共享状态，因此不并行执行。
/// </summary>
[NotInParallel]
public sealed class WindowsClipboardTests
{
    /// <summary>
    /// 在 STA 线程上执行指定操作，并等待完成。
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
    public async Task SetTextAndGetText_RoundTrip()
    {
        // 安排
        var clipboard = new WindowsClipboard();

        // 操作：设置文本后读取。
        // 写入与读取放在同一个 STA 线程内完成，并对读取做有限重试：
        // 剪贴板是系统共享资源，其他进程（Windows 剪贴板历史、剪贴板管理器、RDP 剪贴板重定向）
        // 可能在 SetText 之后、GetText 之前短暂占用或改写它，表现为偶发读到空串。
        // 重试只放宽"时序"，不放宽语义——内容读不到仍然是失败。
        var text = RunOnSTAThread(() =>
        {
            clipboard.SetText("Hello World");
            return ReadWithRetry(clipboard, "Hello World");
        });

        // 断言：内容一致
        await Assert.That(text).IsEqualTo("Hello World");
    }

    /// <summary>
    /// 反复读取剪贴板文本，直到读到期望值或用尽次数（每次间隔 50ms）。
    /// </summary>
    /// <param name="clipboard">剪贴板实现。</param>
    /// <param name="expected">期望的文本内容。</param>
    /// <param name="attempts">最大读取次数。</param>
    /// <returns>最后一次读到的文本。</returns>
    private static string ReadWithRetry(WindowsClipboard clipboard, string expected, int attempts = 5)
    {
        var text = clipboard.GetText();
        for (var i = 1; i < attempts && text != expected; i++)
        {
            Thread.Sleep(50);
            text = clipboard.GetText();
        }

        return text;
    }

    [Test]
    public async Task GetText_ReturnsEmpty_WhenNoText()
    {
        // 安排
        var clipboard = new WindowsClipboard();

        // 操作：清空剪贴板后读取
        RunOnSTAThread(() => clipboard.Clear());
        var text = RunOnSTAThread(() => clipboard.GetText());

        // 断言：返回空字符串
        await Assert.That(text).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Clear_EmptiesClipboard()
    {
        // 安排
        var clipboard = new WindowsClipboard();

        // 操作：设置文本后清空再读取
        RunOnSTAThread(() => clipboard.SetText("Some text"));
        RunOnSTAThread(() => clipboard.Clear());
        var text = RunOnSTAThread(() => clipboard.GetText());

        // 断言：剪贴板已清空
        await Assert.That(text).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task SetHTML_AndGetHTML_RoundTrip()
    {
        // 安排
        var clipboard = new WindowsClipboard();
        var html = "<b>Hello HTML</b>";

        // 操作：设置 HTML 后读取（与文本用例同理：同线程完成 + 有限重试）
        var result = RunOnSTAThread(() =>
        {
            clipboard.SetHTML(html, "fallback");
            var read = clipboard.GetHTML();
            for (var i = 0; i < 4 && !read.Contains(html); i++)
            {
                Thread.Sleep(50);
                read = clipboard.GetHTML();
            }

            return read;
        });

        // 断言：返回内容包含设置的 HTML（剪贴板可能添加头部包装）
        await Assert.That(result.Contains(html)).IsTrue();
    }

    [Test]
    public async Task GetHTML_ReturnsEmpty_WhenNoHTML()
    {
        // 安排
        var clipboard = new WindowsClipboard();

        // 操作：清空剪贴板后读取 HTML
        RunOnSTAThread(() => clipboard.Clear());
        var result = RunOnSTAThread(() => clipboard.GetHTML());

        // 断言：无 HTML 时返回空字符串
        await Assert.That(result).IsEqualTo(string.Empty);
    }
}
