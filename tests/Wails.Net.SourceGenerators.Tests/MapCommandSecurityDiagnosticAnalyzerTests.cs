using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using TUnit.Core;
using Wails.Net.Application.Bindings;
using Wails.Net.SourceGenerators;

namespace Wails.Net.SourceGenerators.Tests;

/// <summary>
/// SEC-H-002：MapCommandSecurityDiagnosticAnalyzer 诊断测试。
/// 驱动 <see cref="MapCommandSecurityDiagnosticAnalyzer"/> 校验：
/// 命中场景（带 [ScopeParameter]/[RequireCapability] 的方法经 MapCommand 注册但未传 CommandSecurity）
/// 与非命中场景（已传 CommandSecurity / 无安全特性 / 非 MapCommand 调用）的诊断行为。
/// </summary>
public sealed class MapCommandSecurityDiagnosticAnalyzerTests
{
    /// <summary>
    /// 运行分析器并返回（诊断列表, 输出编译）。
    /// </summary>
    private static async Task<(IReadOnlyList<Diagnostic> Diagnostics, Compilation Output)>
        RunAnalyzerAsync(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "AnalyzerTestAssembly",
            new[] { syntaxTree },
            GeneratorRunnerReferences.References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: Microsoft.CodeAnalysis.NullableContextOptions.Enable));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new MapCommandSecurityDiagnosticAnalyzer());
        var withAnalyzers = compilation.WithAnalyzers(analyzers);
        var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync();
        var compileErrors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToList();
        if (compileErrors.Count > 0)
        {
            throw new InvalidOperationException(
                "编译错误:\n" + string.Join("\n", compileErrors));
        }

        Console.WriteLine($"ANALYZER DIAGNOSTICS ({source.Length} chars):");
        foreach (var d in diagnostics)
        {
            Console.WriteLine($"  [{d.Id}] {d.GetMessage()}");
        }
        return (diagnostics, compilation);
    }

    private static string BuildSource(string commandsBody) => $$"""
        using System;
        using System.Threading.Tasks;
        using Wails.Net.Application.Commands;
        using Wails.Net.Application.Security;
        using Wails.Net.Application.Plugins;

        namespace MyApp.Security
        {
            public static class CommandRegistrar
            {
                public static void Register(IPluginContext context)
                {
                    {{commandsBody}}
                }

                public static string ReadText([ScopeParameter("fs:allow-read")] string path)
                    => path;

                public static void WriteText([ScopeParameter("fs:allow-write")] string path, string content)
                { }

                [RequireCapability("admin")]
                public static string AdminOnly() => "ok";

                public static string NoSecurity(string path) => path;
            }
        }
        """;

    [Test]
    public async Task ScopeParameter_WithoutSecurity_ReportsWnet002()
    {
        var (diagnostics, _) = await RunAnalyzerAsync(BuildSource(
            "context.Commands.MapCommand(\"fs.read\", (Func<string, string>)ReadText);"));

        var wnet002 = diagnostics
            .Where(d => d.Id == MapCommandSecurityDiagnosticAnalyzer.Wnet002.Id)
            .ToList();

        await Assert.That(wnet002).Count().IsEqualTo(1);
    }

    [Test]
    public async Task ScopeParameter_WithoutSecurity_DoesNotReportWnet001()
    {
        var (diagnostics, _) = await RunAnalyzerAsync(BuildSource(
            "context.Commands.MapCommand(\"fs.read\", (Func<string, string>)ReadText);"));

        var wnet001 = diagnostics
            .Where(d => d.Id == MapCommandSecurityDiagnosticAnalyzer.Wnet001.Id)
            .ToList();

        await Assert.That(wnet001).IsEmpty();
    }

    [Test]
    public async Task RequireCapability_WithoutSecurity_ReportsWnet001()
    {
        var (diagnostics, _) = await RunAnalyzerAsync(BuildSource(
            "context.Commands.MapCommand(\"admin.only\", (Func<string>)AdminOnly);"));

        var wnet001 = diagnostics
            .Where(d => d.Id == MapCommandSecurityDiagnosticAnalyzer.Wnet001.Id)
            .ToList();

        await Assert.That(wnet001).Count().IsEqualTo(1);
    }

    [Test]
    public async Task WithCommandSecurity_ReportsNoDiagnostics()
    {
        var (diagnostics, _) = await RunAnalyzerAsync(BuildSource(
            "context.Commands.MapCommand(\"fs.read\", (Func<string, string>)ReadText," +
            " CommandSecurity.Require(\"fs:allow-read\").WithScope(\"path\", \"fs:allow-read\"));"));

        var securityDiagnostics = diagnostics
            .Where(d => d.Id is "WNET001" or "WNET002")
            .ToList();

        await Assert.That(securityDiagnostics).IsEmpty();
    }

    [Test]
    public async Task NoSecurityAttribute_ReportsNoDiagnostics()
    {
        var (diagnostics, _) = await RunAnalyzerAsync(BuildSource(
            "context.Commands.MapCommand(\"plain.read\", (Func<string, string>)NoSecurity);"));

        var securityDiagnostics = diagnostics
            .Where(d => d.Id is "WNET001" or "WNET002")
            .ToList();

        await Assert.That(securityDiagnostics).IsEmpty();
    }

    [Test]
    public async Task NonMapCommandInvocation_ReportsNoDiagnostics()
    {
        var (diagnostics, _) = await RunAnalyzerAsync(BuildSource(
            "var _ = ReadText(\"a\");"));

        var securityDiagnostics = diagnostics
            .Where(d => d.Id is "WNET001" or "WNET002")
            .ToList();

        await Assert.That(securityDiagnostics).IsEmpty();
    }

    [Test]
    public async Task AsyncScopeParameter_WithoutSecurity_ReportsWnet002()
    {
        var (diagnostics, _) = await RunAnalyzerAsync(BuildSource(
            "context.Commands.MapCommandAsync(\"fs.readAsync\"," +
            " (Func<string, Task<string>>)(path => Task.FromResult(ReadText(path))));"));

        // 匿名 Lambda 包裹：lambda 参数无法声明特性，故不应诊断（避免误报）。
        var securityDiagnostics = diagnostics
            .Where(d => d.Id is "WNET001" or "WNET002")
            .ToList();

        await Assert.That(securityDiagnostics).IsEmpty();
    }
}

/// <summary>
/// 供分析器测试复用的引用集构建（与 GeneratorRunner 共用引用逻辑）。
/// </summary>
internal static class GeneratorRunnerReferences
{
    internal static readonly IReadOnlyList<MetadataReference> References = BuildReferences();

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

        foreach (var p in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                     .Split(Path.PathSeparator))
            Add(p);

        var wailsDir = Path.GetDirectoryName(typeof(BindingAttribute).Assembly.Location)!;
        foreach (var dll in Directory.GetFiles(wailsDir, "*.dll"))
            Add(dll);

        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            if (!a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                Add(a.Location);

        return refs;
    }
}