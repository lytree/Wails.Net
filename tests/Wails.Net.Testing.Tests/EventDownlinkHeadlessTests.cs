using System.Collections.Concurrent;
using TUnit.Core;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Transport;
using Wails.Net.Application.Windows;
using Wails.Net.Testing.Platform;
using WailsApplication = Wails.Net.Application.Application;

namespace Wails.Net.Testing.Tests;

/// <summary>
/// 后端 → 前端事件下行全链路无头测试（OPT-TEST-003，计划 §7「IPC 全链路无头测试」）。
/// <para>
/// 动机：RH-WIN-01 的断链（facade 与平台实现脱钩）在 Windows 上表现为"事件不出现"，
/// 在非 Windows 平台上更隐蔽——一旦平台声明支持原生消息通道，
/// <see cref="EventIPCTransport"/> 就会认为下行已由原生通道接管而跳过 <c>ExecJS</c> 兜底，
/// 前端收不到任何事件却没有任何报错。本文件用「真实 <see cref="EventIPCTransport"/> +
/// Mock 窗口」的组合把两条路径都钉住。
/// </para>
/// <para>
/// 三条契约：
/// <list type="number">
///   <item>平台不支持原生通道（Linux/macOS/Android 的默认姿态）→ 事件必须经 ExecJS 下发到窗口；</item>
///   <item>平台支持原生通道且传输层已装配 → 不得再走 ExecJS（否则前端收到重复事件）；</item>
///   <item>平台声明支持但传输层未装配 → 必须回退 ExecJS，不得静默丢事件。</item>
/// </list>
/// </para>
/// </summary>
[NotInParallel]
public sealed class EventDownlinkHeadlessTests
{
    /// <summary>
    /// 契约 1：平台不支持原生通道时，事件经 ExecJS 注入窗口，且载荷/来源窗口 ID 完整。
    /// 这是 Linux / macOS / Android（以及纯窗口测试）的默认姿态，也是 RH-WIN-01 类断链的守门点。
    /// </summary>
    [Test]
    public async Task NotifyEvent_PlatformLacksNativeChannel_DeliversViaExecJS()
    {
        var options = new ApplicationOptions();
        var platform = new MockPlatformApp(options) { RegisterWindowsWithNativeIpc = false };
        var app = new WailsApplication(options);
        app.SetPlatformApp(platform);

        var window = app.CreateWebviewWindow(new WebviewWindowOptions { Name = "Headless" });
        var transport = new EventIPCTransport();

        transport.NotifyEvent("backend:event", new { Value = 42 }, senderWindowId: window.ID);

        var scripts = await WaitForEventScriptAsync(platform, window.ID, "backend:event");
        await Assert.That(scripts.Count).IsGreaterThan(0);
        await Assert.That(scripts[0]).Contains("_wailsEmitEvent");
        await Assert.That(scripts[0]).Contains("backend:event");
        await Assert.That(scripts[0]).Contains("42", StringComparison.Ordinal);
        await Assert.That(scripts[0]).Contains(window.ID.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// 契约 2：平台支持原生通道且传输层已注册该窗口时，不得重复走 ExecJS。
    /// 否则前端会对同一事件触发两次回调。
    /// </summary>
    [Test]
    public async Task NotifyEvent_NativeChannelWired_SkipsExecJS()
    {
        var options = new ApplicationOptions();
        var platform = new MockPlatformApp(options) { RegisterWindowsWithNativeIpc = true };
        var app = new WailsApplication(options);

        var nativeTransport = new RecordingNativeIpcTransport();
        typeof(WailsApplication)
            .GetProperty(nameof(WailsApplication.NativeIpcTransport))!
            .SetValue(app, nativeTransport);

        app.SetPlatformApp(platform);

        var window = app.CreateWebviewWindow(new WebviewWindowOptions { Name = "Native" });
        await Assert.That(nativeTransport.RegisteredWindowCount).IsEqualTo(1);

        var transport = new EventIPCTransport();
        transport.NotifyEvent("backend:event", new { Value = 7 });

        // 给异步队列留出足够时间：若实现退化回 ExecJS，这里必然能观察到注入脚本。
        await Task.Delay(300);

        var recorded = platform.TryGetWindow(window.ID, out var mock) ? mock!.ExecutedJavaScript : [];
        await Assert.That(recorded.Any(js => js.Contains("backend:event", StringComparison.Ordinal))).IsFalse();
    }

    /// <summary>
    /// 契约 3：平台声明支持原生通道，但应用未装配 <see cref="WailsApplication.NativeIpcTransport"/>
    /// （装配缺失/初始化顺序错）时，事件必须回退 ExecJS，而不是被静默丢弃。
    /// </summary>
    [Test]
    public async Task NotifyEvent_NativeChannelDeclaredButNotWired_FallsBackToExecJS()
    {
        var options = new ApplicationOptions();
        var platform = new MockPlatformApp(options) { RegisterWindowsWithNativeIpc = true };
        var app = new WailsApplication(options);
        app.SetPlatformApp(platform);

        var window = app.CreateWebviewWindow(new WebviewWindowOptions { Name = "DeclaredOnly" });
        var transport = new EventIPCTransport();

        transport.NotifyEvent("backend:fallback", new { Value = 1 });

        var scripts = await WaitForEventScriptAsync(platform, window.ID, "backend:fallback");
        await Assert.That(scripts.Count).IsGreaterThan(0);
    }

    /// <summary>
    /// 轮询等待窗口收到包含指定事件名的注入脚本：下行投递经每窗口有序队列串行执行，
    /// 不能假设 <c>NotifyEvent</c> 返回时脚本已落地。
    /// </summary>
    private static async Task<IReadOnlyList<string>> WaitForEventScriptAsync(
        MockPlatformApp platform, uint windowId, string eventName, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (true)
        {
            if (platform.TryGetWindow(windowId, out var mock))
            {
                var hit = mock!.ExecutedJavaScript
                    .Where(js => js.Contains(eventName, StringComparison.Ordinal))
                    .ToArray();
                if (hit.Length > 0)
                {
                    return hit;
                }
            }

            if (Environment.TickCount64 >= deadline)
            {
                throw new TimeoutException($"等待事件 {eventName} 经 ExecJS 下发超时。");
            }

            await Task.Delay(10);
        }
    }

    /// <summary>
    /// 记录注册/注销的最小原生 IPC 传输层替身：仅用于让
    /// <see cref="EventIPCTransport"/> 判定「原生通道已接管」。
    /// </summary>
    private sealed class RecordingNativeIpcTransport : INativeIpcTransport
    {
        private readonly ConcurrentDictionary<uint, IWebviewWindowImpl> _impls = new();

        /// <inheritdoc />
        public bool SupportsBinary => true;

        /// <inheritdoc />
        public int RegisteredWindowCount => _impls.Count;

        /// <inheritdoc />
        public void RegisterWindow(uint windowId, IWebviewWindowImpl impl) => _impls[windowId] = impl;

        /// <inheritdoc />
        public void UnregisterWindow(uint windowId) => _impls.TryRemove(windowId, out _);

        /// <inheritdoc />
        public Task PostToWindowAsync(uint windowId, string message) => Task.CompletedTask;

        /// <inheritdoc />
        public void NotifyEvent(string eventName, object? data, uint? senderWindowId = null, uint? targetWindowId = null)
        {
        }
    }
}
