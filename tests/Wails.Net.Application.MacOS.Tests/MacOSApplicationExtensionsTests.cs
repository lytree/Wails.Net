using TUnit.Core;
using Wails.Net.Application;
using Wails.Net.Application.Browser;
using Wails.Net.Application.Managers;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;

namespace Wails.Net.Application.MacOS.Tests;

/// <summary>
/// <see cref="MacOSApplicationExtensions.UseMacOS(Wails.Net.Application.Application)"/> 的单元测试（TUnit）。
/// <para>
/// 验证 macOS 平台接入后：平台应用替换为 <see cref="MacOSPlatformApp"/>，
/// 且对话框 / 屏幕 / 托盘 / 快捷键 / 浏览器管理器均被替换为 macOS 专属实现
/// （否则会静默落到默认实现，表现为"看起来能跑但没有原生行为"）。
/// </para>
/// <para>
/// <see cref="Wails.Net.Application.Application"/> 构造函数会设置全局静态实例，
/// 因此本类不并行执行。
/// </para>
/// </summary>
[NotInParallel]
public sealed class MacOSApplicationExtensionsTests
{
    /// <summary>
    /// UseMacOS 应把平台应用安装为 <see cref="MacOSPlatformApp"/>。
    /// </summary>
    [Test]
    public async Task UseMacOS_SetsMacOSPlatformApp()
    {
        var app = new Application(new ApplicationOptions { Name = "TestApp" });

        app.UseMacOS();

        await Assert.That(app.PlatformApp).IsNotNull();
        await Assert.That(app.PlatformApp).IsTypeOf<MacOSPlatformApp>();
    }

    /// <summary>
    /// UseMacOS 应返回同一 Application 实例，以支持链式调用。
    /// </summary>
    [Test]
    public async Task UseMacOS_ReturnsSameApplication()
    {
        var app = new Application(new ApplicationOptions { Name = "TestApp" });

        var result = app.UseMacOS();

        await Assert.That(result).IsSameReferenceAs(app);
    }

    /// <summary>
    /// 平台应用名称应来自 <see cref="ApplicationOptions.Name"/>。
    /// </summary>
    [Test]
    public async Task UseMacOS_PreservesApplicationName()
    {
        var app = new Application(new ApplicationOptions { Name = "MyMacApp" });

        app.UseMacOS();

        await Assert.That(app.PlatformApp!.Name).IsEqualTo("MyMacApp");
    }

    /// <summary>
    /// 对话框与屏幕管理器应注册为通用管理器（委托给 MacOSPlatformApp），
    /// 保证 <see cref="Application.DialogManager"/> / <see cref="Application.ScreenManager"/> 可用。
    /// </summary>
    [Test]
    public async Task UseMacOS_RegistersDialogAndScreenManagers()
    {
        var app = new Application(new ApplicationOptions { Name = "TestApp" });

        app.UseMacOS();

        var dialogManager = app.Services.GetService<IDialogManager>();
        var screenManager = app.Services.GetService<IScreenManager>();

        await Assert.That(dialogManager).IsNotNull();
        await Assert.That(dialogManager).IsTypeOf<DialogManager>();
        await Assert.That(screenManager).IsNotNull();
        await Assert.That(screenManager).IsTypeOf<ScreenManager>();
    }

    /// <summary>
    /// 托盘 / 快捷键 / 浏览器管理器应替换为 macOS 专属实现。
    /// </summary>
    [Test]
    public async Task UseMacOS_InstallsMacOSSpecificManagers()
    {
        var app = new Application(new ApplicationOptions { Name = "TestApp" });

        app.UseMacOS();

        await Assert.That(app.SystemTrayManager).IsNotNull();
        await Assert.That(app.KeyBindingManager).IsTypeOf<MacOSKeyBindingManager>();
        await Assert.That(app.BrowserManager).IsTypeOf<MacOSBrowserManager>();
    }
}
