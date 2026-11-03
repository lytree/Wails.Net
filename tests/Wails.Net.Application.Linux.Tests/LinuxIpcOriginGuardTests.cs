using TUnit.Core;
using Wails.Net.Application.Platform;

namespace Wails.Net.Application.Linux.Tests;

/// <summary>
/// <see cref="LinuxIpcOriginGuard"/> 的单元测试（TUnit），覆盖 Linux 跨源子帧
/// <c>wails:</c> 消息来源校验（§11.2 第 3 项 / GHSA-2p5f-2gx8-mh8m 缓解）。
/// 全部为纯逻辑断言，不触发任何 GTK/WebKit 原生调用，可在任意平台执行。
/// </summary>
[NotInParallel]
public sealed class LinuxIpcOriginGuardTests
{
    /// <summary>
    /// 构造大小写不敏感的请求头字典。
    /// </summary>
    private static IReadOnlyDictionary<string, string> Headers(params (string Key, string Value)[] items)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in items)
        {
            headers[key] = value;
        }

        return headers;
    }

    [Test]
    public async Task IsIpcRequestAllowed_GetAssetRequest_Allowed()
    {
        // 静态资源请求不属于 IPC，永不拦截。
        var allowed = LinuxIpcOriginGuard.IsIpcRequestAllowed(
            "GET", "/index.html", Headers(("Origin", "https://evil.example")), "wails://localhost/");

        await Assert.That(allowed).IsTrue();
    }

    [Test]
    public async Task IsIpcRequestAllowed_PostNonIpcPath_Allowed()
    {
        // POST 但路径不是 /wails/*，不属于 IPC。
        var allowed = LinuxIpcOriginGuard.IsIpcRequestAllowed(
            "POST", "/api/submit", Headers(("Origin", "https://evil.example")), "wails://localhost/");

        await Assert.That(allowed).IsTrue();
    }

    [Test]
    public async Task IsIpcRequestAllowed_IpcWithoutOriginOrFetchMetadata_AllowedForCompatibility()
    {
        // 旧版 WebKitGTK 不透传 Origin/Sec-Fetch-Site：无法判定来源，放行以保持兼容。
        var allowed = LinuxIpcOriginGuard.IsIpcRequestAllowed(
            "POST", "/wails/message", headers: null, "wails://localhost/");

        await Assert.That(allowed).IsTrue();
    }

    [Test]
    public async Task IsIpcRequestAllowed_SameWailsOrigin_Allowed()
    {
        var allowed = LinuxIpcOriginGuard.IsIpcRequestAllowed(
            "POST",
            "/wails/message",
            Headers(("Origin", "wails://localhost")),
            "wails://localhost/index.html");

        await Assert.That(allowed).IsTrue();
    }

    [Test]
    public async Task IsIpcRequestAllowed_SchemeAndHostComparisonIsCaseInsensitive()
    {
        var allowed = LinuxIpcOriginGuard.IsIpcRequestAllowed(
            "POST",
            "/wails/message",
            Headers(("Origin", "WAILS://LocalHost")),
            "wails://localhost/index.html");

        await Assert.That(allowed).IsTrue();
    }

    [Test]
    public async Task IsIpcRequestAllowed_CrossOrigin_Denied()
    {
        var allowed = LinuxIpcOriginGuard.IsIpcRequestAllowed(
            "POST",
            "/wails/message",
            Headers(("Origin", "https://evil.example")),
            "wails://localhost/");

        await Assert.That(allowed).IsFalse();
    }

    [Test]
    public async Task IsIpcRequestAllowed_OpaqueOrigin_WailsWindow_Tolerated()
    {
        // WebKitGTK 可能把 wails:// 页面的 Origin 序列化为字面量 "null"，
        // 此时必须容忍，否则应用自身 IPC 会被拒绝（由脚本守卫兜底）。
        var allowed = LinuxIpcOriginGuard.IsIpcRequestAllowed(
            "POST",
            "/wails/message",
            Headers(("Origin", "null")),
            "wails://localhost/");

        await Assert.That(allowed).IsTrue();
    }

    [Test]
    public async Task IsIpcRequestAllowed_OpaqueOrigin_HttpWindow_Denied()
    {
        // 非自定义协议页面下，不透明源（sandbox / data: 子帧）无法与页面同源。
        var allowed = LinuxIpcOriginGuard.IsIpcRequestAllowed(
            "POST",
            "/wails/message",
            Headers(("Origin", "null")),
            "http://localhost:5173/");

        await Assert.That(allowed).IsFalse();
    }

    [Test]
    public async Task IsIpcRequestAllowed_OpaqueOrigin_CrossSiteFetchMetadata_Denied()
    {
        // Sec-Fetch-Site 明确跨源时，即使窗口为 wails 页面也不容忍 "null" 源。
        var allowed = LinuxIpcOriginGuard.IsIpcRequestAllowed(
            "POST",
            "/wails/message",
            Headers(("Origin", "null"), ("Sec-Fetch-Site", "cross-site")),
            "wails://localhost/");

        await Assert.That(allowed).IsFalse();
    }

    [Test]
    public async Task IsIpcRequestAllowed_OpaqueOrigin_NoWindowUrl_Denied()
    {
        // 窗口尚未导航（无 URL）时无法判断协议，保守拒绝不透明源。
        var allowed = LinuxIpcOriginGuard.IsIpcRequestAllowed(
            "POST",
            "/wails/message",
            Headers(("Origin", "null")),
            null);

        await Assert.That(allowed).IsFalse();
    }

    [Test]
    public async Task IsIpcRequestAllowed_CrossSiteFetchMetadata_Denied()
    {
        var allowed = LinuxIpcOriginGuard.IsIpcRequestAllowed(
            "POST",
            "/wails/message",
            Headers(("Sec-Fetch-Site", "cross-site")),
            "wails://localhost/");

        await Assert.That(allowed).IsFalse();
    }

    [Test]
    public async Task IsIpcRequestAllowed_SameOriginFetchMetadata_Allowed()
    {
        var allowed = LinuxIpcOriginGuard.IsIpcRequestAllowed(
            "POST",
            "/wails/message",
            Headers(("Sec-Fetch-Site", "same-origin")),
            "wails://localhost/");

        await Assert.That(allowed).IsTrue();
    }

    [Test]
    public async Task IsIpcRequestAllowed_LowercaseHeaderNames_StillValidated()
    {
        // 请求头名大小写不敏感：小写 origin 也必须走同源校验并被拒绝。
        var allowed = LinuxIpcOriginGuard.IsIpcRequestAllowed(
            "POST",
            "/wails/message",
            Headers(("origin", "https://evil.example")),
            "wails://localhost/");

        await Assert.That(allowed).IsFalse();
    }

    [Test]
    public async Task IsIpcRequestAllowed_EmptyWindowUrl_AllowedForCompatibility()
    {
        // 窗口尚未导航（无 URL）时无法比较同源，放行以保持兼容。
        var allowed = LinuxIpcOriginGuard.IsIpcRequestAllowed(
            "POST",
            "/wails/message",
            Headers(("Origin", "wails://localhost")),
            windowUrl: null);

        await Assert.That(allowed).IsTrue();
    }

    [Test]
    public async Task IsIpcRequestAllowed_InvalidOriginValue_Denied()
    {
        var allowed = LinuxIpcOriginGuard.IsIpcRequestAllowed(
            "POST",
            "/wails/message",
            Headers(("Origin", "not-a-valid-origin")),
            "wails://localhost/");

        await Assert.That(allowed).IsFalse();
    }

    [Test]
    public async Task IsIpcRequestAllowed_DevServerSameOrigin_Allowed()
    {
        var allowed = LinuxIpcOriginGuard.IsIpcRequestAllowed(
            "POST",
            "/wails/message",
            Headers(("Origin", "http://localhost:5173")),
            "http://localhost:5173/index.html");

        await Assert.That(allowed).IsTrue();
    }

    [Test]
    public async Task IsIpcRequestAllowed_DevServerDifferentPort_Denied()
    {
        var allowed = LinuxIpcOriginGuard.IsIpcRequestAllowed(
            "POST",
            "/wails/message",
            Headers(("Origin", "http://localhost:5174")),
            "http://localhost:5173/index.html");

        await Assert.That(allowed).IsFalse();
    }

    [Test]
    public async Task TryNormalizeOrigin_StripsPathQueryAndFragment()
    {
        var success = LinuxIpcOriginGuard.TryNormalizeOrigin(
            "wails://localhost/index.html?t=1#top", out var normalized);

        await Assert.That(success).IsTrue();
        await Assert.That(normalized).IsEqualTo("wails://localhost");
    }

    [Test]
    public async Task TryNormalizeOrigin_StripsHttpDefaultPort()
    {
        var success = LinuxIpcOriginGuard.TryNormalizeOrigin("http://localhost:80", out var fromDefaultPort);
        var successPlain = LinuxIpcOriginGuard.TryNormalizeOrigin("http://localhost", out var plain);

        await Assert.That(success).IsTrue();
        await Assert.That(successPlain).IsTrue();
        await Assert.That(fromDefaultPort).IsEqualTo(plain);
    }

    [Test]
    public async Task TryNormalizeOrigin_StripsUserInfo()
    {
        var success = LinuxIpcOriginGuard.TryNormalizeOrigin("https://user@app.local", out var normalized);

        await Assert.That(success).IsTrue();
        await Assert.That(normalized).IsEqualTo("https://app.local");
    }

    [Test]
    public async Task TryNormalizeOrigin_InvalidValues_ReturnFalse()
    {
        await Assert.That(LinuxIpcOriginGuard.TryNormalizeOrigin(null, out _)).IsFalse();
        await Assert.That(LinuxIpcOriginGuard.TryNormalizeOrigin("  ", out _)).IsFalse();
        await Assert.That(LinuxIpcOriginGuard.TryNormalizeOrigin("localhost", out _)).IsFalse();
        await Assert.That(LinuxIpcOriginGuard.TryNormalizeOrigin("wails://", out _)).IsFalse();
    }
}
