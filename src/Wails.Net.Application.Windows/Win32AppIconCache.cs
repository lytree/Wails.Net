using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Wails.Net.Application.Platform;

/// <summary>
/// 应用级 Windows 图标句柄缓存。
/// <para>
/// 对应 Wails v3 <c>v3.2.0-nightly.20261103</c> PR #6502（<c>application_windows.go</c> 的
/// <c>currentIcon</c> 字段）：<see cref="Set"/> 负责把图标字节加载为 <c>HICON</c> 并缓存，
/// 替换时立即释放上一个句柄，<see cref="Dispose"/> 在应用销毁时释放当前句柄。
/// 新窗口通过 <see cref="Current"/> 复用同一句柄，不再逐窗口重复加载。
/// </para>
/// <para>
/// 线程安全：加载、替换与释放均在内部锁内完成，可在任意线程调用。
/// </para>
/// </summary>
internal sealed class Win32AppIconCache : IDisposable
{
    /// <summary>
    /// 图标加载委托（字节 → HICON），默认使用 <see cref="WindowsPlatformApp.LoadIconFromBytes"/>。
    /// 可注入以便单测统计加载次数与失败分支。
    /// </summary>
    private readonly Func<byte[], HICON> _loader;

    /// <summary>
    /// 句柄释放委托，默认调用 Win32 <c>DestroyIcon</c>。可注入以便单测断言释放时机。
    /// </summary>
    private readonly Action<HICON> _releaser;

    /// <summary>
    /// 同步锁，保护 <see cref="_current"/> 与 <see cref="_disposed"/>。
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    /// 当前缓存的图标句柄；无图标时为 NULL。
    /// </summary>
    private HICON _current;

    /// <summary>
    /// 是否已释放。
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// 使用默认加载器（<see cref="WindowsPlatformApp.LoadIconFromBytes"/>）与默认释放器
    /// （Win32 <c>DestroyIcon</c>）构造缓存。
    /// </summary>
    public Win32AppIconCache()
        : this(WindowsPlatformApp.LoadIconFromBytes, WindowsPlatformApp.DestroyIconHandle)
    {
    }

    /// <summary>
    /// 使用可注入的加载器与释放器构造缓存（供单元测试使用）。
    /// </summary>
    /// <param name="loader">图标加载委托。</param>
    /// <param name="releaser">句柄释放委托。</param>
    internal Win32AppIconCache(Func<byte[], HICON> loader, Action<HICON> releaser)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(releaser);
        _loader = loader;
        _releaser = releaser;
    }

    /// <summary>
    /// 当前缓存的图标句柄；未设置图标时为 NULL。
    /// </summary>
    public HICON Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>
    /// 是否已持有图标句柄。
    /// </summary>
    public bool HasIcon => !Current.IsNull;

    /// <summary>
    /// 设置应用图标：加载字节为 HICON 并缓存，同时释放被替换的旧句柄。
    /// <para>
    /// 语义对齐上游 <c>windowsApp.setIcon</c>：加载失败（含 <paramref name="iconBytes"/> 为 null/空）
    /// 时保留当前图标不变，避免一次错误调用把已生效的图标清空。
    /// </para>
    /// </summary>
    /// <param name="iconBytes">图标字节数据（.ico 内容）。</param>
    /// <returns>当前生效的图标句柄；加载失败且无旧图标时返回 NULL。</returns>
    public HICON Set(byte[]? iconBytes)
    {
        lock (_gate)
        {
            if (_disposed || iconBytes is null || iconBytes.Length == 0)
            {
                return _current;
            }

            var loaded = _loader(iconBytes);
            if (loaded.IsNull)
            {
                return _current;
            }

            var previous = _current;
            _current = loaded;

            // 替换时立即释放旧句柄，对应上游 setIcon 中的 DestroyIcon(previousIcon)。
            if (!previous.IsNull)
            {
                _releaser(previous);
            }

            return loaded;
        }
    }

    /// <summary>
    /// 释放当前图标句柄并清空缓存。重复调用安全。
    /// 对应上游 <c>windowsApp.destroy</c> 中的 <c>DestroyIcon(currentIcon)</c>。
    /// </summary>
    public void Dispose()
    {
        HICON current;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            current = _current;
            _current = default;
        }

        if (!current.IsNull)
        {
            _releaser(current);
        }
    }
}
