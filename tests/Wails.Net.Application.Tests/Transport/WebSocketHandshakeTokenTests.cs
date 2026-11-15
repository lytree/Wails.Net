using Wails.Net.Application.Bindings;
using Wails.Net.Application.Events;
using Wails.Net.Application.Transport;
using TUnit.Core;

namespace Wails.Net.Application.Tests.Transport;

/// <summary>
/// WebSocket 传输层 IPC handshake token 校验测试（OPT-IPC-002）。
/// 覆盖：
/// <list type="bullet">
/// <item>升级请求缺少 token 被拒绝（403）。</item>
/// <item>升级请求携带错误 token 被拒绝（403）。</item>
/// <item>升级请求携带正确 token 放行。</item>
/// <item><c>x-wails-token</c> 请求头回退可用（对应 <see cref="IpcProtocol.HandshakeTokenHeader"/>）。</item>
/// </list>
/// <para>
/// RH-IPC-04（2026-11-15）：独立 HTTP 监听传输 <c>HttpTransport</c> 已删除，
/// 原文件中 4 个针对 <c>POST /wails/message</c> 的用例随该类一并移除；
/// 头名常量收敛为 <see cref="IpcProtocol.HandshakeTokenHeader"/>。
/// </para>
/// </summary>
[NotInParallel]
public sealed class WebSocketHandshakeTokenTests
{
    private const string TestToken = "test-handshake-token-123";

    private static WebSocketTransport CreateTransport()
    {
        var bindings = new BindingManager();
        var events = new EventProcessor();
        var processor = new MessageProcessor(bindings, events);
        var broadcaster = new WebSocketBroadcaster();
        return new WebSocketTransport(processor, broadcaster)
        {
            HandshakeToken = TestToken,
            RequireHandshakeToken = true,
        };
    }

    [Test]
    public async Task WebSocket_MissingToken_Returns403()
    {
        var transport = CreateTransport();
        await transport.StartAsync();
        try
        {
            var status = await ProbeWebSocketUpgradeAsync(transport, tokenQuery: null);

            await Assert.That(status).IsEqualTo(403);
        }
        finally
        {
            await transport.StopAsync();
        }
    }

    [Test]
    public async Task WebSocket_WrongToken_Returns403()
    {
        var transport = CreateTransport();
        await transport.StartAsync();
        try
        {
            var status = await ProbeWebSocketUpgradeAsync(transport, "wrong-token");

            await Assert.That(status).IsEqualTo(403);
        }
        finally
        {
            await transport.StopAsync();
        }
    }

    [Test]
    public async Task WebSocket_CorrectToken_Not403()
    {
        var transport = CreateTransport();
        await transport.StartAsync();
        try
        {
            var status = await ProbeWebSocketUpgradeAsync(transport, TestToken);

            await Assert.That(status).IsNotEqualTo(403);
        }
        finally
        {
            await transport.StopAsync();
        }
    }

    [Test]
    public async Task WebSocket_TokenViaHeader_Not403()
    {
        // WebSocket 浏览器 API 无法自定义请求头，但非浏览器客户端（测试 / 原生代理）
        // 可以走 x-wails-token 头回退路径。
        var transport = CreateTransport();
        await transport.StartAsync();
        try
        {
            var status = await ProbeWebSocketUpgradeAsync(transport, tokenQuery: null, tokenHeader: TestToken);

            await Assert.That(status).IsNotEqualTo(403);
        }
        finally
        {
            await transport.StopAsync();
        }
    }

    /// <summary>
    /// 发起一次 WebSocket 升级探测，返回服务端状态码（升级成功时 HttpClient 抛
    /// <see cref="HttpRequestException"/>，按 101 处理）。
    /// </summary>
    private static async Task<int> ProbeWebSocketUpgradeAsync(
        WebSocketTransport transport,
        string? tokenQuery,
        string? tokenHeader = null)
    {
        using var client = new HttpClient();
        var query = string.IsNullOrEmpty(tokenQuery)
            ? string.Empty
            : $"?{WebSocketTransport.HandshakeTokenQuery}={Uri.EscapeDataString(tokenQuery)}";
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"{transport.BaseUrl}{WebSocketTransport.WebSocketPath}{query}");
        request.Headers.TryAddWithoutValidation("Upgrade", "websocket");
        request.Headers.TryAddWithoutValidation("Connection", "Upgrade");
        request.Headers.TryAddWithoutValidation("Sec-WebSocket-Version", "13");
        request.Headers.TryAddWithoutValidation("Sec-WebSocket-Key", Convert.ToBase64String(new byte[16]));
        request.Headers.TryAddWithoutValidation("Origin", transport.BaseUrl);
        if (tokenHeader is not null)
        {
            request.Headers.TryAddWithoutValidation(IpcProtocol.HandshakeTokenHeader, tokenHeader);
        }

        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            return (int)response.StatusCode;
        }
        catch (HttpRequestException)
        {
            // 升级成功时连接被接管，HttpClient 可能抛异常——视为已通过 token 校验。
            return 101;
        }
    }
}
