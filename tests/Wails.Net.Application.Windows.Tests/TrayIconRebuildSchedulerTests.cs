using TUnit.Core;
using Wails.Net.Application.SystemTray;

namespace Wails.Net.Application.Windows.Tests;

/// <summary>
/// 托盘图标重建去抖调度器（<see cref="TrayIconRebuildScheduler"/>）的测试。
/// <para>
/// 对应 Wails v3 <c>v3.1.0</c> PR #6386：Explorer 重启会广播多次 <c>TaskbarCreated</c>，
/// 托盘图标需去抖后重新注册。用例通过注入更短延迟（毫秒级）验证合并语义，
/// 不依赖真实 Explorer 重启。
/// </para>
/// </summary>
[NotInParallel]
public sealed class TrayIconRebuildSchedulerTests
{
    /// <summary>去抖窗口内的多次请求应合并为一次重建。</summary>
    [Test]
    public async Task Request_MultipleWithinWindow_RebuildsOnce()
    {
        var count = 0;
        using var scheduler = new TrayIconRebuildScheduler(
            () => Interlocked.Increment(ref count),
            TimeSpan.FromMilliseconds(80));

        scheduler.Request();
        await Task.Delay(20);
        scheduler.Request();
        await Task.Delay(20);
        scheduler.Request();

        var rebuilt = await WaitUntilAsync(() => Volatile.Read(ref count) >= 1, TimeSpan.FromSeconds(5));
        await Assert.That(rebuilt).IsTrue();

        // 超出窗口后确认没有第二次重建（计时器为一次性）。
        await Task.Delay(150);
        await Assert.That(Volatile.Read(ref count)).IsEqualTo(1);
    }

    /// <summary>一次重建完成后再次请求，应触发第二次重建（不永久锁死）。</summary>
    [Test]
    public async Task Request_AfterRebuild_RebuildsAgain()
    {
        var count = 0;
        using var scheduler = new TrayIconRebuildScheduler(
            () => Interlocked.Increment(ref count),
            TimeSpan.FromMilliseconds(50));

        scheduler.Request();
        await Assert.That(await WaitUntilAsync(() => Volatile.Read(ref count) >= 1, TimeSpan.FromSeconds(5))).IsTrue();

        scheduler.Request();
        await Assert.That(await WaitUntilAsync(() => Volatile.Read(ref count) >= 2, TimeSpan.FromSeconds(5))).IsTrue();

        await Assert.That(Volatile.Read(ref count)).IsEqualTo(2);
    }

    /// <summary>释放后尚未触发的请求不应执行重建。</summary>
    [Test]
    public async Task Dispose_BeforeDelay_DoesNotRebuild()
    {
        var count = 0;
        var scheduler = new TrayIconRebuildScheduler(
            () => Interlocked.Increment(ref count),
            TimeSpan.FromMilliseconds(200));

        scheduler.Request();
        scheduler.Dispose();
        scheduler.Dispose();

        await Task.Delay(400);
        await Assert.That(Volatile.Read(ref count)).IsEqualTo(0);
    }

    /// <summary>释放后的请求应静默忽略，不抛异常也不重建。</summary>
    [Test]
    public async Task Request_AfterDispose_IsIgnored()
    {
        var count = 0;
        var scheduler = new TrayIconRebuildScheduler(
            () => Interlocked.Increment(ref count),
            TimeSpan.FromMilliseconds(50));

        scheduler.Dispose();
        scheduler.Request();

        await Task.Delay(150);
        await Assert.That(Volatile.Read(ref count)).IsEqualTo(0);
    }

    /// <summary>无托盘实例时广播 <c>TaskbarCreated</c> 应为空操作（不抛异常）。</summary>
    [Test]
    public async Task NotifyTaskbarCreated_NoTrays_IsNoOp()
    {
        Win32SystemTray.NotifyTaskbarCreated();
        await Assert.That(Win32SystemTray.TaskbarCreatedMessage).IsNotEqualTo(0u);
    }

    /// <summary><c>TaskbarCreated</c> 消息应完成注册并返回非 0 的动态消息 ID。</summary>
    [Test]
    public async Task TaskbarCreatedMessage_IsRegistered()
    {
        await Assert.That(Win32SystemTray.TaskbarCreatedMessage).IsNotEqualTo(0u);
    }

    /// <summary>
    /// 轮询等待条件成立。
    /// </summary>
    /// <param name="condition">判定条件。</param>
    /// <param name="timeout">最长等待时间。</param>
    /// <returns>条件在超时前成立返回 true。</returns>
    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(10);
        }

        return condition();
    }
}
