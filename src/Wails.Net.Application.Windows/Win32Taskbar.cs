using System.Runtime.InteropServices;

namespace Wails.Net.Application.Windows;

/// <summary>
/// Windows 任务栏进度条和叠加图标的 COM 互操作封装。
/// 对应 Windows Shell API 的 ITaskbarList3 接口（Windows 7+）。
/// 手写 COM 互操作以避免依赖 PInvoke.* 包（符合 AGENTS.md 禁令）。
/// </summary>
internal static class Win32Taskbar
{
    /// <summary>
    /// TaskbarList 的 CLSID：{56FDF344-FD6D-11d0-958A-006097C9A090}。
    /// </summary>
    internal static readonly Guid ClsidTaskbarList = new(0x56FDF344, 0xFD6D, 0x11D0, 0x95, 0x8A, 0x00, 0x60, 0x97, 0xC9, 0xA0, 0x90);

    /// <summary>
    /// ITaskbarList3 的 IID：{EA1AFB91-9E28-4B86-90E9-9E9F8A5E4A6F}。
    /// </summary>
    internal static readonly Guid IidITaskbarList3 = new(0xEA1AFB91, 0x9E28, 0x4B86, 0x90, 0xE9, 0x9E, 0x9F, 0x8A, 0x5E, 0x4A, 0x6F);

    /// <summary>
    /// 创建 ITaskbarList3 COM 实例。
    /// </summary>
    /// <returns>ITaskbarList3 实例，失败返回 null。</returns>
    internal static ITaskbarList3? CreateTaskbarList()
    {
        try
        {
            var type = Type.GetTypeFromCLSID(ClsidTaskbarList);
            if (type is null)
            {
                return null;
            }

            var instance = Activator.CreateInstance(type);
            if (instance is null)
            {
                return null;
            }

            var ptr = Marshal.GetIUnknownForObject(instance);
            try
            {
                var taskbar = (ITaskbarList3)Marshal.GetObjectForIUnknown(ptr);
                taskbar.HrInit();
                return taskbar;
            }
            finally
            {
                Marshal.Release(ptr);
            }
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// ITaskbarList COM 接口（Windows Shell API）。
/// 提供 HrInit、AddTab、DeleteTab、ActivateTab、SetActiveTab 方法。
/// </summary>
[ComImport]
[Guid("CB4020C3-E288-41B2-838E-86A5D8A6A6E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITaskbarList
{
    /// <summary>初始化任务栏列表对象。</summary>
    void HrInit();

    /// <summary>向任务栏添加一个标签。</summary>
    /// <param name="hwnd">窗口句柄。</param>
    void AddTab(IntPtr hwnd);

    /// <summary>从任务栏删除一个标签。</summary>
    /// <param name="hwnd">窗口句柄。</param>
    void DeleteTab(IntPtr hwnd);

    /// <summary>激活任务栏上的标签。</summary>
    /// <param name="hwnd">窗口句柄。</param>
    void ActivateTab(IntPtr hwnd);

    /// <summary>设置活动标签。</summary>
    /// <param name="hwnd">窗口句柄。</param>
    void SetActiveTab(IntPtr hwnd);
}

/// <summary>
/// ITaskbarList2 COM 接口，扩展 ITaskbarList。
/// 提供 MarkFullscreenWindow 方法。
/// </summary>
[ComImport]
[Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5E4AF9")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITaskbarList2 : ITaskbarList
{
    /// <summary>将窗口标记为全屏窗口。</summary>
    /// <param name="hwnd">窗口句柄。</param>
    /// <param name="fullscreen">是否为全屏。</param>
    void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
}

/// <summary>
/// ITaskbarList3 COM 接口，扩展 ITaskbarList2。
/// 提供任务栏进度条和叠加图标功能。
/// 仅列出本框架需要的方法（SetProgressValue、SetProgressState、SetOverlayIcon），
/// 其余方法（RegisterTab、UnregisterTab 等）省略以保持简洁。
/// </summary>
[ComImport]
[Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5E4A6F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITaskbarList3 : ITaskbarList2
{
    /// <summary>设置进度条已完成值。</summary>
    /// <param name="hwnd">窗口句柄。</param>
    /// <param name="completed">已完成值。</param>
    /// <param name="total">总值。</param>
    void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);

    /// <summary>设置进度条状态。</summary>
    /// <param name="hwnd">窗口句柄。</param>
    /// <param name="state">状态标志（对应 TBPF 枚举值）。</param>
    void SetProgressState(IntPtr hwnd, uint state);

    /// <summary>注册任务栏标签。</summary>
    /// <param name="hwndTab">标签窗口句柄。</param>
    /// <param name="hwndMDI">MDI 父窗口句柄。</param>
    void RegisterTab(IntPtr hwndTab, IntPtr hwndMDI);

    /// <summary>取消注册任务栏标签。</summary>
    /// <param name="hwndTab">标签窗口句柄。</param>
    void UnregisterTab(IntPtr hwndTab);

    /// <summary>设置标签顺序。</summary>
    /// <param name="hwndTab">标签窗口句柄。</param>
    /// <param name="hwndInsertBefore">前一个窗口句柄。</param>
    void SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);

    /// <summary>设置活动标签。</summary>
    /// <param name="hwndTab">标签窗口句柄。</param>
    /// <param name="hwndMDI">MDI 父窗口句柄。</param>
    void SetTabActive(IntPtr hwndTab, IntPtr hwndMDI);

    /// <summary>设置任务栏叠加图标。</summary>
    /// <param name="hwnd">窗口句柄。</param>
    /// <param name="hIcon">图标句柄，IntPtr.Zero 清除。</param>
    /// <param name="description">无障碍描述文本。</param>
    void SetOverlayIcon(IntPtr hwnd, IntPtr hIcon, [MarshalAs(UnmanagedType.LPWStr)] string? description);
}
