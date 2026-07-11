using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using Wails.Net.Cli.Build;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// 打包器单元测试。
/// 验证 NSIS 脚本生成、AppRun 脚本生成、.desktop 文件生成、可执行文件查找等逻辑。
/// </summary>
[NotInParallel]
public sealed class PackagerTests
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

    #region GenerateNsisScript

    [Test]
    public async Task GenerateNsisScript_ContainsAppName()
    {
        var script = Packager.GenerateNsisScript(
            "MyApp", "2.0.0", "TestPublisher",
            @"C:\out\installer.exe", @"C:\src", "MyApp.exe");

        await Assert.That(script).Contains("!define APPNAME \"MyApp\"");
    }

    [Test]
    public async Task GenerateNsisScript_ContainsVersion()
    {
        var script = Packager.GenerateNsisScript(
            "MyApp", "2.0.0", "TestPublisher",
            @"C:\out\installer.exe", @"C:\src", "MyApp.exe");

        await Assert.That(script).Contains("!define APPVERSION \"2.0.0\"");
    }

    [Test]
    public async Task GenerateNsisScript_ContainsPublisher()
    {
        var script = Packager.GenerateNsisScript(
            "MyApp", "2.0.0", "TestPublisher",
            @"C:\out\installer.exe", @"C:\src", "MyApp.exe");

        await Assert.That(script).Contains("!define APPPUBLISHER \"TestPublisher\"");
    }

    [Test]
    public async Task GenerateNsisScript_ContainsOutFile()
    {
        var script = Packager.GenerateNsisScript(
            "MyApp", "2.0.0", "TestPublisher",
            @"C:\out\installer.exe", @"C:\src", "MyApp.exe");

        await Assert.That(script).Contains("OutFile \"C:\\out\\installer.exe\"");
    }

    [Test]
    public async Task GenerateNsisScript_ContainsSourceDir()
    {
        var script = Packager.GenerateNsisScript(
            "MyApp", "2.0.0", "TestPublisher",
            @"C:\out\installer.exe", @"C:\src", "MyApp.exe");

        await Assert.That(script).Contains("File /r \"C:\\src\\*.*\"");
    }

    [Test]
    public async Task GenerateNsisScript_ContainsExeName()
    {
        var script = Packager.GenerateNsisScript(
            "MyApp", "2.0.0", "TestPublisher",
            @"C:\out\installer.exe", @"C:\src", "MyApp.exe");

        await Assert.That(script).Contains("$INSTDIR\\MyApp.exe");
    }

    [Test]
    public async Task GenerateNsisScript_ContainsNsisVariables()
    {
        var script = Packager.GenerateNsisScript(
            "MyApp", "2.0.0", "TestPublisher",
            @"C:\out\installer.exe", @"C:\src", "MyApp.exe");

        await Assert.That(script).Contains("${APPNAME}");
        await Assert.That(script).Contains("${APPVERSION}");
        await Assert.That(script).Contains("${APPPUBLISHER}");
    }

    [Test]
    public async Task GenerateNsisScript_ContainsInstallSection()
    {
        var script = Packager.GenerateNsisScript(
            "MyApp", "2.0.0", "TestPublisher",
            @"C:\out\installer.exe", @"C:\src", "MyApp.exe");

        await Assert.That(script).Contains("Section \"Install\"");
        await Assert.That(script).Contains("SetOutPath $INSTDIR");
        await Assert.That(script).Contains("WriteUninstaller");
    }

    [Test]
    public async Task GenerateNsisScript_ContainsUninstallSection()
    {
        var script = Packager.GenerateNsisScript(
            "MyApp", "2.0.0", "TestPublisher",
            @"C:\out\installer.exe", @"C:\src", "MyApp.exe");

        await Assert.That(script).Contains("Section \"Uninstall\"");
        await Assert.That(script).Contains("DeleteRegKey");
        await Assert.That(script).Contains("RMDir");
    }

    [Test]
    public async Task GenerateNsisScript_ContainsShortcuts()
    {
        var script = Packager.GenerateNsisScript(
            "MyApp", "2.0.0", "TestPublisher",
            @"C:\out\installer.exe", @"C:\src", "MyApp.exe");

        await Assert.That(script).Contains("$SMPROGRAMS");
        await Assert.That(script).Contains("$DESKTOP");
        await Assert.That(script).Contains("CreateShortcut");
    }

    [Test]
    public async Task GenerateNsisScript_ContainsRegistryEntries()
    {
        var script = Packager.GenerateNsisScript(
            "MyApp", "2.0.0", "TestPublisher",
            @"C:\out\installer.exe", @"C:\src", "MyApp.exe");

        await Assert.That(script).Contains("WriteRegStr");
        await Assert.That(script).Contains("HKLM");
        await Assert.That(script).Contains("Uninstall\\${APPNAME}");
    }

    [Test]
    public async Task GenerateNsisScript_NormalizesForwardSlashes()
    {
        var script = Packager.GenerateNsisScript(
            "MyApp", "1.0.0", "Pub",
            "C:/out/installer.exe", "C:/src", "MyApp.exe");

        await Assert.That(script).Contains("C:\\out\\installer.exe");
        await Assert.That(script).Contains("C:\\src\\*.*");
    }

    #endregion

    #region GenerateAppRunScript

    [Test]
    public async Task GenerateAppRunScript_ContainsShebang()
    {
        var script = Packager.GenerateAppRunScript("myapp");

        await Assert.That(script).Contains("#!/bin/sh");
    }

    [Test]
    public async Task GenerateAppRunScript_ContainsExeName()
    {
        var script = Packager.GenerateAppRunScript("myapp");

        await Assert.That(script).Contains("usr/bin/myapp");
    }

    [Test]
    public async Task GenerateAppRunScript_ContainsReadlink()
    {
        var script = Packager.GenerateAppRunScript("myapp");

        await Assert.That(script).Contains("readlink -f");
    }

    [Test]
    public async Task GenerateAppRunScript_ContainsUnionPreload()
    {
        var script = Packager.GenerateAppRunScript("myapp");

        await Assert.That(script).Contains("UNION_PRELOAD");
        await Assert.That(script).Contains("LD_PRELOAD");
        await Assert.That(script).Contains("libunionpreload.so");
    }

    [Test]
    public async Task GenerateAppRunScript_ContainsExec()
    {
        var script = Packager.GenerateAppRunScript("myapp");

        await Assert.That(script).Contains("exec");
        await Assert.That(script).Contains("\"$@\"");
    }

    #endregion

    #region GenerateDesktopFile

    [Test]
    public async Task GenerateDesktopFile_ContainsAppName()
    {
        var content = Packager.GenerateDesktopFile("MyApp");

        await Assert.That(content).Contains("Name=MyApp");
        await Assert.That(content).Contains("Exec=MyApp");
        await Assert.That(content).Contains("Icon=MyApp");
    }

    [Test]
    public async Task GenerateDesktopFile_HasApplicationType()
    {
        var content = Packager.GenerateDesktopFile("MyApp");

        await Assert.That(content).Contains("Type=Application");
    }

    [Test]
    public async Task GenerateDesktopFile_HasCategoriesAndTerminal()
    {
        var content = Packager.GenerateDesktopFile("MyApp");

        await Assert.That(content).Contains("Categories=Utility;");
        await Assert.That(content).Contains("Terminal=false");
    }

    #endregion

    #region FindExecutableInPath

    [Test]
    public async Task FindExecutableInPath_ReturnsNullForNonExistent()
    {
        var result = Packager.FindExecutableInPath("definitely-not-exist-" + Guid.NewGuid().ToString("N") + ".exe");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task FindExecutableInPath_FindsExistingExecutable()
    {
        // 在 Windows 上 dotnet 应该在 PATH 中
        var expectedName = OperatingSystem.IsWindows() ? "where.exe" : "sh";
        var result = Packager.FindExecutableInPath(expectedName);

        await Assert.That(result).IsNotNull();
        await Assert.That(File.Exists(result!)).IsTrue();
    }

    #endregion

    #region FindMainExecutable

    [Test]
    public async Task FindMainExecutable_FindsExeByName()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "MyApp.exe"), "dummy");
            File.WriteAllText(Path.Combine(tempDir, "other.exe"), "dummy");
            File.WriteAllText(Path.Combine(tempDir, "lib.dll"), "dummy");

            var result = Packager.FindMainExecutable(tempDir, "MyApp");

            await Assert.That(result).IsEqualTo("MyApp.exe");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Test]
    public async Task FindMainExecutable_ReturnsFirstExeIfNoNameMatch()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "App1.exe"), "dummy");
            File.WriteAllText(Path.Combine(tempDir, "App2.exe"), "dummy");

            var result = Packager.FindMainExecutable(tempDir, "NonMatchingName");

            await Assert.That(result).IsNotNull();
            await Assert.That(result!.EndsWith(".exe")).IsTrue();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Test]
    public async Task FindMainExecutable_ReturnsNullIfNoExe()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "lib.dll"), "dummy");
            File.WriteAllText(Path.Combine(tempDir, "config.json"), "dummy");

            var result = Packager.FindMainExecutable(tempDir, "MyApp");

            await Assert.That(result).IsNull();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Test]
    public async Task FindMainExecutable_ReturnsNullForEmptyDirectory()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var result = Packager.FindMainExecutable(tempDir, "MyApp");

            await Assert.That(result).IsNull();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Test]
    public async Task FindMainExecutable_NameMatchIsCaseInsensitive()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "myapp.exe"), "dummy");

            var result = Packager.FindMainExecutable(tempDir, "MyApp");

            await Assert.That(result).IsEqualTo("myapp.exe");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    #endregion

    #region FindLinuxExecutable

    [Test]
    public async Task FindLinuxExecutable_FindsByName()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "myapp"), "dummy");
            File.WriteAllText(Path.Combine(tempDir, "lib.dll"), "dummy");

            var result = Packager.FindLinuxExecutable(tempDir, "myapp");

            await Assert.That(result).IsEqualTo("myapp");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Test]
    public async Task FindLinuxExecutable_FindsFileWithoutExtension()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "executable"), "dummy");
            File.WriteAllText(Path.Combine(tempDir, "lib.dll"), "dummy");
            File.WriteAllText(Path.Combine(tempDir, "config.json"), "dummy");

            var result = Packager.FindLinuxExecutable(tempDir, "nonmatching");

            await Assert.That(result).IsEqualTo("executable");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Test]
    public async Task FindLinuxExecutable_ReturnsNullIfNoExecutable()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "lib.dll"), "dummy");
            File.WriteAllText(Path.Combine(tempDir, "config.json"), "dummy");

            var result = Packager.FindLinuxExecutable(tempDir, "myapp");

            await Assert.That(result).IsNull();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    #endregion

    #region PackageAsync

    [Test]
    public async Task PackageAsync_NonExistentDir_ReturnsFailure()
    {
        var packager = new Packager();
        var options = new PackageOptions
        {
            Format = PackageFormat.Zip,
            OutputDirectory = Path.Combine(Path.GetTempPath(), "wailsnet-test-" + Guid.NewGuid().ToString("N")),
        };

        var result = await packager.PackageAsync(
            Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid().ToString("N")),
            options);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.ErrorMessage).Contains("发布目录不存在");
    }

    [Test]
    public async Task PackageAsync_AppImageOnWindows_ReturnsPlatformError()
    {
        if (!OperatingSystem.IsWindows())
        {
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
                Format = PackageFormat.AppImage,
                OutputDirectory = outputDir,
                AppName = "TestApp",
                Version = "1.0.0",
                GenerateChecksum = false,
            };

            var result = await packager.PackageAsync(tempDir, options);

            await Assert.That(result.Success).IsFalse();
            await Assert.That(result.ErrorMessage).Contains("AppImage");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
            try { Directory.Delete(outputDir, recursive: true); } catch { }
        }
    }

    [Test]
    public async Task PackageAsync_ZipCreatesValidArchive()
    {
        var tempDir = CreateTempDirectory();
        var outputDir = Path.Combine(Path.GetTempPath(), "wailsnet-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "test.txt"), "hello world");

            var packager = new Packager();
            var options = new PackageOptions
            {
                Format = PackageFormat.Zip,
                OutputDirectory = outputDir,
                AppName = "TestApp",
                Version = "1.0.0",
                GenerateChecksum = false,
            };

            var result = await packager.PackageAsync(tempDir, options);

            await Assert.That(result.Success).IsTrue();
            await Assert.That(result.OutputPath).IsNotNull();
            await Assert.That(File.Exists(result.OutputPath!)).IsTrue();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
            try { Directory.Delete(outputDir, recursive: true); } catch { }
        }
    }

    [Test]
    public async Task PackageAsync_TarGzCreatesValidArchive()
    {
        var tempDir = CreateTempDirectory();
        var outputDir = Path.Combine(Path.GetTempPath(), "wailsnet-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "test.txt"), "hello world");

            var packager = new Packager();
            var options = new PackageOptions
            {
                Format = PackageFormat.TarGz,
                OutputDirectory = outputDir,
                AppName = "TestApp",
                Version = "1.0.0",
                GenerateChecksum = false,
            };

            var result = await packager.PackageAsync(tempDir, options);

            await Assert.That(result.Success).IsTrue();
            await Assert.That(result.OutputPath).IsNotNull();
            await Assert.That(File.Exists(result.OutputPath!)).IsTrue();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
            try { Directory.Delete(outputDir, recursive: true); } catch { }
        }
    }

    [Test]
    public async Task PackageAsync_GeneratesChecksumWhenRequested()
    {
        var tempDir = CreateTempDirectory();
        var outputDir = Path.Combine(Path.GetTempPath(), "wailsnet-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "test.txt"), "hello world");

            var packager = new Packager();
            var options = new PackageOptions
            {
                Format = PackageFormat.Zip,
                OutputDirectory = outputDir,
                AppName = "TestApp",
                Version = "1.0.0",
                GenerateChecksum = true,
            };

            var result = await packager.PackageAsync(tempDir, options);

            await Assert.That(result.Success).IsTrue();
            await Assert.That(result.ChecksumPath).IsNotNull();
            await Assert.That(File.Exists(result.ChecksumPath!)).IsTrue();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
            try { Directory.Delete(outputDir, recursive: true); } catch { }
        }
    }

    #endregion
}
