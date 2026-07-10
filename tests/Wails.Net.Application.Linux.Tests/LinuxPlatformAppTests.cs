using System.Text.RegularExpressions;
using TUnit.Core;
using Wails.Net.Application.Dialogs;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;

namespace Wails.Net.Application.Linux.Tests;

/// <summary>
/// LinuxPlatformApp 的单元测试（TUnit）。
/// 测试线程身份判断、主题、强调色、标志位等平台属性。
/// 注意：此类测试线程身份和环境变量，因此不并行执行。
/// </summary>
[NotInParallel]
public sealed class LinuxPlatformAppTests
{
    [Test]
    public async Task Constructor_SetsName()
    {
        // 安排与操作
        var app = new LinuxPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 断言
        await Assert.That(app.Name).IsEqualTo("TestApp");
    }

    [Test]
    public async Task IsOnMainThread_ReturnsTrue_WhenCalledFromSameThread()
    {
        // 安排：在当前线程构造
        var app = new LinuxPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作与断言：在相同线程调用应返回 true
        await Assert.That(app.IsOnMainThread()).IsTrue();
    }

    [Test]
    public async Task IsOnMainThread_ReturnsFalse_WhenCalledFromDifferentThread()
    {
        // 安排：在当前线程构造
        var app = new LinuxPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作：在不同线程调用
        var result = await Task.Run(() => app.IsOnMainThread());

        // 断言：不同线程应返回 false
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsDarkMode_ReturnsFalse_WhenNoThemeEnvVars()
    {
        // 安排：清除主题相关环境变量
        var originalGtkTheme = Environment.GetEnvironmentVariable("GTK_THEME");
        var originalColorScheme = Environment.GetEnvironmentVariable("COLOR_SCHEME");
        try
        {
            Environment.SetEnvironmentVariable("GTK_THEME", null);
            Environment.SetEnvironmentVariable("COLOR_SCHEME", null);

            var app = new LinuxPlatformApp(new ApplicationOptions { Name = "TestApp" });

            // 操作与断言：无环境变量时默认亮色模式
            await Assert.That(app.IsDarkMode()).IsFalse();
        }
        finally
        {
            // 恢复环境变量
            Environment.SetEnvironmentVariable("GTK_THEME", originalGtkTheme);
            Environment.SetEnvironmentVariable("COLOR_SCHEME", originalColorScheme);
        }
    }

    [Test]
    public async Task IsDarkMode_ReturnsTrue_WhenGtkThemeContainsDark()
    {
        // 安排：设置 GTK_THEME 包含 "dark"
        var originalGtkTheme = Environment.GetEnvironmentVariable("GTK_THEME");
        var originalColorScheme = Environment.GetEnvironmentVariable("COLOR_SCHEME");
        try
        {
            Environment.SetEnvironmentVariable("GTK_THEME", "Adwaita-dark");
            Environment.SetEnvironmentVariable("COLOR_SCHEME", null);

            var app = new LinuxPlatformApp(new ApplicationOptions { Name = "TestApp" });

            // 操作与断言：GTK_THEME 含 dark 时为暗色模式
            await Assert.That(app.IsDarkMode()).IsTrue();
        }
        finally
        {
            // 恢复环境变量
            Environment.SetEnvironmentVariable("GTK_THEME", originalGtkTheme);
            Environment.SetEnvironmentVariable("COLOR_SCHEME", originalColorScheme);
        }
    }

    [Test]
    public async Task IsDarkMode_ReturnsTrue_WhenColorSchemeIsDark()
    {
        // 安排：设置 COLOR_SCHEME 为 "dark"
        var originalGtkTheme = Environment.GetEnvironmentVariable("GTK_THEME");
        var originalColorScheme = Environment.GetEnvironmentVariable("COLOR_SCHEME");
        try
        {
            Environment.SetEnvironmentVariable("GTK_THEME", null);
            Environment.SetEnvironmentVariable("COLOR_SCHEME", "dark");

            var app = new LinuxPlatformApp(new ApplicationOptions { Name = "TestApp" });

            // 操作与断言：COLOR_SCHEME=dark 时为暗色模式
            await Assert.That(app.IsDarkMode()).IsTrue();
        }
        finally
        {
            // 恢复环境变量
            Environment.SetEnvironmentVariable("GTK_THEME", originalGtkTheme);
            Environment.SetEnvironmentVariable("COLOR_SCHEME", originalColorScheme);
        }
    }

    [Test]
    public async Task IsDarkMode_ReturnsFalse_WhenColorSchemeIsLight()
    {
        // 安排：设置 COLOR_SCHEME 为 "light"
        var originalGtkTheme = Environment.GetEnvironmentVariable("GTK_THEME");
        var originalColorScheme = Environment.GetEnvironmentVariable("COLOR_SCHEME");
        try
        {
            Environment.SetEnvironmentVariable("GTK_THEME", null);
            Environment.SetEnvironmentVariable("COLOR_SCHEME", "light");

            var app = new LinuxPlatformApp(new ApplicationOptions { Name = "TestApp" });

            // 操作与断言：COLOR_SCHEME=light 时为亮色模式
            await Assert.That(app.IsDarkMode()).IsFalse();
        }
        finally
        {
            // 恢复环境变量
            Environment.SetEnvironmentVariable("GTK_THEME", originalGtkTheme);
            Environment.SetEnvironmentVariable("COLOR_SCHEME", originalColorScheme);
        }
    }

    [Test]
    public async Task GetAccentColor_ReturnsValidHexColor()
    {
        // 安排
        var app = new LinuxPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作
        var color = app.GetAccentColor();

        // 断言：匹配 #RRGGBB 格式
        await Assert.That(Regex.IsMatch(color, @"^#[0-9A-Fa-f]{6}$")).IsTrue();
    }

    [Test]
    public async Task GetAccentColor_ReturnsDefaultBlue()
    {
        // 安排
        var app = new LinuxPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作
        var color = app.GetAccentColor();

        // 断言：当前桩实现返回默认蓝色 #0078D4
        await Assert.That(color).IsEqualTo("#0078D4");
    }

    [Test]
    public async Task GetFlags_ReturnsEmptyDictionary()
    {
        // 安排
        var app = new LinuxPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作
        var flags = app.GetFlags(new ApplicationOptions());

        // 断言：返回空字典
        await Assert.That(flags.Count).IsEqualTo(0);
    }

    [Test]
    public async Task GetPrimaryScreen_ReturnsNull_ForNow()
    {
        // 安排
        var app = new LinuxPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作与断言：当前为 TODO 桩，返回 null
        await Assert.That(app.GetPrimaryScreen()).IsNull();
    }

    [Test]
    public async Task GetScreens_ReturnsEmptyArray_ForNow()
    {
        // 安排
        var app = new LinuxPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作
        var screens = app.GetScreens();

        // 断言：当前为 TODO 桩，返回空数组
        await Assert.That(screens.Length).IsEqualTo(0);
    }

    [Test]
    public async Task Run_ThrowsNotImplementedException()
    {
        // 安排
        var app = new LinuxPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作与断言：Run() 尚未实现
        await Assert.That(() => app.Run()).ThrowsExactly<NotImplementedException>();
    }

    [Test]
    public async Task ShowMessageDialog_ReturnsZero()
    {
        // 安排
        var app = new LinuxPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作
        var result = await app.ShowMessageDialog("Title", "Message", DialogStyle.Info, new[] { "OK" });

        // 断言：桩实现返回 0
        await Assert.That(result).IsEqualTo(0);
    }

    [Test]
    public async Task OpenFileDialog_ReturnsNull()
    {
        // 安排
        var app = new LinuxPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作
        var result = await app.OpenFileDialog(new OpenFileDialogOptions());

        // 断言：桩实现返回 null
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task SaveFileDialog_ReturnsNull()
    {
        // 安排
        var app = new LinuxPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作
        var result = await app.SaveFileDialog(new SaveFileDialogOptions());

        // 断言：桩实现返回 null
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task OpenMultipleFilesDialog_ReturnsNull()
    {
        // 安排
        var app = new LinuxPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作
        var result = await app.OpenMultipleFilesDialog(new OpenFileDialogOptions());

        // 断言：桩实现返回 null
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task DispatchOnMainThread_ExecutesAction()
    {
        // 安排
        var app = new LinuxPlatformApp(new ApplicationOptions { Name = "TestApp" });
        var executed = false;

        // 操作：分发到主线程（当前桩实现直接执行）
        app.DispatchOnMainThread(() => executed = true);

        // 断言：动作被执行
        await Assert.That(executed).IsTrue();
    }
}
