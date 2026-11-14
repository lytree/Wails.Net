using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Platform;

/// <summary>
/// 平台层窗口只读查询入口（RH-WIN-02 终态）。
/// <para>
/// 背景：改造前四个平台 App 各自持有 <c>Dictionary&lt;uint, XxxWebviewWindow&gt; _windows</c>，
/// 与 <see cref="Managers.WindowManager"/> 的窗口注册表并行存在，形成"四套注册表"。
/// 平台侧的用途只有两类：<b>遍历所有窗口</b>（Show/Hide/应用菜单/图标/暗色模式/取主窗口句柄）
/// 与<b>按 ID 找窗口</b>（GetCurrentWindowId）。两类都可以退化为对唯一事实源的只读查询。
/// </para>
/// <para>
/// 实现方：<see cref="Managers.WindowManager"/>（唯一注册表）。
/// 消费方：各平台 <see cref="IPlatformApp"/> 实现，通过
/// <see cref="IPlatformApp.AttachWindowAccessor"/> 注入，<b>不再自行维护窗口字典</b>。
/// </para>
/// <para>
/// 返回的是平台实现接口 <see cref="IWebviewWindowImpl"/> 快照；平台在遍历时用
/// <c>is Win32WebviewWindow</c> 这类类型判断取回自身实现（零反射，AGENTS.md §3.4）。
/// </para>
/// </summary>
public interface IPlatformWindowAccessor
{
    /// <summary>
    /// 获取当前所有已创建窗口的平台实现快照（按窗口 ID 升序）。
    /// <para>
    /// 只包含已完成平台创建并回填实现的窗口（<see cref="WebviewWindow.Impl"/> 非 null）；
    /// 正处于创建过程中的窗口不会出现在快照中，避免平台拿到半初始化实例。
    /// </para>
    /// <para>
    /// 按 ID 升序返回，使平台侧"取第一个窗口"（主窗口 / 当前窗口回退）有确定语义。
    /// </para>
    /// </summary>
    /// <returns>平台窗口实现只读快照（ID 升序）；无窗口时返回空列表。</returns>
    IReadOnlyList<IWebviewWindowImpl> EnumeratePlatformWindows();
}
