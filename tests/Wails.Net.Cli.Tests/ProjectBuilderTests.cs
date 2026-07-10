using System.Diagnostics;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using Wails.Net.Cli.Build;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// 项目构建器单元测试。
/// 验证 dotnet build 参数构造、成功/失败结果处理。
/// 注意：实际 dotnet 命令调用较慢，仅测试关键路径。
/// </summary>
[NotInParallel]
public sealed class ProjectBuilderTests
{
    [Test]
    public async Task BuildAsync_ValidProject_ReturnsSuccess()
    {
        // 使用 Generator 项目作为可构建的测试目标（已知可编译）
        var projectPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "Wails.Net.Generator", "Wails.Net.Generator.csproj"));

        if (!File.Exists(projectPath))
        {
            // 路径推断失败时跳过
            return;
        }

        var builder = new ProjectBuilder();
        var result = await builder.BuildAsync(
            new FileInfo(projectPath),
            "Debug",
            runtime: null,
            selfContained: false);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.OutputPath).IsNotNull();
    }

    [Test]
    public async Task BuildAsync_NonExistentProject_ReturnsFailure()
    {
        var builder = new ProjectBuilder();
        var result = await builder.BuildAsync(
            new FileInfo(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid().ToString("N") + ".csproj")),
            "Debug",
            runtime: null,
            selfContained: false);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.ErrorMessage).IsNotNull();
    }

    [Test]
    public async Task BuildAsync_FailurePopulatesBuildLog()
    {
        var builder = new ProjectBuilder();
        var result = await builder.BuildAsync(
            new FileInfo(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid().ToString("N") + ".csproj")),
            "Debug",
            runtime: null,
            selfContained: false);

        await Assert.That(result.Success).IsFalse();
        // BuildLog 在失败时应有内容（MSBUILD 错误或进程输出）
        await Assert.That(result.BuildLog).IsNotNull();
    }
}
