using System.Text.Json;
using TUnit.Core;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Manifests;
using Wails.Net.Generator;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// 命令清单 → TypeScript 封装生成的端到端测试（P2-1 收尾）。
/// 验证 <c>wails generate --commands-manifest</c> 的核心链路：
/// 应用导出 manifest JSON → <see cref="CommandTypeScriptGenerator.LoadManifest"/> 读取 →
/// <see cref="CommandTypeScriptGenerator.GenerateToDisk"/> 写出 commands.ts。
/// </summary>
[NotInParallel]
public sealed class CommandManifestGenerationTests
{
    [Test]
    public async Task LoadManifest_ReadsManifestJson_ProducedByGenerator()
    {
        // 安排：ManifestGenerator 生成命令清单并序列化为 JSON（模拟应用侧 WriteCommandsManifestToDirectory）
        var registry = new CommandRegistry();
        registry.MapCommand("fs.readFile", (string path) => { /* noop */ }, "fs:allow-read");
        registry.MapCommand("app.greet", () => "Hello");
        var json = ManifestGenerator.GenerateCommandsManifestJson(registry);

        var tempDir = Path.Combine(Path.GetTempPath(), $"wails-manifest-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var manifestPath = Path.Combine(tempDir, ManifestGenerator.ManifestFileName);
        File.WriteAllText(manifestPath, json);
        try
        {
            // 操作：LoadManifest 反序列化
            var manifest = CommandTypeScriptGenerator.LoadManifest(manifestPath);

            // 断言
            await Assert.That(manifest.Version).IsEqualTo(ManifestGenerator.ManifestVersion);
            await Assert.That(manifest.Commands).Count().IsEqualTo(2);
            await Assert.That(manifest.Commands.Any(c => c.Name == "fs.readFile" && !c.Anonymous)).IsTrue();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test]
    public async Task LoadManifest_MissingFile_ThrowsFileNotFound()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"wails-missing-{Guid.NewGuid():N}.json");
        await Assert.That(() => CommandTypeScriptGenerator.LoadManifest(missing))
            .ThrowsExactly<FileNotFoundException>();
    }

    [Test]
    public async Task GenerateToDisk_WritesCommandsTs_WithStronglyTypedFunctions()
    {
        // 安排：构造命令清单
        var registry = new CommandRegistry();
        registry.MapCommand("window.setSize", (int width, int height) => { /* noop */ });
        var manifest = ManifestGenerator.GenerateCommandsManifest(registry);

        var tempDir = Path.Combine(Path.GetTempPath(), $"wails-generate-test-{Guid.NewGuid():N}");
        try
        {
            // 操作：生成到磁盘
            var filePath = CommandTypeScriptGenerator.GenerateToDisk(manifest, tempDir);

            // 断言：文件存在且内容为强类型封装
            await Assert.That(File.Exists(filePath)).IsTrue();
            var content = await File.ReadAllTextAsync(filePath);
            await Assert.That(content).Contains("export function windowSetSize(arg0: number, arg1: number): Promise<unknown>");
            await Assert.That(content).Contains("wails.call('window.setSize', [arg0, arg1])");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test]
    public async Task ManifestRoundTrip_PreservesCommandMetadata()
    {
        // 安排：含 capability 与参数的命令（带 capability 的 MapCommand 重载为单业务参数形态）
        var registry = new CommandRegistry();
        registry.MapCommand("dialog.openFile", (string title) => { /* noop */ }, "dialog:allow-open-file");
        var manifest = ManifestGenerator.GenerateCommandsManifest(registry);
        var json = ManifestGenerator.GenerateCommandsManifestJson(manifest);

        // 操作：JSON 反序列化回清单（模拟 CLI 从文件读取）
        var loaded = JsonSerializer.Deserialize<CommandsManifest>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        })!;

        // 断言：元数据完整保留
        var entry = loaded.Commands.Single(c => c.Name == "dialog.openFile");
        await Assert.That(entry.RequiredCapabilities).Contains("dialog:allow-open-file");
        await Assert.That(entry.Parameters).Contains("arg0: string");
        await Assert.That(entry.Anonymous).IsFalse();
    }
}
