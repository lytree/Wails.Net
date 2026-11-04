using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TUnit.Core;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Plugins;
using Wails.Net.SourceGenerators;

namespace Wails.Net.SourceGenerators.Tests;

public static class ManifestGeneratorRunner
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

    private static readonly IReadOnlyList<MetadataReference> References = BuildReferences();

    public static (GeneratorDriverRunResult RunResult, Compilation OutputCompilation, string GeneratedText, string[] GeneratedFileNames)
        Run(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "ManifestGenTestAssembly",
            new[] { syntaxTree },
            References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: Microsoft.CodeAnalysis.NullableContextOptions.Enable));

        var generator = new PluginPermissionManifestGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
        var runResult = driver.GetRunResult();
        var names = runResult.Results
            .SelectMany(r => r.GeneratedSources)
            .Select(s => s.HintName)
            .ToArray();
        var generated = string.Join(
            "\n",
            runResult.Results
                .SelectMany(r => r.GeneratedSources)
                .Select(s => s.SourceText.ToString()));
        return (runResult, outputCompilation, generated, names);
    }
}

public class PluginPermissionManifestGeneratorTests
{
    private const string SinglePluginSource = "using Wails.Net.Application.Plugins;\nusing Microsoft.Extensions.DependencyInjection;\nnamespace TestApp.Plugins { public class FooPlugin : IPlugin { public string Name => \"foo\"; public void ConfigureServices(IServiceCollection s) { } public void Configure(IPluginContext c) { c.Permissions.RegisterPermissionSet(\"foo:default\", \"Foo 默认权限集\", \"foo:allow-read\", \"foo:allow-write\"); c.Permissions.DeclarePermission(\"foo:allow-read\", \"允许读取\"); c.Permissions.DeclarePermission(\"foo:allow-write\", \"允许写入\"); } } }";

    private const string MobileUmbrellaSource = "using Wails.Net.Application.Plugins;\nusing Microsoft.Extensions.DependencyInjection;\nnamespace TestApp.Plugins.Mobile { public class CameraPlugin : IPlugin { public string Name => \"camera\"; public void ConfigureServices(IServiceCollection s) { } public void Configure(IPluginContext c) { c.Permissions.RegisterPermissionSet(\"mobile:default\", \"mobile 默认权限集\", \"mobile:allow-default\"); c.Permissions.RegisterPermissionSet(\"camera:default\", \"相机默认权限集\", \"camera:allow-capture\"); c.Permissions.DeclarePermission(\"camera:allow-capture\", \"允许拍照\"); } } }";

    [Test]
    public async Task SinglePlugin_GeneratesManifestWithAllPermissions()
    {
        var (_, _, generated, names) = ManifestGeneratorRunner.Run(SinglePluginSource);
        await Assert.That(generated).IsNotNull();
        await Assert.That(names).Contains("WailsGeneratedPermissionManifest.g.cs");
        await Assert.That(generated).Contains("public static class Foo");
        await Assert.That(generated).Contains("PluginName = \"foo\"");
        await Assert.That(generated).Contains("\"foo:default\"");
        await Assert.That(generated).Contains("\"foo:allow-read\"");
        await Assert.That(generated).Contains("\"foo:allow-write\"");
        await Assert.That(generated).Contains("[ModuleInitializer]");
        await Assert.That(generated).Contains("PluginPermissionManifestRegistry.Register");
    }

    [Test]
    public async Task GeneratedCode_CompilesWithoutErrors()
    {
        var (_, outputCompilation, _, _) = ManifestGeneratorRunner.Run(SinglePluginSource);
        var errors = outputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.GetMessage())
            .ToArray();
        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task NonPluginClass_NoManifestGenerated()
    {
        const string source = "namespace TestApp { public class Helper { public string Name => \"helper\"; public void DoSomething() { } } }";
        var (_, _, generated, _) = ManifestGeneratorRunner.Run(source);
        await Assert.That(generated).IsEmpty();
    }

    [Test]
    public async Task MultiplePlugins_GenerateMultipleInnerClasses()
    {
        const string source = "using Wails.Net.Application.Plugins;\nusing Microsoft.Extensions.DependencyInjection;\nnamespace TestApp.Plugins { public class FooPlugin : IPlugin { public string Name => \"foo\"; public void ConfigureServices(IServiceCollection s) { } public void Configure(IPluginContext c) { c.Permissions.DeclarePermission(\"foo:allow-x\", \"X\"); } } public class BarPlugin : IPlugin { public string Name => \"bar\"; public void ConfigureServices(IServiceCollection s) { } public void Configure(IPluginContext c) { c.Permissions.DeclarePermission(\"bar:allow-y\", \"Y\"); } } }";
        var (_, _, generated, _) = ManifestGeneratorRunner.Run(source);
        await Assert.That(generated).Contains("public static class Foo");
        await Assert.That(generated).Contains("public static class Bar");
        await Assert.That(generated).Contains("\"foo:allow-x\"");
        await Assert.That(generated).Contains("\"bar:allow-y\"");
    }

    [Test]
    public async Task PluginWithMobileUmbrella_ManifestIncludesBothSets()
    {
        var (_, _, generated, _) = ManifestGeneratorRunner.Run(MobileUmbrellaSource);
        await Assert.That(generated).Contains("\"camera:default\"");
        await Assert.That(generated).Contains("\"camera:allow-capture\"");
        await Assert.That(generated).Contains("\"mobile:default\"");
        await Assert.That(generated).Contains("\"mobile:allow-default\"");
    }

    [Test]
    public async Task NameFromBlockProperty_StillExtracted()
    {
        const string source = "using Wails.Net.Application.Plugins;\nusing Microsoft.Extensions.DependencyInjection;\nnamespace TestApp { public class FooPlugin : IPlugin { public string Name { get { return \"foo\"; } } public void ConfigureServices(IServiceCollection s) { } public void Configure(IPluginContext c) { c.Permissions.DeclarePermission(\"foo:allow-x\", \"X\"); } } }";
        var (_, _, generated, _) = ManifestGeneratorRunner.Run(source);
        await Assert.That(generated).Contains("PluginName = \"foo\"");
    }
}