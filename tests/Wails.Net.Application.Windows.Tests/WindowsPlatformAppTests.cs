using System.Text.RegularExpressions;
using TUnit.Core;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;

namespace Wails.Net.Application.Windows.Tests;

/// <summary>
/// WindowsPlatformApp 的单元测试（TUnit）。
/// 测试线程身份判断、主题、强调色、标志位等平台属性。
/// 注意：此类测试线程身份，因此不并行执行。
/// </summary>
[NotInParallel]
public sealed class WindowsPlatformAppTests
{
    [Test]
    public async Task Constructor_SetsName()
    {
        // 安排与操作
        var app = new WindowsPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 断言
        await Assert.That(app.Name).IsEqualTo("TestApp");
    }

    [Test]
    public async Task IsOnMainThread_ReturnsTrue_WhenCalledFromSameThread()
    {
        // 安排：在当前线程构造
        var app = new WindowsPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作与断言：在相同线程调用应返回 true
        await Assert.That(app.IsOnMainThread()).IsTrue();
    }

    [Test]
    public async Task IsOnMainThread_ReturnsFalse_WhenCalledFromDifferentThread()
    {
        // 安排：在当前线程构造
        var app = new WindowsPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作：在不同线程调用
        var result = await Task.Run(() => app.IsOnMainThread());

        // 断言：不同线程应返回 false
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsDarkMode_ReturnsBool()
    {
        // 安排
        var app = new WindowsPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作与断言：返回布尔值（取决于系统主题设置）
        await Assert.That(app.IsDarkMode()).IsTypeOf<bool>();
    }

    [Test]
    public async Task GetAccentColor_ReturnsValidHexColor()
    {
        // 安排
        var app = new WindowsPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作
        var color = app.GetAccentColor();

        // 断言：匹配 #RRGGBB 格式
        await Assert.That(Regex.IsMatch(color, @"^#[0-9A-Fa-f]{6}$")).IsTrue();
    }

    [Test]
    public async Task GetFlags_ReturnsEmptyDictionary()
    {
        // 安排
        var app = new WindowsPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作
        var flags = app.GetFlags(new ApplicationOptions());

        // 断言：返回空字典
        await Assert.That(flags.Count).IsEqualTo(0);
    }

    [Test]
    public async Task GetPrimaryScreen_ReturnsNull_ForNow()
    {
        // 安排
        var app = new WindowsPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作与断言：当前为 TODO 桩，返回 null
        await Assert.That(app.GetPrimaryScreen()).IsNull();
    }

    [Test]
    public async Task GetScreens_ReturnsEmptyArray_ForNow()
    {
        // 安排
        var app = new WindowsPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作
        var screens = app.GetScreens();

        // 断言：当前为 TODO 桩，返回空数组
        await Assert.That(screens.Length).IsEqualTo(0);
    }

    [Test]
    public async Task Run_ThrowsNotImplementedException()
    {
        // 安排
        var app = new WindowsPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作与断言：Run() 尚未实现
        await Assert.That(() => app.Run()).ThrowsExactly<NotImplementedException>();
    }
}
