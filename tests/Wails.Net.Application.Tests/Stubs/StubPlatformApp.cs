using Wails.Net.Application.Dialogs;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Screens;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Tests.Stubs;

/// <summary>
/// 最小 <see cref="IPlatformApp"/> 替身（Application.Tests 不引用 Wails.Net.Testing，
/// 因此不能复用 <c>Wails.Net.Testing.Platform.MockPlatformApp</c>）。
/// <para>
/// 只实现无默认值的成员；单实例相关行为通过 <see cref="SingleInstanceLockResult"/> 与
/// <see cref="NotifiedArgs"/> 观测，其余平台能力一律 no-op / 空返回。
/// </para>
/// </summary>
internal sealed class StubPlatformApp : IPlatformApp
{
    /// <summary><see cref="AcquireSingleInstanceLock"/> 的返回值，默认 true。</summary>
    public bool SingleInstanceLockResult { get; set; } = true;

    /// <summary>最近一次 <see cref="NotifySingleInstance"/> 收到的参数。</summary>
    public string[]? NotifiedArgs { get; private set; }

    /// <summary>是否已被销毁。</summary>
    public bool IsDestroyed { get; private set; }

    /// <inheritdoc />
    public string Name => "stub";

    /// <inheritdoc />
    public int Run() => 0;

    /// <inheritdoc />
    public bool AcquireSingleInstanceLock(string uniqueId) => SingleInstanceLockResult;

    /// <inheritdoc />
    public void NotifySingleInstance(string[] args) => NotifiedArgs = args;

    /// <inheritdoc />
    public void Destroy() => IsDestroyed = true;

    /// <inheritdoc />
    public void SetApplicationMenu(Menu? menu) { }

    /// <inheritdoc />
    public uint GetCurrentWindowId() => 0;

    /// <inheritdoc />
    public void ShowAboutDialog(string name, string description, byte[]? icon) { }

    /// <inheritdoc />
    public void SetIcon(byte[]? icon) { }

    /// <inheritdoc />
    public void On(uint id) { }

    /// <inheritdoc />
    public void DispatchOnMainThread(uint id) { }

    /// <inheritdoc />
    public void Hide() { }

    /// <inheritdoc />
    public void Show() { }

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
    public string GetAccentColor() => string.Empty;

    /// <inheritdoc />
    public void DispatchOnMainThread(Action action) => action();

    /// <inheritdoc />
    public IWebviewWindowImpl? CreateWebviewWindow(uint id, WebviewWindowOptions options) => null;

    /// <inheritdoc />
    public Task<int> ShowMessageDialog(string title, string message, DialogStyle style, string[] buttons)
        => Task.FromResult(0);

    /// <inheritdoc />
    public Task<string?> OpenFileDialog(OpenFileDialogOptions options) => Task.FromResult<string?>(null);

    /// <inheritdoc />
    public Task<string?> SaveFileDialog(SaveFileDialogOptions options) => Task.FromResult<string?>(null);

    /// <inheritdoc />
    public Task<string[]?> OpenMultipleFilesDialog(OpenFileDialogOptions options) => Task.FromResult<string[]?>(null);
}
