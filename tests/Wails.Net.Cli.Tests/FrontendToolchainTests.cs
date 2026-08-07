using TUnit.Assertions;
using TUnit.Core;
using Wails.Net.Cli.Build;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// FrontendToolchain 单元测试。
/// 覆盖 monorepo 工作区根查找逻辑（pnpm-workspace.yaml / package.json 特征字段）。
/// 对应任务：CLI 构建与开发链路改造（vite + pnpm）。
/// </summary>
[NotInParallel]
public sealed class FrontendToolchainTests
{
    /// <summary>
    /// 创建一个临时目录并返回其完整路径（测试结束由调用方清理）。
    /// </summary>
    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wails-net-fe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Test]
    public async Task FindWorkspaceRoot_PnpmWorkspaceYaml_ReturnsRoot()
    {
        var root = CreateTempDir();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "pnpm-workspace.yaml"),
                "packages:\n  - 'packages/*'\n");

            var frontend = Path.Combine(root, "examples", "demo", "frontend");
            Directory.CreateDirectory(frontend);

            var result = FrontendToolchain.FindWorkspaceRoot(frontend);

            await Assert.That(result).IsEqualTo(Path.GetFullPath(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task FindWorkspaceRoot_PackageJsonWithWorkspaces_ReturnsRoot()
    {
        var root = CreateTempDir();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "package.json"),
                "{\"name\":\"root\",\"workspaces\":[\"packages/*\"]}");

            var frontend = Path.Combine(root, "app", "frontend");
            Directory.CreateDirectory(frontend);

            var result = FrontendToolchain.FindWorkspaceRoot(frontend);

            await Assert.That(result).IsEqualTo(Path.GetFullPath(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task FindWorkspaceRoot_PackageJsonWithPackageManager_ReturnsRoot()
    {
        var root = CreateTempDir();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "package.json"),
                "{\"name\":\"root\",\"packageManager\":\"pnpm@9.0.0\"}");

            var frontend = Path.Combine(root, "frontend");
            Directory.CreateDirectory(frontend);

            var result = FrontendToolchain.FindWorkspaceRoot(frontend);

            await Assert.That(result).IsEqualTo(Path.GetFullPath(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task FindWorkspaceRoot_NoWorkspaceMarker_ReturnsFrontendDir()
    {
        var root = CreateTempDir();
        try
        {
            var frontend = Path.Combine(root, "standalone", "frontend");
            Directory.CreateDirectory(frontend);

            // 普通 package.json（无 workspaces / packageManager）不应被识别为工作区根
            await File.WriteAllTextAsync(
                Path.Combine(frontend, "package.json"),
                "{\"name\":\"standalone-frontend\",\"version\":\"0.0.0\"}");

            var result = FrontendToolchain.FindWorkspaceRoot(frontend);

            await Assert.That(result).IsEqualTo(Path.GetFullPath(frontend));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task FindWorkspaceRoot_FrontendDirIsRootItself_ReturnsSameDir()
    {
        var root = CreateTempDir();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "pnpm-workspace.yaml"),
                "packages:\n  - '.'\n");

            var result = FrontendToolchain.FindWorkspaceRoot(root);

            await Assert.That(result).IsEqualTo(Path.GetFullPath(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
