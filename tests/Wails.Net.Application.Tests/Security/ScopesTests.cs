using System.IO;
using TUnit.Core;
using Wails.Net.Application.Security;

namespace Wails.Net.Application.Tests.Security;

/// <summary>
/// 权限作用域单元测试（TUnit）。
/// 对应主题 C：FileSystemScope 路径匹配、目录前缀、UrlScope 模式匹配。
/// </summary>
public sealed class ScopesTests
{
    // === FileSystemScope ===

    [Test]
    public async Task FileSystemScope_AddPath_AllowsExactMatch()
    {
        var scope = new FileSystemScope();
        var tempFile = Path.GetTempFileName();
        scope.AddPath(tempFile);

        await Assert.That(scope.Allows(tempFile)).IsTrue();
    }

    [Test]
    public async Task FileSystemScope_AddDirectory_AllowsSubPaths()
    {
        var scope = new FileSystemScope();
        var tempDir = Path.GetTempPath();
        scope.AddPath(tempDir);

        var subPath = Path.Combine(tempDir, "subdir", "file.txt");
        await Assert.That(scope.Allows(subPath)).IsTrue();
    }

    [Test]
    public async Task FileSystemScope_NonAddedPath_ReturnsFalse()
    {
        var scope = new FileSystemScope();
        scope.AddPath(Path.GetTempPath());

        // 系统目录不在允许范围内
        await Assert.That(scope.Allows("C:\\Windows\\System32\\config.sys")).IsFalse();
    }

    [Test]
    public async Task FileSystemScope_EmptyScope_ReturnsFalse()
    {
        var scope = new FileSystemScope();

        await Assert.That(scope.Allows("C:\\test.txt")).IsFalse();
    }

    [Test]
    public async Task FileSystemScope_InvalidPath_ReturnsFalse()
    {
        var scope = new FileSystemScope();
        scope.AddPath(Path.GetTempPath());

        // 非法路径字符
        await Assert.That(scope.Allows("||||invalid||||")).IsFalse();
    }

    [Test]
    public async Task FileSystemScope_RemovePath_RemovesFromScope()
    {
        var scope = new FileSystemScope();
        var tempFile = Path.GetTempFileName();
        scope.AddPath(tempFile);
        scope.RemovePath(tempFile);

        await Assert.That(scope.Allows(tempFile)).IsFalse();
    }

    [Test]
    public async Task FileSystemScope_Clear_RemovesAllPaths()
    {
        var scope = new FileSystemScope();
        scope.AddPath(Path.GetTempPath());
        scope.Clear();

        await Assert.That(scope.AllowedPaths.Count).IsEqualTo(0);
    }

    // === UrlScope ===

    [Test]
    public async Task UrlScope_AddPattern_AllowsMatchingUrl()
    {
        var scope = new UrlScope();
        scope.AddPattern("https://*.example.com");

        await Assert.That(scope.Allows("https://app.example.com")).IsTrue();
    }

    [Test]
    public async Task UrlScope_NonMatchingUrl_ReturnsFalse()
    {
        var scope = new UrlScope();
        scope.AddPattern("https://*.example.com");

        await Assert.That(scope.Allows("https://evil.com")).IsFalse();
    }

    [Test]
    public async Task UrlScope_EmptyUrl_ReturnsFalse()
    {
        var scope = new UrlScope();

        await Assert.That(scope.Allows("")).IsFalse();
        await Assert.That(scope.Allows(null!)).IsFalse();
    }

    [Test]
    public async Task UrlScope_EmptyScope_ReturnsFalse()
    {
        var scope = new UrlScope();

        await Assert.That(scope.Allows("https://example.com")).IsFalse();
    }
}
