using TUnit.Core;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;

namespace Wails.Net.Application.Linux.Tests;

/// <summary>
/// 原生标题栏主题按钮（Linux，Wails.Net 扩展）的测试。
/// <para>
/// 按钮控件本身依赖 GTK，无法在非 Linux 主机实例化；
/// 这里覆盖两处纯逻辑：图标候选映射，以及平台的模式锁定语义
/// （非 Linux / GTK 未初始化时只记录模式，不触碰 GTK）。
/// </para>
/// </summary>
[NotInParallel]
public sealed class LinuxTitleBarThemeButtonTests
{
    /// <summary>
    /// 三种模式各自给出三档候选图标，顺序为「首选 → 兼容 → 兜底」。
    /// </summary>
    [Test]
    public async Task IconCandidates_ProvideThreeFallbacks()
    {
        foreach (var mode in new[] { ThemeMode.System, ThemeMode.Light, ThemeMode.Dark })
        {
            var candidates = LinuxTitleBarThemeButton.IconCandidates(mode);

            await Assert.That(candidates.Count).IsEqualTo(3);
            await Assert.That(string.IsNullOrWhiteSpace(candidates[0])).IsFalse();
        }
    }

    /// <summary>
    /// 明亮 / 暗黑首选天气类 symbolic 图标；按系统用「夜灯 / 亮度」类半明半暗图标。
    /// </summary>
    [Test]
    public async Task IconCandidates_UseSymbolicThemeIcons()
    {
        await Assert.That(LinuxTitleBarThemeButton.IconCandidates(ThemeMode.Light)[0])
            .IsEqualTo("weather-clear-symbolic");
        await Assert.That(LinuxTitleBarThemeButton.IconCandidates(ThemeMode.Dark)[0])
            .IsEqualTo("weather-clear-night-symbolic");
        await Assert.That(LinuxTitleBarThemeButton.IconCandidates(ThemeMode.System)[0])
            .IsEqualTo("night-light-symbolic");
    }

    /// <summary>
    /// 所有候选都必须是 symbolic 图标：GTK 才会按主题自动反色，避免暗色下看不清。
    /// </summary>
    [Test]
    public async Task IconCandidates_AreAllSymbolic()
    {
        foreach (var mode in new[] { ThemeMode.System, ThemeMode.Light, ThemeMode.Dark })
        {
            foreach (var name in LinuxTitleBarThemeButton.IconCandidates(mode))
            {
                await Assert.That(name.EndsWith("-symbolic", StringComparison.Ordinal)).IsTrue();
            }
        }
    }

    /// <summary>
    /// 平台记录锁定模式：暗黑 / 明亮直接决定 <c>IsDarkTheme</c>，不依赖系统设置。
    /// </summary>
    [Test]
    public async Task SetThemeMode_RecordsLockedModes()
    {
        var app = new LinuxPlatformApp(new ApplicationOptions { Name = "ThemeTest" });

        app.SetThemeMode(ThemeMode.Dark);
        await Assert.That(app.IsDarkTheme).IsTrue();

        app.SetThemeMode(ThemeMode.Light);
        await Assert.That(app.IsDarkTheme).IsFalse();
    }
}
