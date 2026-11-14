using TUnit.Core;
using Wails.Net.Application.Dialogs;
using Wails.Net.Application.Managers;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Platform.ServerMode;
using Wails.Net.Application.Screens;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Tests;

/// <summary>
/// RH-WIN-02 终态：平台层不再自持窗口表，改为通过
/// <see cref="IPlatformWindowAccessor"/> 查询 <see cref="WindowManager"/> 的唯一注册表。
/// <para>
/// 改造前四个平台 App 各自持有 <c>Dictionary&lt;uint, XxxWebviewWindow&gt; _windows</c>，
/// 与 <see cref="WindowManager"/> 并行维护；改造后平台只保留只读查询，本文件守住三条契约：
/// 注入时机（构造 / 后置 SetPlatformApp）、快照完整性、创建过程中的窗口不可见。
/// </para>
/// </summary>
[NotInParallel]
public sealed class WindowPlatformAccessorTests
{
    /// <summary>
    /// 构造 <see cref="WindowManager"/> 时应立即把查询入口注入平台应用，
    /// 否则平台创建窗口时拿不到 accessor，Hide/Show/菜单等枚举路径会退化为空操作。
    /// </summary>
    [Test]
    public async Task Constructor_InjectsAccessorIntoPlatform()
    {
        var platform = new AccessorRecordingPlatformApp();

        var manager = new WindowManager(platform);

        await Assert.That(platform.Accessor).IsNotNull();
        await Assert.That(platform.Accessor).IsSameReferenceAs(manager);
    }

    /// <summary>
    /// DI 路径下 platformApp 后置注入（构造时传 null）也必须绑定查询入口，
    /// 否则 <c>Application.SetPlatformApp</c> 之后平台层永远看不到窗口。
    /// </summary>
    [Test]
    public async Task SetPlatformApp_InjectsAccessorIntoLatePlatform()
    {
        var manager = new WindowManager(platformApp: null);
        var platform = new AccessorRecordingPlatformApp();

        manager.SetPlatformApp(platform);

        await Assert.That(platform.Accessor).IsNotNull();
        await Assert.That(platform.Accessor).IsSameReferenceAs(manager);
    }

    /// <summary>
    /// 快照语义：创建完成后包含该窗口实现；窗口创建过程中（<c>Impl</c> 尚未回填）不可见；
    /// 销毁后从快照移除。第二条尤其重要——平台在 <c>CreateWebviewWindow</c> 内部
    /// 若能看到半初始化实例，会对未完成原生初始化的窗口执行 Show/菜单操作。
    /// </summary>
    [Test]
    public async Task EnumeratePlatformWindows_SnapshotLifecycle_MatchesWindowRegistry()
    {
        var platform = new AccessorRecordingPlatformApp();
        var manager = new WindowManager(platform);

        var id = manager.CreateWebviewWindow(new WebviewWindowOptions { Name = "Snapshot" });

        // 创建过程中平台内部查询到 0 个窗口（Impl 未回填）。
        await Assert.That(platform.WindowsVisibleDuringCreate).IsEqualTo(0);

        var snapshot = manager.EnumeratePlatformWindows();
        await Assert.That(snapshot).Count().IsEqualTo(1);
        await Assert.That(snapshot[0]).IsSameReferenceAs(manager.GetWindow(id)!.Impl);

        manager.DestroyWindow(id);

        await Assert.That(manager.EnumeratePlatformWindows()).IsEmpty();
    }

    /// <summary>
    /// 无窗口时返回空列表而非 null（平台侧 <c>foreach</c> 无需判空）。
    /// </summary>
    [Test]
    public async Task EnumeratePlatformWindows_NoWindows_ReturnsEmpty()
    {
        var manager = new WindowManager(new AccessorRecordingPlatformApp());

        await Assert.That(manager.EnumeratePlatformWindows()).IsEmpty();
    }

    /// <summary>
    /// 记录 accessor 注入与创建瞬间可见窗口数的平台替身。
    /// </summary>
    private sealed class AccessorRecordingPlatformApp : IPlatformApp
    {
        /// <summary>收到的窗口查询入口。</summary>
        public IPlatformWindowAccessor? Accessor { get; private set; }

        /// <summary>平台在 <see cref="CreateWebviewWindow"/> 内部能看到的窗口数；-1 表示未调用。</summary>
        public int WindowsVisibleDuringCreate { get; private set; } = -1;

        /// <inheritdoc />
        public void AttachWindowAccessor(IPlatformWindowAccessor accessor) => Accessor = accessor;

        /// <inheritdoc />
        public string Name => "AccessorRecordingPlatform";

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
        {
            // WindowManager 在调用本方法前已登记 facade 记录，但 Impl 尚未回填，
            // 因此此刻查询应看不到本窗口。
            WindowsVisibleDuringCreate = Accessor?.EnumeratePlatformWindows().Count ?? -1;
            return new ServerWebviewWindow(id);
        }

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
