using TUnit.Core;
using Wails.Net.Application.Hosting;

namespace Wails.Net.Application.Tests.Hosting;

/// <summary>
/// 配置对齐单元测试（TUnit）。
/// 对应主题 H-4.6：验证 HostingAppConfig / WindowConfig / SecurityConfig / DesktopHostOptions.App
/// 的默认值、属性读写和配置节绑定。
/// 对应 Tauri v2 的 app/windows/security 配置结构。
/// </summary>
[NotInParallel]
public sealed class ConfigAlignmentTests
{
    /// <summary>
    /// HostingAppConfig.Windows 默认应为空列表（非 null），Security 默认应非 null。
    /// </summary>
    [Test]
    public async Task HostingAppConfig_DefaultEmptyWindows_EmptyList()
    {
        var opts = new HostingAppConfig();

        await Assert.That(opts.Windows).IsNotNull();
        await Assert.That(opts.Windows.Count).IsEqualTo(0);
        await Assert.That(opts.Security).IsNotNull();
    }

    /// <summary>
    /// WindowConfig 默认值：Width=1280, Height=720, Resizable=true, 其余 bool 默认 false。
    /// </summary>
    [Test]
    public async Task WindowConfig_DefaultValues_MatchDefaults()
    {
        var cfg = new WindowConfig();

        await Assert.That(cfg.Width).IsEqualTo(1280);
        await Assert.That(cfg.Height).IsEqualTo(720);
        await Assert.That(cfg.Resizable).IsTrue();
        await Assert.That(cfg.Centered).IsFalse();
        await Assert.That(cfg.Fullscreen).IsFalse();
        await Assert.That(cfg.Frameless).IsFalse();
        await Assert.That(cfg.AlwaysOnTop).IsFalse();
        await Assert.That(cfg.Name).IsNull();
        await Assert.That(cfg.Title).IsNull();
        await Assert.That(cfg.Url).IsNull();
    }

    /// <summary>
    /// SecurityConfig 默认值：所有字段为 null。
    /// </summary>
    [Test]
    public async Task SecurityConfig_DefaultValues_AllNull()
    {
        var cfg = new SecurityConfig();

        await Assert.That(cfg.Csp).IsNull();
        await Assert.That(cfg.CapabilitiesDir).IsNull();
        await Assert.That(cfg.VerifySignature).IsNull();
        await Assert.That(cfg.TrustedPublicKey).IsNull();
    }

    /// <summary>
    /// DesktopHostOptions.App 属性默认为 null，可读写。
    /// </summary>
    [Test]
    public async Task DesktopHostOptions_App_Property_ReadWrite()
    {
        var opts = new DesktopHostOptions();

        // 默认 null
        await Assert.That(opts.App).IsNull();

        // 可赋值
        var appConfig = new HostingAppConfig
        {
            Windows = new List<WindowConfig>
            {
                new() { Name = "main", Title = "Main Window" },
                new() { Name = "settings", Title = "Settings" },
            },
        };
        opts.App = appConfig;

        // 读取验证
        await Assert.That(opts.App).IsNotNull();
        await Assert.That(opts.App!.Windows.Count).IsEqualTo(2);
        await Assert.That(opts.App.Windows[0].Name).IsEqualTo("main");
        await Assert.That(opts.App.Windows[1].Name).IsEqualTo("settings");
    }

    /// <summary>
    /// HostingAppConfig.Windows 支持添加多个 WindowConfig。
    /// </summary>
    [Test]
    public async Task HostingAppConfig_Windows_CanAddMultipleConfigs()
    {
        var opts = new HostingAppConfig();

        opts.Windows.Add(new WindowConfig { Name = "win1", Title = "窗口 1" });
        opts.Windows.Add(new WindowConfig { Name = "win2", Title = "窗口 2" });
        opts.Windows.Add(new WindowConfig { Name = "win3", Title = "窗口 3" });

        await Assert.That(opts.Windows.Count).IsEqualTo(3);
        await Assert.That(opts.Windows[0].Name).IsEqualTo("win1");
        await Assert.That(opts.Windows[2].Title).IsEqualTo("窗口 3");
    }

    /// <summary>
    /// SecurityConfig 的 VerifySignature 作为 bool? 可被设置为 true 或 false。
    /// </summary>
    [Test]
    public async Task SecurityConfig_VerifySignature_CanBeTrueOrFalse()
    {
        var cfg = new SecurityConfig();

        cfg.VerifySignature = true;
        await Assert.That(cfg.VerifySignature).IsTrue();

        cfg.VerifySignature = false;
        await Assert.That(cfg.VerifySignature).IsFalse();

        cfg.VerifySignature = null;
        await Assert.That(cfg.VerifySignature).IsNull();
    }

    /// <summary>
    /// DesktopHostOptions.App.Security.CapabilitiesDir 可设置相对或绝对路径。
    /// </summary>
    [Test]
    public async Task DesktopHostOptions_AppSecurity_CapabilitiesDir_ReadWrite()
    {
        var opts = new DesktopHostOptions
        {
            App = new HostingAppConfig
            {
                Security = new SecurityConfig
                {
                    CapabilitiesDir = "capabilities",
                    Csp = "default-src 'self'",
                    TrustedPublicKey = "RWR" + new string('A', 64),
                },
            },
        };

        await Assert.That(opts.App!.Security.CapabilitiesDir).IsEqualTo("capabilities");
        await Assert.That(opts.App.Security.Csp).IsEqualTo("default-src 'self'");
        await Assert.That(opts.App.Security.TrustedPublicKey!.Length).IsGreaterThan(3);
    }

    /// <summary>
    /// WindowConfig 支持自定义所有字段值。
    /// </summary>
    [Test]
    public async Task WindowConfig_CustomValues_AllFieldsSet()
    {
        var cfg = new WindowConfig
        {
            Name = "main",
            Title = "My App",
            Width = 1920,
            Height = 1080,
            Resizable = false,
            Centered = true,
            Fullscreen = true,
            Frameless = true,
            AlwaysOnTop = true,
            Url = "https://example.com",
        };

        await Assert.That(cfg.Name).IsEqualTo("main");
        await Assert.That(cfg.Title).IsEqualTo("My App");
        await Assert.That(cfg.Width).IsEqualTo(1920);
        await Assert.That(cfg.Height).IsEqualTo(1080);
        await Assert.That(cfg.Resizable).IsFalse();
        await Assert.That(cfg.Centered).IsTrue();
        await Assert.That(cfg.Fullscreen).IsTrue();
        await Assert.That(cfg.Frameless).IsTrue();
        await Assert.That(cfg.AlwaysOnTop).IsTrue();
        await Assert.That(cfg.Url).IsEqualTo("https://example.com");
    }
}
