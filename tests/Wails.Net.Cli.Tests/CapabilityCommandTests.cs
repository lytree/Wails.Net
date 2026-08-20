using System.IO;
using System.Text;
using TUnit.Core;
using Wails.Net.Application.Security;
using Wails.Net.Cli.Commands;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// R3-D.C 集成测试：<see cref="CapabilityCommand"/> 三个子命令（init / list / validate）。
/// 通过 Console 重定向断言 Info/Error/Success 输出。
/// </summary>
[NotInParallel]
public sealed class CapabilityCommandTests
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

    // === init 子命令 ===

    [Test]
    public async Task ExecuteInitAsync_RegisteredPlugin_WritesJsonToStdout()
    {
        PluginPermissionManifestRegistry.Register("window", """{"permissions":["window:default","window:allow-close"]}""");
        var cmd = new CapabilityCommand();

        var exit = await cmd.ExecuteInitAsync("window", outFile: null);

        await Assert.That(exit).IsEqualTo(0);
        var output = _outCapture.ToString();
        await Assert.That(output).Contains("\"identifier\": \"window:default\"");
        await Assert.That(output).Contains("window:allow-close");
    }

    [Test]
    public async Task ExecuteInitAsync_WritesJsonToFile()
    {
        PluginPermissionManifestRegistry.Register("fs", """{"permissions":["fs:default"]}""");
        var tmpDir = CreateTempDir("wails-cap-init");
        var outFile = new FileInfo(Path.Combine(tmpDir, "fs-default.json"));

        try
        {
            var cmd = new CapabilityCommand();
            var exit = await cmd.ExecuteInitAsync("fs", outFile);

            await Assert.That(exit).IsEqualTo(0);
            await Assert.That(outFile.Exists).IsTrue();
            var content = File.ReadAllText(outFile.FullName);
            await Assert.That(content).Contains("\"identifier\": \"fs:default\"");
            await Assert.That(_outCapture.ToString()).Contains("已生成 capability 文件");
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Test]
    public async Task ExecuteInitAsync_UnknownPlugin_ReturnsError()
    {
        PluginPermissionManifestRegistry.Register("window", """{"permissions":["window:default"]}""");
        var cmd = new CapabilityCommand();

        var exit = await cmd.ExecuteInitAsync("nosuchplugin", outFile: null);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(_errCapture.ToString()).Contains("未在已注册清单中找到");
    }

    [Test]
    public async Task ExecuteInitAsync_EmptyPluginName_ReturnsError()
    {
        var cmd = new CapabilityCommand();
        var exit = await cmd.ExecuteInitAsync("", outFile: null);
        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(_errCapture.ToString()).Contains("插件名不能为空");
    }

    // === list 子命令 ===

    [Test]
    public async Task ExecuteListAsync_EmptyRegistry_PrintsNoPluginsMessage()
    {
        var cmd = new CapabilityCommand();
        var exit = await cmd.ExecuteListAsync();

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(_outCapture.ToString()).Contains("未注册任何插件清单");
    }

    [Test]
    public async Task ExecuteListAsync_TwoPlugins_ListsBothWithDefaultCapabilityId()
    {
        PluginPermissionManifestRegistry.Register("window", """{"permissions":["window:default","window:allow-close","window:allow-minimize"]}""");
        PluginPermissionManifestRegistry.Register("fs", """{"permissions":["fs:default"]}""");
        var cmd = new CapabilityCommand();

        var exit = await cmd.ExecuteListAsync();

        await Assert.That(exit).IsEqualTo(0);
        var output = _outCapture.ToString();
        await Assert.That(output).Contains("已注册插件清单：2 个");
        await Assert.That(output).Contains("- window");
        await Assert.That(output).Contains("3 个权限");
        await Assert.That(output).Contains("default capability: window:default");
        await Assert.That(output).Contains("- fs");
        await Assert.That(output).Contains("default capability: fs:default");
    }

    // === validate 子命令 ===

    [Test]
    public async Task ExecuteValidateAsync_NonExistentFile_ReturnsError()
    {
        var cmd = new CapabilityCommand();
        var missing = new FileInfo(Path.Combine(Path.GetTempPath(), "wails-no-such-" + Guid.NewGuid().ToString("N")[..8] + ".json"));

        var exit = await cmd.ExecuteValidateAsync(missing);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(_errCapture.ToString()).Contains("文件不存在");
    }

    [Test]
    public async Task ExecuteValidateAsync_ValidFile_Passes()
    {
        var tmpDir = CreateTempDir("wails-cap-validate");
        var file = new FileInfo(Path.Combine(tmpDir, "valid.json"));
        File.WriteAllText(file.FullName, """
        {
          "identifier": "main-cap",
          "description": "test",
          "permissions": ["window:default"]
        }
        """);

        try
        {
            var cmd = new CapabilityCommand();
            var exit = await cmd.ExecuteValidateAsync(file);

            await Assert.That(exit).IsEqualTo(0);
            await Assert.That(_outCapture.ToString()).Contains("capability 校验通过");
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Test]
    public async Task ExecuteValidateAsync_UnknownPlatform_ReturnsErrorOrWarning()
    {
        var tmpDir = CreateTempDir("wails-cap-validate-platform");
        var file = new FileInfo(Path.Combine(tmpDir, "unknown.json"));
        File.WriteAllText(file.FullName, """
        {
          "identifier": "unknown-platform-cap",
          "permissions": ["window:default"],
          "platforms": ["plan9"]
        }
        """);

        try
        {
            var cmd = new CapabilityCommand();
            var exit = await cmd.ExecuteValidateAsync(file);

            // CM005: unknown platform triggers warning (not blocking)
            await Assert.That(exit).IsEqualTo(0);
            await Assert.That(_outCapture.ToString()).Contains("CM005");
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }
}
