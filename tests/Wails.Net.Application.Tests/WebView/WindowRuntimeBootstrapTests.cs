using TUnit.Core;
using Wails.Net.Application.Webview;

namespace Wails.Net.Application.Tests.WebView;

/// <summary>
/// RH-IPC-02 回归：<see cref="WindowRuntimeBootstrap"/> 统一四平台的
/// 「运行时注入 + 导航完成」双条件与 WindowRuntimeReady 只派发一次语义。
/// <para>
/// 改造前四平台各写一套：Windows 在 NavigationCompleted 派发、Linux 在注入完成即派发、
/// macOS 在 DidFinishNavigation 无条件派发、Android 在 OnPageFinished 派发。
/// 本文件守住统一后的契约。
/// </para>
/// </summary>
[NotInParallel]
public sealed class WindowRuntimeBootstrapTests
{
    /// <summary>
    /// 仅注入完成、导航未完成时不派发（Linux 改造前在此时机就派发，属缺陷）。
    /// </summary>
    [Test]
    public async Task MarkRuntimeInjected_WithoutNavigation_DoesNotDispatch()
    {
        var dispatches = new List<uint>();
        var bootstrap = new WindowRuntimeBootstrap(7, dispatches.Add, () => "runtime-js");

        await Assert.That(bootstrap.TryGetScript(out var script)).IsTrue();
        await Assert.That(script).IsEqualTo("runtime-js");

        bootstrap.MarkRuntimeInjected();

        await Assert.That(dispatches.Count).IsEqualTo(0);
    }

    /// <summary>
    /// 仅导航完成、注入未完成时不派发（macOS 改造前不校验注入结果即派发)。
    /// </summary>
    [Test]
    public async Task MarkNavigationCompleted_WithoutInjection_DoesNotDispatch()
    {
        var dispatches = new List<uint>();
        var bootstrap = new WindowRuntimeBootstrap(8, dispatches.Add, () => "runtime-js");

        bootstrap.MarkNavigationCompleted();

        await Assert.That(dispatches.Count).IsEqualTo(0);
    }

    /// <summary>
    /// 两个条件满足后派发一次，且携带正确窗口 ID。
    /// </summary>
    [Test]
    public async Task BothConditions_Satisfied_DispatchesOnce()
    {
        var dispatches = new List<uint>();
        var bootstrap = new WindowRuntimeBootstrap(9, dispatches.Add, () => "runtime-js");

        await Assert.That(bootstrap.TryGetScript(out _)).IsTrue();
        bootstrap.MarkRuntimeInjected();
        bootstrap.MarkNavigationCompleted();

        await Assert.That(dispatches.Count).IsEqualTo(1);
        await Assert.That(dispatches[0]).IsEqualTo(9u);
    }

    /// <summary>
    /// 到达顺序不确定：先导航完成、后注入完成，同样应派发一次。
    /// </summary>
    [Test]
    public async Task NavigationBeforeInjection_StillDispatchesOnce()
    {
        var dispatches = new List<uint>();
        var bootstrap = new WindowRuntimeBootstrap(10, dispatches.Add, () => "runtime-js");

        await Assert.That(bootstrap.TryGetScript(out _)).IsTrue();
        bootstrap.MarkNavigationCompleted();
        bootstrap.MarkRuntimeInjected();

        await Assert.That(dispatches.Count).IsEqualTo(1);
    }

    /// <summary>
    /// 重复上报（多个导航完成回调、重复注入成功）不得重复派发。
    /// </summary>
    [Test]
    public async Task RepeatedMarks_DispatchOnlyOnce()
    {
        var dispatches = new List<uint>();
        var bootstrap = new WindowRuntimeBootstrap(11, dispatches.Add, () => "runtime-js");

        await Assert.That(bootstrap.TryGetScript(out _)).IsTrue();
        bootstrap.MarkRuntimeInjected();
        bootstrap.MarkRuntimeInjected();
        bootstrap.MarkNavigationCompleted();
        bootstrap.MarkNavigationCompleted();

        await Assert.That(dispatches.Count).IsEqualTo(1);
    }

    /// <summary>
    /// Android 语义：不需要独立注入，导航完成即派发。
    /// </summary>
    [Test]
    public async Task RuntimeInjectionNotRequired_DispatchesOnNavigation()
    {
        var dispatches = new List<uint>();
        var bootstrap = new WindowRuntimeBootstrap(12, dispatches.Add, runtimeInjectionRequired: false);

        await Assert.That(bootstrap.TryGetScript(out _)).IsFalse();

        bootstrap.MarkNavigationCompleted();

        await Assert.That(dispatches.Count).IsEqualTo(1);
    }

    /// <summary>
    /// 脚本只取一次：重复调用返回 false，避免 document-start 脚本被注册两遍。
    /// </summary>
    [Test]
    public async Task TryGetScript_IsConsumedOnce()
    {
        var bootstrap = new WindowRuntimeBootstrap(13, _ => { }, () => "runtime-js");

        await Assert.That(bootstrap.TryGetScript(out var first)).IsTrue();
        await Assert.That(first).IsEqualTo("runtime-js");
        await Assert.That(bootstrap.TryGetScript(out _)).IsFalse();
    }

    /// <summary>
    /// 脚本生成失败（返回空）时不占用配额，后续仍可重试。
    /// </summary>
    [Test]
    public async Task TryGetScript_GenerationFails_AllowsRetry()
    {
        var calls = 0;
        var bootstrap = new WindowRuntimeBootstrap(
            14,
            _ => { },
            () => ++calls == 1 ? null : "runtime-js");

        await Assert.That(bootstrap.TryGetScript(out _)).IsFalse();
        await Assert.That(bootstrap.TryGetScript(out var script)).IsTrue();
        await Assert.That(script).IsEqualTo("runtime-js");
    }

    /// <summary>
    /// 脚本生成抛异常时按「未生成」处理，不向上冒泡（平台注入路径不应中断窗口初始化）。
    /// </summary>
    [Test]
    public async Task TryGetScript_GenerationThrows_ReturnsFalse()
    {
        var bootstrap = new WindowRuntimeBootstrap(
            15,
            _ => { },
            static () => throw new InvalidOperationException("生成失败"));

        await Assert.That(bootstrap.TryGetScript(out _)).IsFalse();
    }

    /// <summary>
    /// 控制器重建（Windows 浏览器进程故障恢复）后可重新注入并重新派发一次。
    /// </summary>
    [Test]
    public async Task ResetInjection_AllowsReinjectionAndRedispatch()
    {
        var dispatches = new List<uint>();
        var bootstrap = new WindowRuntimeBootstrap(16, dispatches.Add, () => "runtime-js");

        await Assert.That(bootstrap.TryGetScript(out _)).IsTrue();
        bootstrap.MarkRuntimeInjected();
        bootstrap.MarkNavigationCompleted();
        await Assert.That(dispatches.Count).IsEqualTo(1);

        bootstrap.ResetInjection();

        await Assert.That(bootstrap.TryGetScript(out _)).IsTrue();
        bootstrap.MarkRuntimeInjected();
        bootstrap.MarkNavigationCompleted();

        await Assert.That(dispatches.Count).IsEqualTo(2);
    }

    /// <summary>
    /// 派发委托抛出异常时不得影响 WebView 生命周期（吞掉并保持状态一致）。
    /// </summary>
    [Test]
    public async Task DispatchingReady_Throws_DoesNotPropagate()
    {
        var bootstrap = new WindowRuntimeBootstrap(
            17,
            static _ => throw new InvalidOperationException("订阅方异常"),
            () => "runtime-js");

        await Assert.That(bootstrap.TryGetScript(out _)).IsTrue();

        // 不应抛出
        bootstrap.MarkRuntimeInjected();
        bootstrap.MarkNavigationCompleted();
    }
}
