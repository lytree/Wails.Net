using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using Wails.Net.Generator;
using Wails.Net.Generator.Models;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// TypeScript 代码生成器单元测试。
/// 验证 .d.ts 定义文件、调用封装文件、ID 映射文件的生成逻辑。
/// </summary>
[NotInParallel]
public sealed class TypeScriptGeneratorTests
{
    [Test]
    public async Task GenerateDefinitions_EmptyList_ProducesEmptyNotice()
    {
        var generator = new TypeScriptGenerator();
        var content = generator.GenerateDefinitions(new List<BoundMethodModel>());

        await Assert.That(content).Contains("未发现绑定方法");
    }

    [Test]
    public async Task GenerateDefinitions_WithMethods_ContainsClassInterface()
    {
        var generator = new TypeScriptGenerator();
        var methods = CreateSampleMethods();

        var content = generator.GenerateDefinitions(methods);

        await Assert.That(content).Contains("interface GreetingService");
        await Assert.That(content).Contains("Greet(name: string): string");
    }

    [Test]
    public async Task GenerateDefinitions_WithNamespace_WrapsInNamespaceDeclaration()
    {
        var generator = new TypeScriptGenerator();
        var methods = CreateSampleMethods();

        var content = generator.GenerateDefinitions(methods);

        await Assert.That(content).Contains("declare namespace Wails.Net.Services {");
    }

    [Test]
    public async Task GenerateDefinitions_AsyncMethod_WrapsReturnTypeInPromise()
    {
        var generator = new TypeScriptGenerator();
        var methods = new List<BoundMethodModel>
        {
            new(
                fullName: "Wails.Net.Services.AsyncService.FetchAsync",
                id: 42u,
                @namespace: "Wails.Net.Services",
                className: "AsyncService",
                methodName: "FetchAsync",
                parameters: new List<ParameterModel>(),
                returnTypeName: "string",
                isAsync: true),
        };

        var content = generator.GenerateDefinitions(methods);

        await Assert.That(content).Contains("FetchAsync(): Promise<string>");
    }

    [Test]
    public async Task GenerateDefinitions_FiltersCancellationTokenParameters()
    {
        var generator = new TypeScriptGenerator();
        var methods = new List<BoundMethodModel>
        {
            new(
                fullName: "Wails.Net.Services.CancellableService.Run",
                id: 1u,
                @namespace: "Wails.Net.Services",
                className: "CancellableService",
                methodName: "Run",
                parameters: new List<ParameterModel>
                {
                    new("input", "string", false, false),
                    new("token", "void", false, true),
                },
                returnTypeName: "void",
                isAsync: false),
        };

        var content = generator.GenerateDefinitions(methods);

        await Assert.That(content).Contains("Run(input: string): void;");
        await Assert.That(content).DoesNotContain("token");
    }

    [Test]
    public async Task GenerateDefinitions_VariadicParameter_PrefixedWithEllipsis()
    {
        var generator = new TypeScriptGenerator();
        var methods = new List<BoundMethodModel>
        {
            new(
                fullName: "Wails.Net.Services.MathService.Sum",
                id: 1u,
                @namespace: "Wails.Net.Services",
                className: "MathService",
                methodName: "Sum",
                parameters: new List<ParameterModel>
                {
                    new("values", "number[]", true, false),
                },
                returnTypeName: "number",
                isAsync: false),
        };

        var content = generator.GenerateDefinitions(methods);

        await Assert.That(content).Contains("Sum(...values: number[]): number");
    }

    [Test]
    public async Task GenerateCaller_EmptyList_ProducesEmptyNotice()
    {
        var generator = new TypeScriptGenerator();
        var content = generator.GenerateCaller(new List<BoundMethodModel>());

        await Assert.That(content).Contains("未发现绑定方法");
    }

    [Test]
    public async Task GenerateCaller_IncludesRuntimeImport()
    {
        var generator = new TypeScriptGenerator();
        var content = generator.GenerateCaller(CreateSampleMethods());

        await Assert.That(content).Contains("import { wails } from '@wails/runtime';");
    }

    [Test]
    public async Task GenerateCaller_GeneratesExportClassWithStaticMethod()
    {
        var generator = new TypeScriptGenerator();
        var methods = CreateSampleMethods();

        var content = generator.GenerateCaller(methods);

        await Assert.That(content).Contains("export class GreetingService");
        await Assert.That(content).Contains("static Greet(name: string): string");
    }

    [Test]
    public async Task GenerateCaller_AsyncMethod_MarksMethodAsyncAndAwaitsCall()
    {
        var generator = new TypeScriptGenerator();
        var methods = new List<BoundMethodModel>
        {
            new(
                fullName: "Wails.Net.Services.AsyncService.FetchAsync",
                id: 42u,
                @namespace: "Wails.Net.Services",
                className: "AsyncService",
                methodName: "FetchAsync",
                parameters: new List<ParameterModel>(),
                returnTypeName: "string",
                isAsync: true),
        };

        var content = generator.GenerateCaller(methods);

        await Assert.That(content).Contains("static async FetchAsync(): Promise<string>");
        await Assert.That(content).Contains("await wails.bindings.call(42, [])");
    }

    [Test]
    public async Task GenerateCaller_NonAsyncMethod_DoesNotUseAsyncKeyword()
    {
        var generator = new TypeScriptGenerator();
        var methods = CreateSampleMethods();

        var content = generator.GenerateCaller(methods);

        await Assert.That(content).Contains("static Greet(name: string): string");
        await Assert.That(content).DoesNotContain("async Greet");
    }

    [Test]
    public async Task GenerateCaller_UsesBindingIdInCall()
    {
        var generator = new TypeScriptGenerator();
        var methods = CreateSampleMethods();
        var expectedId = methods[0].ID;

        var content = generator.GenerateCaller(methods);

        await Assert.That(content).Contains($"wails.bindings.call({expectedId},");
    }

    [Test]
    public async Task GenerateIdMap_EmptyList_StillContainsExportStatement()
    {
        var generator = new TypeScriptGenerator();
        var content = generator.GenerateIdMap(new List<BoundMethodModel>());

        await Assert.That(content).Contains("export const bindingIds = {");
        await Assert.That(content).Contains("export default bindingIds;");
    }

    [Test]
    public async Task GenerateIdMap_WithMethods_ContainsAllFullNamesAndIds()
    {
        var generator = new TypeScriptGenerator();
        var methods = CreateSampleMethods();

        var content = generator.GenerateIdMap(methods);

        foreach (var m in methods)
        {
            await Assert.That(content).Contains($"\"{m.FullName}\": {m.ID},");
        }
    }

    private static List<BoundMethodModel> CreateSampleMethods()
    {
        return
        [
            new BoundMethodModel(
                fullName: "Wails.Net.Services.GreetingService.Greet",
                id: 12345u,
                @namespace: "Wails.Net.Services",
                className: "GreetingService",
                methodName: "Greet",
                parameters: new List<ParameterModel>
                {
                    new("name", "string", false, false),
                },
                returnTypeName: "string",
                isAsync: false),
        ];
    }
}
