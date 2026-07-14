using System.Text.Json;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using Wails.Net.Cli.Build;
using Wails.Net.Cli.Config;
using Wails.Net.Cli.Scaffolding;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// CLI 配置与构建钩子单元测试。
/// 对应主题 E（CLI 增强）：覆盖 ProjectConfig 加载、BuildHooks 执行、wails.json 模板字段。
/// </summary>
[NotInParallel]
public sealed class CliConfigAndHooksTests
{
    // ============== ProjectConfig.LoadFromFileAsync ==============

    [Test]
    public async Task LoadFromFileAsync_NonExistentFile_ReturnsNull()
    {
        var path = Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N") + ".json");
        var config = await ProjectConfig.LoadFromFileAsync(path);
        await Assert.That(config).IsNull();
    }

    [Test]
    public async Task LoadFromFileAsync_ValidJson_LoadsAllFields()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var path = Path.Combine(tempDir.FullName, "wails.json");
            await File.WriteAllTextAsync(path, """
            {
              "name": "MyApp",
              "version": "1.2.3",
              "template": "vue-ts",
              "assetDir": "build",
              "outputFilename": "MyApp",
              "wailsJsDir": "frontend/wails",
              "beforeBuildCommand": "npm run lint",
              "afterBuildCommand": "echo done",
              "beforeDevCommand": "npm run dev:prep",
              "afterDevCommand": "echo dev-done",
              "author": { "name": "Tester", "email": "t@example.com" },
              "frontend": {
                "dir": "ui",
                "devServerUrl": "http://localhost:3000",
                "installCommand": "pnpm install",
                "buildCommand": "pnpm build",
                "outputDir": "ui/dist"
              },
              "bindings": {
                "outputDir": "frontend/src/wails"
              }
            }
            """);

            var config = await ProjectConfig.LoadFromFileAsync(path);

            await Assert.That(config).IsNotNull();
            await Assert.That(config!.Name).IsEqualTo("MyApp");
            await Assert.That(config.Version).IsEqualTo("1.2.3");
            await Assert.That(config.Template).IsEqualTo("vue-ts");
            await Assert.That(config.AssetDir).IsEqualTo("build");
            await Assert.That(config.OutputFilename).IsEqualTo("MyApp");
            await Assert.That(config.WailsJsDir).IsEqualTo("frontend/wails");
            await Assert.That(config.BeforeBuildCommand).IsEqualTo("npm run lint");
            await Assert.That(config.AfterBuildCommand).IsEqualTo("echo done");
            await Assert.That(config.BeforeDevCommand).IsEqualTo("npm run dev:prep");
            await Assert.That(config.AfterDevCommand).IsEqualTo("echo dev-done");
            await Assert.That(config.Author).IsNotNull();
            await Assert.That(config.Author!.Name).IsEqualTo("Tester");
            await Assert.That(config.Author.Email).IsEqualTo("t@example.com");
            await Assert.That(config.Frontend.Dir).IsEqualTo("ui");
            await Assert.That(config.Frontend.DevServerUrl).IsEqualTo("http://localhost:3000");
            await Assert.That(config.Frontend.InstallCommand).IsEqualTo("pnpm install");
            await Assert.That(config.Frontend.BuildCommand).IsEqualTo("pnpm build");
            await Assert.That(config.Frontend.OutputDir).IsEqualTo("ui/dist");
            await Assert.That(config.Bindings.OutputDir).IsEqualTo("frontend/src/wails");
        }
        finally
        {
            DeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task LoadFromFileAsync_WithCommentsAndTrailingCommas_ParsesSuccessfully()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var path = Path.Combine(tempDir.FullName, "wails.json");
            await File.WriteAllTextAsync(path, """
            {
              // 项目名称
              "name": "CommentedApp",
              "version": "0.1.0",
              "frontend": {
                "dir": "frontend",
                "buildCommand": "npm run build",
              },
            }
            """);

            var config = await ProjectConfig.LoadFromFileAsync(path);

            await Assert.That(config).IsNotNull();
            await Assert.That(config!.Name).IsEqualTo("CommentedApp");
            await Assert.That(config.Frontend.Dir).IsEqualTo("frontend");
        }
        finally
        {
            DeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task LoadFromFileAsync_MinimalJson_UsesFieldDefaults()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var path = Path.Combine(tempDir.FullName, "wails.json");
            await File.WriteAllTextAsync(path, """{ "name": "Tiny" }""");

            var config = await ProjectConfig.LoadFromFileAsync(path);

            await Assert.That(config).IsNotNull();
            await Assert.That(config!.Name).IsEqualTo("Tiny");
            // 默认值
            await Assert.That(config.Version).IsEqualTo("0.1.0");
            await Assert.That(config.Frontend.Dir).IsEqualTo("frontend");
            await Assert.That(config.Bindings.OutputDir).IsEqualTo("frontend/src/wails");
            // 未设置的可空字段应为 null
            await Assert.That(config.AssetDir).IsNull();
            await Assert.That(config.OutputFilename).IsNull();
            await Assert.That(config.WailsJsDir).IsNull();
            await Assert.That(config.BeforeBuildCommand).IsNull();
            await Assert.That(config.AfterBuildCommand).IsNull();
            await Assert.That(config.BeforeDevCommand).IsNull();
            await Assert.That(config.AfterDevCommand).IsNull();
            await Assert.That(config.Frontend.BuildCommand).IsNull();
            await Assert.That(config.Frontend.InstallCommand).IsNull();
        }
        finally
        {
            DeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task LoadFromFileAsync_InvalidJson_ThrowsJsonException()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var path = Path.Combine(tempDir.FullName, "wails.json");
            await File.WriteAllTextAsync(path, """{ invalid json }""");

            await Assert.That(async () => await ProjectConfig.LoadFromFileAsync(path))
                .ThrowsExactly<JsonException>();
        }
        finally
        {
            DeleteDirectory(tempDir);
        }
    }

    // ============== ProjectConfig.FindConfigPath / FindAndLoadAsync ==============

    [Test]
    public async Task FindConfigPath_NoWailsJson_ReturnsNull()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var path = ProjectConfig.FindConfigPath(tempDir.FullName);
            await Assert.That(path).IsNull();
        }
        finally
        {
            DeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task FindConfigPath_InProjectDirectory_ReturnsPath()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var configPath = Path.Combine(tempDir.FullName, "wails.json");
            await File.WriteAllTextAsync(configPath, """{ "name": "X" }""");

            var path = ProjectConfig.FindConfigPath(tempDir.FullName);

            await Assert.That(path).IsNotNull();
            await Assert.That(Path.GetFullPath(path!)).IsEqualTo(Path.GetFullPath(configPath));
        }
        finally
        {
            DeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task FindConfigPath_FromProjectFile_ResolvesToDirectory()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var configPath = Path.Combine(tempDir.FullName, "wails.json");
            var projectPath = Path.Combine(tempDir.FullName, "App.csproj");
            await File.WriteAllTextAsync(configPath, """{ "name": "X" }""");
            await File.WriteAllTextAsync(projectPath, "<Project/>");

            var path = ProjectConfig.FindConfigPath(projectPath);

            await Assert.That(path).IsNotNull();
            await Assert.That(Path.GetFullPath(path!)).IsEqualTo(Path.GetFullPath(configPath));
        }
        finally
        {
            DeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task FindAndLoadAsync_NoConfig_ReturnsNullTuple()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var (config, path) = await ProjectConfig.FindAndLoadAsync(tempDir.FullName);
            await Assert.That(config).IsNull();
            await Assert.That(path).IsNull();
        }
        finally
        {
            DeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task FindAndLoadAsync_ExistingConfig_ReturnsBothConfigAndPath()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var configPath = Path.Combine(tempDir.FullName, "wails.json");
            await File.WriteAllTextAsync(configPath, """{ "name": "Loaded", "version": "2.0.0" }""");

            var (config, path) = await ProjectConfig.FindAndLoadAsync(tempDir.FullName);

            await Assert.That(config).IsNotNull();
            await Assert.That(config!.Name).IsEqualTo("Loaded");
            await Assert.That(config.Version).IsEqualTo("2.0.0");
            await Assert.That(path).IsNotNull();
            await Assert.That(Path.GetFullPath(path!)).IsEqualTo(Path.GetFullPath(configPath));
        }
        finally
        {
            DeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task FindAndLoadAsync_NullInput_FallsBackToCurrentDirectory()
    {
        // 在当前工作目录创建临时 wails.json，验证后清理
        var original = Directory.GetCurrentDirectory();
        var tempDir = CreateTempDirectory();
        try
        {
            Directory.SetCurrentDirectory(tempDir.FullName);
            var configPath = Path.Combine(tempDir.FullName, "wails.json");
            await File.WriteAllTextAsync(configPath, """{ "name": "CwdApp" }""");

            var (config, path) = await ProjectConfig.FindAndLoadAsync(null);

            await Assert.That(config).IsNotNull();
            await Assert.That(config!.Name).IsEqualTo("CwdApp");
            await Assert.That(path).IsNotNull();
        }
        finally
        {
            Directory.SetCurrentDirectory(original);
            DeleteDirectory(tempDir);
        }
    }

    // ============== BuildHooks.ExecuteAsync ==============

    [Test]
    public async Task ExecuteAsync_NullCommand_ReturnsSkipped()
    {
        var result = await BuildHooks.ExecuteAsync(null);
        await Assert.That(result.Skipped).IsTrue();
        await Assert.That(result.Success).IsTrue();
    }

    [Test]
    public async Task ExecuteAsync_EmptyCommand_ReturnsSkipped()
    {
        var result = await BuildHooks.ExecuteAsync(string.Empty);
        await Assert.That(result.Skipped).IsTrue();
        await Assert.That(result.Success).IsTrue();
    }

    [Test]
    public async Task ExecuteAsync_WhitespaceCommand_ReturnsSkipped()
    {
        var result = await BuildHooks.ExecuteAsync("   ");
        await Assert.That(result.Skipped).IsTrue();
        await Assert.That(result.Success).IsTrue();
    }

    [Test]
    public async Task ExecuteAsync_EchoCommand_ReturnsSuccessWithOutput()
    {
        // 跨平台的 echo：Windows cmd /c "echo ..."，Linux/macOS sh -c "echo ..."
        var result = await BuildHooks.ExecuteAsync("echo hello-wails");

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Skipped).IsFalse();
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Output ?? string.Empty).Contains("hello-wails");
    }

    [Test]
    public async Task ExecuteAsync_CommandWithNonZeroExit_ReturnsFailureWithCode()
    {
        // 跨平台的失败命令：exit 7
        var result = await BuildHooks.ExecuteAsync("exit 7");

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.ExitCode).IsEqualTo(7);
        await Assert.That(result.ErrorMessage ?? string.Empty).Contains("7");
    }

    [Test]
    public async Task ExecuteAsync_NonExistentCommand_ReturnsFailureWithError()
    {
        // 调用一个几乎肯定不存在的可执行文件
        var result = await BuildHooks.ExecuteAsync("wails-net-not-a-real-binary-xyz --version");

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.ErrorMessage).IsNotNull();
    }

    [Test]
    public async Task ExecuteAsync_WithWorkingDirectory_ExecutesInThatDirectory()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            // 在目录中创建标记文件
            var markerPath = Path.Combine(tempDir.FullName, "marker.txt");
            await File.WriteAllTextAsync(markerPath, "marker-content");

            // 跨平台列出当前目录文件：dir（Windows）/ls（POSIX）。使用通配命令：BuildHooks 自身按平台选择解释器。
            // 在 Windows 上，cmd /c "type marker.txt" 输出文件内容；在 POSIX 上，cat marker.txt 同样输出。
            var command = OperatingSystem.IsWindows() ? "type marker.txt" : "cat marker.txt";
            var result = await BuildHooks.ExecuteAsync(command, tempDir.FullName);

            await Assert.That(result.Success).IsTrue();
            await Assert.That(result.Output ?? string.Empty).Contains("marker-content");
        }
        finally
        {
            DeleteDirectory(tempDir);
        }
    }

    // ============== ProjectScaffolder wails.json 模板新增字段 ==============

    [Test]
    public async Task ScaffoldAsync_WailsJson_ContainsNewConfigFields()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var scaffolder = new ProjectScaffolder();
            var result = await scaffolder.ScaffoldAsync("MyApp", "vanilla-ts", tempRoot);

            await Assert.That(result.Success).IsTrue();
            var jsonPath = Path.Combine(tempRoot.FullName, "MyApp", "wails.json");
            var content = await File.ReadAllTextAsync(jsonPath);

            await Assert.That(content).Contains("\"assetDir\":");
            await Assert.That(content).Contains("\"outputFilename\":");
            await Assert.That(content).Contains("\"wailsJsDir\":");
            await Assert.That(content).Contains("\"beforeBuildCommand\":");
            await Assert.That(content).Contains("\"afterBuildCommand\":");
            await Assert.That(content).Contains("\"beforeDevCommand\":");
            await Assert.That(content).Contains("\"afterDevCommand\":");
            await Assert.That(content).Contains("\"installCommand\":");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Test]
    public async Task ScaffoldAsync_WailsJson_OutputFilenameMatchesProjectName()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var scaffolder = new ProjectScaffolder();
            var result = await scaffolder.ScaffoldAsync("CustomName", "vanilla-ts", tempRoot);

            await Assert.That(result.Success).IsTrue();
            var jsonPath = Path.Combine(tempRoot.FullName, "CustomName", "wails.json");
            var content = await File.ReadAllTextAsync(jsonPath);

            await Assert.That(content).Contains("\"outputFilename\": \"CustomName\"");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Test]
    public async Task ScaffoldAsync_WailsJson_CanBeParsedByProjectConfig()
    {
        // 验证脚手架生成的 wails.json 能被 ProjectConfig 正确反序列化
        var tempRoot = CreateTempDirectory();
        try
        {
            var scaffolder = new ProjectScaffolder();
            var result = await scaffolder.ScaffoldAsync("MyApp", "react-ts", tempRoot);

            await Assert.That(result.Success).IsTrue();
            var jsonPath = Path.Combine(tempRoot.FullName, "MyApp", "wails.json");

            var config = await ProjectConfig.LoadFromFileAsync(jsonPath);

            await Assert.That(config).IsNotNull();
            await Assert.That(config!.Name).IsEqualTo("MyApp");
            await Assert.That(config.Version).IsEqualTo("0.1.0");
            await Assert.That(config.Template).IsEqualTo("react-ts");
            await Assert.That(config.AssetDir).IsEqualTo("frontend/dist");
            await Assert.That(config.OutputFilename).IsEqualTo("MyApp");
            await Assert.That(config.WailsJsDir).IsEqualTo("frontend/src/wails");
            await Assert.That(config.Frontend.InstallCommand).IsEqualTo("npm install");
            await Assert.That(config.Frontend.BuildCommand).IsEqualTo("npm run build");
            await Assert.That(config.Frontend.DevServerUrl).IsEqualTo("http://localhost:5173");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    // ============== 辅助方法 ==============

    private static DirectoryInfo CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "wails-cli-config-tests-" + Guid.NewGuid().ToString("N"));
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
