using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using TUnit.Assertions;
using TUnit.Core;
using Wails.Net.Cli.Commands;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// VersionCommand、CleanCommand、InfoCommand 的单元测试（TUnit）。
/// 验证版本号获取、bin/obj 清理、项目信息解析。
/// </summary>
[NotInParallel]
public sealed class NewCliCommandsTests
{
    // ---------------------------------------------------------------------
    // VersionCommand
    // ---------------------------------------------------------------------

    [Test]
    public async Task VersionCommand_GetCliVersion_ReturnsNonEmptyString()
    {
        var version = VersionCommand.GetCliVersion();
        await Assert.That(version).IsNotNull();
        await Assert.That(string.IsNullOrEmpty(version)).IsFalse();
    }

    [Test]
    public async Task VersionCommand_GetOsDescription_ReturnsNonEmptyString()
    {
        var osDesc = VersionCommand.GetOsDescription();
        await Assert.That(osDesc).IsNotNull();
        await Assert.That(string.IsNullOrEmpty(osDesc)).IsFalse();
    }

    [Test]
    public async Task VersionCommand_GetOsDescription_ContainsPlatformName()
    {
        var osDesc = VersionCommand.GetOsDescription();
        var expectedPlatform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "Windows"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                ? "Linux"
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                    ? "macOS"
                    : "未知";

        await Assert.That(osDesc).Contains(expectedPlatform);
    }

    // ---------------------------------------------------------------------
    // CleanCommand
    // ---------------------------------------------------------------------

    [Test]
    public async Task CleanCommand_CleanBinObjDirectories_RemovesAllBinAndObj()
    {
        // 在临时目录中创建模拟的 bin/obj 目录结构
        var tempRoot = Path.Combine(Path.GetTempPath(), $"wails_clean_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            // 创建 bin 和 obj 目录
            var binDir = Path.Combine(tempRoot, "bin");
            var objDir = Path.Combine(tempRoot, "obj");
            Directory.CreateDirectory(binDir);
            Directory.CreateDirectory(objDir);
            File.WriteAllText(Path.Combine(binDir, "test.txt"), "test");
            File.WriteAllText(Path.Combine(objDir, "test.txt"), "test");

            // 创建子目录中的 bin/obj
            var subDir = Path.Combine(tempRoot, "sub");
            Directory.CreateDirectory(subDir);
            var subBinDir = Path.Combine(subDir, "bin");
            var subObjDir = Path.Combine(subDir, "obj");
            Directory.CreateDirectory(subBinDir);
            Directory.CreateDirectory(subObjDir);

            // 执行清理
            var rootInfo = new DirectoryInfo(tempRoot);
            var count = CleanCommand.CleanBinObjDirectories(rootInfo);

            // 验证删除了 4 个目录
            await Assert.That(count).IsEqualTo(4);
            await Assert.That(Directory.Exists(binDir)).IsFalse();
            await Assert.That(Directory.Exists(objDir)).IsFalse();
            await Assert.That(Directory.Exists(subBinDir)).IsFalse();
            await Assert.That(Directory.Exists(subObjDir)).IsFalse();
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { /* 忽略 */ }
        }
    }

    [Test]
    public async Task CleanCommand_CleanBinObjDirectories_NoBinObj_ReturnsZero()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"wails_clean_empty_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            // 只创建普通目录，不含 bin/obj
            var normalDir = Path.Combine(tempRoot, "src");
            Directory.CreateDirectory(normalDir);
            File.WriteAllText(Path.Combine(normalDir, "file.txt"), "test");

            var rootInfo = new DirectoryInfo(tempRoot);
            var count = CleanCommand.CleanBinObjDirectories(rootInfo);

            await Assert.That(count).IsEqualTo(0);
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { /* 忽略 */ }
        }
    }

    [Test]
    public async Task CleanCommand_CleanBinObjDirectories_CaseInsensitive()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"wails_clean_case_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            // 创建大写 BIN 和 OBJ 目录（在 Windows 上不区分大小写，但在 Linux 上区分）
            var binDir = Path.Combine(tempRoot, "BIN");
            Directory.CreateDirectory(binDir);

            var rootInfo = new DirectoryInfo(tempRoot);
            var count = CleanCommand.CleanBinObjDirectories(rootInfo);

            // Windows 上 BIN 与 bin 等价（已删除），Linux 上也应该被删除（OrdinalIgnoreCase）
            await Assert.That(count).IsEqualTo(1);
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { /* 忽略 */ }
        }
    }

    // ---------------------------------------------------------------------
    // InfoCommand (仅测试静态方法，不测试完整命令执行)
    // ---------------------------------------------------------------------

    [Test]
    public async Task InfoCommand_PrintProjectInfo_ValidProject_DoesNotThrow()
    {
        // 创建一个临时 .csproj 文件
        var tempDir = Path.Combine(Path.GetTempPath(), $"wails_info_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var csprojPath = Path.Combine(tempDir, "TestProject.csproj");
            var csprojContent = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="TUnit" Version="1.58.0" />
                    <PackageReference Include="NSubstitute" Version="5.3.0" />
                  </ItemGroup>
                  <ItemGroup>
                    <ProjectReference Include="..\Other\Other.csproj" />
                  </ItemGroup>
                </Project>
                """;
            File.WriteAllText(csprojPath, csprojContent);

            // 应不抛异常
            var projectFile = new FileInfo(csprojPath);
            InfoCommand.PrintProjectInfo(projectFile);

            // 验证文件存在即可（输出已打印到控制台）
            await Assert.That(File.Exists(csprojPath)).IsTrue();
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* 忽略 */ }
        }
    }

    [Test]
    public async Task InfoCommand_PrintProjectInfo_NonExistentProject_DoesNotThrow()
    {
        var nonExistentFile = new FileInfo(Path.Combine(Path.GetTempPath(), "non_existent.csproj"));

        // 应静默处理不抛异常
        await Assert.That(() => InfoCommand.PrintProjectInfo(nonExistentFile)).ThrowsNothing();
    }

    [Test]
    public async Task InfoCommand_PrintEnvironmentInfo_DoesNotThrow()
    {
        // 应不抛异常
        await Assert.That(() => InfoCommand.PrintEnvironmentInfo()).ThrowsNothing();
    }
}
