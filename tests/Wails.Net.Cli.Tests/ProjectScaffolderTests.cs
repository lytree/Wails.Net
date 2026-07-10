using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using Wails.Net.Cli.Scaffolding;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// 项目脚手架器单元测试。
/// 验证模板验证、目录创建、文件生成逻辑。
/// </summary>
[NotInParallel]
public sealed class ProjectScaffolderTests
{
    [Test]
    public async Task GetSupportedTemplates_ReturnsAllExpectedTemplates()
    {
        var templates = ProjectScaffolder.GetSupportedTemplates();

        await Assert.That(templates).Contains("vanilla-ts");
        await Assert.That(templates).Contains("vue-ts");
        await Assert.That(templates).Contains("react-ts");
        await Assert.That(templates).Contains("svelte-ts");
    }

    [Test]
    [Arguments("vanilla-ts", true)]
    [Arguments("vue-ts", true)]
    [Arguments("react-ts", true)]
    [Arguments("svelte-ts", true)]
    [Arguments("VUE-TS", true)] // 大小写不敏感
    [Arguments("invalid", false)]
    [Arguments("", false)]
    [Arguments("angular", false)]
    public async Task IsValidTemplateName_VariousNames_ReturnsExpected(string name, bool expected)
    {
        var result = ProjectScaffolder.IsValidTemplateName(name);
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task ScaffoldAsync_CreatesProjectDirectory()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var scaffolder = new ProjectScaffolder();
            var result = await scaffolder.ScaffoldAsync("MyApp", "vanilla-ts", tempRoot);

            await Assert.That(result.Success).IsTrue();
            var projectDir = Path.Combine(tempRoot.FullName, "MyApp");
            await Assert.That(Directory.Exists(projectDir)).IsTrue();
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Test]
    public async Task ScaffoldAsync_CreatesExpectedFiles()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var scaffolder = new ProjectScaffolder();
            var result = await scaffolder.ScaffoldAsync("MyApp", "vanilla-ts", tempRoot);

            await Assert.That(result.Success).IsTrue();
            // CreatedFiles 返回相对于父目录的路径，直接检查文件是否实际存在
            var projectDir = Path.Combine(tempRoot.FullName, "MyApp");
            await Assert.That(File.Exists(Path.Combine(projectDir, "MyApp.slnx"))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(projectDir, "src", "MyApp", "Program.cs"))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(projectDir, "src", "MyApp", "MyApp.csproj"))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(projectDir, "src", "MyApp", "Bindings.cs"))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(projectDir, "wails.json"))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(projectDir, "frontend", "package.json"))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(projectDir, "frontend", "index.html"))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(projectDir, "frontend", "src", "main.ts"))).IsTrue();
            // 同时验证 CreatedFiles 列表非空
            await Assert.That(result.CreatedFiles.Count).IsGreaterThan(0);
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Test]
    public async Task ScaffoldAsync_ProgramCsContainsProjectName()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var scaffolder = new ProjectScaffolder();
            var result = await scaffolder.ScaffoldAsync("CustomApp", "vanilla-ts", tempRoot);

            await Assert.That(result.Success).IsTrue();
            var programPath = Path.Combine(tempRoot.FullName, "CustomApp", "src", "CustomApp", "Program.cs");
            var content = await File.ReadAllTextAsync(programPath);

            await Assert.That(content).Contains("namespace CustomApp");
            await Assert.That(content).Contains("\"CustomApp\"");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Test]
    public async Task ScaffoldAsync_CsprojUsesWindowsTarget()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var scaffolder = new ProjectScaffolder();
            var result = await scaffolder.ScaffoldAsync("MyApp", "vanilla-ts", tempRoot);

            await Assert.That(result.Success).IsTrue();
            var csprojPath = Path.Combine(tempRoot.FullName, "MyApp", "src", "MyApp", "MyApp.csproj");
            var content = await File.ReadAllTextAsync(csprojPath);

            await Assert.That(content).Contains("net10.0-windows");
            await Assert.That(content).Contains("OutputType>WinExe");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Test]
    public async Task ScaffoldAsync_WailsJsonContainsTemplate()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var scaffolder = new ProjectScaffolder();
            var result = await scaffolder.ScaffoldAsync("MyApp", "vue-ts", tempRoot);

            await Assert.That(result.Success).IsTrue();
            var jsonPath = Path.Combine(tempRoot.FullName, "MyApp", "wails.json");
            var content = await File.ReadAllTextAsync(jsonPath);

            await Assert.That(content).Contains("\"template\": \"vue-ts\"");
            await Assert.That(content).Contains("\"frontend\":");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Test]
    public async Task ScaffoldAsync_VueTemplate_AddsVueDependency()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var scaffolder = new ProjectScaffolder();
            var result = await scaffolder.ScaffoldAsync("MyApp", "vue-ts", tempRoot);

            await Assert.That(result.Success).IsTrue();
            var pkgPath = Path.Combine(tempRoot.FullName, "MyApp", "frontend", "package.json");
            var content = await File.ReadAllTextAsync(pkgPath);

            await Assert.That(content).Contains("\"vue\":");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Test]
    public async Task ScaffoldAsync_ReactTemplate_AddsReactDependencies()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var scaffolder = new ProjectScaffolder();
            var result = await scaffolder.ScaffoldAsync("MyApp", "react-ts", tempRoot);

            await Assert.That(result.Success).IsTrue();
            var pkgPath = Path.Combine(tempRoot.FullName, "MyApp", "frontend", "package.json");
            var content = await File.ReadAllTextAsync(pkgPath);

            await Assert.That(content).Contains("\"react\":");
            await Assert.That(content).Contains("\"react-dom\":");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Test]
    public async Task ScaffoldAsync_SvelteTemplate_AddsSvelteDependency()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var scaffolder = new ProjectScaffolder();
            var result = await scaffolder.ScaffoldAsync("MyApp", "svelte-ts", tempRoot);

            await Assert.That(result.Success).IsTrue();
            var pkgPath = Path.Combine(tempRoot.FullName, "MyApp", "frontend", "package.json");
            var content = await File.ReadAllTextAsync(pkgPath);

            await Assert.That(content).Contains("\"svelte\":");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Test]
    public async Task ScaffoldAsync_VanillaTemplate_DoesNotAddFrameworkDependency()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var scaffolder = new ProjectScaffolder();
            var result = await scaffolder.ScaffoldAsync("MyApp", "vanilla-ts", tempRoot);

            await Assert.That(result.Success).IsTrue();
            var pkgPath = Path.Combine(tempRoot.FullName, "MyApp", "frontend", "package.json");
            var content = await File.ReadAllTextAsync(pkgPath);

            await Assert.That(content).DoesNotContain("\"vue\":");
            await Assert.That(content).DoesNotContain("\"react\":");
            await Assert.That(content).DoesNotContain("\"svelte\":");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Test]
    public async Task ScaffoldAsync_BindingsCsContainsGreetingService()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var scaffolder = new ProjectScaffolder();
            var result = await scaffolder.ScaffoldAsync("MyApp", "vanilla-ts", tempRoot);

            await Assert.That(result.Success).IsTrue();
            var bindingsPath = Path.Combine(tempRoot.FullName, "MyApp", "src", "MyApp", "Bindings.cs");
            var content = await File.ReadAllTextAsync(bindingsPath);

            await Assert.That(content).Contains("GreetingService");
            await Assert.That(content).Contains("namespace MyApp");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Test]
    public async Task ScaffoldAsync_GeneratesSolutionFile()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var scaffolder = new ProjectScaffolder();
            var result = await scaffolder.ScaffoldAsync("MyApp", "vanilla-ts", tempRoot);

            await Assert.That(result.Success).IsTrue();
            var slnxPath = Path.Combine(tempRoot.FullName, "MyApp", "MyApp.slnx");
            var content = await File.ReadAllTextAsync(slnxPath);

            await Assert.That(content).Contains("<Solution>");
            await Assert.That(content).Contains("MyApp.csproj");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    private static DirectoryInfo CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "wails-scaffold-tests-" + Guid.NewGuid().ToString("N"));
        return Directory.CreateDirectory(path);
    }

    private static void DeleteDirectory(DirectoryInfo dir)
    {
        try
        {
            if (dir.Exists)
            {
                dir.Delete(recursive: true);
            }
        }
        catch
        {
            // 忽略清理错误
        }
    }
}
