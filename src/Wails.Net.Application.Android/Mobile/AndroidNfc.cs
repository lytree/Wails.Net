using Android.Content;
using Android.Nfc;
using Wails.Net.Application.Plugins.Mobile;

namespace Wails.Net.Application.Android.Mobile;

/// <summary>
/// Android 平台 NFC 实现。
/// 对应 Tauri v2 <c>@tauri-apps/plugin-nfc</c> 的 Android 后端。
/// 通过 <c>Android.Nfc.NfcAdapter</c> 提供标签读写能力。
/// <para>
/// 完整 NFC 标签读写需要 <c>Activity.OnNewIntent</c> 接收 <c>ACTION_TECH_DISCOVERED</c> Intent，
/// 本实现通过注入的读写委托解耦 Activity 生命周期，由 <c>AndroidPlatformApp</c> 提供实际实现。
/// </para>
/// <para>
/// 非 Android 环境（单元测试）下 <c>NfcAdapter</c> 为 null，Read 返回空字符串，Write 为 no-op。
/// </para>
/// </summary>
public sealed class AndroidNfc : IPlatformNfc
{
    /// <summary>
    /// 读委托，由平台层注入。为 null 时返回空字符串。
    /// </summary>
    private readonly Func<CancellationToken, Task<string>>? _readImpl;

    /// <summary>
    /// 写委托，由平台层注入。为 null 时为 no-op。
    /// </summary>
    private readonly Func<string, CancellationToken, Task>? _writeImpl;

    /// <summary>
    /// 取消委托，由平台层注入。为 null 时为 no-op。
    /// </summary>
    private readonly Action? _cancelImpl;

    /// <summary>
    /// Android NFC 适配器引用，延迟获取。非 Android 环境下为 null。
    /// </summary>
    private readonly NfcAdapter? _adapter;

    /// <summary>
    /// 构造 <see cref="AndroidNfc"/> 实例，使用默认（无委托）模式。
    /// </summary>
    public AndroidNfc() : this(readImpl: null, writeImpl: null, cancelImpl: null)
    {
    }

    /// <summary>
    /// 构造 <see cref="AndroidNfc"/> 实例，注入读写委托。
    /// </summary>
    /// <param name="readImpl">读委托，由 <c>AndroidPlatformApp</c> 提供实际实现。</param>
    /// <param name="writeImpl">写委托。</param>
    /// <param name="cancelImpl">取消委托。</param>
    public AndroidNfc(
        Func<CancellationToken, Task<string>>? readImpl,
        Func<string, CancellationToken, Task>? writeImpl,
        Action? cancelImpl)
    {
        _readImpl = readImpl;
        _writeImpl = writeImpl;
        _cancelImpl = cancelImpl;

        var context = global::Android.App.Application.Context;
        if (context is null)
        {
            return;
        }

        // NfcAdapter.GetDefaultAdapter 在 API 10+ 可用
        _adapter = NfcAdapter.GetDefaultAdapter(context);
    }

    /// <summary>
    /// 获取 NFC 适配器是否可用（设备支持 NFC 且已启用）。
    /// 供前端在调用 read/write 前预检查。
    /// </summary>
    /// <returns>可用返回 true；不可用或非 Android 环境返回 false。</returns>
    public bool IsAvailable()
    {
        return _adapter is not null && _adapter.IsEnabled;
    }

    /// <inheritdoc />
    public Task<string> ReadAsync()
    {
        if (_readImpl is null)
        {
            // 无注入委托时返回空字符串（需要 Activity.OnNewIntent 才能接收标签数据）
            return Task.FromResult(string.Empty);
        }

        return _readImpl(CancellationToken.None);
    }

    /// <inheritdoc />
    public Task WriteAsync(string data)
    {
        if (_writeImpl is null)
        {
            return Task.CompletedTask;
        }

        return _writeImpl(data, CancellationToken.None);
    }

    /// <inheritdoc />
    public void Cancel()
    {
        _cancelImpl?.Invoke();
    }
}
