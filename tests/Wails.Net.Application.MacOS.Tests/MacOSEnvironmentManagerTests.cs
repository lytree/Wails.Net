using System.Runtime.InteropServices;
using TUnit.Core;
using Wails.Net.Application.Managers;

namespace Wails.Net.Application.MacOS.Tests;

/// <summary>
/// <see cref="MacOSEnvironmentManager"/> 的单元测试（TUnit）。
/// <para>
/// 覆盖与原生 AppKit 无关的部分：OS/架构标识、数据目录布局、
/// <see cref="Wails.Net.Application.SystemEnvironment.EnvironmentInfo"/> 组装，
/// 以及 net10.0 骨架目标下的暗色模式 / 强调色 / 文件管理器降级行为。
/// </para>
/// </summary>
public sealed class MacOSEnvironmentManagerTests
{
    /// <summary>
    /// OS 标识固定为 Wails 约定的 <c>macos</c>（与前端 runtime 判断一致）。
    /// </summary>
    [Test]
    public async Task GetOS_ReturnsMacos()
    {
        var env = new MacOSEnvironmentManager("MyApp");

        await Assert.That(env.GetOS()).IsEqualTo("macos");
    }

    /// <summary>
    /// 架构标识应按 Wails 命名约定映射 .NET 的 <see cref="Architecture"/>（Go 风格：amd64/386/arm）。
    /// </summary>
    [Test]
    public async Task GetArch_MapsRuntimeArchitecture()
    {
        var expected = RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "amd64",
            Architecture.Arm => "arm",
            Architecture.X86 => "386",
            var other => other.ToString().ToLowerInvariant(),
        };

        var env = new MacOSEnvironmentManager("MyApp");

        await Assert.That(env.GetArch()).IsEqualTo(expected);
    }

    /// <summary>
    /// 用户主目录应直接取 <see cref="Environment.SpecialFolder.UserProfile"/>。
    /// </summary>
    [Test]
    public async Task GetHomeDir_ReturnsUserProfile()
    {
        var expected = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var env = new MacOSEnvironmentManager("MyApp");

        await Assert.That(env.GetHomeDir()).IsEqualTo(expected);
    }

    /// <summary>
    /// 数据目录遵循 macOS 约定：<c>~/Library/Application Support/{AppName}</c>。
    /// </summary>
    [Test]
    public async Task GetDataDir_UsesApplicationSupportLayout()
    {
        var env = new MacOSEnvironmentManager("MyApp");

        var expected = Path.Combine(env.GetHomeDir(), "Library", "Application Support", "MyApp");

        await Assert.That(env.GetDataDir()).IsEqualTo(expected);
    }

    /// <summary>
    /// 不同应用名的数据目录必须互不覆盖（同一父目录、不同叶子目录）。
    /// </summary>
    [Test]
    public async Task GetDataDir_DifferentAppNames_AreDistinct()
    {
        var first = new MacOSEnvironmentManager("AppOne").GetDataDir();
        var second = new MacOSEnvironmentManager("AppTwo").GetDataDir();

        await Assert.That(first).IsNotEqualTo(second);
        await Assert.That(Path.GetDirectoryName(first)).IsEqualTo(Path.GetDirectoryName(second));
    }

    /// <summary>
    /// <see cref="MacOSEnvironmentManager.Info"/> 应同时填充 OS 与 Arch，且与单独查询结果一致。
    /// </summary>
    [Test]
    public async Task Info_ContainsOsAndArch()
    {
        var env = new MacOSEnvironmentManager("MyApp");

        var info = env.Info();

        await Assert.That(info.OS).IsEqualTo("macos");
        await Assert.That(info.Arch).IsEqualTo(env.GetArch());
    }

    /// <summary>
    /// net10.0 骨架目标下暗色模式恒为 false（真实值需 NSApp.effectiveAppearance，仅 macos TFM 可得）。
    /// </summary>
    [Test]
    public async Task IsDarkMode_Skeleton_ReturnsFalse()
    {
        var env = new MacOSEnvironmentManager("MyApp");

        await Assert.That(env.IsDarkMode()).IsFalse();
    }

    /// <summary>
    /// net10.0 骨架目标下强调色回退为系统蓝 <c>rgb(0,122,255)</c>。
    /// </summary>
    [Test]
    public async Task GetAccentColor_Skeleton_FallsBackToSystemBlue()
    {
        var env = new MacOSEnvironmentManager("MyApp");

        await Assert.That(env.GetAccentColor()).IsEqualTo("rgb(0,122,255)");
    }

    /// <summary>
    /// macOS 不支持"焦点跟随鼠标"，恒为 false。
    /// </summary>
    [Test]
    public async Task HasFocusFollowsMouse_ReturnsFalse()
    {
        var env = new MacOSEnvironmentManager("MyApp");

        await Assert.That(env.HasFocusFollowsMouse()).IsFalse();
    }

    /// <summary>
    /// 骨架目标下打开文件管理器为 no-op：任意路径（存在/不存在）均不得抛出异常。
    /// </summary>
    [Test]
    [Arguments("")]
    [Arguments("/tmp/not-exists-wails-test")]
    public async Task OpenFileManager_Skeleton_DoesNotThrow(string path)
    {
        var env = new MacOSEnvironmentManager("MyApp");

        await Assert.That(() => env.OpenFileManager(path, selectFile: false)).ThrowsNothing();
        await Assert.That(() => env.OpenFileManager(path, selectFile: true)).ThrowsNothing();
    }
}
