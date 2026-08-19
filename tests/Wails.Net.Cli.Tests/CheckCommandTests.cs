using System.IO;
using System.Text;
using TUnit.Core;
using Wails.Net.Application.Security;
using Wails.Net.Cli.Commands;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// R3-D.A 集成测试：<see cref="CheckCommand"/> 在不同来源组合下的
/// 自动加载行为（源生成器聚合 + 外部清单目录）。
/// 通过重定向 <see cref="Console.Out"/> 与 <see cref="Console.Error"/>
/// 抑制被测方法的标准输出，避免污染测试运行器。
/// </summary>
[NotInParallel]
public sealed class CheckCommandTests
{
    private TextWriter? _originalOut;
    private TextWriter? _originalErr;
    private StringBuilder _outCapture = new();
    private StringBuilder _errCapture = new();

    [Before(Test)]
    public void Setup()
    {
#pragma warning disable TUnit0055
        PluginPermissionManifestRegistry.ClearForTests();
        _originalOut = Console.Out;
        _originalErr = Console.Error;
        _outCapture = new StringBuilder();
        _errCapture = new StringBuilder();
        Console.SetOut(new StringWriter(_outCapture));
        Console.SetError(new StringWriter(_errCapture));
#pragma warning restore TUnit0055
    }

    [After(Test)]
    public void Teardown()
    {
#pragma warning disable TUnit0055
        Console.SetOut(_originalOut!);
        Console.SetError(_originalErr!);
#pragma warning restore TUnit0055
        PluginPermissionManifestRegistry.ClearForTests();
    }

    private static string CreateTempDir(string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), prefix + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string CreateCapabilitiesDirWithJson()
    {
        var dir = CreateTempDir("wails-check-test-cap");
        File.WriteAllText(Path.Combine(dir, "default.json"), """
        {
          "identifier": "main-cap",
          "description": "test",
          "permissions": ["window:default", "fs:allow-read"]
        }
        """);
        return dir;
    }

    private static string CreateManifestFile(string pluginName, params string[] permissions)
    {
        var dir = CreateTempDir("wails-check-test-manifest");
        var arr = string.Join(",", permissions.Select(p => "\"" + p + "\""));
        File.WriteAllText(Path.Combine(dir, pluginName + ".json"), "[" + arr + "]");
        return dir;
    }

    private static string CreateMinimalProject()
    {
        var dir = CreateTempDir("wails-check-test-proj");
        File.WriteAllText(Path.Combine(dir, "Test.csproj"), """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <OutputType>Exe</OutputType>
          </PropertyGroup>
        </Project>
        """);
        File.WriteAllText(Path.Combine(dir, "Program.cs"), "// empty");
        return dir;
    }

    [Test]
    public async Task ExecuteAsync_RegistryOnly_AutoLoadsGeneratedPermissions()
    {
        // Arrange: registry has 2 plugins with 3 permissions total
        PluginPermissionManifestRegistry.Register("window", """{"permissions":["window:default","window:allow-close"]}""");
        PluginPermissionManifestRegistry.Register("fs", """{"permissions":["fs:allow-read"]}""");
        var projectDir = CreateMinimalProject();
        var capDir = CreateCapabilitiesDirWithJson();

        try
        {
            var cmd = new CheckCommand();
            var exit = await cmd.ExecuteAsync(
                new FileInfo(Path.Combine(projectDir, "Test.csproj")),
                new DirectoryInfo(capDir),
                manifestDir: null);

            await Assert.That(exit).IsEqualTo(0);
            var combined = _outCapture.ToString();
            await Assert.That(combined).Contains("源生成");
            await Assert.That(combined).Contains("3");
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
            Directory.Delete(capDir, recursive: true);
        }
    }

    [Test]
    public async Task ExecuteAsync_RegistryPlusManifestDir_MergesBoth()
    {
        // Arrange: registry 2 + manifest dir 2 (1 overlap) = 3 distinct
        PluginPermissionManifestRegistry.Register("window", """{"permissions":["window:default","window:allow-close"]}""");
        var projectDir = CreateMinimalProject();
        var capDir = CreateCapabilitiesDirWithJson();
        var manifestDir = CreateManifestFile("extra", "window:default", "core:default");

        try
        {
            var cmd = new CheckCommand();
            var exit = await cmd.ExecuteAsync(
                new FileInfo(Path.Combine(projectDir, "Test.csproj")),
                new DirectoryInfo(capDir),
                new DirectoryInfo(manifestDir));

            await Assert.That(exit).IsEqualTo(0);
            var combined = _outCapture.ToString();
            await Assert.That(combined).Contains("源生成 2");
            await Assert.That(combined).Contains("外部目录 2");
            await Assert.That(combined).Contains("去重后 3");
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
            Directory.Delete(capDir, recursive: true);
            Directory.Delete(manifestDir, recursive: true);
        }
    }

    [Test]
    public async Task ExecuteAsync_ManifestDirMissing_ReturnsError()
    {
        // Arrange: user explicitly pointed to nonexistent dir
        var projectDir = CreateMinimalProject();
        var missing = Path.Combine(Path.GetTempPath(), "wails-no-such-" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            var cmd = new CheckCommand();
            var exit = await cmd.ExecuteAsync(
                new FileInfo(Path.Combine(projectDir, "Test.csproj")),
                capabilitiesDir: null,
                new DirectoryInfo(missing));

            await Assert.That(exit).IsEqualTo(1);
            await Assert.That(_errCapture.ToString()).Contains("权限清单目录不存在");
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    [Test]
    public async Task ExecuteAsync_NoRegistryNoManifest_StillPassesFormatValidation()
    {
        // Arrange: both sources empty -> format-only validation
        var projectDir = CreateMinimalProject();
        var capDir = CreateCapabilitiesDirWithJson();

        try
        {
            var cmd = new CheckCommand();
            var exit = await cmd.ExecuteAsync(
                new FileInfo(Path.Combine(projectDir, "Test.csproj")),
                new DirectoryInfo(capDir),
                manifestDir: null);

            await Assert.That(exit).IsEqualTo(0);
            var combined = _outCapture.ToString();
            await Assert.That(combined).Contains("源生成 0");
            await Assert.That(combined).Contains("外部目录 0");
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
            Directory.Delete(capDir, recursive: true);
        }
    }

    [Test]
    public async Task ExecuteAsync_UnknownPermissionInCapability_EmitsWarningFromRegistry()
    {
        // Arrange: capability references a permission not in registry
        // (CapabilityChecker design: unknown permissions are Warning not Error, do not block build)
        PluginPermissionManifestRegistry.Register("core", """{"permissions":["core:default"]}""");
        var projectDir = CreateMinimalProject();
        var capDir = CreateTempDir("wails-check-unknown-cap");
        File.WriteAllText(Path.Combine(capDir, "bad.json"), """
        {
          "identifier": "bad-cap",
          "description": "typo",
          "permissions": ["core:default", "core:allow-typo"]
        }
        """);

        try
        {
            var cmd = new CheckCommand();
            var exit = await cmd.ExecuteAsync(
                new FileInfo(Path.Combine(projectDir, "Test.csproj")),
                new DirectoryInfo(capDir),
                manifestDir: null);

            // Exit 0 (warning does not block), but output includes the typo warning
            await Assert.That(exit).IsEqualTo(0);
            await Assert.That(_outCapture.ToString()).Contains("core:allow-typo");
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
            Directory.Delete(capDir, recursive: true);
        }
    }
}
