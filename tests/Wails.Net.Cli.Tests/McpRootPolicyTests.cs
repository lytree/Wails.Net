using TUnit.Assertions;
using TUnit.Core;
using Wails.Net.Cli.Mcp;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// MCP 允许根目录与参数校验单元测试（P1-4）。
/// 对应 Wails v3 Go 版本 <c>internal/commands/mcp.go</c> 的
/// <c>resolveMCPRoot</c> / <c>resolveMCPPath</c> / <c>validateMCPArgs</c>。
/// </summary>
[NotInParallel]
public sealed class McpRootPolicyTests
{
    [Test]
    public async Task Create_ValidDirectory_NormalizesRoot()
    {
        var root = CreateTempRoot();
        try
        {
            var policy = McpRootPolicy.Create(root);

            await Assert.That(policy.Root)
                .IsEqualTo(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Test]
    public async Task Create_MissingDirectory_Throws()
    {
        var root = Path.Combine(Path.GetTempPath(), "wailsnet-mcp-missing-" + Guid.NewGuid().ToString("N"));

        await Assert.That(() => McpRootPolicy.Create(root)).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task Create_FilePath_Throws()
    {
        var file = Path.Combine(Path.GetTempPath(), "wailsnet-mcp-file-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(file, "x");
        try
        {
            await Assert.That(() => McpRootPolicy.Create(file)).ThrowsExactly<InvalidOperationException>();
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Test]
    public async Task ResolvePath_Empty_ReturnsRoot()
    {
        var root = CreateTempRoot();
        try
        {
            var policy = McpRootPolicy.Create(root);

            await Assert.That(policy.ResolvePath(null)).IsEqualTo(policy.Root);
            await Assert.That(policy.ResolvePath(string.Empty)).IsEqualTo(policy.Root);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Test]
    public async Task ResolvePath_RelativeInside_ReturnsAbsolute()
    {
        var root = CreateTempRoot();
        try
        {
            var policy = McpRootPolicy.Create(root);

            await Assert.That(policy.ResolvePath("src"))
                .IsEqualTo(Path.Combine(policy.Root, "src"));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Test]
    public async Task ResolvePath_RelativeEscape_Throws()
    {
        var root = CreateTempRoot();
        try
        {
            var policy = McpRootPolicy.Create(root);

            await Assert.That(() => policy.ResolvePath("../outside")).ThrowsExactly<InvalidOperationException>();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Test]
    public async Task ResolvePath_AbsoluteOutside_Throws()
    {
        var root = CreateTempRoot();
        try
        {
            var policy = McpRootPolicy.Create(root);
            var outside = Path.Combine(Path.GetTempPath(), "wailsnet-mcp-outside.txt");

            await Assert.That(() => policy.ResolvePath(outside)).ThrowsExactly<InvalidOperationException>();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Test]
    public async Task ValidateArguments_PathFlagInside_Accepts()
    {
        var root = CreateTempRoot();
        try
        {
            var policy = McpRootPolicy.Create(root);

            policy.ValidateArguments(policy.Root, ["--project", "src", "--configuration", "Release"]);
            policy.ValidateArguments(policy.Root, ["--output=src/bindings"]);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Test]
    public async Task ValidateArguments_PathFlagOutside_Throws()
    {
        var root = CreateTempRoot();
        try
        {
            var policy = McpRootPolicy.Create(root);

            await Assert.That(() => policy.ValidateArguments(policy.Root, ["--project", "../outside"]))
                .ThrowsExactly<InvalidOperationException>();
            await Assert.That(() => policy.ValidateArguments(policy.Root, ["--output=../outside"]))
                .ThrowsExactly<InvalidOperationException>();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Test]
    public async Task ValidateArguments_MissingPathValue_Throws()
    {
        var root = CreateTempRoot();
        try
        {
            var policy = McpRootPolicy.Create(root);

            await Assert.That(() => policy.ValidateArguments(policy.Root, ["--project"]))
                .ThrowsExactly<InvalidOperationException>();
            await Assert.That(() => policy.ValidateArguments(policy.Root, ["--project", "--verbose"]))
                .ThrowsExactly<InvalidOperationException>();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Test]
    public async Task ValidateArguments_NulByte_Throws()
    {
        var root = CreateTempRoot();
        try
        {
            var policy = McpRootPolicy.Create(root);

            await Assert.That(() => policy.ValidateArguments(policy.Root, ["bad\0arg"]))
                .ThrowsExactly<InvalidOperationException>();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Test]
    public async Task ValidateArguments_TooLong_Throws()
    {
        var root = CreateTempRoot();
        try
        {
            var policy = McpRootPolicy.Create(root);

            await Assert.That(() => policy.ValidateArguments(policy.Root, [new string('a', 4097)]))
                .ThrowsExactly<InvalidOperationException>();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Test]
    public async Task ValidateArguments_TooMany_Throws()
    {
        var root = CreateTempRoot();
        try
        {
            var policy = McpRootPolicy.Create(root);
            var args = Enumerable.Range(0, 65).Select(i => $"a{i}").ToArray();

            await Assert.That(() => policy.ValidateArguments(policy.Root, args))
                .ThrowsExactly<InvalidOperationException>();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Test]
    public async Task IsWithin_DetectsContainment()
    {
        var root = Path.Combine(Path.GetTempPath(), "wailsnet-root");
        var child = Path.Combine(root, "src", "app.cs");
        var sibling = root + "-evil";

        await Assert.That(McpRootPolicy.IsWithin(root, root)).IsTrue();
        await Assert.That(McpRootPolicy.IsWithin(root, child)).IsTrue();
        await Assert.That(McpRootPolicy.IsWithin(root, sibling)).IsFalse();
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "wailsnet-mcp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempRoot(string root)
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // 测试清理失败不影响断言
        }
    }
}
