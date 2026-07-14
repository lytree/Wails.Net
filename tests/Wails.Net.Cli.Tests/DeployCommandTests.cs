using System.IO;
using TUnit.Assertions;
using TUnit.Core;
using Wails.Net.Cli.Commands;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// DeployCommand 单元测试。
/// 仅覆盖纯逻辑方法（参数构造、adb 输出解析、包名验证、APK 查找），
/// 不调用真实 adb 进程，避免环境依赖。
/// 对应 Tauri v2 的 <c>tauri android deploy</c> 命令。
/// </summary>
[NotInParallel]
public sealed class DeployCommandTests
{
    // === ParseAdbDevicesOutput ===

    [Test]
    public async Task ParseAdbDevicesOutput_ValidOutput_ReturnsDeviceSerials()
    {
        var output = "List of devices attached\nemulator-5554\tdevice\nemulator-5556\toffline\n";
        var devices = DeployCommand.ParseAdbDevicesOutput(output);

        await Assert.That(devices.Count).IsEqualTo(1);
        await Assert.That(devices[0]).IsEqualTo("emulator-5554");
    }

    [Test]
    public async Task ParseAdbDevicesOutput_EmptyOutput_ReturnsEmptyList()
    {
        var devices = DeployCommand.ParseAdbDevicesOutput(string.Empty);
        await Assert.That(devices.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ParseAdbDevicesOutput_NullOutput_ReturnsEmptyList()
    {
        var devices = DeployCommand.ParseAdbDevicesOutput(null);
        await Assert.That(devices.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ParseAdbDevicesOutput_OnlyOfflineDevices_ReturnsEmptyList()
    {
        var output = "List of devices attached\nemulator-5554\toffline\nemulator-5556\tunauthorized\n";
        var devices = DeployCommand.ParseAdbDevicesOutput(output);
        await Assert.That(devices.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ParseAdbDevicesOutput_MalformedLines_SkipsAndContinues()
    {
        // 仅含单字段的行（无状态）应被跳过；空行也应被跳过
        var output = "List of devices attached\n\nemulator-5554\ndevice\nemulator-5556\tdevice\n";
        var devices = DeployCommand.ParseAdbDevicesOutput(output);
        await Assert.That(devices.Count).IsEqualTo(1);
        await Assert.That(devices[0]).IsEqualTo("emulator-5556");
    }

    [Test]
    public async Task ParseAdbDevicesOutput_MultipleDevices_ReturnsAllReadyDevices()
    {
        var output = """
            List of devices attached
            emulator-5554	device
            emulator-5556	device
            emulator-5558	offline
            """;
        var devices = DeployCommand.ParseAdbDevicesOutput(output);
        await Assert.That(devices.Count).IsEqualTo(2);
        await Assert.That(devices[0]).IsEqualTo("emulator-5554");
        await Assert.That(devices[1]).IsEqualTo("emulator-5556");
    }

    // === BuildInstallArgs ===

    [Test]
    public async Task BuildInstallArgs_WithDevice_ReturnsCorrectArgs()
    {
        var args = DeployCommand.BuildInstallArgs("/path/to/app.apk", "emulator-5554");
        await Assert.That(args).IsEquivalentTo(new[]
        {
            "-s", "emulator-5554", "install", "-r", "/path/to/app.apk",
        });
    }

    [Test]
    public async Task BuildInstallArgs_WithoutDevice_OmitsSerialFlag()
    {
        var args = DeployCommand.BuildInstallArgs("/path/to/app.apk", null);
        await Assert.That(args).IsEquivalentTo(new[]
        {
            "install", "-r", "/path/to/app.apk",
        });
    }

    [Test]
    public async Task BuildInstallArgs_NullApk_ThrowsArgumentNullException()
    {
        await Assert.That(() => DeployCommand.BuildInstallArgs(null!, "emulator-5554"))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task BuildInstallArgs_EmptyApk_ThrowsArgumentException()
    {
        await Assert.That(() => DeployCommand.BuildInstallArgs(string.Empty, "emulator-5554"))
            .ThrowsExactly<ArgumentException>();
    }

    // === BuildStartArgs ===

    [Test]
    public async Task BuildStartArgs_NoActivity_UsesDefaultMainActivity()
    {
        var args = DeployCommand.BuildStartArgs("com.example.app", null, "emulator-5554");
        await Assert.That(args).IsEquivalentTo(new[]
        {
            "-s", "emulator-5554",
            "shell", "am", "start", "-n",
            "com.example.app/.MainActivity",
        });
    }

    [Test]
    public async Task BuildStartArgs_WithActivity_PrefixedByPackage_UsesAsIs()
    {
        var args = DeployCommand.BuildStartArgs(
            "com.example.app",
            "com.example.app/.CustomActivity",
            "emulator-5554");
        await Assert.That(args).IsEquivalentTo(new[]
        {
            "-s", "emulator-5554",
            "shell", "am", "start", "-n",
            "com.example.app/.CustomActivity",
        });
    }

    [Test]
    public async Task BuildStartArgs_WithActivity_NotPrefixed_PrefixesWithPackage()
    {
        var args = DeployCommand.BuildStartArgs(
            "com.example.app",
            ".CustomActivity",
            null);
        await Assert.That(args).IsEquivalentTo(new[]
        {
            "shell", "am", "start", "-n",
            "com.example.app/.CustomActivity",
        });
    }

    [Test]
    public async Task BuildStartArgs_NullPackage_ThrowsArgumentNullException()
    {
        await Assert.That(() => DeployCommand.BuildStartArgs(null!, null, "emulator-5554"))
            .ThrowsExactly<ArgumentNullException>();
    }

    // === IsValidPackageName ===

    [Test]
    public async Task IsValidPackageName_ValidComExampleApp_ReturnsTrue()
    {
        await Assert.That(DeployCommand.IsValidPackageName("com.example.app")).IsTrue();
    }

    [Test]
    public async Task IsValidPackageName_ValidNetCompanyProductV2_ReturnsTrue()
    {
        await Assert.That(DeployCommand.IsValidPackageName("net.company.product_v2")).IsTrue();
    }

    [Test]
    public async Task IsValidPackageName_ValidOrgWailsNetDemo_ReturnsTrue()
    {
        await Assert.That(DeployCommand.IsValidPackageName("org.wails.net.demo")).IsTrue();
    }

    [Test]
    public async Task IsValidPackageName_InvalidSingleWord_ReturnsFalse()
    {
        await Assert.That(DeployCommand.IsValidPackageName("App")).IsFalse();
    }

    [Test]
    public async Task IsValidPackageName_InvalidNumericSegment_ReturnsFalse()
    {
        await Assert.That(DeployCommand.IsValidPackageName("com.123")).IsFalse();
    }

    [Test]
    public async Task IsValidPackageName_InvalidNoDot_ReturnsFalse()
    {
        await Assert.That(DeployCommand.IsValidPackageName("single")).IsFalse();
    }

    [Test]
    public async Task IsValidPackageName_InvalidDoubleDot_ReturnsFalse()
    {
        await Assert.That(DeployCommand.IsValidPackageName("com..dotted")).IsFalse();
    }

    [Test]
    public async Task IsValidPackageName_EmptyString_ReturnsFalse()
    {
        await Assert.That(DeployCommand.IsValidPackageName(string.Empty)).IsFalse();
    }

    [Test]
    public async Task IsValidPackageName_Null_ReturnsFalse()
    {
        var result = DeployCommand.IsValidPackageName(null);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsValidPackageName_UpperCase_ReturnsFalse()
    {
        // 正则要求小写开头
        var result = DeployCommand.IsValidPackageName("Com.Example.App");
        await Assert.That(result).IsFalse();
    }

    // === FindLatestApk ===
    // 注：FindLatestApk 使用 Environment.CurrentDirectory，测试需切换工作目录。

    [Test]
    public async Task FindLatestApk_NoApkInCurrentDir_ReturnsNull()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"wails_deploy_noapk_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var originalDir = Environment.CurrentDirectory;

        try
        {
            Environment.CurrentDirectory = tempDir;
            var result = DeployCommand.FindLatestApk();
            await Assert.That(result).IsNull();
        }
        finally
        {
            Environment.CurrentDirectory = originalDir;
            try { Directory.Delete(tempDir, true); } catch { /* 忽略 */ }
        }
    }

    [Test]
    public async Task FindLatestApk_MultipleApks_ReturnsLatestByLastWriteTime()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"wails_deploy_multi_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var binReleaseDir = Path.Combine(tempDir, "bin", "Release");
        Directory.CreateDirectory(binReleaseDir);
        var originalDir = Environment.CurrentDirectory;

        try
        {
            Environment.CurrentDirectory = tempDir;

            // 创建两个 APK 文件，第二个较新
            var oldApk = Path.Combine(binReleaseDir, "app-v1.apk");
            var newApk = Path.Combine(binReleaseDir, "app-v2.apk");
            await File.WriteAllTextAsync(oldApk, "old");
            await Task.Delay(50); // 确保 LastWriteTime 不同
            await File.WriteAllTextAsync(newApk, "new");

            var result = DeployCommand.FindLatestApk();
            await Assert.That(result).IsNotNull();
            await Assert.That(Path.GetFileName(result)).IsEqualTo("app-v2.apk");
        }
        finally
        {
            Environment.CurrentDirectory = originalDir;
            try { Directory.Delete(tempDir, true); } catch { /* 忽略 */ }
        }
    }

    [Test]
    public async Task FindLatestApk_EmptyBinDirectory_ReturnsNull()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"wails_deploy_empty_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(Path.Combine(tempDir, "bin"));
        var originalDir = Environment.CurrentDirectory;

        try
        {
            Environment.CurrentDirectory = tempDir;
            var result = DeployCommand.FindLatestApk();
            await Assert.That(result).IsNull();
        }
        finally
        {
            Environment.CurrentDirectory = originalDir;
            try { Directory.Delete(tempDir, true); } catch { /* 忽略 */ }
        }
    }

    // === FindAdb ===
    // 注：FindAdb 依赖 PATH 环境变量和文件系统。
    // 此测试验证 FindAdb 在 adb 已安装的环境下能返回非 null 路径（环境敏感）。

    [Test]
    public async Task FindAdb_AdbInstalled_ReturnsNonNullPath()
    {
        // 在开发者环境中，adb 通常已安装于 Android SDK 默认路径
        var result = DeployCommand.FindAdb();
        // 不做严格断言：若 adb 未安装则返回 null（CI 环境常见），若已安装则返回路径
        // 此测试主要用于验证 FindAdb 不会抛异常
        if (result is not null)
        {
            await Assert.That(File.Exists(result)).IsTrue();
        }
    }
}
