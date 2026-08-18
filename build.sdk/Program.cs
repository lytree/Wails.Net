// ============================================================================
// Cake.Sdk 项目式构建脚本（DSL 形态）
// ----------------------------------------------------------------------------
// 说明：这是本项目引入的 Cake.Sdk 构建入口，与 build/ 目录下的 Cake.Frosting
// 项目并存（build/ 为 C# 类任务实现，此为文件式 .cs DSL 实现）。
//
// 运行方式（已通过 RunWorkingDirectory 切到仓库根：
//   dotnet build build.sdk/cake.csproj                 # 编译验证脚本
//   dotnet run --project build.sdk/cake.csproj         # 默认目标 Test
//   dotnet run --project build.sdk/cake.csproj -- --target Pack
//   dotnet run --project build.sdk/cake.csproj -- --target Build --configuration Debug
//
// 参考：https://cakebuild.net/docs/running-builds/runners/cake-sdk
// ============================================================================

var target = Argument("target", "Test");
var configuration = Argument("configuration", "Release");

// === 常量 ===
const string SolutionFile = "Wails.Net.slnx";
const string ApplicationTestsProject = "tests/Wails.Net.Application.Tests/Wails.Net.Application.Tests.csproj";
const string CliTestsProject = "tests/Wails.Net.Cli.Tests/Wails.Net.Cli.Tests.csproj";
const string AssetServerTestsProject = "tests/Wails.Net.AssetServer.Tests/Wails.Net.AssetServer.Tests.csproj";

// ============================================================================
// TASK: Clean（--rebuild 时执行）
// ============================================================================

Task("Clean")
    .WithCriteria(c => HasArgument("rebuild"))
    .Does(() =>
    {
        Information("========== 清理构建输出 ==========");
        CleanDirectories($"src/**/bin/{configuration}");
        CleanDirectories($"src/**/obj/{configuration}");
        CleanDirectories($"tests/**/bin/{configuration}");
        CleanDirectories($"tests/**/obj/{configuration}");
    });

// ============================================================================
// TASK: Build
// ============================================================================

Task("Build")
    .IsDependentOn("Clean")
    .Does(() =>
    {
        Information("========== 构建解决方案 ==========");
        DotNetBuild(SolutionFile, new DotNetBuildSettings
        {
            Configuration = configuration,
        });
    });

// ============================================================================
// TASK: Test（.NET 10 使用 dotnet run，非 dotnet test，详见 AGENTS.md §4.4）
// ============================================================================

Task("Test")
    .IsDependentOn("Build")
    .Does(() =>
    {
        Information("========== 运行跨平台单元测试 ==========");

        Information("--- Application 测试 ---");
        DotNetRun(ApplicationTestsProject, new DotNetRunSettings
        {
            Configuration = configuration,
            NoBuild = true,
        });

        Information("--- CLI 测试 ---");
        DotNetRun(CliTestsProject, new DotNetRunSettings
        {
            Configuration = configuration,
            NoBuild = true,
        });

        Information("--- AssetServer 测试 ---");
        DotNetRun(AssetServerTestsProject, new DotNetRunSettings
        {
            Configuration = configuration,
            NoBuild = true,
        });
    });

// ============================================================================
// TASK: Pack（打包 NuGet 包）
// ============================================================================

Task("Pack")
    .IsDependentOn("Test")
    .Does(() =>
    {
        Information("========== 打包 NuGet 包 ==========");
        var packageOutput = "artifacts/packages";
        EnsureDirectoryExists(packageOutput);

        DotNetPack(SolutionFile, new DotNetPackSettings
        {
            Configuration = configuration,
            NoBuild = true,
            OutputDirectory = packageOutput,
        });
    });

// ============================================================================
// 执行入口
// ============================================================================

RunTarget(target);