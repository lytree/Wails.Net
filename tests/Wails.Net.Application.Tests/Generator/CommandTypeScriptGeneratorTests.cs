using TUnit.Core;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Manifests;
using Wails.Net.Generator;

namespace Wails.Net.Application.Tests.Generator;

/// <summary>
/// <see cref="CommandTypeScriptGenerator"/> 的单元测试（TUnit）。
/// 对应审计 P2-1：以命令 manifest 为源生成 TypeScript 调用封装，消除前后端 API 漂移。
/// </summary>
[NotInParallel]
public sealed class CommandTypeScriptGeneratorTests
{
    [Test]
    public async Task ToFunctionName_ConvertsDotName_ToCamelCaseIdentifier()
    {
        await Assert.That(CommandTypeScriptGenerator.ToFunctionName("fs.readFile")).IsEqualTo("fsReadFile");
        await Assert.That(CommandTypeScriptGenerator.ToFunctionName("window.setTitle")).IsEqualTo("windowSetTitle");
        await Assert.That(CommandTypeScriptGenerator.ToFunctionName("app")).IsEqualTo("app");
        await Assert.That(CommandTypeScriptGenerator.ToFunctionName("counter.increment")).IsEqualTo("counterIncrement");
    }

    [Test]
    public async Task Generate_ProducesStronglyTypedFunctions_ForEachCommand()
    {
        // 安排：无参、单参、多参命令
        var registry = new CommandRegistry();
        registry.MapCommand("app.greet", () => "Hello");
        registry.MapCommand("fs.readFile", (string path) => { /* noop */ }, "fs:allow-read");
        registry.MapCommand("window.setSize", (int width, int height) => { /* noop */ });
        var manifest = ManifestGenerator.GenerateCommandsManifest(registry);

        // 操作
        var ts = new CommandTypeScriptGenerator().Generate(manifest);

        // 断言：函数声明、参数类型、调用封装齐全
        await Assert.That(ts).Contains("export function appGreet(): Promise<unknown>");
        await Assert.That(ts).Contains("export function fsReadFile(arg0: string): Promise<unknown>");
        await Assert.That(ts).Contains("export function windowSetSize(arg0: number, arg1: number): Promise<unknown>");
        await Assert.That(ts).Contains("wails.call('fs.readFile', [arg0])");
        await Assert.That(ts).Contains("wails.call('window.setSize', [arg0, arg1])");
        await Assert.That(ts).Contains("wails.call('app.greet', [])");
    }

    [Test]
    public async Task Generate_EmptyManifest_ProducesHeaderWithImport()
    {
        // 安排：空注册表
        var registry = new CommandRegistry();
        var manifest = ManifestGenerator.GenerateCommandsManifest(registry);

        // 操作
        var ts = new CommandTypeScriptGenerator().Generate(manifest);

        // 断言：保留 import 与"未发现命令"提示
        await Assert.That(ts).Contains("@wails-net/runtime");
        await Assert.That(ts).Contains("未发现命令");
    }

    [Test]
    public async Task Generate_CommandManifest_IncludesParameterTypes()
    {
        // 安排：带业务参数的命令
        var registry = new CommandRegistry();
        registry.MapCommand("dialog.openFile", (string title, bool multiSelect) => { /* noop */ });

        // 操作
        var manifest = ManifestGenerator.GenerateCommandsManifest(registry);

        // 断言：参数类型映射为 TypeScript 声明
        var entry = manifest.Commands.Single(c => c.Name == "dialog.openFile");
        await Assert.That(entry.Parameters).Contains("arg0: string");
        await Assert.That(entry.Parameters).Contains("arg1: boolean");
    }
}
