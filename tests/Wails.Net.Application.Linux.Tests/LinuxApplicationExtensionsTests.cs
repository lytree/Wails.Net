using TUnit.Core;
using Wails.Net.Application;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;

namespace Wails.Net.Application.Linux.Tests;

/// <summary>
/// LinuxApplicationExtensions 的单元测试（TUnit）。
/// 测试 UseLinux() 扩展方法是否正确设置平台应用。
/// 注意：Application 构造函数会设置全局静态实例，因此不并行执行。
/// </summary>
[NotInParallel]
public sealed class LinuxApplicationExtensionsTests
{
    [Test]
    public async Task UseLinux_SetsPlatformApp()
    {
        // 安排
        var app = new Application(new ApplicationOptions { Name = "TestApp" });

        // 操作：调用 UseLinux 扩展方法
        app.UseLinux();

        // 断言：PlatformApp 已设置且为 LinuxPlatformApp 类型
        await Assert.That(app.PlatformApp).IsNotNull();
        await Assert.That(app.PlatformApp).IsTypeOf<LinuxPlatformApp>();
    }

    [Test]
    public async Task UseLinux_ReturnsSameApplication()
    {
        // 安排
        var app = new Application(new ApplicationOptions { Name = "TestApp" });

        // 操作：调用 UseLinux 扩展方法并获取返回值
        var result = app.UseLinux();

        // 断言：返回的是同一个 Application 实例
        await Assert.That(result).IsSameReferenceAs(app);
    }

    [Test]
    public async Task UseLinux_PreservesApplicationName()
    {
        // 安排
        var app = new Application(new ApplicationOptions { Name = "MyLinuxApp" });

        // 操作：调用 UseLinux 扩展方法
        app.UseLinux();

        // 断言：平台应用名称与应用配置一致
        await Assert.That(app.PlatformApp!.Name).IsEqualTo("MyLinuxApp");
    }
}
