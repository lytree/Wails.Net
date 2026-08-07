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
    public async Task ScaffoldAsync_ProgramCsUsesCorrectApiCalls()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var scaffolder = new ProjectScaffolder();
            var result = await scaffolder.ScaffoldAsync("MyApp", "vanilla-ts", tempRoot);

            await Assert.That(result.Success).IsTrue();
            var programPath = Path.Combine(tempRoot.FullName, "MyApp", "src", "MyApp", "Program.cs");
            var content = await File.ReadAllTextAsync(programPath);

            // 验证使用正确的 API（而非不存在的 app.Window.NewWebviewWindow）
            await Assert.That(content).Contains("app.CreateWebviewWindow(");
            await Assert.That(content).DoesNotContain("app.Window.NewWebviewWindow");
            // 验证 URL 属性名（大写）
            await Assert.That(content).Contains("URL =");
            await Assert.That(content).DoesNotContain("Url =");
            // 验证注册服务
            await Assert.That(content).Contains("Services = { new GreetingService() }");
            // 验证 using 语句
            await Assert.That(content).Contains("using Wails.Net.Application;");
            await Assert.That(content).Contains("using Wails.Net.Application.Options;");
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
            // 验证不包含未定义的 UseWails 属性
            await Assert.That(content).DoesNotContain("UseWails");
            // 验证包含 Nullable 和 ImplicitUsings
            await Assert.That(content).Contains("Nullable>enable");
            await Assert.That(content).Contains("ImplicitUsings>enable");
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
    public async Task ScaffoldAsync_VueTemplate_AddsVueAndPluginDependencies()
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
            // 验证 Vite 插件
            await Assert.That(content).Contains("\"@vitejs/plugin-vue\":");
            // 验证 Wails 运行时
            await Assert.That(content).Contains("\"@wails-net/runtime\":");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Test]
    public async Task ScaffoldAsync_ReactTemplate_AddsReactAndPluginDependencies()
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
            // 验证 Vite 插件和类型定义
            await Assert.That(content).Contains("\"@vitejs/plugin-react\":");
            await Assert.That(content).Contains("\"@types/react\":");
            await Assert.That(content).Contains("\"@types/react-dom\":");
            // 验证 Wails 运行时
            await Assert.That(content).Contains("\"@wails-net/runtime\":");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Test]
    public async Task ScaffoldAsync_SvelteTemplate_AddsSvelteAndPluginDependencies()
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
            // 验证 Vite 插件
            await Assert.That(content).Contains("\"@sveltejs/vite-plugin-svelte\":");
            await Assert.That(content).Contains("\"svelte-check\":");
            // 验证 Wails 运行时
            await Assert.That(content).Contains("\"@wails-net/runtime\":");
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
            // vanilla 仍应包含 Wails 运行时
            await Assert.That(content).Contains("\"@wails-net/runtime\":");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Test]
    public async Task ScaffoldAsync_VueTemplate_GeneratesVueEntryPoint()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var scaffolder = new ProjectScaffolder();
            var result = await scaffolder.ScaffoldAsync("MyApp", "vue-ts", tempRoot);

            await Assert.That(result.Success).IsTrue();
            var mainPath = Path.Combine(tempRoot.FullName, "MyApp", "frontend", "src", "main.ts");
            var content = await File.ReadAllTextAsync(mainPath);

            await Assert.That(content).Contains("createApp");
            await Assert.That(content).Contains("from 'vue'");
            await Assert.That(content).Contains("mount('#app')");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Test]
    public async Task ScaffoldAsync_ReactTemplate_GeneratesReactEntryPoint()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var scaffolder = new ProjectScaffolder();
            var result = await scaffolder.ScaffoldAsync("MyApp", "react-ts", tempRoot);

            await Assert.That(result.Success).IsTrue();
            // React 模板使用 JSX，入口文件为 main.tsx
            var mainPath = Path.Combine(tempRoot.FullName, "MyApp", "frontend", "src", "main.tsx");
            var content = await File.ReadAllTextAsync(mainPath);

            await Assert.That(content).Contains("createRoot");
            await Assert.That(content).Contains("from 'react-dom/client'");
            await Assert.That(content).Contains("@wails-net/runtime");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Test]
    public async Task ScaffoldAsync_VanillaTemplate_GeneratesSimpleEntryPoint()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var scaffolder = new ProjectScaffolder();
            var result = await scaffolder.ScaffoldAsync("MyApp", "vanilla-ts", tempRoot);

            await Assert.That(result.Success).IsTrue();
            var mainPath = Path.Combine(tempRoot.FullName, "MyApp", "frontend", "src", "main.ts");
            var content = await File.ReadAllTextAsync(mainPath);

            await Assert.That(content).Contains("Wails.Net");
            await Assert.That(content).Contains("getElementById");
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

    [Test]
    public async Task ScaffoldAsync_GeneratesViteAndTsConfigAndGitIgnore()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var scaffolder = new ProjectScaffolder();
            var result = await scaffolder.ScaffoldAsync("MyApp", "vanilla-ts", tempRoot);

            await Assert.That(result.Success).IsTrue();
            var projectDir = Path.Combine(tempRoot.FullName, "MyApp");

            var vitePath = Path.Combine(projectDir, "frontend", "vite.config.ts");
            await Assert.That(File.Exists(vitePath)).IsTrue();
            var viteContent = await File.ReadAllTextAsync(vitePath);
            await Assert.That(viteContent).Contains("defineConfig");
            await Assert.That(viteContent).Contains("outDir: 'dist'");
            await Assert.That(viteContent).Contains("port: 5173");

            var tsconfigPath = Path.Combine(projectDir, "frontend", "tsconfig.json");
            await Assert.That(File.Exists(tsconfigPath)).IsTrue();
            var tsconfigContent = await File.ReadAllTextAsync(tsconfigPath);
            // 宽松 TS 配置
            await Assert.That(tsconfigContent).Contains("\"strict\": false");
            await Assert.That(tsconfigContent).Contains("\"moduleResolution\": \"bundler\"");

            var gitignorePath = Path.Combine(projectDir, ".gitignore");
            await Assert.That(File.Exists(gitignorePath)).IsTrue();
            var gitignoreContent = await File.ReadAllTextAsync(gitignorePath);
            await Assert.That(gitignoreContent).Contains("node_modules/");
            await Assert.That(gitignoreContent).Contains("frontend/dist/");

            await Assert.That(File.Exists(Path.Combine(projectDir, "README.md"))).IsTrue();
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Test]
    public async Task ScaffoldAsync_ReactTemplate_ViteConfigUsesReactPluginAndJsx()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var scaffolder = new ProjectScaffolder();
            var result = await scaffolder.ScaffoldAsync("MyApp", "react-ts", tempRoot);

            await Assert.That(result.Success).IsTrue();
            var frontendDir = Path.Combine(tempRoot.FullName, "MyApp", "frontend");

            var viteContent = await File.ReadAllTextAsync(Path.Combine(frontendDir, "vite.config.ts"));
            await Assert.That(viteContent).Contains("@vitejs/plugin-react");
            await Assert.That(viteContent).Contains("plugins: [react()]");

            var tsconfigContent = await File.ReadAllTextAsync(Path.Combine(frontendDir, "tsconfig.json"));
            await Assert.That(tsconfigContent).Contains("\"jsx\": \"react-jsx\"");

            // index.html 入口应指向 main.tsx
            var indexContent = await File.ReadAllTextAsync(Path.Combine(frontendDir, "index.html"));
            await Assert.That(indexContent).Contains("/src/main.tsx");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Test]
    public async Task ScaffoldAsync_WailsJson_LeavesFrontendCommandsEmptyForCliAutoDetect()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var scaffolder = new ProjectScaffolder();
            var result = await scaffolder.ScaffoldAsync("MyApp", "vanilla-ts", tempRoot);

            await Assert.That(result.Success).IsTrue();
            var content = await File.ReadAllTextAsync(Path.Combine(tempRoot.FullName, "MyApp", "wails.json"));

            // 留空以便 CLI 自动探测 pnpm（回退 npm）
            await Assert.That(content).Contains("\"installCommand\": \"\"");
            await Assert.That(content).Contains("\"buildCommand\": \"\"");
            await Assert.That(content).DoesNotContain("npm install");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Test]
    public async Task ScaffoldAsync_PackageJson_IsValidJsonWithRuntimeDependency()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var scaffolder = new ProjectScaffolder();
            var result = await scaffolder.ScaffoldAsync("My.App", "vue-ts", tempRoot);

            await Assert.That(result.Success).IsTrue();
            var pkgPath = Path.Combine(tempRoot.FullName, "My.App", "frontend", "package.json");
            var content = await File.ReadAllTextAsync(pkgPath);

            // 必须是合法 JSON（历史实现用手工拼接逗号，易产生非法 JSON）
            using var doc = System.Text.Json.JsonDocument.Parse(content);
            var root = doc.RootElement;

            await Assert.That(root.GetProperty("name").GetString()).IsEqualTo("my.app-frontend");
            await Assert.That(root.GetProperty("dependencies")
                .TryGetProperty("@wails-net/runtime", out _)).IsTrue();
            await Assert.That(root.GetProperty("dependencies")
                .TryGetProperty("vue", out _)).IsTrue();
            await Assert.That(root.GetProperty("devDependencies")
                .TryGetProperty("vite", out _)).IsTrue();
            await Assert.That(root.GetProperty("devDependencies")
                .TryGetProperty("@vitejs/plugin-vue", out _)).IsTrue();
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
