using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Wails.Net.Application.Platform;

/// <summary>
/// Windows 主题处理：暗色模式（DWM 标题栏 + uxtheme 应用级暗色 + 原生菜单主题）。
/// 对应 Wails v3 Go 版本 <c>v3/pkg/w32/theme.go</c>。
/// </summary>
/// <remarks>
/// 对齐 Wails v3（master，v3.0.0-beta.3/4）的关键修复：
/// <list type="bullet">
/// <item><b>uxtheme 应用级暗色</b>：<c>SetPreferredAppMode(AllowDark)</c> 让原生控件（菜单、滚动条）跟随暗色；
///     相关导出按 ordinal 导出（无名称），从 Windows 10 1809（build 17763，含 Windows Server 2019）起可用，
///     门槛不可设更高（此前限制在 18334 导致 1809/Server 2019 上菜单不可读）。</item>
/// <item><b>菜单可读性回退</b>：应用请求暗色但系统未启用暗色（<c>ShouldAppsUseDarkMode</c> 为假）时，
///     菜单不强暗色（保持浅色背景 + 深色文字），避免「暗底暗字」不可读。</item>
/// <item><b>AllowDarkModeForWindow 传 HWND</b>：窗口级暗色 opt-in 必须传入窗口句柄，
///     漏传会导致该窗口的菜单/控件暗色 opt-in 失效。</item>
/// </list>
/// </remarks>
internal static class Win32Theme
{
    // uxtheme 暗色相关导出（无导出名，仅 ordinal）。
    // 对应 v3 theme.go init() 中 GetProcAddressByOrdinal 的加载清单。
    private const int OrdinalShouldAppsUseDarkMode = 132;               // 系统是否启用应用暗色
    private const int OrdinalAllowDarkModeForWindow = 133;              // 窗口级暗色 opt-in
    private const int OrdinalSetPreferredAppMode = 135;                 // 应用级暗色 opt-in
    private const int OrdinalFlushMenuThemes = 136;                     // 刷新菜单主题缓存
    private const int OrdinalRefreshImmersiveColorPolicyState = 104;    // 刷新沉浸式颜色策略

    /// <summary>SetPreferredAppMode 的 AllowDark 模式值（17763 起该 ordinal 即此语义）。</summary>
    private const int PreferredAppModeAllowDark = 1;

    private const int DwmwaUseImmersiveDarkModeBefore20h1 = 19;
    private const int DwmwaUseImmersiveDarkMode = 20;

    /// <summary>uxtheme 暗色导出自 Windows 10 1809（build 17763）起可用，含 Windows Server 2019。</summary>
    private static readonly bool s_supported = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763);

    private static bool s_initialized;
    private static nint s_shouldAppsUseDarkMode;
    private static nint s_allowDarkModeForWindow;
    private static nint s_setPreferredAppMode;
    private static nint s_flushMenuThemes;
    private static nint s_refreshImmersiveColorPolicyState;

    /// <summary>当前系统是否支持 uxtheme 暗色 API（Windows 10 1809 / Server 2019 及以上）。</summary>
    public static bool IsSupported => s_supported;

    /// <summary>
    /// 初始化 uxtheme 暗色支持：按 ordinal 加载导出并执行应用级暗色 opt-in。
    /// 幂等；应在创建任何窗口前调用（SetPreferredAppMode 为进程级设置）。
    /// </summary>
    public static void Initialize()
    {
        if (!s_supported || s_initialized)
        {
            return;
        }

        var hUxtheme = LoadLibraryW("uxtheme.dll");
        if (hUxtheme == 0)
        {
            return;
        }

        try
        {
            s_shouldAppsUseDarkMode = GetProcAddressByOrdinal(hUxtheme, OrdinalShouldAppsUseDarkMode);
            s_allowDarkModeForWindow = GetProcAddressByOrdinal(hUxtheme, OrdinalAllowDarkModeForWindow);
            s_setPreferredAppMode = GetProcAddressByOrdinal(hUxtheme, OrdinalSetPreferredAppMode);
            s_flushMenuThemes = GetProcAddressByOrdinal(hUxtheme, OrdinalFlushMenuThemes);
            s_refreshImmersiveColorPolicyState = GetProcAddressByOrdinal(hUxtheme, OrdinalRefreshImmersiveColorPolicyState);

            // 应用级暗色 opt-in：让原生控件（菜单、滚动条等）跟随暗色。
            // 对应 v3 theme.go init()：SetPreferredAppMode(PreferredAppModeAllowDark) + RefreshImmersiveColorPolicyState。
            if (s_setPreferredAppMode != 0)
            {
                InvokeInt1(s_setPreferredAppMode, PreferredAppModeAllowDark);
                if (s_refreshImmersiveColorPolicyState != 0)
                {
                    InvokeVoid(s_refreshImmersiveColorPolicyState);
                }
            }

            s_initialized = true;
        }
        finally
        {
            FreeLibrary(hUxtheme);
        }
    }

    /// <summary>系统是否处于暗色模式（注册表 AppsUseLightTheme == 0，对应 v3 IsCurrentlyDarkMode）。</summary>
    public static bool IsCurrentlyDarkMode()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            // 注册表查询失败时视为浅色模式。
            return false;
        }
    }

    /// <summary>系统是否启用应用暗色（uxtheme ordinal 132，对应 v3 ShouldAppsUseDarkMode）。</summary>
    public static bool ShouldAppsUseDarkMode()
    {
        if (s_shouldAppsUseDarkMode == 0)
        {
            return false;
        }

        return InvokeVoid(s_shouldAppsUseDarkMode) != 0;
    }

    /// <summary>
    /// 应用完整窗口主题：DWM 标题栏暗色 + 原生菜单主题。
    /// 对应 v3 theme.go SetTheme。
    /// </summary>
    /// <param name="hwnd">窗口句柄。</param>
    /// <param name="useDarkMode">是否暗色模式。</param>
    public static void SetTheme(HWND hwnd, bool useDarkMode)
    {
        if (hwnd.IsNull)
        {
            return;
        }

        if (!s_supported)
        {
            return;
        }

        // DWM 标题栏暗色：20H1（18985）前使用 attribute 19（DwmwaUseImmersiveDarkModeBefore20h1）。
        var attr = DwmwaUseImmersiveDarkMode;
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18985))
        {
            attr = DwmwaUseImmersiveDarkModeBefore20h1;
        }

        var value = useDarkMode ? 1 : 0;
        unsafe
        {
            PInvoke.DwmSetWindowAttribute(hwnd, (DWMWINDOWATTRIBUTE)attr, &value, (uint)sizeof(int));
        }

        SetMenuTheme(hwnd, useDarkMode);
    }

    /// <summary>
    /// 设置窗口原生菜单主题（对应 v3 theme.go SetMenuTheme）。
    /// 关键回退：应用请求暗色但系统未启用暗色时，菜单不强暗色（保持浅色背景 + 深色文字），
    /// 避免「暗底暗字」不可读。
    /// </summary>
    /// <param name="hwnd">窗口句柄。</param>
    /// <param name="useDarkMode">是否暗色模式。</param>
    public static void SetMenuTheme(HWND hwnd, bool useDarkMode)
    {
        if (!s_supported || hwnd.IsNull)
        {
            return;
        }

        // 回退：系统未启用应用暗色时，菜单不强暗色（否则暗底暗字不可读）。
        if (useDarkMode && s_shouldAppsUseDarkMode != 0 && !ShouldAppsUseDarkMode())
        {
            useDarkMode = false;
        }

        var themeName = useDarkMode ? "DarkMode_Explorer" : "Explorer";
        SetWindowTheme(hwnd, themeName, null);

        if (s_refreshImmersiveColorPolicyState != 0)
        {
            InvokeVoid(s_refreshImmersiveColorPolicyState);
        }

        if (s_flushMenuThemes != 0)
        {
            InvokeVoid(s_flushMenuThemes);
        }

        // 窗口级暗色 opt-in：必须传 HWND（v3 #5877 修复，此前漏传导致菜单暗色 opt-in 失效）。
        if (s_allowDarkModeForWindow != 0)
        {
            InvokeInt2(s_allowDarkModeForWindow, hwnd, useDarkMode ? 1 : 0);
        }

        // 强制重绘，使主题立即生效。
        PInvoke.InvalidateRect(hwnd, null, true);
    }

    // ---- 原生调用（uxtheme ordinal 导出无法由 CsWin32 生成，手动声明，参照项目既有 DllImport 先例）----

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadLibraryW(string lpFileName);

    [DllImport("kernel32", SetLastError = true)]
    private static extern int FreeLibrary(nint hModule);

    [DllImport("kernel32", SetLastError = true)]
    private static extern nint GetProcAddress(nint hModule, nint lpProcName);

    /// <summary>按 ordinal 获取导出：lpProcName 为 IS_INTRESOURCE（值 &lt; 0x10000）时按导出序数解析。</summary>
    private static nint GetProcAddressByOrdinal(nint hModule, int ordinal)
    {
        return GetProcAddress(hModule, new nint(ordinal));
    }

    [DllImport("uxtheme", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(HWND hwnd, string? pszSubAppName, string? pszSubIdList);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ProcVoid();

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ProcInt1(int value);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ProcInt2(HWND hwnd, int value);

    private static int InvokeVoid(nint proc)
    {
        return Marshal.GetDelegateForFunctionPointer<ProcVoid>(proc)();
    }

    private static int InvokeInt1(nint proc, int value)
    {
        return Marshal.GetDelegateForFunctionPointer<ProcInt1>(proc)(value);
    }

    private static int InvokeInt2(nint proc, HWND hwnd, int value)
    {
        return Marshal.GetDelegateForFunctionPointer<ProcInt2>(proc)(hwnd, value);
    }
}
