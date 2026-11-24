using System.Text.Json;
using TUnit.Core;
using Wails.Net.Application.Events;
using Wails.Net.Application.Options;
using Wails.Net.Application.Tests.Stubs;
using Wails.Net.Events;

namespace Wails.Net.Application.Tests;

/// <summary>
/// 应用级明暗主题模式（Wails.Net 扩展）的测试。
/// <para>
/// 覆盖 <see cref="ApplicationOptions.ThemeMode"/>、<see cref="Application.SetThemeMode"/>、
/// <see cref="Application.IsDarkTheme"/> 的三态解析，以及
/// <see cref="KnownEvents.ThemeChanged"/> 事件负载（供前端同步页面主题）。
/// </para>
/// </summary>
[NotInParallel]
public sealed class ThemeModeTests
{
    /// <summary>
    /// 默认模式应为「按系统」，此时实际明暗取自平台（即系统设置）。
    /// </summary>
    [Test]
    public async Task DefaultMode_FollowsSystem()
    {
        var app = new Application(new ApplicationOptions());
        var platform = new StubPlatformApp { IsDarkModeResult = true };
        app.SetPlatformApp(platform);

        await Assert.That(app.ThemeMode).IsEqualTo(ThemeMode.System);
        await Assert.That(app.IsDarkTheme).IsTrue();
    }

    /// <summary>
    /// 构造期传入的模式应下推给平台（保证首个窗口即按该模式创建）。
    /// </summary>
    [Test]
    public async Task OptionsMode_PushedToPlatformOnAttach()
    {
        var app = new Application(new ApplicationOptions { ThemeMode = ThemeMode.Dark });
        var platform = new StubPlatformApp();
        app.SetPlatformApp(platform);

        await Assert.That(app.ThemeMode).IsEqualTo(ThemeMode.Dark);
        await Assert.That(platform.LastThemeMode).IsEqualTo(ThemeMode.Dark);
        await Assert.That(app.IsDarkTheme).IsTrue();
    }

    /// <summary>
    /// 锁定暗黑时忽略系统明亮：实际明暗为暗，并向平台下推模式。
    /// </summary>
    [Test]
    public async Task Dark_OverridesLightSystem()
    {
        var app = new Application(new ApplicationOptions());
        var platform = new StubPlatformApp { IsDarkModeResult = false };
        app.SetPlatformApp(platform);

        app.SetThemeMode(ThemeMode.Dark);

        await Assert.That(app.IsDarkTheme).IsTrue();
        await Assert.That(platform.LastThemeMode).IsEqualTo(ThemeMode.Dark);
    }

    /// <summary>
    /// 锁定明亮时忽略系统暗色。
    /// </summary>
    [Test]
    public async Task Light_OverridesDarkSystem()
    {
        var app = new Application(new ApplicationOptions());
        var platform = new StubPlatformApp { IsDarkModeResult = true };
        app.SetPlatformApp(platform);

        app.SetThemeMode(ThemeMode.Light);

        await Assert.That(app.IsDarkTheme).IsFalse();
    }

    /// <summary>
    /// 从锁定模式回到「按系统」时，实际明暗重新跟随系统。
    /// </summary>
    [Test]
    public async Task BackToSystem_ReReadsSystemMode()
    {
        var app = new Application(new ApplicationOptions());
        var platform = new StubPlatformApp { IsDarkModeResult = true };
        app.SetPlatformApp(platform);

        app.SetThemeMode(ThemeMode.Light);
        await Assert.That(app.IsDarkTheme).IsFalse();

        app.SetThemeMode(ThemeMode.System);
        await Assert.That(app.IsDarkTheme).IsTrue();
    }

    /// <summary>
    /// 模式未变化时不应重复广播事件（避免前端无谓重绘）。
    /// </summary>
    [Test]
    public async Task SetSameMode_DoesNotEmitTwice()
    {
        var app = new Application(new ApplicationOptions());
        app.SetPlatformApp(new StubPlatformApp());

        var count = 0;
        app.Events.On(KnownEvents.ThemeChanged, _ => Interlocked.Increment(ref count));

        app.SetThemeMode(ThemeMode.Dark);
        app.SetThemeMode(ThemeMode.Dark);

        await Assert.That(count).IsEqualTo(1);
    }

    /// <summary>
    /// 事件负载应携带三态模式（小写）与解析后的实际明暗，前端可直接使用。
    /// </summary>
    [Test]
    public async Task EmitThemeChanged_CarriesModeAndIsDark()
    {
        var app = new Application(new ApplicationOptions());
        app.SetPlatformApp(new StubPlatformApp());

        object? payload = null;
        app.Events.On(KnownEvents.ThemeChanged, e => payload = e.Data);

        app.SetThemeMode(ThemeMode.Dark);

        await Assert.That(payload).IsNotNull();

        var json = JsonSerializer.Serialize(payload);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        await Assert.That(root.GetProperty("themeMode").GetString()).IsEqualTo("dark");
        await Assert.That(root.GetProperty("isDark").GetBoolean()).IsTrue();
    }

    /// <summary>
    /// 「按系统」模式下的负载应为 <c>system</c>，且 isDark 跟随平台解析结果。
    /// </summary>
    [Test]
    public async Task CreateThemeChangedPayload_SystemMode()
    {
        var app = new Application(new ApplicationOptions());
        app.SetPlatformApp(new StubPlatformApp { IsDarkModeResult = true });

        var json = JsonSerializer.Serialize(app.CreateThemeChangedPayload());
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        await Assert.That(root.GetProperty("themeMode").GetString()).IsEqualTo("system");
        await Assert.That(root.GetProperty("isDark").GetBoolean()).IsTrue();
    }

    /// <summary>
    /// 三个平台（Windows 标题栏自绘、macOS 标题栏附件视图、Linux HeaderBar 按钮）
    /// 共用同一循环顺序：按系统 → 暗黑 → 明亮 → 按系统。
    /// </summary>
    [Test]
    public async Task Cycle_OrderIsSystemThenDarkThenLight()
    {
        await Assert.That(ThemeModeCycle.Next(ThemeMode.System)).IsEqualTo(ThemeMode.Dark);
        await Assert.That(ThemeModeCycle.Next(ThemeMode.Dark)).IsEqualTo(ThemeMode.Light);
        await Assert.That(ThemeModeCycle.Next(ThemeMode.Light)).IsEqualTo(ThemeMode.System);
    }

    /// <summary>
    /// 循环三次应回到起点（保证按钮反复点击不会漂移到无效态）。
    /// </summary>
    [Test]
    public async Task Cycle_ThreeSteps_ReturnsToStart()
    {
        foreach (var start in new[] { ThemeMode.System, ThemeMode.Light, ThemeMode.Dark })
        {
            var mode = start;
            for (var i = 0; i < 3; i++)
            {
                mode = ThemeModeCycle.Next(mode);
            }

            await Assert.That(mode).IsEqualTo(start);
        }
    }

    /// <summary>
    /// 线上名称固定为小写，前端据此写入 <c>data-theme</c>。
    /// </summary>
    [Test]
    public async Task ToWireName_ReturnsLowercase()
    {
        await Assert.That(ThemeModeCycle.ToWireName(ThemeMode.System)).IsEqualTo("system");
        await Assert.That(ThemeModeCycle.ToWireName(ThemeMode.Light)).IsEqualTo("light");
        await Assert.That(ThemeModeCycle.ToWireName(ThemeMode.Dark)).IsEqualTo("dark");
    }

    /// <summary>
    /// 按钮提示文案同时表达「当前模式」与「点击后的模式」，三个平台共用。
    /// </summary>
    [Test]
    public async Task Describe_MentionsCurrentAndNextMode()
    {
        await Assert.That(ThemeModeCycle.Describe(ThemeMode.System)).Contains("按系统");
        await Assert.That(ThemeModeCycle.Describe(ThemeMode.System)).Contains("暗黑");

        await Assert.That(ThemeModeCycle.Describe(ThemeMode.Dark)).Contains("暗黑");
        await Assert.That(ThemeModeCycle.Describe(ThemeMode.Dark)).Contains("明亮");

        await Assert.That(ThemeModeCycle.Describe(ThemeMode.Light)).Contains("明亮");
        await Assert.That(ThemeModeCycle.Describe(ThemeMode.Light)).Contains("按系统");
    }
}
