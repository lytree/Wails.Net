using System.Net.Http.Headers;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Events;
using Wails.Net.Application.Transport;
using TUnit.Core;

namespace Wails.Net.Application.Tests.Transport;

/// <summary>
/// IPC handshake token 传输层校验测试（OPT-IPC-002）。
/// 覆盖：
/// <list type="bullet">
/// <item>HTTP fallback：缺少 token / 错误 token 请求被拒绝（403）。</item>
/// <item>HTTP fallback：正确 token 请求放行。</item>
/// <item>HTTP fallback：RequireHandshakeToken=false 时不强制校验。</item>
/// <item>WebSocket fallback：升级请求缺少 / 错误 token 被拒绝（403）。</item>
/// </list>
/// </summary>
[NotInParallel]
public sealed class IpcTransportTokenValidationTests
{
    private const string TestToken = "test-handshake-token-123";

    private static (HttpTransport transport, MessageProcessor processor, BindingManager bindings) CreateHttpTransport(
        bool requireToken = true)
    {
        var bindings = new BindingManager();
        var events = new EventProcessor();
        var processor = new MessageProcessor(bindings, events);
        var broadcaster = new WebSocketBroadcaster();
        var transport = new HttpTransport(processor, broadcaster)
        {
            HandshakeToken = TestToken,
            RequireHandshakeToken = requireToken,
        };
        return (transport, processor, bindings);
    }

    private static async Task<(int StatusCode, string Body)> PostMessageAsync(HttpTransport transport, string? tokenHeader)
    {
        using var client = new HttpClient();
        var request = new HttpRequestMessage(HttpMethod.Post, $"{transport.BaseUrl}{HttpTransport.MessageEndpoint}")
        {
            Content = new StringContent("""{"id":"1","type":"call","payload":{"method":"Nope"}}""" ),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        if (tokenHeader is not null)
        {
            request.Headers.Add(HttpTransport.HandshakeTokenHeader, tokenHeader);
        }

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        return ((int)response.StatusCode, body);
    }

    [Test]
    public async Task Http_MissingToken_Returns403()
    {
        var (transport, _, _) = CreateHttpTransport();
        await transport.StartAsync();
        try
        {
            var (status, _) = await PostMessageAsync(transport, tokenHeader: null);

            await Assert.That(status).IsEqualTo(403);
        }
        finally
        {
            await transport.StopAsync();
        }
    }

    [Test]
    public async Task Http_WrongToken_Returns403()
    {
        var (transport, _, _) = CreateHttpTransport();
        await transport.StartAsync();
        try
        {
            var (status, _) = await PostMessageAsync(transport, "wrong-token");

            await Assert.That(status).IsEqualTo(403);
        }
        finally
        {
            await transport.StopAsync();
        }
    }

    [Test]
    public async Task Http_CorrectToken_NotForbidden()
    {
        var (transport, _, _) = CreateHttpTransport();
        await transport.StartAsync();
        try
        {
            var (status, _) = await PostMessageAsync(transport, TestToken);

            // 正确 token 不应被 403 拒绝（消息本身无效则可能返回 400）
            await Assert.That(status).IsNotEqualTo(403);
        }
        finally
        {
            await transport.StopAsync();
        }
    }

    [Test]
    public async Task Http_TokenNotRequired_MissingTokenAllowed()
    {
        var (transport, _, _) = CreateHttpTransport(requireToken: false);
        await transport.StartAsync();
        try
        {
            var (status, _) = await PostMessageAsync(transport, tokenHeader: null);

            await Assert.That(status).IsNotEqualTo(403);
        }
        finally
        {
            await transport.StopAsync();
        }
    }

    [Test]
    public async Task WebSocket_MissingToken_Returns403()
    {
        var bindings = new BindingManager();
        var events = new EventProcessor();
        var processor = new MessageProcessor(bindings, events);
        var broadcaster = new WebSocketBroadcaster();
        var transport = new WebSocketTransport(processor, broadcaster)
        {
            HandshakeToken = TestToken,
            RequireHandshakeToken = true,
        };

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
        var bindings = new BindingManager();
        var events = new EventProcessor();
        var processor = new MessageProcessor(bindings, events);
        var broadcaster = new WebSocketBroadcaster();
        var transport = new WebSocketTransport(processor, broadcaster)
        {
            HandshakeToken = TestToken,
            RequireHandshakeToken = true,
        };

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
        var bindings = new BindingManager();
        var events = new EventProcessor();
        var processor = new MessageProcessor(bindings, events);
        var broadcaster = new WebSocketBroadcaster();
        var transport = new WebSocketTransport(processor, broadcaster)
        {
            HandshakeToken = TestToken,
            RequireHandshakeToken = true,
        };

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

    /// <summary>
    /// 发送带 Upgrade 头的 HTTP 请求探测 WebSocket 升级路径。
    /// 服务器拒绝（403）时直接返回状态码；接受时返回升级结果（101 或握手后的状态）。
    /// </summary>
    private static async Task<int> ProbeWebSocketUpgradeAsync(WebSocketTransport transport, string? tokenQuery)
    {
        using var client = new HttpClient();
        var url = $"{transport.BaseUrl}{WebSocketTransport.WebSocketPath}";
        if (tokenQuery is not null)
        {
            url += $"?{WebSocketTransport.HandshakeTokenQuery}={Uri.EscapeDataString(tokenQuery)}";
        }

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Upgrade", "websocket");
        request.Headers.TryAddWithoutValidation("Connection", "Upgrade");
        request.Headers.TryAddWithoutValidation("Sec-WebSocket-Version", "13");
        request.Headers.TryAddWithoutValidation("Sec-WebSocket-Key", Convert.ToBase64String(new byte[16]));

        try
        {
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            return (int)response.StatusCode;
        }
        catch (HttpRequestException)
        {
            // 升级成功时连接被接管，HttpClient 可能抛异常——视为服务器接受升级（非 403）
            return 101;
        }
    }
}
