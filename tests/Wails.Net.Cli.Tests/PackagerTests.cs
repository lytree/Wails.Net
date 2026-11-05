using System.IO.Compression;
using System.Text;
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

    /// <summary>
    /// 读取 tar.gz 归档内每个文件的 Unix 权限位。
    /// </summary>
    /// <param name="archivePath">tar.gz 路径。</param>
    /// <returns>归档内路径到八进制权限值的映射。</returns>
    private static async Task<Dictionary<string, int>> ReadTarGzModesAsync(string archivePath)
    {
        var modes = new Dictionary<string, int>(StringComparer.Ordinal);
        await using var fileStream = File.OpenRead(archivePath);
        await using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);

        var header = new byte[512];
        while (true)
        {
            var read = 0;
            while (read < header.Length)
            {
                var chunk = await gzipStream.ReadAsync(header.AsMemory(read, header.Length - read));
                if (chunk == 0)
                {
                    return modes;
                }
                read += chunk;
            }

            if (header[0] == 0)
            {
                // 两个全零块为归档结束标记
                return modes;
            }

            var name = Encoding.UTF8.GetString(header, 0, 100).TrimEnd('\0');
            var modeText = Encoding.ASCII.GetString(header, 100, 8).Trim('\0', ' ');
            modes[name] = Convert.ToInt32(modeText, 8);

            var sizeText = Encoding.ASCII.GetString(header, 124, 12).Trim('\0', ' ');
            var size = Convert.ToInt64(sizeText, 8);
            if (size > 0)
            {
                var total = (int)size + (512 - (int)(size % 512)) % 512;
                var body = new byte[total];
                var skipped = 0;
                while (skipped < body.Length)
                {
                    var chunk = await gzipStream.ReadAsync(body.AsMemory(skipped, body.Length - skipped));
                    if (chunk == 0)
                    {
                        return modes;
                    }
                    skipped += chunk;
                }
            }
        }
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
            Skip.Test("仅在 Windows 上运行");
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

    [Test]
    public async Task PackageAsync_UnsupportedFormat_PreservesPreviousOutput()
    {
        var tempDir = CreateTempDirectory();
        var outputDir = Path.Combine(Path.GetTempPath(), "wailsnet-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDir);
        var previousOutput = Path.Combine(outputDir, "TestApp-1.0.0.zip");

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "test.txt"), "hello world");
            await File.WriteAllTextAsync(previousOutput, "previous package");

            var packager = new Packager();
            var options = new PackageOptions
            {
                Format = (PackageFormat)99,
                OutputDirectory = outputDir,
                AppName = "TestApp",
                Version = "1.0.0",
                GenerateChecksum = false,
            };

            var result = await packager.PackageAsync(tempDir, options);

            await Assert.That(result.Success).IsFalse();
            await Assert.That(result.ErrorMessage).Contains("不支持的打包格式");
            // 打包失败时上一次产物必须保留（Wails v3 #6480），且不残留暂存目录。
            await Assert.That(await File.ReadAllTextAsync(previousOutput)).IsEqualTo("previous package");
            await Assert.That(Directory.GetFileSystemEntries(outputDir).Length).IsEqualTo(1);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
            try { Directory.Delete(outputDir, recursive: true); } catch { }
        }
    }

    [Test]
    public async Task PackageAsync_ZipSuccess_ReplacesPreviousOutput()
    {
        var tempDir = CreateTempDirectory();
        var outputDir = Path.Combine(Path.GetTempPath(), "wailsnet-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDir);
        var previousOutput = Path.Combine(outputDir, "TestApp-1.0.0.zip");

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "test.txt"), "hello world");
            await File.WriteAllTextAsync(previousOutput, "previous package");

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
            await Assert.That(result.OutputPath).IsEqualTo(previousOutput);
            using var archive = System.IO.Compression.ZipFile.OpenRead(previousOutput);
            await Assert.That(archive.Entries.Count).IsEqualTo(1);
            await Assert.That(Directory.GetFileSystemEntries(outputDir).Length).IsEqualTo(1);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
            try { Directory.Delete(outputDir, recursive: true); } catch { }
        }
    }

    [Test]
    public async Task PackageAsync_TarGz_MainExecutableHasExecutableMode()
    {
        var tempDir = CreateTempDirectory();
        var outputDir = Path.Combine(Path.GetTempPath(), "wailsnet-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            // 主程序命名为 AppName，命中 FindLinuxExecutable 的优先匹配路径。
            await File.WriteAllTextAsync(Path.Combine(tempDir, "TestApp"), "#!/bin/sh\necho app");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "data.txt"), "config");

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
            var modes = await ReadTarGzModesAsync(result.OutputPath!);
            // 0755（八进制）= 493：与 deb/rpm 的 install -m 0755 一致。
            await Assert.That(modes["TestApp"]).IsEqualTo(493);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
            try { Directory.Delete(outputDir, recursive: true); } catch { }
        }
    }

    [Test]
    public async Task PackageAsync_TarGz_NonExecutableFileIsNotElevated()
    {
        var tempDir = CreateTempDirectory();
        var outputDir = Path.Combine(Path.GetTempPath(), "wailsnet-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir, "TestApp"), "#!/bin/sh\necho app");
            var dataPath = Path.Combine(tempDir, "data.txt");
            await File.WriteAllTextAsync(dataPath, "config");

            // Unix 主机上显式设为 0640，验证保留源权限且不被提权；Windows 无 Unix 权限位，预期默认 0644。
            var expectedMode = 420;
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    dataPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
                expectedMode = 416;
            }

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
            var modes = await ReadTarGzModesAsync(result.OutputPath!);
            await Assert.That(modes["data.txt"]).IsEqualTo(expectedMode);
            await Assert.That(modes["data.txt"]).IsNotEqualTo(493);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
            try { Directory.Delete(outputDir, recursive: true); } catch { }
        }
    }

    [Test]
    public async Task PackageAsync_TarGz_PreservesExecutableBitOfSecondaryScript()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip.Test("仅在 Unix 主机上验证源文件可执行位保留");
            return;
        }

        var tempDir = CreateTempDirectory();
        var outputDir = Path.Combine(Path.GetTempPath(), "wailsnet-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir, "TestApp"), "#!/bin/sh\necho app");
            var scriptPath = Path.Combine(tempDir, "helper.sh");
            await File.WriteAllTextAsync(scriptPath, "#!/bin/sh\necho helper");
            File.SetUnixFileMode(
                scriptPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

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
            var modes = await ReadTarGzModesAsync(result.OutputPath!);
            await Assert.That(modes["helper.sh"]).IsEqualTo(493);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
            try { Directory.Delete(outputDir, recursive: true); } catch { }
        }
    }

    #endregion
}
