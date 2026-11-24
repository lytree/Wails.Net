namespace Wails.Net.Application.Options;

/// <summary>
/// 应用明暗主题模式（Wails.Net 扩展，非 Wails v3 概念）。
/// <para>
/// 与 <see cref="Wails.Net.Application.Platform.IPlatformApp.IsDarkMode"/> 的区别：
/// 后者只回答「系统当前是否暗色」，本枚举额外表达「应用主动锁定为明亮 / 暗黑」，
/// 用于原生标题栏、原生菜单等平台绘制的界面元素。
/// </para>
/// </summary>
public enum ThemeMode
{
    /// <summary>
    /// 跟随系统（默认）。系统主题变化时自动重算明暗。
    /// </summary>
    System = 0,

    /// <summary>
    /// 强制明亮，忽略系统设置。
    /// </summary>
    Light = 1,

    /// <summary>
    /// 强制暗黑，忽略系统设置。
    /// </summary>
    Dark = 2,
}

/// <summary>
/// <see cref="ThemeMode"/> 的循环切换规则（Wails.Net 扩展）。
/// <para>
/// 原生标题栏主题按钮在三个平台上都按「按系统 → 暗黑 → 明亮 → 按系统」循环，
/// 顺序固定在此处，避免各平台各写一遍导致行为不一致。
/// </para>
/// </summary>
public static class ThemeModeCycle
{
    /// <summary>
    /// 取下一个模式：按系统 → 暗黑 → 明亮 → 按系统。
    /// </summary>
    /// <param name="mode">当前模式。</param>
    /// <returns>下一模式。</returns>
    public static ThemeMode Next(ThemeMode mode) => mode switch
    {
        ThemeMode.System => ThemeMode.Dark,
        ThemeMode.Dark => ThemeMode.Light,
        _ => ThemeMode.System,
    };

    /// <summary>
    /// 取模式的线上名称（事件负载与前端 API 使用的小写值）。
    /// </summary>
    /// <param name="mode">主题模式。</param>
    /// <returns><c>system</c> / <c>light</c> / <c>dark</c>。</returns>
    public static string ToWireName(ThemeMode mode) => mode switch
    {
        ThemeMode.Dark => "dark",
        ThemeMode.Light => "light",
        _ => "system",
    };

    /// <summary>
    /// 取主题切换按钮的提示文案（三个平台共用，避免各平台文案不一致）。
    /// </summary>
    /// <param name="mode">当前模式。</param>
    /// <returns>形如「主题：按系统（点击切换为暗黑）」的文案。</returns>
    public static string Describe(ThemeMode mode) => mode switch
    {
        ThemeMode.Dark => "主题：暗黑（点击切换为明亮）",
        ThemeMode.Light => "主题：明亮（点击切换为按系统）",
        _ => "主题：按系统（点击切换为暗黑）",
    };
}
