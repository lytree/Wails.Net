using System.Text;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Wails.Net.Application.Platform;

/// <summary>
/// Win32 高级封装工具类，提供窗口枚举、查找、属性查询等能力。
/// 对应 Go 版 pkg/w32/ 中的高级 Win32 封装。
/// 通过 CsWin32 源生成器调用 Win32 API，不使用 PInvoke.* 包。
/// </summary>
public static class Win32Utilities
{
    /// <summary>
    /// 枚举所有顶层窗口。
    /// 对应 Win32 EnumWindows API。
    /// </summary>
    /// <returns>所有顶层窗口句柄列表。</returns>
    public static List<IntPtr> EnumerateWindows()
    {
        var handles = new List<IntPtr>();
        PInvoke.EnumWindows((hwnd, lParam) =>
        {
            handles.Add((IntPtr)hwnd);
            return true;
        }, IntPtr.Zero);
        return handles;
    }

    /// <summary>
    /// 按类名查找顶层窗口。
    /// 对应 Win32 FindWindowW API。
    /// </summary>
    /// <param name="className">窗口类名，null 表示忽略类名。</param>
    /// <param name="windowName">窗口标题，null 表示忽略标题。</param>
    /// <returns>窗口句柄，未找到返回 IntPtr.Zero。</returns>
    public static IntPtr FindWindow(string? className, string? windowName)
    {
        return (IntPtr)PInvoke.FindWindow(className, windowName);
    }

    /// <summary>
    /// 在子窗口中查找指定类名的窗口。
    /// 对应 Win32 FindWindowExW API。
    /// </summary>
    /// <param name="parent">父窗口句柄，IntPtr.Zero 表示桌面。</param>
    /// <param name="childAfter">从此子窗口之后开始查找，IntPtr.Zero 表示从第一个开始。</param>
    /// <param name="className">窗口类名。</param>
    /// <param name="windowName">窗口标题，null 表示忽略。</param>
    /// <returns>窗口句柄，未找到返回 IntPtr.Zero。</returns>
    public static IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string? windowName)
    {
        return (IntPtr)PInvoke.FindWindowEx((HWND)parent, (HWND)childAfter, className, windowName);
    }

    /// <summary>
    /// 获取窗口标题文本。
    /// 对应 Win32 GetWindowTextW + GetWindowTextLengthW API。
    /// </summary>
    /// <param name="hwnd">窗口句柄。</param>
    /// <returns>窗口标题，失败返回空字符串。</returns>
    public static string GetWindowTitle(IntPtr hwnd)
    {
        var length = PInvoke.GetWindowTextLength((HWND)hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        Span<char> buffer = new char[length + 1];
        var written = PInvoke.GetWindowText((HWND)hwnd, buffer);
        return written > 0 ? new string(buffer[..written]) : string.Empty;
    }

    /// <summary>
    /// 获取窗口类名。
    /// 对应 Win32 GetClassNameW API。
    /// </summary>
    /// <param name="hwnd">窗口句柄。</param>
    /// <returns>窗口类名，失败返回空字符串。</returns>
    public static string GetWindowClassName(IntPtr hwnd)
    {
        Span<char> buffer = new char[256];
        var written = PInvoke.GetClassName((HWND)hwnd, buffer);
        return written > 0 ? new string(buffer[..written]) : string.Empty;
    }

    /// <summary>
    /// 获取窗口所属进程 ID。
    /// 对应 Win32 GetWindowThreadProcessId API。
    /// </summary>
    /// <param name="hwnd">窗口句柄。</param>
    /// <returns>进程 ID。</returns>
    public static uint GetWindowProcessId(IntPtr hwnd)
    {
        uint processId = 0;
        PInvoke.GetWindowThreadProcessId((HWND)hwnd, out processId);
        return processId;
    }

    /// <summary>
    /// 获取窗口所属线程 ID。
    /// </summary>
    /// <param name="hwnd">窗口句柄。</param>
    /// <returns>线程 ID。</returns>
    public static uint GetWindowThreadId(IntPtr hwnd)
    {
        uint processId = 0;
        return PInvoke.GetWindowThreadProcessId((HWND)hwnd, out processId);
    }

    /// <summary>
    /// 判断窗口句柄是否有效。
    /// </summary>
    /// <param name="hwnd">窗口句柄。</param>
    /// <returns>有效返回 true。</returns>
    public static bool IsWindow(IntPtr hwnd)
    {
        return PInvoke.IsWindow((HWND)hwnd);
    }

    /// <summary>
    /// 获取父窗口句柄。
    /// </summary>
    /// <param name="hwnd">子窗口句柄。</param>
    /// <returns>父窗口句柄，无父窗口返回 IntPtr.Zero。</returns>
    public static IntPtr GetParentWindow(IntPtr hwnd)
    {
        return (IntPtr)PInvoke.GetParent((HWND)hwnd);
    }

    /// <summary>
    /// 设置窗口属性（SetProp）。
    /// </summary>
    /// <param name="hwnd">窗口句柄。</param>
    /// <param name="name">属性名。</param>
    /// <param name="value">属性值。</param>
    /// <returns>成功返回 true。</returns>
    public static bool SetWindowProperty(IntPtr hwnd, string name, IntPtr value)
    {
        using var safeHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle(value, ownsHandle: false);
        return PInvoke.SetProp((HWND)hwnd, name, safeHandle);
    }

    /// <summary>
    /// 获取窗口属性（GetProp）。
    /// </summary>
    /// <param name="hwnd">窗口句柄。</param>
    /// <param name="name">属性名。</param>
    /// <returns>属性值句柄，不存在返回 IntPtr.Zero。</returns>
    public static IntPtr GetWindowProperty(IntPtr hwnd, string name)
    {
        var handle = PInvoke.GetProp((HWND)hwnd, name);
        var result = handle.DangerousGetHandle();
        handle.SetHandleAsInvalid();
        return result;
    }

    /// <summary>
    /// 删除窗口属性（RemoveProp）。
    /// </summary>
    /// <param name="hwnd">窗口句柄。</param>
    /// <param name="name">属性名。</param>
    /// <returns>被删除的属性值句柄。</returns>
    public static IntPtr RemoveWindowProperty(IntPtr hwnd, string name)
    {
        var handle = PInvoke.RemoveProp((HWND)hwnd, name);
        var result = handle.DangerousGetHandle();
        handle.SetHandleAsInvalid();
        return result;
    }

    /// <summary>
    /// 枚举指定父窗口的所有子窗口。
    /// </summary>
    /// <param name="parentHwnd">父窗口句柄，IntPtr.Zero 枚举桌面所有子窗口。</param>
    /// <returns>子窗口句柄列表。</returns>
    public static List<IntPtr> EnumerateChildWindows(IntPtr parentHwnd)
    {
        var handles = new List<IntPtr>();
        PInvoke.EnumChildWindows(
            (HWND)parentHwnd,
            (hwnd, lParam) =>
            {
                handles.Add((IntPtr)hwnd);
                return true;
            },
            IntPtr.Zero);
        return handles;
    }

    /// <summary>
    /// 按类名查找所有匹配的顶层窗口。
    /// </summary>
    /// <param name="className">窗口类名。</param>
    /// <returns>匹配的窗口句柄列表。</returns>
    public static List<IntPtr> FindWindowsByClassName(string className)
    {
        var result = new List<IntPtr>();
        foreach (var hwnd in EnumerateWindows())
        {
            if (string.Equals(GetWindowClassName(hwnd), className, StringComparison.Ordinal))
            {
                result.Add(hwnd);
            }
        }

        return result;
    }

    /// <summary>
    /// 按窗口标题查找窗口（模糊匹配）。
    /// </summary>
    /// <param name="titlePart">标题片段（不区分大小写）。</param>
    /// <returns>匹配的窗口句柄列表。</returns>
    public static List<IntPtr> FindWindowsByTitle(string titlePart)
    {
        var result = new List<IntPtr>();
        foreach (var hwnd in EnumerateWindows())
        {
            var title = GetWindowTitle(hwnd);
            if (title.Contains(titlePart, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(hwnd);
            }
        }

        return result;
    }

    /// <summary>
    /// 枚举所有可见的顶层窗口。
    /// </summary>
    /// <returns>可见窗口句柄列表。</returns>
    public static List<IntPtr> EnumerateVisibleWindows()
    {
        var result = new List<IntPtr>();
        foreach (var hwnd in EnumerateWindows())
        {
            if (PInvoke.IsWindowVisible((HWND)hwnd))
            {
                var title = GetWindowTitle(hwnd);
                if (!string.IsNullOrEmpty(title))
                {
                    result.Add(hwnd);
                }
            }
        }

        return result;
    }
}
