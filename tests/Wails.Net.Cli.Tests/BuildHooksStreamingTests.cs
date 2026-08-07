using TUnit.Assertions;
using TUnit.Core;
using Wails.Net.Cli.Build;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// BuildHooks 流式输出模式单元测试。
/// 验证 <c>streamOutput: true</c> 时输出仍被完整累积，且退出码语义与缓冲模式一致。
/// 对应任务：CLI 构建与开发链路改造（长耗时前端命令需要实时输出）。
/// </summary>
[NotInParallel]
public sealed class BuildHooksStreamingTests
{
    [Test]
    public async Task ExecuteAsync_Streaming_CapturesStdout()
    {
        var result = await BuildHooks.ExecuteAsync(
            "echo wails-net-stream",
            Path.GetTempPath(),
            CancellationToken.None,
            streamOutput: true);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Output).IsNotNull();
        await Assert.That(result.Output!.Contains("wails-net-stream", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task ExecuteAsync_Streaming_NonZeroExitCode_ReturnsFailure()
    {
        var result = await BuildHooks.ExecuteAsync(
            "exit 3",
            Path.GetTempPath(),
            CancellationToken.None,
            streamOutput: true);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.ExitCode).IsEqualTo(3);
        await Assert.That(result.ErrorMessage).IsNotNull();
    }

    [Test]
    public async Task ExecuteAsync_Streaming_EmptyCommand_IsSkipped()
    {
        var result = await BuildHooks.ExecuteAsync(
            "   ",
            Path.GetTempPath(),
            CancellationToken.None,
            streamOutput: true);

        await Assert.That(result.Skipped).IsTrue();
        await Assert.That(result.Success).IsTrue();
    }

    [Test]
    public async Task ExecuteAsync_Buffered_StillReturnsOutput()
    {
        // 默认（非流式）行为保持不变
        var result = await BuildHooks.ExecuteAsync(
            "echo wails-net-buffered",
            Path.GetTempPath());

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Output).IsNotNull();
        await Assert.That(result.Output!.Contains("wails-net-buffered", StringComparison.Ordinal)).IsTrue();
    }
}
