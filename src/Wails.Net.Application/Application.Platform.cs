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
/// Application 的平台转发分部（RH-HOST-02 文件级拆分）。<para>承载菜单 / 关于对话框 / 图标 / 显示隐藏 / 屏幕信息 / 暗色模式 / 强调色 / 当前窗口 ID / SetParent 等对 IPlatformApp 的直接转发。public API 与拆分前完全一致，仅按关注点拆分文件。</para>
/// </summary>
public partial class Application
{
    /// <summary>
    /// 获取当前活动窗口的 ID。委托给平台应用 <see cref="IPlatformApp.GetCurrentWindowId"/>。
    /// 对应 Wails v3 Go 版本 application.go 中的 GetCurrentWindowID 方法。
    /// </summary>
    /// <returns>当前活动窗口的 ID；若平台应用未设置则返回 0。</returns>
    public uint GetCurrentWindowID()
    {
        return _platformApp?.GetCurrentWindowId() ?? 0u;
    }

    /// <summary>
    /// 将应用主窗口的父窗口设置为指定平台原生句柄。
    /// 主要用于将应用嵌入到外部宿主进程（如插件宿主、IDE 面板等）。
    /// 委托给平台应用 <see cref="IPlatformApp.SetParent"/>；不支持的平台为 no-op。
    /// 对应 Wails v3 Go 版本 application.go 中的 SetParent 方法。
    /// </summary>
    /// <param name="parent">父窗口的平台原生句柄（如 Win32 HWND）。</param>
    public void SetParent(IntPtr parent)
    {
        _platformApp?.SetParent(parent);
    }

    /// <summary>
    /// 设置应用菜单。委托给平台应用。
    /// 对应 Wails v3 Go 版本 application.go 中的 SetApplicationMenu 方法。
    /// </summary>
    /// <param name="menu">菜单实例，可为 null。</param>
    public void SetApplicationMenu(Menu? menu)
    {
        _platformApp?.SetApplicationMenu(menu);
    }

    /// <summary>
    /// 显示关于对话框。使用应用选项中的名称、描述和图标。
    /// 对应 Wails v3 Go 版本 application.go 中的 ShowAboutDialog 方法。
    /// </summary>
    public void ShowAboutDialog()
    {
        _platformApp?.ShowAboutDialog(_options.Name, _options.Description, _options.Icon);
    }

    /// <summary>
    /// 设置应用图标。委托给平台应用。
    /// 对应 Wails v3 Go 版本 application.go 中的 SetIcon 方法。
    /// </summary>
    /// <param name="icon">图标字节数据，可为 null。</param>
    public void SetIcon(byte[]? icon)
    {
        _platformApp?.SetIcon(icon);
    }

    /// <summary>
    /// 隐藏应用。委托给平台应用。
    /// 对应 Wails v3 Go 版本 application.go 中的 Hide 方法。
    /// </summary>
    public void Hide()
    {
        _platformApp?.Hide();
    }

    /// <summary>
    /// 显示应用。委托给平台应用。
    /// 对应 Wails v3 Go 版本 application.go 中的 Show 方法。
    /// </summary>
    public void Show()
    {
        _platformApp?.Show();
    }

    /// <summary>
    /// 获取主屏幕信息。委托给平台应用。
    /// 对应 Wails v3 Go 版本 application.go 中的 GetPrimaryScreen 方法。
    /// </summary>
    /// <returns>主屏幕实例，若平台应用未设置则返回 null。</returns>
    public Screen? GetPrimaryScreen()
    {
        return _platformApp?.GetPrimaryScreen();
    }

    /// <summary>
    /// 获取所有屏幕信息。委托给平台应用。
    /// 对应 Wails v3 Go 版本 application.go 中的 GetScreens 方法。
    /// </summary>
    /// <returns>屏幕数组，若平台应用未设置则返回空数组。</returns>
    public Screen[] GetScreens()
    {
        return _platformApp?.GetScreens() ?? Array.Empty<Screen>();
    }

    /// <summary>
    /// 判断当前是否为暗色模式。委托给平台应用。
    /// 对应 Wails v3 Go 版本 application.go 中的 IsDarkMode 方法。
    /// </summary>
    /// <returns>若为暗色模式返回 true，否则返回 false。平台应用未设置时返回 false。</returns>
    public bool IsDarkMode()
    {
        return _platformApp?.IsDarkMode() ?? false;
    }

    /// <summary>
    /// 获取系统强调色。委托给平台应用。
    /// 对应 Wails v3 Go 版本 application.go 中的 GetAccentColor 方法。
    /// </summary>
    /// <returns>系统强调色字符串，平台应用未设置时返回 "#000000"。</returns>
    public string GetAccentColor()
    {
        return _platformApp?.GetAccentColor() ?? "#000000";
    }

    /// <summary>
    /// 获取当前应用级明暗主题模式（Wails.Net 扩展）。
    /// <para>
    /// 与 <see cref="IsDarkMode"/> 的区别：<see cref="IsDarkMode"/> 只反映系统设置，
    /// 本属性反映应用实际采用的模式（可能被 <see cref="SetThemeMode"/> 锁定为明亮或暗黑）。
    /// </para>
    /// </summary>
    public ThemeMode ThemeMode => _themeMode;

    /// <summary>
    /// 获取按当前主题模式解析后的实际明暗（Wails.Net 扩展）。
    /// <para>
    /// <see cref="ThemeMode.System"/> 时取 <see cref="IsDarkMode"/>；
    /// <see cref="ThemeMode.Light"/> / <see cref="ThemeMode.Dark"/> 时直接返回锁定值。
    /// </para>
    /// </summary>
    public bool IsDarkTheme => ResolveDarkTheme(_themeMode);

    /// <summary>
    /// 设置应用级明暗主题模式（Wails.Net 扩展），并立即作用于所有窗口。
    /// <para>
    /// 生效范围：平台绘制的界面元素与原生标题栏主题按钮
    /// （Windows 为 DWM 标题栏 / 原生菜单 / 非客户区自绘按钮，
    /// macOS 为 <c>NSApplication.Appearance</c> 与标题栏附件视图，
    /// Linux 为 GTK 暗色偏好与 HeaderBar 按钮）。
    /// 前端页面主题不由框架接管，前端应订阅 <see cref="KnownEvents.ThemeChanged"/> 后
    /// 按 <see cref="ThemeMode"/> / <see cref="IsDarkTheme"/> 自行切换样式。
    /// </para>
    /// <para>
    /// 模式发生变化时会广播 <see cref="KnownEvents.ThemeChanged"/>，负载为
    /// <see cref="CreateThemeChangedPayload"/> 的返回值，前端据此同步页面主题。
    /// </para>
    /// </summary>
    /// <param name="mode">目标主题模式。</param>
    public void SetThemeMode(ThemeMode mode)
    {
        if (_themeMode == mode)
        {
            return;
        }

        _themeMode = mode;
        _platformApp?.SetThemeMode(mode);
        EmitThemeChanged();
    }

    /// <summary>
    /// 构建主题变化事件负载（Wails.Net 扩展）：<c>{ themeMode, isDark }</c>。
    /// <para>
    /// <c>themeMode</c> 取 <c>"system"</c> / <c>"light"</c> / <c>"dark"</c>（小写，便于前端
    /// 直接写入 <c>document.documentElement.dataset.theme</c>）；<c>isDark</c> 为解析后的实际明暗，
    /// 供不区分模式、只关心明暗的前端使用。
    /// </para>
    /// </summary>
    /// <returns>可被 JSON 序列化的事件负载。</returns>
    public object CreateThemeChangedPayload() => new
    {
        themeMode = ThemeModeCycle.ToWireName(_themeMode),
        isDark = IsDarkTheme,
    };

    /// <summary>
    /// 广播主题变化事件（Wails.Net 扩展），统一负载结构。
    /// <para>
    /// 应用主动切换（<see cref="SetThemeMode"/>）与系统主题变化（平台事件、<c>WM_SETTINGCHANGE</c> 等）
    /// 都走这里，前端只需实现一种处理逻辑。
    /// </para>
    /// </summary>
    public void EmitThemeChanged()
    {
        Events.Emit(KnownEvents.ThemeChanged, CreateThemeChangedPayload(), null);
    }

    /// <summary>
    /// 按主题模式解析实际明暗：<see cref="ThemeMode.System"/> 回落到系统设置。
    /// </summary>
    /// <param name="mode">主题模式。</param>
    /// <returns>暗色返回 true，明亮返回 false。</returns>
    internal bool ResolveDarkTheme(ThemeMode mode)
    {
        return mode switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => IsDarkMode(),
        };
    }

}
