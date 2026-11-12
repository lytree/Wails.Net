using TUnit.Core;
using Windows.Win32.UI.WindowsAndMessaging;
using Wails.Net.Application.Platform;

namespace Wails.Net.Application.Windows.Tests;

/// <summary>
/// <see cref="Win32AppIconCache"/> 的单元测试（TUnit）。
/// 通过注入加载器与释放器，验证上游 #6502 的图标句柄生命周期语义：
/// 缓存、替换即释放、加载失败保留旧图标、销毁释放当前句柄。
/// 纯托管逻辑，不创建窗口，可在 CI 中安全运行。
/// </summary>
[NotInParallel]
public sealed class Win32AppIconCacheTests
{
    /// <summary>
    /// 构造指定数值的图标句柄（测试用伪句柄，不参与 Win32 调用）。
    /// </summary>
    /// <param name="value">句柄数值。</param>
    /// <returns>图标句柄。</returns>
    private static unsafe HICON Handle(nint value) => (HICON)value;

    /// <summary>
    /// 读取图标句柄数值。
    /// </summary>
    /// <param name="icon">图标句柄。</param>
    /// <returns>句柄数值。</returns>
    private static unsafe nint ValueOf(HICON icon) => (nint)icon.Value;

    /// <summary>
    /// 首次 Set 加载图标并返回句柄，缓存随之持有该句柄。
    /// </summary>
    [Test]
    public async Task Set_LoadsIcon_AndCachesHandle()
    {
        // 安排：加载器返回固定句柄
        var loadCount = 0;
        var cache = new Win32AppIconCache(
            _ => { loadCount++; return Handle(101); },
            _ => { });

        // 操作
        var result = cache.Set([1, 2, 3]);

        // 断言
        await Assert.That(loadCount).IsEqualTo(1);
        await Assert.That(ValueOf(result)).IsEqualTo((nint)101);
        await Assert.That(ValueOf(cache.Current)).IsEqualTo((nint)101);
        await Assert.That(cache.HasIcon).IsTrue();
    }

    /// <summary>
    /// 替换图标时立即释放上一个句柄（避免长期运行下句柄泄漏）。
    /// </summary>
    [Test]
    public async Task Set_ReplacingIcon_DestroysPreviousHandle()
    {
        // 安排：每次加载返回递增句柄
        var next = 100;
        var destroyed = new List<nint>();
        var cache = new Win32AppIconCache(
            _ => Handle(++next),
            icon => destroyed.Add(ValueOf(icon)));

        // 操作
        cache.Set([1]);
        cache.Set([2]);
        cache.Set([3]);

        // 断言：前两次的句柄各被释放一次，当前句柄未被释放
        await Assert.That(destroyed.Count).IsEqualTo(2);
        await Assert.That(destroyed[0]).IsEqualTo((nint)101);
        await Assert.That(destroyed[1]).IsEqualTo((nint)102);
        await Assert.That(ValueOf(cache.Current)).IsEqualTo((nint)103);
    }

    /// <summary>
    /// null 或空字节不触发加载，且保留当前图标（对齐上游 setIcon 的失败语义）。
    /// </summary>
    [Test]
    public async Task Set_WithNullOrEmptyBytes_KeepsCurrentIcon()
    {
        // 安排：先设置有效图标
        var loadCount = 0;
        var destroyed = new List<nint>();
        var cache = new Win32AppIconCache(
            _ => { loadCount++; return Handle(200); },
            icon => destroyed.Add(ValueOf(icon)));
        cache.Set([1]);

        // 操作
        var afterNull = cache.Set(null);
        var afterEmpty = cache.Set([]);

        // 断言：加载次数未增加，图标未被替换或释放
        await Assert.That(loadCount).IsEqualTo(1);
        await Assert.That(ValueOf(afterNull)).IsEqualTo((nint)200);
        await Assert.That(ValueOf(afterEmpty)).IsEqualTo((nint)200);
        await Assert.That(destroyed).IsEmpty();
    }

    /// <summary>
    /// 加载失败（返回 NULL）时保留旧图标，不释放它。
    /// </summary>
    [Test]
    public async Task Set_WhenLoaderFails_KeepsCurrentIcon()
    {
        // 安排：首次加载成功，随后加载失败
        var succeed = true;
        var destroyed = new List<nint>();
        var cache = new Win32AppIconCache(
            _ => succeed ? Handle(300) : default,
            icon => destroyed.Add(ValueOf(icon)));
        cache.Set([1]);

        // 操作
        succeed = false;
        var result = cache.Set([2]);

        // 断言
        await Assert.That(ValueOf(result)).IsEqualTo((nint)300);
        await Assert.That(ValueOf(cache.Current)).IsEqualTo((nint)300);
        await Assert.That(destroyed).IsEmpty();
    }

    /// <summary>
    /// 无图标时 Set 失败返回 NULL，HasIcon 为 false。
    /// </summary>
    [Test]
    public async Task Set_WhenLoaderFailsWithoutPreviousIcon_ReturnsNull()
    {
        // 安排：加载器恒失败
        var cache = new Win32AppIconCache(_ => default, _ => { });

        // 操作
        var result = cache.Set([1]);

        // 断言
        await Assert.That(result.IsNull).IsTrue();
        await Assert.That(cache.HasIcon).IsFalse();
        await Assert.That(cache.Current.IsNull).IsTrue();
    }

    /// <summary>
    /// Dispose 释放当前句柄并清空缓存，重复调用只释放一次。
    /// </summary>
    [Test]
    public async Task Dispose_ReleasesCurrentHandle_OnlyOnce()
    {
        // 安排
        var destroyed = new List<nint>();
        var cache = new Win32AppIconCache(_ => Handle(400), icon => destroyed.Add(ValueOf(icon)));
        cache.Set([1]);

        // 操作
        cache.Dispose();
        cache.Dispose();

        // 断言
        await Assert.That(destroyed.Count).IsEqualTo(1);
        await Assert.That(destroyed[0]).IsEqualTo((nint)400);
        await Assert.That(cache.HasIcon).IsFalse();
    }

    /// <summary>
    /// 释放后再次 Set 为无操作：不加载新句柄，也不缓存。
    /// </summary>
    [Test]
    public async Task Set_AfterDispose_IsNoOp()
    {
        // 安排：已释放的缓存
        var loadCount = 0;
        var destroyed = new List<nint>();
        var cache = new Win32AppIconCache(
            _ => { loadCount++; return Handle(500); },
            icon => destroyed.Add(ValueOf(icon)));
        cache.Dispose();

        // 操作
        var result = cache.Set([1]);

        // 断言：不加载、不缓存、无句柄需要释放
        await Assert.That(result.IsNull).IsTrue();
        await Assert.That(cache.HasIcon).IsFalse();
        await Assert.That(loadCount).IsEqualTo(0);
        await Assert.That(destroyed).IsEmpty();
    }

    /// <summary>
    /// 并发替换图标不抛异常，最终只保留一个句柄且已释放句柄数等于替换次数。
    /// </summary>
    [Test]
    public async Task Set_Concurrent_KeepsSingleHandleAndReleasesReplaced()
    {
        // 安排：线程安全的释放记录
        var destroyed = new List<nint>();
        var gate = new object();
        var next = 600;
        var cache = new Win32AppIconCache(
            _ => Handle(Interlocked.Increment(ref next)),
            icon => { lock (gate) { destroyed.Add(ValueOf(icon)); } });

        // 操作：8 个线程各设置一次图标
        var tasks = new List<Task>();
        for (var i = 0; i < 8; i++)
        {
            tasks.Add(Task.Run(() => cache.Set([1])));
        }

        await Task.WhenAll(tasks);

        // 断言：8 次加载中 7 个旧句柄被释放，当前句柄仍存活
        int released;
        lock (gate)
        {
            released = destroyed.Count;
        }

        await Assert.That(released).IsEqualTo(7);
        await Assert.That(destroyed.Contains(ValueOf(cache.Current))).IsFalse();
        await Assert.That(cache.HasIcon).IsTrue();
    }
}
