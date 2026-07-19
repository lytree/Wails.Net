using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using Wails.Net.Application.Bindings;
using Wails.Net.Generator;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// 绑定代码生成管道单元测试。
/// 验证统一生成入口、文件写入、选项开关和失败处理。
/// </summary>
/// <remarks>
/// 此测试使用源生成器在测试程序集编译期填充的元数据
/// （<see cref="GeneratedBindingsMetadata"/> 和 <see cref="Wails.Net.Events.GeneratedEventsMetadata"/>），
/// 不再依赖运行时程序集加载和反射分析。
/// </remarks>
[NotInParallel]
public sealed class BindingGenerationPipelineTests
{
    [Test]
    public async Task Generate_ReturnsSuccessWithDefinitions()
    {
        var pipeline = new BindingGenerationPipeline();
        var options = new BindingGenerationOptions
        {
            GenerateDefinitions = true,
            GenerateCaller = false,
            GenerateIdMap = false,
            GenerateEvents = false,
            GenerateKnownEvents = false,
        };

        var result = pipeline.Generate(options);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.GeneratedFiles).ContainsKey(options.DefinitionsFileName);
    }

    [Test]
    public async Task Generate_RespectsCallerOption()
    {
        var pipeline = new BindingGenerationPipeline();
        var options = new BindingGenerationOptions
        {
            GenerateDefinitions = false,
            GenerateCaller = true,
            GenerateIdMap = false,
            GenerateEvents = false,
            GenerateKnownEvents = false,
        };

        var result = pipeline.Generate(options);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.GeneratedFiles).ContainsKey(options.CallerFileName);
        await Assert.That(result.GeneratedFiles).DoesNotContainKey(options.DefinitionsFileName);
    }

    [Test]
    public async Task Generate_RespectsIdMapOption()
    {
        var pipeline = new BindingGenerationPipeline();
        var options = new BindingGenerationOptions
        {
            GenerateDefinitions = false,
            GenerateCaller = false,
            GenerateIdMap = true,
            GenerateEvents = false,
            GenerateKnownEvents = false,
        };

        var result = pipeline.Generate(options);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.GeneratedFiles).ContainsKey(options.IdMapFileName);
    }

    [Test]
    public async Task Generate_RespectsEventsOption()
    {
        var pipeline = new BindingGenerationPipeline();
        var options = new BindingGenerationOptions
        {
            GenerateDefinitions = false,
            GenerateCaller = false,
            GenerateIdMap = false,
            GenerateEvents = true,
            GenerateKnownEvents = false,
        };

        var result = pipeline.Generate(options);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.GeneratedFiles).ContainsKey(options.EventsFileName);
    }

    [Test]
    public async Task Generate_ReportsMethodAndClassCounts()
    {
        var pipeline = new BindingGenerationPipeline();
        var options = new BindingGenerationOptions
        {
            GenerateDefinitions = true,
            GenerateCaller = false,
            GenerateIdMap = false,
            GenerateEvents = false,
            GenerateKnownEvents = false,
        };

        var result = pipeline.Generate(options);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.MethodCount).IsGreaterThan(0);
        await Assert.That(result.ClassCount).IsGreaterThan(0);
    }

    [Test]
    public async Task GenerateToDisk_WritesFilesToOutputDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "wails-net-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var pipeline = new BindingGenerationPipeline();
            var options = new BindingGenerationOptions
            {
                OutputDirectory = tempDir,
                GenerateDefinitions = true,
                GenerateCaller = true,
                GenerateIdMap = true,
                GenerateEvents = false,
                GenerateKnownEvents = false,
            };

            var result = pipeline.GenerateToDisk(options);

            await Assert.That(result.Success).IsTrue();
            await Assert.That(Directory.Exists(tempDir)).IsTrue();

            var definitionsPath = Path.Combine(tempDir, options.DefinitionsFileName);
            var callerPath = Path.Combine(tempDir, options.CallerFileName);
            var idMapPath = Path.Combine(tempDir, options.IdMapFileName);

            await Assert.That(File.Exists(definitionsPath)).IsTrue();
            await Assert.That(File.Exists(callerPath)).IsTrue();
            await Assert.That(File.Exists(idMapPath)).IsTrue();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Test]
    public async Task GenerateToDisk_CreatesOutputDirectoryIfMissing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "wails-net-tests-nested-" + Guid.NewGuid().ToString("N"), "out");
        try
        {
            var pipeline = new BindingGenerationPipeline();
            var options = new BindingGenerationOptions
            {
                OutputDirectory = tempDir,
                GenerateDefinitions = true,
                GenerateCaller = false,
                GenerateIdMap = false,
                GenerateEvents = false,
                GenerateKnownEvents = false,
            };

            var result = pipeline.GenerateToDisk(options);

            await Assert.That(result.Success).IsTrue();
            await Assert.That(Directory.Exists(tempDir)).IsTrue();
        }
        finally
        {
            var parent = Directory.GetParent(tempDir)?.FullName;
            if (parent is not null && Directory.Exists(parent))
            {
                Directory.Delete(parent, recursive: true);
            }
        }
    }

    [Test]
    public async Task GenerateFromInstances_ReturnsMethodsForGivenInstances()
    {
        var pipeline = new BindingGenerationPipeline();
        var options = new BindingGenerationOptions
        {
            GenerateDefinitions = true,
            GenerateCaller = false,
            GenerateIdMap = false,
            GenerateEvents = false,
            GenerateKnownEvents = false,
        };

        var instances = new object[] { new PipelineSampleService(), new PipelineAnotherService() };
        var result = pipeline.GenerateFromInstances(instances, options);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.MethodCount).IsGreaterThanOrEqualTo(2);
        await Assert.That(result.ClassCount).IsEqualTo(2);
    }

    [Test]
    public async Task GenerateFromInstances_ProducesDefinitionsContainingBothClasses()
    {
        var pipeline = new BindingGenerationPipeline();
        var options = new BindingGenerationOptions
        {
            GenerateDefinitions = true,
            GenerateCaller = false,
            GenerateIdMap = false,
            GenerateEvents = false,
            GenerateKnownEvents = false,
        };

        var instances = new object[] { new PipelineSampleService(), new PipelineAnotherService() };
        var result = pipeline.GenerateFromInstances(instances, options);

        await Assert.That(result.Success).IsTrue();
        var content = result.GeneratedFiles[options.DefinitionsFileName];
        await Assert.That(content).Contains("PipelineSampleService");
        await Assert.That(content).Contains("PipelineAnotherService");
    }

    [Test]
    public async Task Generate_FailureResult_PreservesErrorMessage()
    {
        // 通过 GenerateFromInstances 传入 null 实例来触发 NullReferenceException
        var pipeline = new BindingGenerationPipeline();
        var options = new BindingGenerationOptions
        {
            GenerateDefinitions = true,
            GenerateEvents = false,
            GenerateKnownEvents = false,
        };

        var result = pipeline.GenerateFromInstances(new object[] { null! }, options);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.ErrorMessage).IsNotNull();
    }
}

public sealed class PipelineSampleService
{
    [Binding]
    public string GetName() => "Wails";
}

public sealed class PipelineAnotherService
{
    [Binding]
    public int GetValue() => 42;
}
