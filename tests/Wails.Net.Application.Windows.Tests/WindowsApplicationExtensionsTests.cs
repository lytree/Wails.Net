using TUnit.Core;
using Wails.Net.Application;
using Wails.Net.Application.Managers;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;

namespace Wails.Net.Application.Windows.Tests;

/// <summary>
/// WindowsApplicationExtensions 的单元测试（TUnit）。
/// 测试 UseWindows() 扩展方法是否正确设置平台应用并注册对话框和屏幕服务。
/// 注意：Application 构造函数会设置全局静态实例，因此不并行执行。
/// </summary>
[NotInParallel]
public sealed class WindowsApplicationExtensionsTests
{
    [Test]
    public async Task UseWindows_SetsPlatformApp()
    {
        // 安排
        var app = new Application(new ApplicationOptions { Name = "TestApp" });

        // 操作：调用 UseWindows 扩展方法
        app.UseWindows();

        // 断言：PlatformApp 已设置且为 WindowsPlatformApp 类型
        await Assert.That(app.PlatformApp).IsNotNull();
        await Assert.That(app.PlatformApp).IsTypeOf<WindowsPlatformApp>();
    }

    [Test]
    public async Task UseWindows_ReturnsSameApplication()
    {
        // 安排
        var app = new Application(new ApplicationOptions { Name = "TestApp" });

        // 操作：调用 UseWindows 扩展方法并获取返回值
        var result = app.UseWindows();

        // 断言：返回的是同一个 Application 实例
        await Assert.That(result).IsSameReferenceAs(app);
    }

    [Test]
    public async Task UseWindows_RegistersDialogManagerService()
    {
        // 安排
        var app = new Application(new ApplicationOptions { Name = "TestApp" });

        // 操作：调用 UseWindows 扩展方法
        app.UseWindows();

        // 断言：DialogManager 服务已注册
        var dialogManager = app.Services.GetService<IDialogManager>();
        await Assert.That(dialogManager).IsNotNull();
        await Assert.That(dialogManager).IsTypeOf<DialogManager>();
    }

    [Test]
    public async Task UseWindows_RegistersScreenManagerService()
    {
        // 安排
        var app = new Application(new ApplicationOptions { Name = "TestApp" });

        // 操作：调用 UseWindows 扩展方法
        app.UseWindows();

        // 断言：ScreenManager 服务已注册
        var screenManager = app.Services.GetService<IScreenManager>();
        await Assert.That(screenManager).IsNotNull();
        await Assert.That(screenManager).IsTypeOf<ScreenManager>();
    }
}
