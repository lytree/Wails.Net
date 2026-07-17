using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using Wails.Net.Cli.Build;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// Debian/RPM 打包单元测试。
/// 验证 control/spec 文件生成格式及平台门控逻辑，不依赖真实 dpkg-deb/rpmbuild。
/// </summary>
[NotInParallel]
public sealed class PackagerDebRpmTests
{
    /// <summary>
    /// 创建临时目录并返回路径。
    /// </summary>
    private static string CreateTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wailsnet-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    #region GenerateDebControl

    [Test]
    public async Task GenerateDebControl_ReturnsValidFormat()
    {
        var control = Packager.GenerateDebControl(
            "myapp", "1.0.0", "amd64", "Wails.Net", "短描述", "长描述");

        await Assert.That(control).Contains("Package: myapp");
        await Assert.That(control).Contains("Version: 1.0.0");
        await Assert.That(control).Contains("Architecture: amd64");
        await Assert.That(control).Contains("Maintainer: Wails.Net");
        await Assert.That(control).Contains("Depends: libgtk-4-1");
        await Assert.That(control).Contains("Section: utils");
        await Assert.That(control).Contains("Description: 短描述");
    }

    [Test]
    public async Task GenerateDebControl_NullDescriptions_UsesDefaults()
    {
        var control = Packager.GenerateDebControl(
            "myapp", "2.0.0", "arm64", "Pub", null, null);

        await Assert.That(control).Contains("Architecture: arm64");
        await Assert.That(control).Contains("Description: myapp application");
    }

    #endregion

    #region GenerateRpmSpec

    [Test]
    public async Task GenerateRpmSpec_ReturnsValidFormat()
    {
        var spec = Packager.GenerateRpmSpec(
            "myapp", "1.0.0", "myapp", "短描述", "长描述", "MIT", "Utility");

        await Assert.That(spec).Contains("Name:           myapp");
        await Assert.That(spec).Contains("Version:        1.0.0");
        await Assert.That(spec).Contains("Summary:        短描述");
        await Assert.That(spec).Contains("License:        MIT");
        await Assert.That(spec).Contains("Requires:       gtk4, webkitgtk6.0");
        await Assert.That(spec).Contains("%description");
        await Assert.That(spec).Contains("%install");
        await Assert.That(spec).Contains("%files");
        await Assert.That(spec).Contains("/usr/bin/myapp");
    }

    [Test]
    public async Task GenerateRpmSpec_NullDescriptions_UsesDefaults()
    {
        var spec = Packager.GenerateRpmSpec(
            "myapp", "1.0.0", "myapp", null, null, "Proprietary", "Utility");

        await Assert.That(spec).Contains("Summary:        myapp application");
        await Assert.That(spec).Contains("License:        Proprietary");
    }

    #endregion

    #region PackageAsync 平台门控

    [Test]
    public async Task PackageAsync_DebFormat_OnNonLinux_ReturnsPlatformError()
    {
        // 非 Linux 环境应返回平台错误（PackageAsync 捕获异常返回失败结果）
        if (OperatingSystem.IsLinux())
        {
            Skip.Test("此用例验证非 Linux 平台门控，Linux 环境跳过");
            return;
        }

        var tempDir = CreateTempDirectory();
        var outputDir = Path.Combine(Path.GetTempPath(), "wailsnet-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "dummy.txt"), "test");

            var packager = new Packager();
            var options = new PackageOptions
            {
                Format = PackageFormat.Deb,
                OutputDirectory = outputDir,
                AppName = "TestApp",
                Version = "1.0.0",
                GenerateChecksum = false,
            };

            var result = await packager.PackageAsync(tempDir, options);

            await Assert.That(result.Success).IsFalse();
            await Assert.That(result.ErrorMessage).Contains("只能在 Linux");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
            try { Directory.Delete(outputDir, recursive: true); } catch { }
        }
    }

    [Test]
    public async Task PackageAsync_RpmFormat_OnNonLinux_ReturnsPlatformError()
    {
        if (OperatingSystem.IsLinux())
        {
            Skip.Test("此用例验证非 Linux 平台门控，Linux 环境跳过");
            return;
        }

        var tempDir = CreateTempDirectory();
        var outputDir = Path.Combine(Path.GetTempPath(), "wailsnet-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "dummy.txt"), "test");

            var packager = new Packager();
            var options = new PackageOptions
            {
                Format = PackageFormat.Rpm,
                OutputDirectory = outputDir,
                AppName = "TestApp",
                Version = "1.0.0",
                GenerateChecksum = false,
            };

            var result = await packager.PackageAsync(tempDir, options);

            await Assert.That(result.Success).IsFalse();
            await Assert.That(result.ErrorMessage).Contains("只能在 Linux");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
            try { Directory.Delete(outputDir, recursive: true); } catch { }
        }
    }

    [Test]
    public async Task PackageAsync_DebFormat_DpkgDebMissing_OnNonLinux_Skips()
    {
        // dpkg-deb 缺失场景仅在 Linux 上有意义；非 Linux 平台已被平台门控拦截
        if (!OperatingSystem.IsLinux())
        {
            Skip.Test("需 Linux 环境验证 dpkg-deb 缺失场景");
            return;
        }

        // Linux 环境：若 dpkg-deb 存在则跳过（无法模拟缺失），仅验证门控逻辑
        Skip.Test("需 Linux 环境且 dpkg-deb 未安装才能验证此场景");
    }

    [Test]
    public async Task PackageAsync_RpmFormat_RpmbuildMissing_OnNonLinux_Skips()
    {
        if (!OperatingSystem.IsLinux())
        {
            Skip.Test("需 Linux 环境验证 rpmbuild 缺失场景");
            return;
        }

        Skip.Test("需 Linux 环境且 rpmbuild 未安装才能验证此场景");
    }

    #endregion
}
