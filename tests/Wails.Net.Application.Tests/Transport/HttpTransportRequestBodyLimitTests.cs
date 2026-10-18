using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Events;
using Wails.Net.Application.Transport;
using TUnit.Core;

namespace Wails.Net.Application.Tests.Transport;

/// <summary>
/// IPC 请求体上限（P1-8，64 MiB → HTTP 413）测试。
/// 对应 Wails v3 beta.28（beta.19 #6091）：transport_http.go 中
/// <c>r.Body = http.MaxBytesReader(rw, r.Body, int64(maxRuntimeBodyBytes))</c>。
/// <para>
/// 为避免在测试中真实发送 64 MiB 实体，端到端 413 用例仅声明
/// <c>Content-Length = 上限 + 1</c> 且不发送实体（验证 Content-Length 预判分支）；
/// 边界（等于 / 超过上限）通过 <see cref="HttpTransport.ReadBodyWithLimitAsync"/> 以
/// 小上限进行单测，并对上限常量本身做断言。
/// </para>
/// </summary>
[NotInParallel]
public sealed class HttpTransportRequestBodyLimitTests
{
    private static (HttpTransport transport, MessageProcessor processor) CreateTransport()
    {
        var bindings = new BindingManager();
        var events = new EventProcessor();
        var processor = new MessageProcessor(bindings, events);
        var broadcaster = new WebSocketBroadcaster();
        var transport = new HttpTransport(processor, broadcaster);
        return (transport, processor);
    }

    /// <summary>
    /// 上限常量必须为 64 MiB（与 Go 的 maxRuntimeBodyBytes = maxAssembledBytes 一致）。
    /// </summary>
    [Test]
    public async Task MaxRuntimeBodyBytes_Equals64MiB()
    {
        var limit = HttpTransport.MaxRuntimeBodyBytes;

        await Assert.That(limit).IsEqualTo(64L * 1024 * 1024);
    }

    /// <summary>
    /// 读取字节数超过上限时返回 null（触发 413）。
    /// </summary>
    [Test]
    public async Task ReadBodyWithLimitAsync_BodyExceedsLimit_ReturnsNull()
    {
        using var stream = new MemoryStream(new byte[1025]);

        var result = await HttpTransport.ReadBodyWithLimitAsync(stream, 1024, CancellationToken.None);

        await Assert.That(result).IsNull();
    }

    /// <summary>
    /// 读取字节数等于上限时全部放行（边界，等于上限应允许）。
    /// </summary>
    [Test]
    public async Task ReadBodyWithLimitAsync_BodyEqualsLimit_ReturnsAllBytes()
    {
        using var stream = new MemoryStream(new byte[1024]);

        var result = await HttpTransport.ReadBodyWithLimitAsync(stream, 1024, CancellationToken.None);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Length).IsEqualTo(1024);
    }

    /// <summary>
    /// Content-Length 超过上限的请求返回 HTTP 413，且响应体文本对齐 Go。
    /// 仅声明长度头、不发送 64 MiB 实体，以验证 Content-Length 预判分支。
    /// </summary>
    [Test]
    public async Task Http_ContentLengthExceedsLimit_Returns413WithGoAlignedMessage()
    {
        var (transport, _) = CreateTransport();
        await transport.StartAsync();
        try
        {
            var (status, body) = await SendRawPostWithDeclaredLengthAsync(
                transport.Port,
                HttpTransport.MessageEndpoint,
                HttpTransport.MaxRuntimeBodyBytes + 1);

            await Assert.That(status).IsEqualTo(413);
            await Assert.That(body).Contains("runtime request body exceeds 64 MiB");
        }
        finally
        {
            await transport.StopAsync();
        }
    }

    /// <summary>
    /// 未超限的正常 IPC 请求仍按原路径成功（避免误伤）。
    /// </summary>
    [Test]
    public async Task Http_NormalPayload_NotRejected()
    {
        var (transport, _) = CreateTransport();
        await transport.StartAsync();
        try
        {
            using var client = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{transport.BaseUrl}{HttpTransport.MessageEndpoint}")
            {
                Content = new StringContent("""{"id":"1","type":"call","payload":{"name":"Nope.DoesNotExist"}}"""),
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            using var response = await client.SendAsync(request);

            await Assert.That((int)response.StatusCode).IsEqualTo(200);
        }
        finally
        {
            await transport.StopAsync();
        }
    }

    /// <summary>
    /// 发送声明了指定 Content-Length 但（默认）不发送实体的原始 HTTP 请求，
    /// 读取服务端响应状态码与正文。
    /// </summary>
    private static async Task<(int StatusCode, string Body)> SendRawPostWithDeclaredLengthAsync(
        int port,
        string path,
        long declaredContentLength)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync("127.0.0.1", port);
        using var stream = tcp.GetStream();

        var header = string.Concat(
            "POST ", path, " HTTP/1.1\r\n",
            "Host: localhost:", port.ToString(), "\r\n",
            "Content-Type: application/json\r\n",
            "Content-Length: ", declaredContentLength.ToString(), "\r\n",
            "Connection: close\r\n",
            "\r\n");
        var headerBytes = Encoding.ASCII.GetBytes(header);
        await stream.WriteAsync(headerBytes, 0, headerBytes.Length);
        await stream.FlushAsync();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var statusLine = await reader.ReadLineAsync(timeoutCts.Token) ?? string.Empty;
        var parts = statusLine.Split(' ');
        var status = parts.Length >= 2 && int.TryParse(parts[1], out var code) ? code : 0;

        string? line;
        while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(timeoutCts.Token)))
        {
        }

        var body = await reader.ReadToEndAsync(timeoutCts.Token);
        return (status, body);
    }
}
