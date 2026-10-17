using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TUnit.Core;
using Wails.Net.Application.Bindings;
using Wails.Net.SourceGenerators;

namespace Wails.Net.SourceGenerators.Tests;

/// <summary>
/// 阶段 B1：源代码生成器（BindingSourceGenerator）快照 / 编译性 / 增量性测试。
/// 手动驱动 CSharpGeneratorDriver（不用 Microsoft.CodeAnalysis.*.Testing 的 Verifier 包，其绑死 xUnit/MSTest）。
/// </summary>
public static class TestServices
{
    public const string BindingService = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Wails.Net.Application.Bindings;

        namespace MyApp.Services
        {
            public class GreetingService
            {
                [Binding]
                public string Greet(string name) => $"Hello, {name}!";

                [Binding(Name = "getCount")]
                public int GetCount() => 42;

                [Binding]
                public Task<string> GreetAsync(string name, CancellationToken ct)
                    => Task.FromResult($"Hi {name}");
            }
        }
        """;

    public const string CommandService = """
        using System;
        using System.Threading.Tasks;
        using Wails.Net.Application.Commands;

        namespace MyApp.Commands
        {
            public class CounterCommands
            {
                [Command("counter.increment")]
                public Task<int> IncrementAsync() => Task.FromResult(1);
            }
        }
        """;

    /// <summary>
    /// 两个不同命名空间下的同名 [Binding] 类：短名别名（ClassName.MethodName）冲突，
    /// 用于验证 WNET004 诊断与冲突别名的 fail-closed 跳过。
    /// </summary>
    public const string DuplicateShortNameServices = """
        using System;
        using Wails.Net.Application.Bindings;

        namespace MyApp.Alpha
        {
            public class SyncService
            {
                [Binding]
                public string Ping() => "alpha";
            }
        }

        namespace MyApp.Beta
        {
            public class SyncService
            {
                [Binding]
                public string Ping() => "beta";
            }
        }
        """;
}

public static class GeneratorRunner
{
    private static IReadOnlyList<MetadataReference> BuildReferences()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var refs = new List<MetadataReference>();

        void Add(string p)
        {
            if (string.IsNullOrEmpty(p) || !File.Exists(p))
                return;
            var full = Path.GetFullPath(p);
            if (!paths.Add(full))
                return;
            refs.Add(MetadataReference.CreateFromFile(full));
        }

        // 1) 运行时引用集（替代 Basic.Reference.Assemblies.*，离线可用）
        foreach (var p in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                     .Split(Path.PathSeparator))
            Add(p);

        // 2) Wails.Net.Application 及其全部本地依赖（生成代码引用的真实类型所在）
        var wailsDir = Path.GetDirectoryName(typeof(BindingAttribute).Assembly.Location)!;
        foreach (var dll in Directory.GetFiles(wailsDir, "*.dll"))
            Add(dll);

        // 3) 兜底：当前已加载程序集（覆盖 transitive 依赖）
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            if (!a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                Add(a.Location);

        return refs;
    }

    private static readonly IReadOnlyList<MetadataReference> References = BuildReferences();

    public static (GeneratorDriverRunResult RunResult, Compilation OutputCompilation, string GeneratedText)
        Run(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "GeneratorTestAssembly",
            new[] { syntaxTree },
            References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: Microsoft.CodeAnalysis.NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new BindingSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
        var runResult = driver.GetRunResult();
        var generated = string.Join(
            "\n",
            runResult.Results
                .SelectMany(r => r.GeneratedSources)
                .Select(s => s.SourceText.ToString()));
        return (runResult, outputCompilation, generated);
    }
}

public class BindingGeneratorTests
{
    [Test]
    public async Task Binding_SimpleService_GeneratesInvoker()
    {
        var (_, _, generated) = GeneratorRunner.Run(TestServices.BindingService);
        await Assert.That(generated).IsNotNull();
        await Assert.That(generated).Contains("Greet");
        await Assert.That(generated).Contains("GeneratedBindingRegistry");
        await Assert.That(generated).Contains("ModuleInitializer");
    }

    [Test]
    public async Task Binding_GeneratedCode_CompilesWithoutErrors()
    {
        var (_, outputCompilation, _) = GeneratorRunner.Run(TestServices.BindingService);
        var errors = outputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();
        await Assert.That(errors).IsEmpty()
            .Because("生成代码未能通过编译：\n" + string.Join("\n", errors.Select(d => d.ToString())));
    }

    [Test]
    public async Task Binding_GeneratedCode_IsSyntacticallyValid()
    {
        var (_, _, generated) = GeneratorRunner.Run(TestServices.BindingService);
        var tree = CSharpSyntaxTree.ParseText(generated);
        var parseErrors = tree.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();
        await Assert.That(parseErrors).IsEmpty()
            .Because(string.Join("\n", parseErrors.Select(d => d.ToString())));
    }

    [Test]
    public async Task Binding_CommandAttribute_AlsoGenerates()
    {
        var (_, _, generated) = GeneratorRunner.Run(TestServices.CommandService);
        await Assert.That(generated).Contains("counter.increment");
        await Assert.That(generated).Contains("GeneratedBindingRegistry");
    }

    [Test]
    public async Task Binding_Generation_IsDeterministic()
    {
        var first = GeneratorRunner.Run(TestServices.BindingService).GeneratedText;
        var second = GeneratorRunner.Run(TestServices.BindingService).GeneratedText;
        await Assert.That(second).IsEqualTo(first);
    }

    /// <summary>
    /// 同名绑定类导致短名别名冲突时，生成器应报告 WNET004 警告。
    /// </summary>
    [Test]
    public async Task Binding_DuplicateShortName_ReportsWnet004()
    {
        var (runResult, _, _) = GeneratorRunner.Run(TestServices.DuplicateShortNameServices);

        var diagnostics = runResult.Diagnostics.Where(d => d.Id == "WNET004").ToArray();

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(diagnostics[0].GetMessage()).Contains("SyncService.Ping");
        await Assert.That(diagnostics[0].GetMessage()).Contains("MyApp.Alpha.SyncService");
        await Assert.That(diagnostics[0].GetMessage()).Contains("MyApp.Beta.SyncService");
    }

    /// <summary>
    /// 短名别名冲突时只跳过该别名，全限定名仍注册（fail-closed 且不影响正常调用路径）。
    /// </summary>
    [Test]
    public async Task Binding_DuplicateShortName_SkipsAliasButKeepsFullNames()
    {
        var (_, _, generated) = GeneratorRunner.Run(TestServices.DuplicateShortNameServices);

        // 全限定名仍注册
        await Assert.That(generated).Contains("MyApp.Alpha.SyncService.Ping");
        await Assert.That(generated).Contains("MyApp.Beta.SyncService.Ping");

        // 冲突短名别名不再生成任何注册调用
        await Assert.That(generated.Contains("\"SyncService.Ping\"", StringComparison.Ordinal)).IsFalse();
    }

    /// <summary>
    /// 短名唯一时仍照常注册别名，保证非冲突场景行为不变。
    /// </summary>
    [Test]
    public async Task Binding_UniqueShortName_StillRegistersAlias()
    {
        var (_, _, generated) = GeneratorRunner.Run(TestServices.BindingService);

        await Assert.That(generated).Contains("\"GreetingService.Greet\"");
    }

    /// <summary>
    /// 同名类的方法名不重复时，冲突仅限重复键，其余短名别名不受影响。
    /// </summary>
    [Test]
    public async Task Binding_DuplicateShortName_OtherKeysUnaffected()
    {
        var (_, _, generated) = GeneratorRunner.Run(TestServices.DuplicateShortNameServices);

        // 两个类的全限定名键均不同且都注册
        await Assert.That(generated.Contains("\"MyApp.Alpha.SyncService.Ping\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(generated.Contains("\"MyApp.Beta.SyncService.Ping\"", StringComparison.Ordinal)).IsTrue();
    }
}
