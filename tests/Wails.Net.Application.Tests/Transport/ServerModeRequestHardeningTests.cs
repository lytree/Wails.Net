using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Events;
using Wails.Net.Application.Security;
using Wails.Net.Application.Transport;
using TUnit.Core;

namespace Wails.Net.Application.Tests.Transport;

/// <summary>
/// Server 模式请求安全加固端到端测试（P0）。
/// 对齐 Wails v3 `v3.0.0`：
/// <list type="bullet">
/// <item><c>transport_http_server.go</c> <c>checkRuntimeRequest</c>：消息端点仅 POST（否则 405）、
/// Origin 同源 / 白名单校验（否则 403）、无 Origin 时按 <c>Sec-Fetch-Site</c> 判定；</item>
/// <item><c>host_guard_server.go</c>：loopback 绑定拒绝可疑 Host 头（DNS rebinding 防护）；</item>
/// <item><c>websocket_server.go</c>：WebSocket 升级使用同一 Origin 策略。</item>
/// </list>
/// </summary>
[NotInParallel]
public sealed class ServerModeRequestHardeningTests
{
    private const string AllowedOrigin = "http://localhost:5173";

    private static (HttpTransport Transport, MessageProcessor Processor) CreateHttpTransport()
    {
        var bindings = new BindingManager();
        var events = new EventProcessor();
        var processor = new MessageProcessor(bindings, events);
        var broadcaster = new WebSocketBroadcaster();
        var transport = new HttpTransport(processor, broadcaster)
        {
            RequireHandshakeToken = false,
            RequestGuard = new IpcRequestGuard([AllowedOrigin]),
        };
        return (transport, processor);
    }

    private static (WebSocketTransport Transport, MessageProcessor Processor) CreateWebSocketTransport()
    {
        var bindings = new BindingManager();
        var events = new EventProcessor();
        var processor = new MessageProcessor(bindings, events);
        var broadcaster = new WebSocketBroadcaster();
        var transport = new WebSocketTransport(processor, broadcaster)
        {
            RequireHandshakeToken = false,
            RequestGuard = new IpcRequestGuard([AllowedOrigin]),
        };
        return (transport, processor);
    }

    private static async Task<(int StatusCode, string Body)> PostMessageAsync(
        HttpTransport transport,
        string? origin = null,
        string? secFetchSite = null)
    {
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"{transport.BaseUrl}{HttpTransport.MessageEndpoint}")
        {
            Content = new StringContent("""{"id":"1","type":"call","payload":{"name":"Nope.DoesNotExist"}}"""),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        if (origin is not null)
        {
            request.Headers.TryAddWithoutValidation("Origin", origin);
        }

        if (secFetchSite is not null)
        {
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", secFetchSite);
        }

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        return ((int)response.StatusCode, body);
    }

    // ---------- HTTP runtime 端点 ----------

    [Test]
    public async Task Http_NonPostOnMessageEndpoint_Returns405WithAllowPost()
    {
        var (transport, _) = CreateHttpTransport();
        await transport.StartAsync();
        try
        {
            using var client = new HttpClient();
            using var response = await client.GetAsync($"{transport.BaseUrl}{HttpTransport.MessageEndpoint}");

            await Assert.That((int)response.StatusCode).IsEqualTo(405);
            await Assert.That(HasAllowHeader(response, "POST")).IsTrue();
        }
        finally
        {
            await transport.StopAsync();
        }
    }

    [Test]
    public async Task Http_CrossOriginPost_Returns403()
    {
        var (transport, _) = CreateHttpTransport();
        await transport.StartAsync();
        try
        {
            var (status, _) = await PostMessageAsync(transport, "https://evil.com");

            await Assert.That(status).IsEqualTo(403);
        }
        finally
        {
            await transport.StopAsync();
        }
    }

    [Test]
    public async Task Http_LocalhostOtherPortPost_Returns403()
    {
        // 加固点：任意 localhost 源不再无条件信任，必须是同源或命中白名单。
        var (transport, _) = CreateHttpTransport();
        await transport.StartAsync();
        try
        {
            var (status, _) = await PostMessageAsync(transport, "http://localhost:9999");

            await Assert.That(status).IsEqualTo(403);
        }
        finally
        {
            await transport.StopAsync();
        }
    }

    [Test]
    public async Task Http_SameOriginPost_Returns200()
    {
        var (transport, _) = CreateHttpTransport();
        await transport.StartAsync();
        try
        {
            var (status, _) = await PostMessageAsync(transport, transport.BaseUrl);

            await Assert.That(status).IsEqualTo(200);
        }
        finally
        {
            await transport.StopAsync();
        }
    }

    [Test]
    public async Task Http_WhitelistedOriginPost_Returns200()
    {
        var (transport, _) = CreateHttpTransport();
        await transport.StartAsync();
        try
        {
            var (status, _) = await PostMessageAsync(transport, AllowedOrigin);

            await Assert.That(status).IsEqualTo(200);
        }
        finally
        {
            await transport.StopAsync();
        }
    }

    [Test]
    public async Task Http_NoOriginButCrossSiteFetch_Returns403()
    {
        var (transport, _) = CreateHttpTransport();
        await transport.StartAsync();
        try
        {
            var (status, _) = await PostMessageAsync(transport, secFetchSite: "cross-site");

            await Assert.That(status).IsEqualTo(403);
        }
        finally
        {
            await transport.StopAsync();
        }
    }

    [Test]
    public async Task Http_ForeignHostHeader_Rejected()
    {
        var (transport, _) = CreateHttpTransport();
        await transport.StartAsync();
        try
        {
            var (status, statusLine) = await SendRawStatusAsync(
                transport.Port,
                string.Concat(
                    "POST ", HttpTransport.MessageEndpoint, " HTTP/1.1\r\n",
                    "Host: evil.example\r\n",
                    "Content-Type: application/json\r\n",
                    "Content-Length: 2\r\n",
                    "Connection: close\r\n",
                    "\r\n",
                    "{}"));

            // Windows HTTP.SYS 在 Host 与监听前缀不匹配时可能在进入托管层前返回 400；
            // Linux 的托管 HttpListener 则到达闸门并返回 403；也有平台直接断开连接
            // （读不到状态行，status=0）。三者都表示请求未被服务，属于拒绝语义。
            AssertRejected(status, statusLine);
        }
        finally
        {
            await transport.StopAsync();
        }
    }

    // ---------- WebSocket 端点 ----------

    [Test]
    public async Task WebSocket_CrossOriginUpgrade_Returns403()
    {
        var (transport, _) = CreateWebSocketTransport();
        await transport.StartAsync();
        try
        {
            var status = await ProbeWebSocketUpgradeAsync(transport, origin: "https://evil.com");

            await Assert.That(status).IsEqualTo(403);
        }
        finally
        {
            await transport.StopAsync();
        }
    }

    [Test]
    public async Task WebSocket_LocalhostOtherPortUpgrade_Returns403()
    {
        var (transport, _) = CreateWebSocketTransport();
        await transport.StartAsync();
        try
        {
            var status = await ProbeWebSocketUpgradeAsync(transport, origin: "http://localhost:9999");

            await Assert.That(status).IsEqualTo(403);
        }
        finally
        {
            await transport.StopAsync();
        }
    }

    [Test]
    public async Task WebSocket_SameOriginUpgrade_NotForbidden()
    {
        var (transport, _) = CreateWebSocketTransport();
        await transport.StartAsync();
        try
        {
            var status = await ProbeWebSocketUpgradeAsync(transport, origin: transport.BaseUrl);

            await Assert.That(status).IsNotEqualTo(403);
        }
        finally
        {
            await transport.StopAsync();
        }
    }

    [Test]
    public async Task WebSocket_WhitelistedOriginUpgrade_NotForbidden()
    {
        var (transport, _) = CreateWebSocketTransport();
        await transport.StartAsync();
        try
        {
            var status = await ProbeWebSocketUpgradeAsync(transport, origin: AllowedOrigin);

            await Assert.That(status).IsNotEqualTo(403);
        }
        finally
        {
            await transport.StopAsync();
        }
    }

    [Test]
    public async Task WebSocket_NonUpgradeRequest_Returns405WithAllowGet()
    {
        var (transport, _) = CreateWebSocketTransport();
        await transport.StartAsync();
        try
        {
            using var client = new HttpClient();
            using var response = await client.GetAsync(
                $"{transport.BaseUrl}{WebSocketTransport.WebSocketPath}");

            await Assert.That((int)response.StatusCode).IsEqualTo(405);
            await Assert.That(HasAllowHeader(response, "GET")).IsTrue();
        }
        finally
        {
            await transport.StopAsync();
        }
    }

    [Test]
    public async Task WebSocket_ForeignHostHeader_Rejected()
    {
        var (transport, _) = CreateWebSocketTransport();
        await transport.StartAsync();
        try
        {
            var (status, statusLine) = await SendRawStatusAsync(
                transport.Port,
                string.Concat(
                    "GET ", WebSocketTransport.WebSocketPath, " HTTP/1.1\r\n",
                    "Host: evil.example\r\n",
                    "Upgrade: websocket\r\n",
                    "Connection: Upgrade\r\n",
                    "Sec-WebSocket-Version: 13\r\n",
                    "Sec-WebSocket-Key: ", Convert.ToBase64String(new byte[16]), "\r\n",
                    "\r\n"));

            // 同上：400（HTTP.SYS 前缀校验）、403（闸门）、或直接断开（读不到状态行）
            // 都属于请求被拒绝。
            AssertRejected(status, statusLine);
        }
        finally
        {
            await transport.StopAsync();
        }
    }

    // ---------- helpers ----------

    private static async Task<int> ProbeWebSocketUpgradeAsync(
        WebSocketTransport transport,
        string? origin = null)
    {
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"{transport.BaseUrl}{WebSocketTransport.WebSocketPath}");
        request.Headers.TryAddWithoutValidation("Upgrade", "websocket");
        request.Headers.TryAddWithoutValidation("Connection", "Upgrade");
        request.Headers.TryAddWithoutValidation("Sec-WebSocket-Version", "13");
        request.Headers.TryAddWithoutValidation("Sec-WebSocket-Key", Convert.ToBase64String(new byte[16]));

        if (origin is not null)
        {
            request.Headers.TryAddWithoutValidation("Origin", origin);
        }

        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            return (int)response.StatusCode;
        }
        catch (HttpRequestException)
        {
            // 升级成功时连接被接管，HttpClient 可能抛异常——视为已通过来源校验。
            return 101;
        }
    }

    /// <summary>
    /// 判断响应是否携带指定方法值的 <c>Allow</c> 头（HttpClient 可能将其归入内容头或响应头）。
    /// </summary>
    private static bool HasAllowHeader(HttpResponseMessage response, string method)
    {
        if (response.Content.Headers.TryGetValues("Allow", out var contentValues) &&
            contentValues.Contains(method, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return response.Headers.TryGetValues("Allow", out var values) &&
               values.Contains(method, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 断言请求被拒绝：400（HTTP.SYS 的 Host/前缀不匹配）、403（请求闸门），
    /// 或服务端直接断开而读不到状态行（status=0）都算拒绝。
    /// 失败时把实际状态行写进消息，否则跨平台差异很难定位（此前只有一句
    /// "Expected to be true"，看不出服务端到底回了什么）。
    /// </summary>
    private static void AssertRejected(int status, string statusLine)
    {
        if (status is 0 or 400 or 403)
        {
            return;
        }

        Assert.Fail($"期望请求被拒绝（400/403 或连接被断开），实际状态 {status}；状态行: '{statusLine}'");
    }

    private static async Task<(int Status, string StatusLine)> SendRawStatusAsync(int port, string rawRequest)
    {
        using var tcp = new TcpClient();
        // 与传输层的监听前缀保持一致：HttpTransport 绑定的是 http://localhost:{port}/
        // （见 BaseUrl），而 localhost 在部分 Linux 环境下可能只落到 ::1。此处写死
        // 127.0.0.1 会在那种环境里被拒绝连接；用 localhost 让 TcpClient 依次尝试
        // 解析结果（IPv6/IPv4），与走 BaseUrl 的 HttpClient 用例行为一致。
        await tcp.ConnectAsync("localhost", port);
        using var stream = tcp.GetStream();

        var bytes = Encoding.ASCII.GetBytes(rawRequest);
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var statusLine = await reader.ReadLineAsync(timeoutCts.Token) ?? string.Empty;
        var parts = statusLine.Split(' ');
        var status = parts.Length >= 2 && int.TryParse(parts[1], out var code) ? code : 0;
        return (status, statusLine);
    }
}
