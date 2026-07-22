using TUnit.Core;
using Wails.Net.Application.Managers;
using Wails.Net.Application.SystemEnvironment;

namespace Wails.Net.Application.Tests;

/// <summary>
/// EnvironmentInfo 和 OperatingSystemInfo 的单元测试（TUnit）。
/// 对应项 C：应用运行环境信息查询。
/// </summary>
[NotInParallel]
public sealed class EnvironmentInfoTests
{
    /// <summary>
    /// EnvironmentInfo 默认构造所有字段为空/默认值。
    /// </summary>
    [Test]
    public async Task EnvironmentInfo_Default_AllFieldsDefault()
    {
        // 安排
        var info = new EnvironmentInfo();

        // 断言
        await Assert.That(info.OS).IsEqualTo(string.Empty);
        await Assert.That(info.Arch).IsEqualTo(string.Empty);
        await Assert.That(info.Debug).IsFalse();
        await Assert.That(info.OSInfo).IsNull();
        await Assert.That(info.PlatformInfo).IsEmpty();
    }

    /// <summary>
    /// EnvironmentInfo 各字段可独立赋值。
    /// </summary>
    [Test]
    public async Task EnvironmentInfo_SetProperties_AllFieldsRetained()
    {
        // 安排
        var osInfo = new OperatingSystemInfo
        {
            Name = "Windows 11",
            Version = "10.0.26200",
            Build = "26200",
        };
        var info = new EnvironmentInfo
        {
            OS = "windows",
            Arch = "amd64",
            Debug = true,
            OSInfo = osInfo,
            PlatformInfo = new Dictionary<string, object?>
            {
                ["version"] = "10.0.26200",
                ["focusFollowsMouse"] = false,
            },
        };

        // 断言
        await Assert.That(info.OS).IsEqualTo("windows");
        await Assert.That(info.Arch).IsEqualTo("amd64");
        await Assert.That(info.Debug).IsTrue();
        await Assert.That(info.OSInfo).IsSameReferenceAs(osInfo);
        await Assert.That(info.PlatformInfo).ContainsKey("version");
        await Assert.That(info.PlatformInfo["version"]).IsEqualTo("10.0.26200");
    }

    /// <summary>
    /// OperatingSystemInfo 默认构造所有字段为空。
    /// </summary>
    [Test]
    public async Task OperatingSystemInfo_Default_AllFieldsEmpty()
    {
        // 安排
        var osInfo = new OperatingSystemInfo();

        // 断言
        await Assert.That(osInfo.Name).IsEqualTo(string.Empty);
        await Assert.That(osInfo.Version).IsEqualTo(string.Empty);
        await Assert.That(osInfo.Build).IsEqualTo(string.Empty);
        await Assert.That(osInfo.Hardware).IsNull();
    }

    /// <summary>
    /// OperatingSystemInfo 各字段可独立赋值。
    /// </summary>
    [Test]
    public async Task OperatingSystemInfo_SetProperties_AllFieldsRetained()
    {
        // 安排
        var osInfo = new OperatingSystemInfo
        {
            Name = "Ubuntu 22.04 LTS",
            Version = "22.04",
            Build = "jammy",
            Hardware = "Dell XPS 15",
        };

        // 断言
        await Assert.That(osInfo.Name).IsEqualTo("Ubuntu 22.04 LTS");
        await Assert.That(osInfo.Version).IsEqualTo("22.04");
        await Assert.That(osInfo.Build).IsEqualTo("jammy");
        await Assert.That(osInfo.Hardware).IsEqualTo("Dell XPS 15");
    }

    /// <summary>
    /// EnvironmentInfo.ToString 输出包含 OS 和 Arch 字段。
    /// </summary>
    [Test]
    public async Task EnvironmentInfo_ToString_ContainsOsAndArch()
    {
        // 安排
        var info = new EnvironmentInfo
        {
            OS = "linux",
            Arch = "arm64",
            Debug = true,
        };

        // 操作
        var str = info.ToString();

        // 断言
        await Assert.That(str).Contains("linux");
        await Assert.That(str).Contains("arm64");
        await Assert.That(str).Contains("Debug=True");
    }

    /// <summary>
    /// OperatingSystemInfo.ToString 输出包含名称、版本和构建号。
    /// </summary>
    [Test]
    public async Task OperatingSystemInfo_ToString_ContainsNameVersionBuild()
    {
        // 安排
        var osInfo = new OperatingSystemInfo
        {
            Name = "Windows 11",
            Version = "10.0.26200",
            Build = "26200",
        };

        // 操作
        var str = osInfo.ToString();

        // 断言
        await Assert.That(str).Contains("Windows 11");
        await Assert.That(str).Contains("10.0.26200");
        await Assert.That(str).Contains("26200");
    }

    /// <summary>
    /// IEnvironmentManager 接口默认实现 Info() 返回基于 GetOS/GetArch 的 EnvironmentInfo。
    /// </summary>
    [Test]
    public async Task IEnvironmentManager_DefaultInfo_BuiltFromGetOSAndGetArch()
    {
        // 安排：使用最小桩实现验证默认 Info() 行为。
        // 接口默认实现方法必须通过接口类型调用。
        IEnvironmentManager manager = new StubEnvironmentManager
        {
            OS = "test-os",
            Arch = "test-arch",
        };

        // 操作
        var info = manager.Info();

        // 断言
        await Assert.That(info.OS).IsEqualTo("test-os");
        await Assert.That(info.Arch).IsEqualTo("test-arch");
        await Assert.That(info.Debug).IsFalse();
        await Assert.That(info.OSInfo).IsNull();
    }

    /// <summary>
    /// IEnvironmentManager 接口默认实现 IsDarkMode 返回 false。
    /// </summary>
    [Test]
    public async Task IEnvironmentManager_DefaultIsDarkMode_ReturnsFalse()
    {
        // 安排
        IEnvironmentManager manager = new StubEnvironmentManager();

        // 断言
        await Assert.That(manager.IsDarkMode()).IsFalse();
    }

    /// <summary>
    /// IEnvironmentManager 接口默认实现 GetAccentColor 返回 rgb(0,122,255)。
    /// </summary>
    [Test]
    public async Task IEnvironmentManager_DefaultGetAccentColor_ReturnsDefaultBlue()
    {
        // 安排
        IEnvironmentManager manager = new StubEnvironmentManager();

        // 断言
        await Assert.That(manager.GetAccentColor()).IsEqualTo("rgb(0,122,255)");
    }

    /// <summary>
    /// IEnvironmentManager 接口默认实现 HasFocusFollowsMouse 返回 false。
    /// </summary>
    [Test]
    public async Task IEnvironmentManager_DefaultHasFocusFollowsMouse_ReturnsFalse()
    {
        // 安排
        IEnvironmentManager manager = new StubEnvironmentManager();

        // 断言
        await Assert.That(manager.HasFocusFollowsMouse()).IsFalse();
    }

    /// <summary>
    /// IEnvironmentManager 接口默认实现 OpenFileManager 不抛异常。
    /// </summary>
    [Test]
    public async Task IEnvironmentManager_DefaultOpenFileManager_NoThrow()
    {
        // 安排
        IEnvironmentManager manager = new StubEnvironmentManager();

        // 操作 + 断言：不应抛出异常
        await Assert.That(() => manager.OpenFileManager("/tmp", false)).ThrowsNothing();
    }

    /// <summary>
    /// 最小桩实现，用于测试 IEnvironmentManager 的默认实现方法。
    /// </summary>
    private sealed class StubEnvironmentManager : IEnvironmentManager
    {
        public string OS { get; set; } = "stub";
        public string Arch { get; set; } = "stub";

        public string GetOS() => OS;
        public string GetArch() => Arch;
        public string GetHomeDir() => "/home/stub";
        public string GetDataDir() => "/data/stub";
    }
}
