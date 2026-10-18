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
    [NonLinuxOnly]
    public async Task GetPrimaryScreen_ReturnsNull_OnNonLinux()
    {
        // 安排
        var app = new LinuxPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作与断言：非 Linux 平台上 GetPrimaryScreen 返回 null
        await Assert.That(app.GetPrimaryScreen()).IsNull();
    }

    [Test]
    [NonLinuxOnly]
    public async Task GetScreens_ReturnsEmptyArray_OnNonLinux()
    {
        // 安排
        var app = new LinuxPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作
        var screens = app.GetScreens();

        // 断言：非 Linux 平台上返回空数组
        await Assert.That(screens.Length).IsEqualTo(0);
    }

    [Test]
    [NonLinuxOnly]
    public async Task Run_ThrowsPlatformNotSupportedException_OnNonLinux()
    {
        // 安排
        var app = new LinuxPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作与断言：非 Linux 平台上 Run() 抛出 PlatformNotSupportedException
        await Assert.That(() => app.Run()).ThrowsExactly<PlatformNotSupportedException>();
    }

    [Test]
    public async Task Destroy_DoesNotThrow_WhenMainLoopNotStarted()
    {
        // 安排
        var app = new LinuxPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作与断言：未启动主循环时 Destroy 不抛异常
        await Assert.That(() => app.Destroy()).ThrowsNothing();
    }

    [Test]
    [NonLinuxOnly]
    public async Task ShowMessageDialog_ReturnsZero_OnNonLinux()
    {
        // 安排
        var app = new LinuxPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作
        var result = await app.ShowMessageDialog("Title", "Message", DialogStyle.Info, new[] { "OK" });

        // 断言：非 Linux 平台上返回 0
        await Assert.That(result).IsEqualTo(0);
    }

    [Test]
    [NonLinuxOnly]
    public async Task OpenFileDialog_ReturnsNull_OnNonLinux()
    {
        // 安排
        var app = new LinuxPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作
        var result = await app.OpenFileDialog(new OpenFileDialogOptions());

        // 断言：非 Linux 平台上返回 null
        await Assert.That(result).IsNull();
    }

    [Test]
    [NonLinuxOnly]
    public async Task SaveFileDialog_ReturnsNull_OnNonLinux()
    {
        // 安排
        var app = new LinuxPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作
        var result = await app.SaveFileDialog(new SaveFileDialogOptions());

        // 断言：非 Linux 平台上返回 null
        await Assert.That(result).IsNull();
    }

    [Test]
    [NonLinuxOnly]
    public async Task OpenMultipleFilesDialog_ReturnsNull_OnNonLinux()
    {
        // 安排
        var app = new LinuxPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作
        var result = await app.OpenMultipleFilesDialog(new OpenFileDialogOptions());

        // 断言：非 Linux 平台上返回 null
        await Assert.That(result).IsNull();
    }

    [Test]
    [NonLinuxOnly]
    public async Task CreateWebviewWindow_DoesNotThrow_OnNonLinux()
    {
        // 安排
        var app = new LinuxPlatformApp(new ApplicationOptions { Name = "TestApp" });
        var options = new WebviewWindowOptions { Title = "Test" };

        // 操作与断言：非 Linux 平台上 CreateWebviewWindow 不创建窗口但不抛异常
        await Assert.That(() => app.CreateWebviewWindow(1, options)).ThrowsNothing();
    }

    [Test]
    public async Task GetCurrentWindowId_ReturnsZero_WhenNoWindows()
    {
        // 安排
        var app = new LinuxPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作与断言：无窗口时返回 0
        await Assert.That(app.GetCurrentWindowId()).IsEqualTo((uint)0);
    }

    [Test]
    [NonLinuxOnly]
    public async Task Hide_DoesNotThrow_OnNonLinux()
    {
        // 安排
        var app = new LinuxPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作与断言：非 Linux 平台上 Hide 不抛异常
        await Assert.That(() => app.Hide()).ThrowsNothing();
    }

    [Test]
    [NonLinuxOnly]
    public async Task Show_DoesNotThrow_OnNonLinux()
    {
        // 安排
        var app = new LinuxPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作与断言：非 Linux 平台上 Show 不抛异常
        await Assert.That(() => app.Show()).ThrowsNothing();
    }

    [Test]
    [NonLinuxOnly]
    public async Task ShowAboutDialog_DoesNotThrow_OnNonLinux()
    {
        // 安排
        var app = new LinuxPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作与断言：非 Linux 平台上 ShowAboutDialog 不抛异常
        await Assert.That(() => app.ShowAboutDialog("TestApp", "Description", null)).ThrowsNothing();
    }

    [Test]
    public async Task SetIcon_DoesNotThrow_WithNullIcon()
    {
        // 安排
        var app = new LinuxPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作与断言：null 图标不抛异常
        await Assert.That(() => app.SetIcon(null)).ThrowsNothing();
    }

    [Test]
    [NonLinuxOnly]
    public async Task DispatchOnMainThread_ExecutesAction_OnNonLinux()
    {
        // 安排
        var app = new LinuxPlatformApp(new ApplicationOptions { Name = "TestApp" });
        var executed = false;

        // 操作：非 Linux 环境下直接执行
        app.DispatchOnMainThread(() => executed = true);

        // 断言：动作被执行
        await Assert.That(executed).IsTrue();
    }

    [Test]
    public async Task Constructor_DerivesApplicationId_FromName()
    {
        // 安排与操作
        var app = new LinuxPlatformApp(new ApplicationOptions { Name = "My App" });

        // 断言：默认 id 为 "org.wails." + 清洗后的名称；未设置程序名时保持 null
        await Assert.That(app.ApplicationId).IsEqualTo("org.wails.my_app");
        await Assert.That(app.ApplicationIdError).IsNull();
        await Assert.That(app.ProgramName).IsNull();
    }

    [Test]
    public async Task Constructor_UsesApplicationIdOverride_AndInheritsProgramName()
    {
        // 安排
        try
        {
            LinuxApplicationId.ApplicationIdOverride = "com.example.MyApp";

            // 操作
            var app = new LinuxPlatformApp(new ApplicationOptions { Name = "My App" });

            // 断言：覆盖值原样使用且程序名继承该 id（Wayland 窗口分组需要）
            await Assert.That(app.ApplicationId).IsEqualTo("com.example.MyApp");
            await Assert.That(app.ApplicationIdError).IsNull();
            await Assert.That(app.ProgramName).IsEqualTo("com.example.MyApp");
        }
        finally
        {
            LinuxApplicationId.ApplicationIdOverride = null;
        }
    }

    [Test]
    public async Task Constructor_ExplicitProgramName_WinsOverInheritance()
    {
        // 安排
        try
        {
            LinuxApplicationId.ApplicationIdOverride = "com.example.MyApp";
            LinuxApplicationId.ProgramNameOverride = "myapp";

            // 操作
            var app = new LinuxPlatformApp(new ApplicationOptions { Name = "My App" });

            // 断言：显式程序名优先于继承 id
            await Assert.That(app.ProgramName).IsEqualTo("myapp");
        }
        finally
        {
            LinuxApplicationId.ApplicationIdOverride = null;
            LinuxApplicationId.ProgramNameOverride = null;
        }
    }

    [Test]
    public async Task Constructor_ReportsError_AndFallsBackToDerivedId_WhenOverrideInvalid()
    {
        // 安排
        try
        {
            LinuxApplicationId.ApplicationIdOverride = "yeehaw";

            // 操作
            var app = new LinuxPlatformApp(new ApplicationOptions { Name = "My App" });

            // 断言：非法覆盖被拒绝并回退到推导 id，原因可查；程序名继承的是回退后的 id
            await Assert.That(app.ApplicationId).IsEqualTo("org.wails.my_app");
            await Assert.That(app.ApplicationIdError).IsNotNull();
            await Assert.That(app.ProgramName).IsEqualTo("org.wails.my_app");
        }
        finally
        {
            LinuxApplicationId.ApplicationIdOverride = null;
        }
    }

    [Test]
    public async Task AcquireSingleInstanceLock_EmptyUniqueId_ReturnsTrue()
    {
        // 安排
        var app = new LinuxPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作与断言：空 UniqueID 时不做任何占位，直接视为首实例（与既有行为一致）
        await Assert.That(app.AcquireSingleInstanceLock(string.Empty)).IsTrue();
    }
}
