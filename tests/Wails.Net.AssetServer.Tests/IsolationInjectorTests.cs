using System.IO;
using System.Text.RegularExpressions;
using TUnit.Assertions;
using TUnit.Core;
using Wails.Net.AssetServer.Security;

namespace Wails.Net.AssetServer.Tests;

/// <summary>
/// IsolationInjector 单元测试。
/// 覆盖 isolation iframe 注入与文件验证。
/// 对应 Tauri v2 的 Isolation Pattern。
/// </summary>
[NotInParallel]
public sealed class IsolationInjectorTests
{
    // === InjectIsolationIframe ===

    [Test]
    public async Task InjectIsolationIframe_AddsIframeToBody()
    {
        var html = """<html><body><p>content</p></body></html>""";
        var options = new IsolationOptions { Enabled = true };

        var result = IsolationInjector.InjectIsolationIframe(html, options);

        await Assert.That(result).Contains("<iframe");
        await Assert.That(result).Contains("</iframe>");
    }

    [Test]
    public async Task InjectIsolationIframe_PlacesIframeAsFirstChildOfBody()
    {
        var html = """<html><body><div id="content">content</div></body></html>""";
        var options = new IsolationOptions { Enabled = true };

        var result = IsolationInjector.InjectIsolationIframe(html, options);

        // iframe 应在 div 之前
        var iframeIndex = result.IndexOf("<iframe", StringComparison.OrdinalIgnoreCase);
        var divIndex = result.IndexOf("<div", StringComparison.OrdinalIgnoreCase);
        await Assert.That(iframeIndex).IsGreaterThan(-1);
        await Assert.That(iframeIndex).IsLessThan(divIndex);
    }

    [Test]
    public async Task InjectIsolationIframe_HasCorrectSandboxAttribute()
    {
        var html = """<html><body></body></html>""";
        var options = new IsolationOptions
        {
            Enabled = true,
            Sandbox = "allow-scripts allow-same-origin",
        };

        var result = IsolationInjector.InjectIsolationIframe(html, options);

        await Assert.That(result).Contains("sandbox=\"allow-scripts allow-same-origin\"");
    }

    [Test]
    public async Task InjectIsolationIframe_DefaultSandbox_IsAllowScripts()
    {
        var html = """<html><body></body></html>""";
        var options = new IsolationOptions { Enabled = true };

        var result = IsolationInjector.InjectIsolationIframe(html, options);

        await Assert.That(result).Contains("sandbox=\"allow-scripts\"");
    }

    [Test]
    public async Task InjectIsolationIframe_CustomSrc_UsesProvidedSrc()
    {
        var html = """<html><body></body></html>""";
        var options = new IsolationOptions
        {
            Enabled = true,
            IsolationSrc = "/custom/isolation.html",
        };

        var result = IsolationInjector.InjectIsolationIframe(html, options);

        await Assert.That(result).Contains("src=\"/custom/isolation.html\"");
    }

    [Test]
    public async Task InjectIsolationIframe_CustomFrameName_UsesProvidedName()
    {
        var html = """<html><body></body></html>""";
        var options = new IsolationOptions
        {
            Enabled = true,
            FrameName = "my-isolation-frame",
        };

        var result = IsolationInjector.InjectIsolationIframe(html, options);

        await Assert.That(result).Contains("name=\"my-isolation-frame\"");
    }

    [Test]
    public async Task InjectIsolationIframe_EmptyHtml_ReturnsEmpty()
    {
        var result = IsolationInjector.InjectIsolationIframe(string.Empty, new IsolationOptions());
        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task InjectIsolationIframe_NullHtml_ThrowsArgumentNullException()
    {
        await Assert.That(() => IsolationInjector.InjectIsolationIframe(null!, new IsolationOptions()))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task InjectIsolationIframe_NullOptions_ThrowsArgumentNullException()
    {
        await Assert.That(() => IsolationInjector.InjectIsolationIframe("<html></html>", null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task InjectIsolationIframe_NoBodyTag_ThrowsInvalidOperationException()
    {
        var html = """<html><head></head></html>""";

        await Assert.That(() => IsolationInjector.InjectIsolationIframe(html, new IsolationOptions()))
            .ThrowsExactly<InvalidOperationException>();
    }

    // === ValidateIsolationFiles ===

    [Test]
    public async Task ValidateIsolationFiles_FileExists_ReturnsTrue()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"wails_iso_exists_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(tempDir, "isolation"));
        await File.WriteAllTextAsync(Path.Combine(tempDir, "isolation", "index.html"), "<html></html>");

        try
        {
            var result = IsolationInjector.ValidateIsolationFiles(tempDir, new IsolationOptions());
            await Assert.That(result).IsTrue();
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* 忽略 */ }
        }
    }

    [Test]
    public async Task ValidateIsolationFiles_FileNotExists_ReturnsFalse()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"wails_iso_notexists_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var result = IsolationInjector.ValidateIsolationFiles(tempDir, new IsolationOptions());
            await Assert.That(result).IsFalse();
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* 忽略 */ }
        }
    }

    [Test]
    public async Task ValidateIsolationFiles_CustomDir_ChecksCorrectPath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"wails_iso_custom_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(tempDir, "isolated-js"));
        await File.WriteAllTextAsync(Path.Combine(tempDir, "isolated-js", "index.html"), "<html></html>");

        try
        {
            var options = new IsolationOptions { IsolationDir = "isolated-js" };
            var result = IsolationInjector.ValidateIsolationFiles(tempDir, options);
            await Assert.That(result).IsTrue();
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* 忽略 */ }
        }
    }

    [Test]
    public async Task ValidateIsolationFiles_EmptyIsolationDir_ReturnsFalse()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"wails_iso_empty_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var options = new IsolationOptions { IsolationDir = "" };
            var result = IsolationInjector.ValidateIsolationFiles(tempDir, options);
            await Assert.That(result).IsFalse();
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* 忽略 */ }
        }
    }
}
