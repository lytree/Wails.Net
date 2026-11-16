using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Coordinators;
using Wails.Net.Application.Events;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Invocation;
using Wails.Net.Application.Managers;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Screens;
using Wails.Net.Application.Services;
using Wails.Net.Application.Streams;
using Wails.Net.Application.Transport;
using Wails.Net.Application.Windows;
using Wails.Net.Events;

namespace Wails.Net.Application;

/// <summary>
/// Application 的窗口分部（RH-HOST-02 文件级拆分）。<para>承载窗口创建（配置驱动 / notch）、按 ID / 名称查找与销毁的 facade。public API 与拆分前完全一致，仅按关注点拆分文件。</para>
/// </summary>
public partial class Application
{
    /// <summary>
    /// 设置配置驱动的窗口配置，由 DesktopApplicationBuilder.Build 调用。
    /// 窗口不在 Build（主线程）创建，而是延迟到 Run()（UI 线程）按需创建，
    /// 避免 Win32 窗口线程亲和性导致的死锁。
    /// </summary>
    /// <param name="configuredWindows">多窗口配置列表，可为 null。</param>
    /// <param name="defaultWindow">默认窗口配置，可为 null。</param>
    internal void SetWindowConfigs(
        List<WindowConfig>? configuredWindows,
        DesktopHostOptions.WindowOptions? defaultWindow)
    {
        _windowFactory.SetConfigs(configuredWindows, defaultWindow);
    }

    /// <summary>
    /// 根据配置创建窗口。在 UI 线程的 Run() 方法中调用，
    /// 确保窗口由 STA UI 线程创建，可被该线程的 GetMessage 循环正确处理。
    /// 仅当 OnAfterStart 未设置时执行，避免与用户回调重复创建窗口。
    /// </summary>
    private void CreateWindowsFromConfig() => _windowFactory.CreateAll(CreateWebviewWindow);

    /// <summary>
    /// 创建 Webview 窗口，使用自动递增的 ID。
    /// 若平台应用已设置，则委托给平台应用创建实际窗口。
    /// </summary>
    /// <param name="options">窗口选项。</param>
    /// <returns>新创建的窗口实例。</returns>
    public virtual WebviewWindow CreateWebviewWindow(WebviewWindowOptions options)
    {
        // R1-2：委托给 WindowManager，确保窗口元数据（ID 分配、名称索引、关闭监听）
        // 与平台创建（_platformApp.CreateWebviewWindow）走同一注册表，
        // 修复 R0-3 提到的 EventIPCTransport 枚举窗口丢失 bug。
        var id = _windowManager!.CreateWebviewWindow(options);
        return _windowManager.GetWindow(id)!;
    }

    /// <summary>
    /// 创建 notch 窗口（挂接在 macOS 显示器顶部摄像头刘海区域的独立窗口）。
    /// 对应 Wails v3 Go 版本 <c>notch_window.go</c> 中的
    /// <c>WindowManager.NewNotchWindow</c>。
    /// <para>
    /// 原生几何、外观与动画由 macOS 平台层接管；非 macOS 平台返回空包装
    /// （与 Go 侧 <c>notchWindowsSupported</c> 的降级语义一致）。
    /// 多个 notch 窗口可共存，最近显示者位于最前。
    /// </para>
    /// </summary>
    /// <param name="options">notch 窗口配置。</param>
    /// <returns>notch 窗口包装实例。</returns>
    public virtual NotchWindow CreateNotchWindow(NotchWindowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!OperatingSystem.IsMacOS())
        {
            return new NotchWindow(null);
        }

        return new NotchWindow(CreateWebviewWindow(NotchWindowOptionsBuilder.Build(options)));
    }

    /// <summary>
    /// 将 notch 窗口配置映射为标准窗口选项。
    /// 对应 Wails v3 Go 版本 <c>notch_window.go</c> 的 <c>webviewOptionsForNotchWindow</c>：
    /// 补齐原生形状边距、透明无边框、NSPanel 与非激活浮动面板等固定外观字段。
    /// </summary>
    /// <param name="options">notch 窗口配置。</param>
    /// <returns>标准窗口选项。</returns>
    internal static WebviewWindowOptions BuildNotchWindowOptions(NotchWindowOptions options)
        => NotchWindowOptionsBuilder.Build(options);

    /// <summary>
    /// 根据 ID 获取窗口。
    /// </summary>
    /// <param name="id">窗口 ID。</param>
    /// <returns>匹配的窗口实例，若不存在则返回 null。</returns>
    public virtual WebviewWindow? GetWindow(uint id)
    {
        // R1-2：委托给 WindowManager 单一注册表。
        return _windowManager?.GetWindow(id);
    }

    /// <summary>
    /// 根据名称获取窗口。
    /// </summary>
    /// <param name="name">窗口名称。</param>
    /// <returns>匹配的窗口实例，若不存在则返回 null。</returns>
    public virtual WebviewWindow? GetWindowByName(string name)
    {
        // R1-2：委托给 WindowManager 单一注册表（O(1) 按名称索引）。
        return _windowManager?.GetWindowByName(name);
    }

    /// <summary>
    /// 销毁窗口，从窗口字典中移除并关闭其平台实现。
    /// </summary>
    /// <param name="id">窗口 ID。</param>
    public virtual void DestroyWindow(uint id)
    {
        // R1-2：委托给 WindowManager 单一注册表。
        _windowManager?.DestroyWindow(id);
    }

}
