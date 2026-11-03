using TUnit.Core;
using Wails.Net.Application.Security;

namespace Wails.Net.Application.Tests.Security;

/// <summary>
/// Server 模式请求安全闸门单元测试（P0 安全加固）。
/// 对齐 Wails v3 `v3.0.0` 的 <c>host_guard_server.go</c>（DNS rebinding 防护）与
/// <c>transport_http_server.go</c>（runtime 端点 POST + 同源 Origin 校验）。
/// </summary>
[NotInParallel]
public sealed class IpcRequestGuardTests
{
    // ---------- Host 校验（DNS rebinding 防护） ----------

    [Test]
    public async Task IsHostAllowed_LocalhostWithPort_ReturnsTrue()
    {
        var guard = new IpcRequestGuard();

        await Assert.That(guard.IsHostAllowed("localhost:34116")).IsTrue();
        await Assert.That(guard.IsHostAllowed("LOCALHOST:34116")).IsTrue();
        await Assert.That(guard.IsHostAllowed("localhost")).IsTrue();
    }

    [Test]
    public async Task IsHostAllowed_LoopbackIpLiterals_ReturnsTrue()
    {
        var guard = new IpcRequestGuard();

        await Assert.That(guard.IsHostAllowed("127.0.0.1:34116")).IsTrue();
        await Assert.That(guard.IsHostAllowed("[::1]:34116")).IsTrue();
    }

    [Test]
    public async Task IsHostAllowed_AnyIpLiteral_ReturnsTrue()
    {
        // 任意 IP 字面量都无法被 DNS 改写，与 Go serverHostAllowed 一致。
        var guard = new IpcRequestGuard();

        await Assert.That(guard.IsHostAllowed("192.168.1.10:34116")).IsTrue();
    }

    [Test]
    public async Task IsHostAllowed_EmptyHost_ReturnsTrue()
    {
        // 浏览器总发送 Host；仅 HTTP/1.0 客户端会省略（与 Go 一致放行）。
        var guard = new IpcRequestGuard();

        await Assert.That(guard.IsHostAllowed((string?)null)).IsTrue();
        await Assert.That(guard.IsHostAllowed(string.Empty)).IsTrue();
    }

    [Test]
    public async Task IsHostAllowed_ReboundHostName_ReturnsFalse()
    {
        var guard = new IpcRequestGuard();

        await Assert.That(guard.IsHostAllowed("evil.example:34116")).IsFalse();
        await Assert.That(guard.IsHostAllowed("localhost.evil.example:34116")).IsFalse();
        await Assert.That(guard.IsHostAllowed("localhost.:34116")).IsFalse();
    }

    [Test]
    public async Task IsHostAllowed_WhitelistedHostPattern_ReturnsTrue()
    {
        // 对应 Go：模式剥离 scheme 前缀后分别匹配带端口与不带端口的 Host。
        var guard = new IpcRequestGuard(["https://app.example.com", "*.internal.example"]);

        await Assert.That(guard.IsHostAllowed("app.example.com:34116")).IsTrue();
        await Assert.That(guard.IsHostAllowed("api.internal.example:34116")).IsTrue();
        await Assert.That(guard.IsHostAllowed("other.example.com:34116")).IsFalse();
    }

    [Test]
    public async Task IsHostAllowed_NonLoopbackBind_DisablesHostCheck()
    {
        // 绑定非 loopback 主机时视为有意网络部署，不做 Host 校验（与 Go 一致）。
        var guard = new IpcRequestGuard(bindHost: "0.0.0.0");

        await Assert.That(guard.IsHostCheckEnabled).IsFalse();
        await Assert.That(guard.IsHostAllowed("evil.example:34116")).IsTrue();
    }

    [Test]
    public async Task IsLoopbackBindHost_ClassifiesBindHosts()
    {
        await Assert.That(IpcRequestGuard.IsLoopbackBindHost("localhost")).IsTrue();
        await Assert.That(IpcRequestGuard.IsLoopbackBindHost("127.0.0.1")).IsTrue();
        await Assert.That(IpcRequestGuard.IsLoopbackBindHost("[::1]")).IsTrue();
        await Assert.That(IpcRequestGuard.IsLoopbackBindHost("example.com")).IsFalse();
        await Assert.That(IpcRequestGuard.IsLoopbackBindHost(null)).IsFalse();
    }

    // ---------- runtime 端点 Origin 校验 ----------

    [Test]
    public async Task IsRuntimeRequestAllowed_SameOrigin_ReturnsTrue()
    {
        var guard = new IpcRequestGuard();

        await Assert.That(guard.IsRuntimeRequestAllowed(
            "http://localhost:34116", secFetchSite: null, requestHost: "localhost:34116")).IsTrue();
    }

    [Test]
    public async Task IsRuntimeRequestAllowed_LocalhostOtherPort_ReturnsFalse()
    {
        // 加固点：不再无条件信任任意 localhost 源（与 Go 同源判定一致）。
        var guard = new IpcRequestGuard();

        await Assert.That(guard.IsRuntimeRequestAllowed(
            "http://localhost:9999", secFetchSite: null, requestHost: "localhost:34116")).IsFalse();
    }

    [Test]
    public async Task IsRuntimeRequestAllowed_CrossOrigin_ReturnsFalse()
    {
        var guard = new IpcRequestGuard();

        await Assert.That(guard.IsRuntimeRequestAllowed(
            "https://evil.com", secFetchSite: null, requestHost: "localhost:34116")).IsFalse();
        await Assert.That(guard.IsRuntimeRequestAllowed(
            "null", secFetchSite: null, requestHost: "localhost:34116")).IsFalse();
        await Assert.That(guard.IsRuntimeRequestAllowed(
            "not a url", secFetchSite: null, requestHost: "localhost:34116")).IsFalse();
    }

    [Test]
    public async Task IsRuntimeRequestAllowed_WhitelistedOrigin_ReturnsTrue()
    {
        var guard = new IpcRequestGuard(["https://*.example.com"]);

        await Assert.That(guard.IsRuntimeRequestAllowed(
            "https://app.example.com", secFetchSite: null, requestHost: "localhost:34116")).IsTrue();
        await Assert.That(guard.IsRuntimeRequestAllowed(
            "http://app.example.com", secFetchSite: null, requestHost: "localhost:34116")).IsFalse();
    }

    [Test]
    public async Task IsRuntimeRequestAllowed_NoOrigin_UsesSecFetchSite()
    {
        var guard = new IpcRequestGuard();

        await Assert.That(guard.IsRuntimeRequestAllowed(null, null, "localhost:34116")).IsTrue();
        await Assert.That(guard.IsRuntimeRequestAllowed(null, "same-origin", "localhost:34116")).IsTrue();
        await Assert.That(guard.IsRuntimeRequestAllowed(null, "none", "localhost:34116")).IsTrue();
        await Assert.That(guard.IsRuntimeRequestAllowed(null, "cross-site", "localhost:34116")).IsFalse();
        await Assert.That(guard.IsRuntimeRequestAllowed(null, "same-site", "localhost:34116")).IsFalse();
    }

    [Test]
    public async Task IsRuntimeRequestAllowed_AllowAllOrigins_SkipsOriginCheck()
    {
        var guard = new IpcRequestGuard
        {
            AllowAllOrigins = true,
        };

        await Assert.That(guard.IsRuntimeRequestAllowed(
            "https://evil.com", "cross-site", "localhost:34116")).IsTrue();
    }

    [Test]
    public async Task IsWebSocketOriginAllowed_MatchesRuntimePolicy()
    {
        var guard = new IpcRequestGuard(["http://localhost:5173"]);

        await Assert.That(guard.IsWebSocketOriginAllowed(
            "http://localhost:34116", "localhost:34116")).IsTrue();
        await Assert.That(guard.IsWebSocketOriginAllowed(
            "http://localhost:5173", "localhost:34116")).IsTrue();
        await Assert.That(guard.IsWebSocketOriginAllowed(
            "https://evil.com", "localhost:34116")).IsFalse();
        await Assert.That(guard.IsWebSocketOriginAllowed(null, "localhost:34116")).IsTrue();
    }
}
