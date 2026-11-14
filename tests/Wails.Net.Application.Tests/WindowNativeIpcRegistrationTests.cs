using System.Collections.Concurrent;
using System.Reflection;
using TUnit.Core;
using Wails.Net.Application.Dialogs;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Platform.ServerMode;
using Wails.Net.Application.Screens;
using Wails.Net.Application.Transport;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Tests;

/// <summary>
/// RH-WIN-02 回归：原生 IPC 的窗口注册与注销由 <see cref="Managers.WindowManager"/> 统一完成。
/// <para>
/// 改动前注册点散在 <c>WindowsPlatformApp.CreateWebviewWindow</c> 与 <c>MockPlatformApp</c>，
/// 注销点散在 <c>Win32WebviewWindow.Close</c>，三处都要通过 <c>Application.Get()</c> 静态定位器。
/// 改动后由 <see cref="Managers.WindowManager"/> 依据
/// <see cref="IPlatformApp.SupportsNativeMessageChannel"/> 统一装配，本文件守住该契约。
/// </para>
/// </summary>
[NotInParallel]
public sealed class WindowNativeIpcRegistrationTests
{
    /// <summary>
    /// 平台声明支持原生消息通道时，创建窗口应完成注册，且注册的是平台返回的实现对象。
    /// </summary>
    [Test]
    public async Task CreateWindow_PlatformSupportsNativeChannel_RegistersImpl()
    {
        var platform = new RecordingPlatformApp { SupportsNativeMessageChannelValue = true };
        var app = CreateApp(platform, out var transport);

        var window = app.CreateWebviewWindow(new WebviewWindowOptions { Name = "Native" });

        await Assert.That(transport.RegisterCalls.Count).IsEqualTo(1);
        await Assert.That(transport.RegisterCalls[0].WindowId).IsEqualTo(window.ID);
        await Assert.That(transport.RegisterCalls[0].Impl).IsSameReferenceAs(window.Impl);
        await Assert.That(transport.RegisteredWindowCount).IsEqualTo(1);
    }

    /// <summary>
    /// 平台未声明原生通道（Linux / Android / 纯窗口测试）时不得注册：
    /// 一旦误注册，<c>EventIPCTransport</c> 会认为原生通道已接管而跳过 ExecJS 兜底，
    /// 导致后端事件在前端丢失（RH-WIN-01 曾因此被掩盖）。
    /// </summary>
    [Test]
    public async Task CreateWindow_PlatformDoesNotSupportNativeChannel_SkipsRegistration()
    {
        var platform = new RecordingPlatformApp { SupportsNativeMessageChannelValue = false };
        var app = CreateApp(platform, out var transport);

        app.CreateWebviewWindow(new WebviewWindowOptions { Name = "NoNative" });

        await Assert.That(transport.RegisterCalls.Count).IsEqualTo(0);
        await Assert.That(transport.RegisteredWindowCount).IsEqualTo(0);
    }

    /// <summary>
    /// 销毁窗口应从原生 IPC 反注册，并通知平台清理自身窗口缓存（避免继续操作已销毁的原生窗口）。
    /// </summary>
    [Test]
    public async Task DestroyWindow_UnregistersFromTransport_AndNotifiesPlatform()
    {
        var platform = new RecordingPlatformApp { SupportsNativeMessageChannelValue = true };
        var app = CreateApp(platform, out var transport);
        var window = app.CreateWebviewWindow(new WebviewWindowOptions { Name = "Doomed" });

        app.DestroyWindow(window.ID);

        await Assert.That(transport.UnregisterCalls).Contains(window.ID);
        await Assert.That(transport.RegisteredWindowCount).IsEqualTo(0);
        await Assert.That(platform.DestroyedWindowIds).Contains(window.ID);
    }

    /// <summary>
    /// 关闭事件与 DestroyWindow 都可能触发释放：重复释放必须幂等，
    /// 不能出现"缓存已清理但平台重复收到销毁通知"以外的副作用（Unregister 至多一次）。
    /// </summary>
    [Test]
    public async Task DestroyWindow_Twice_IsIdempotent()
    {
        var platform = new RecordingPlatformApp { SupportsNativeMessageChannelValue = true };
        var app = CreateApp(platform, out var transport);
        var window = app.CreateWebviewWindow(new WebviewWindowOptions { Name = "Twice" });

        app.DestroyWindow(window.ID);
        app.DestroyWindow(window.ID);

        await Assert.That(transport.UnregisterCalls.Count).IsEqualTo(1);
        await Assert.That(platform.DestroyedWindowIds.Count).IsEqualTo(1);
    }

    /// <summary>
    /// 并发建窗：ID 由 WindowManager 的 <see cref="Interlocked"/> 递增分配，不得冲突，
    /// 且每个窗口都应完成一次注册。
    /// </summary>
    [Test]
    public async Task CreateWindow_Concurrently_AssignsUniqueIdsAndRegistersEach()
    {
        var platform = new RecordingPlatformApp { SupportsNativeMessageChannelValue = true };
        var app = CreateApp(platform, out var transport);

        const int count = 32;
        var ids = new ConcurrentBag<uint>();
        await Parallel.ForEachAsync(
            Enumerable.Range(0, count),
            new ParallelOptions { MaxDegreeOfParallelism = 8 },
            (index, _) =>
            {
                var window = app.CreateWebviewWindow(new WebviewWindowOptions { Name = $"Window{index}" });
                ids.Add(window.ID);
                return ValueTask.CompletedTask;
            });

        await Assert.That(ids.Distinct().Count()).IsEqualTo(count);
        await Assert.That(transport.RegisteredWindowCount).IsEqualTo(count);
    }

    /// <summary>
    /// 用反射注入传输层：<see cref="Application.NativeIpcTransport"/> 的 setter 为 private，
    /// 且真实初始化发生在 <c>Application.Run</c>（需启动平台主循环），单元测试不启动宿主。
    /// </summary>
    private static Application CreateApp(RecordingPlatformApp platform, out RecordingNativeIpcTransport transport)
    {
        var app = new Application(new ApplicationOptions());
        transport = new RecordingNativeIpcTransport();

        var property = typeof(Application).GetProperty(nameof(Application.NativeIpcTransport))!;
        property.SetValue(app, transport);

        app.SetPlatformApp(platform);
        return app;
    }

    /// <summary>
    /// 记录注册/注销调用的原生 IPC 传输层替身。
    /// </summary>
    private sealed class RecordingNativeIpcTransport : INativeIpcTransport
    {
        private readonly ConcurrentDictionary<uint, IWebviewWindowImpl> _impls = new();

        /// <summary>注册调用记录。</summary>
        public List<(uint WindowId, IWebviewWindowImpl Impl)> RegisterCalls { get; } = [];

        /// <summary>注销调用记录。</summary>
        public List<uint> UnregisterCalls { get; } = [];

        /// <inheritdoc />
        public bool SupportsBinary => true;

        /// <inheritdoc />
        public int RegisteredWindowCount => _impls.Count;

        /// <inheritdoc />
        public void RegisterWindow(uint windowId, IWebviewWindowImpl impl)
        {
            _impls[windowId] = impl;
            RegisterCalls.Add((windowId, impl));
        }

        /// <inheritdoc />
        public void UnregisterWindow(uint windowId)
        {
            _impls.TryRemove(windowId, out _);
            UnregisterCalls.Add(windowId);
        }

        /// <inheritdoc />
        public Task PostToWindowAsync(uint windowId, string message) => Task.CompletedTask;

        /// <inheritdoc />
        public void NotifyEvent(string eventName, object? data, uint? senderWindowId = null, uint? targetWindowId = null)
        {
        }
    }

    /// <summary>
    /// 可配置原生通道能力、可记录窗口销毁通知的平台替身。
    /// </summary>
    private sealed class RecordingPlatformApp : IPlatformApp
    {
        /// <summary>本平台是否声明支持原生消息通道。</summary>
        public bool SupportsNativeMessageChannelValue { get; init; }

        /// <summary>平台侧收到的窗口销毁通知。</summary>
        public List<uint> DestroyedWindowIds { get; } = [];

        /// <inheritdoc />
        public string Name => "RecordingPlatform";

        /// <inheritdoc />
        public bool SupportsNativeMessageChannel => SupportsNativeMessageChannelValue;

        /// <inheritdoc />
        public void OnWindowDestroyed(uint windowId) => DestroyedWindowIds.Add(windowId);

        /// <inheritdoc />
        public int Run() => 0;

        /// <inheritdoc />
        public void Destroy()
        {
        }

        /// <inheritdoc />
        public void SetApplicationMenu(Menu? menu)
        {
        }

        /// <inheritdoc />
        public uint GetCurrentWindowId() => 0;

        /// <inheritdoc />
        public void ShowAboutDialog(string name, string description, byte[]? icon)
        {
        }

        /// <inheritdoc />
        public void SetIcon(byte[]? icon)
        {
        }

        /// <inheritdoc />
        public void On(uint id)
        {
        }

        /// <inheritdoc />
        public void DispatchOnMainThread(uint id)
        {
        }

        /// <inheritdoc />
        public void Hide()
        {
        }

        /// <inheritdoc />
        public void Show()
        {
        }

        /// <inheritdoc />
        public Screen? GetPrimaryScreen() => null;

        /// <inheritdoc />
        public Screen[] GetScreens() => [];

        /// <inheritdoc />
        public Dictionary<string, object?> GetFlags(ApplicationOptions options) => [];

        /// <inheritdoc />
        public bool IsOnMainThread() => true;

        /// <inheritdoc />
        public bool IsDarkMode() => false;

        /// <inheritdoc />
        public string GetAccentColor() => "#000000";

        /// <inheritdoc />
        public void DispatchOnMainThread(Action action) => action();

        /// <inheritdoc />
        public IWebviewWindowImpl? CreateWebviewWindow(uint id, WebviewWindowOptions options)
            => new ServerWebviewWindow(id);

        /// <inheritdoc />
        public Task<int> ShowMessageDialog(string title, string message, DialogStyle style, string[] buttons)
            => Task.FromResult(0);

        /// <inheritdoc />
        public Task<string?> OpenFileDialog(OpenFileDialogOptions options) => Task.FromResult<string?>(null);

        /// <inheritdoc />
        public Task<string?> SaveFileDialog(SaveFileDialogOptions options) => Task.FromResult<string?>(null);

        /// <inheritdoc />
        public Task<string[]?> OpenMultipleFilesDialog(OpenFileDialogOptions options)
            => Task.FromResult<string[]?>(null);
    }
}
