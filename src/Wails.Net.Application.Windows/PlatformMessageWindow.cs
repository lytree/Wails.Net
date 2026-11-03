using System.Runtime.InteropServices;
using Wails.Net.Application.SystemTray;
using Wails.Net.Events;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using WailsApplication = Wails.Net.Application.Application;

namespace Wails.Net.Application.Windows;

/// <summary>
/// Windows 平台层顶层消息窗口（隐藏、无客户区）。
/// <para>
/// 存在意义：<c>WM_SETTINGCHANGE</c> 等系统消息只广播给**顶层窗口**，
/// 而托盘使用的 message-only 窗口与 webview 窗口并非始终存在——
/// 无 webview 窗口时（例如纯托盘应用）系统主题变化将无人接收。
/// 本窗口由平台层在消息循环前创建，充当应用级消息接收点，
/// 使主题变化事件（<see cref="KnownEvents.ThemeChanged"/>）在任何情况下都能发出，
/// 托盘图标因而能随明暗主题切换。
/// </para>
/// <para>
/// 同时接收 Explorer 重启广播的 <c>TaskbarCreated</c>（同样只发往顶层窗口），
/// 转发给各托盘实例去抖重建图标（Wails v3 <c>v3.1.0</c> PR #6386）。
/// </para>
/// <para>
/// 对应 Wails v3 Go 版本 <c>application_windows.go</c> 中 <c>mainThreadWindow</c> 的
/// <c>WM_SETTINGCHANGE</c> 处理（：280-296，读取 <c>"ImmersiveColorSet"</c> 后发出
/// <c>events.Windows.SystemThemeChanged</c>）。
/// </para>
/// </summary>
internal sealed class PlatformMessageWindow : IDisposable
{
    /// <summary>
    /// 窗口类名称，用于 <c>RegisterClassEx</c> 注册。与 webview 窗口类区分，避免相互干扰。
    /// </summary>
    internal const string WindowClassName = "WailsNetPlatformMessageWindow";

    /// <summary>WM_SETTINGCHANGE（0x001A）。</summary>
    private const uint WmSettingChange = 0x001A;

    /// <summary>WM_CLOSE（0x0010）。</summary>
    private const uint WmClose = 0x0010;

    /// <summary>静态窗口过程委托；必须保持强引用，否则被 GC 回收后窗口过程失效。</summary>
    private static WNDPROC? _wndProc;

    /// <summary>类注册锁。</summary>
    private static readonly object _classLock = new();

    /// <summary>窗口句柄。</summary>
    private HWND _hwnd;

    /// <summary>是否已释放。</summary>
    private bool _disposed;

    /// <summary>窗口句柄；未创建时为 NULL。</summary>
    public HWND Hwnd => _hwnd;

    /// <summary>窗口是否已成功创建。</summary>
    public bool IsCreated => !_hwnd.IsNull;

    /// <summary>
    /// 创建隐藏的顶层窗口。需在将运行消息循环的线程（UI 线程）上调用。
    /// </summary>
    public void Create()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureClassRegistered();

        // dwStyle = 0（不含 WS_VISIBLE）→ 隐藏；hWndParent = null → 顶层窗口，可收到系统广播。
        // PCWSTR 由 string 隐式转换，需处于 unsafe 上下文（与 Win32WebviewWindow 一致）。
        unsafe
        {
            _hwnd = PInvoke.CreateWindowEx(
                dwExStyle: 0,
                lpClassName: WindowClassName,
                lpWindowName: string.Empty,
                dwStyle: 0,
                X: 0,
                Y: 0,
                nWidth: 0,
                nHeight: 0,
                hWndParent: default,
                hMenu: null,
                hInstance: null,
                lpParam: null);
        }
    }

    /// <summary>
    /// 销毁窗口。可安全重复调用。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!_hwnd.IsNull)
        {
            PInvoke.DestroyWindow(_hwnd);
            _hwnd = default;
        }
    }

    /// <summary>
    /// 注册窗口类（幂等）。<c>hInstance</c>/<c>hCursor</c>/<c>hbrBackground</c> 均为默认，
    /// 与 <c>Win32WebviewWindow</c> 的注册方式保持一致。
    /// </summary>
    private static void EnsureClassRegistered()
    {
        if (_wndProc is not null)
        {
            return;
        }

        lock (_classLock)
        {
            if (_wndProc is not null)
            {
                return;
            }

            _wndProc = StaticWndProc;
            unsafe
            {
                fixed (char* className = WindowClassName)
                {
                    var wcx = new WNDCLASSEXW
                    {
                        cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                        lpfnWndProc = _wndProc,
                        hInstance = default,
                        hCursor = default,
                        hbrBackground = default,
                        lpszClassName = className,
                    };
                    PInvoke.RegisterClassEx(in wcx);
                }
            }
        }
    }

    /// <summary>
    /// 静态窗口过程：仅处理 <c>WM_SETTINGCHANGE</c>（主题变化）与 <c>WM_CLOSE</c>，
    /// 其余交回 <c>DefWindowProc</c>。
    /// </summary>
    /// <param name="hWnd">窗口句柄。</param>
    /// <param name="msg">消息 ID。</param>
    /// <param name="wParam">wParam。</param>
    /// <param name="lParam">lParam。</param>
    /// <returns>消息处理结果。</param>
    private static LRESULT StaticWndProc(HWND hWnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        switch (msg)
        {
            case WmSettingChange:
                // lParam 指向变化类别的字符串；"ImmersiveColorSet" 表示系统主题（明/暗）变化。
                if (lParam.Value != 0)
                {
                    try
                    {
                        HandleSettingChange(Marshal.PtrToStringUni(lParam.Value));
                    }
                    catch
                    {
                        // 忽略指针读取异常。
                    }
                }

                return (LRESULT)0;

            case WmClose:
                PInvoke.DestroyWindow(hWnd);
                return (LRESULT)0;

            default:
                // Explorer 重启：TaskbarCreated 只广播给顶层窗口，转发给各托盘实例去抖重建。
                // 注册失败时消息 ID 为 0（WM_NULL），必须排除，避免误触发重建。
                var taskbarCreatedMessage = Win32SystemTray.TaskbarCreatedMessage;
                if (taskbarCreatedMessage != 0 && msg == taskbarCreatedMessage)
                {
                    Win32SystemTray.NotifyTaskbarCreated();
                    return (LRESULT)0;
                }

                return PInvoke.DefWindowProc(hWnd, msg, wParam, lParam);
        }
    }

    /// <summary>
    /// 处理 <c>WM_SETTINGCHANGE</c> 的类别字符串：仅当类别为 <c>"ImmersiveColorSet"</c>
    /// （系统主题明/暗切换）时发出 <see cref="KnownEvents.ThemeChanged"/>。
    /// </summary>
    /// <param name="category">变化类别字符串（对应 lParam 指向的 UTF-16 串）。</param>
    private static void HandleSettingChange(string? category)
    {
        if (category == "ImmersiveColorSet")
        {
            WailsApplication.Get()?.Events.Emit(KnownEvents.ThemeChanged, null, null);
        }
    }

    /// <summary>
    /// 测试用：以指定类别字符串模拟一次 <c>WM_SETTINGCHANGE</c> 处理，
    /// 走与真实窗口过程完全相同的代码路径。
    /// </summary>
    /// <param name="category">变化类别字符串。</param>
    internal static void HandleSettingChangeForTest(string category) => HandleSettingChange(category);
}
