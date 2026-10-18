using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using Wails.Net.Cli.Build;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// macOS 输出准备（P2-23）单元测试。
/// 验证可在任意平台运行的纯逻辑：项目内/外路径判定与重定位决策
/// （file provider 路径 → 重定位并建立符号链接；普通路径 → 原样标记）。
/// 对应 Wails v3 Go 版本 internal/commands/tool_prepare_macos_output.go。
/// </summary>
[NotInParallel]
public sealed class MacOsOutputPreparerTests
{
    /// <summary>
    /// file provider 下的项目内输出目录应决策为「重定位到本地文件系统并建立符号链接」。
    /// </summary>
    [Test]
    public async Task Decide_FileProviderInsideProject_Relocates()
    {
        var probe = new MacOsOutputProbe
        {
            InsideProject = true,
            IsSymlink = false,
            IsLocalMarked = false,
            IsFileProvider = true,
        };

        var action = MacOsOutputPreparer.Decide(probe);

        await Assert.That(action).IsEqualTo(MacOsOutputAction.Relocate);
    }

    /// <summary>
    /// 普通（非 file provider）项目内输出目录应决策为「原样使用并写入本地标记」。
    /// </summary>
    [Test]
    public async Task Decide_OrdinaryPathInsideProject_MarksLocal()
    {
        var probe = new MacOsOutputProbe
        {
            InsideProject = true,
            IsSymlink = false,
            IsLocalMarked = false,
            IsFileProvider = false,
        };

        var action = MacOsOutputPreparer.Decide(probe);

        await Assert.That(action).IsEqualTo(MacOsOutputAction.MarkLocal);
    }

    /// <summary>
    /// 项目外的目录应保留用户选择（仅确保目录存在）。
    /// </summary>
    [Test]
    public async Task Decide_OutsideProject_CreatesDirectoryOnly()
    {
        var probe = new MacOsOutputProbe
        {
            InsideProject = false,
            IsFileProvider = true,
        };

        var action = MacOsOutputPreparer.Decide(probe);

        await Assert.That(action).IsEqualTo(MacOsOutputAction.CreateDirectoryOnly);
    }

    /// <summary>
    /// 已是符号链接时只确保链接目标位于本地文件系统。
    /// </summary>
    [Test]
    public async Task Decide_Symlink_EnsuresLinkTargetLocal()
    {
        var probe = new MacOsOutputProbe
        {
            InsideProject = true,
            IsSymlink = true,
            IsFileProvider = true,
        };

        var action = MacOsOutputPreparer.Decide(probe);

        await Assert.That(action).IsEqualTo(MacOsOutputAction.EnsureLinkTargetLocal);
    }

    /// <summary>
    /// 已带本地标记的目录无需处理。
    /// </summary>
    [Test]
    public async Task Decide_AlreadyMarked_AlreadyLocal()
    {
        var probe = new MacOsOutputProbe
        {
            InsideProject = true,
            IsLocalMarked = true,
            IsFileProvider = true,
        };

        var action = MacOsOutputPreparer.Decide(probe);

        await Assert.That(action).IsEqualTo(MacOsOutputAction.AlreadyLocal);
    }

    /// <summary>
    /// 项目内子目录应判定为项目内。
    /// </summary>
    [Test]
    public async Task IsProjectOutputDirectory_InsideProject_ReturnsTrue()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "wailsnet-mac-proj");
        var binPath = Path.Combine(projectRoot, "bin");

        var result = MacOsOutputPreparer.IsProjectOutputDirectory(projectRoot, binPath);

        await Assert.That(result).IsTrue();
    }

    /// <summary>
    /// 项目外的目录应判定为项目外（不对其重定位）。
    /// </summary>
    [Test]
    public async Task IsProjectOutputDirectory_OutsideProject_ReturnsFalse()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "wailsnet-mac-proj");
        var binPath = Path.Combine(Path.GetTempPath(), "wailsnet-mac-other", "bin");

        var result = MacOsOutputPreparer.IsProjectOutputDirectory(projectRoot, binPath);

        await Assert.That(result).IsFalse();
    }

    /// <summary>
    /// 输出目录等于项目根目录时应报错（对应 Go 的 "must not be the project root"）。
    /// </summary>
    [Test]
    public async Task IsProjectOutputDirectory_ProjectRoot_Throws()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "wailsnet-mac-proj");

        await Assert.That(() => MacOsOutputPreparer.IsProjectOutputDirectory(projectRoot, projectRoot))
            .ThrowsExactly<InvalidOperationException>();
    }

    /// <summary>
    /// 非 macOS 主机上 PrepareAsync 应为 no-op（Skipped=true），不改变任何目录。
    /// </summary>
    [Test]
    public async Task PrepareAsync_NonMacOs_Skipped()
    {
        if (OperatingSystem.IsMacOS())
        {
            Skip.Test("本用例验证非 macOS 主机的 no-op 行为");
            return;
        }

        var projectRoot = Path.Combine(Path.GetTempPath(), $"wailsnet-mac-noop-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        try
        {
            var result = await new MacOsOutputPreparer()
                .PrepareAsync(projectRoot, Path.Combine(projectRoot, MacOsOutputPreparer.DefaultDirectoryName));

            await Assert.That(result.Skipped).IsTrue();
            await Assert.That(result.Success).IsTrue();

            // no-op：不会创建 bin 目录、标记文件或符号链接。
            await Assert.That(Directory.Exists(Path.Combine(projectRoot, "bin"))).IsFalse();
        }
        finally
        {
            try { Directory.Delete(projectRoot, recursive: true); } catch { }
        }
    }
}
