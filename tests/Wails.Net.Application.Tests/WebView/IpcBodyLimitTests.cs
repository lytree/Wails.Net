using TUnit.Core;
using Wails.Net.Application.Transport;
using Wails.Net.Application.Webview;

namespace Wails.Net.Application.Tests.WebView;

/// <summary>
/// <see cref="WebviewRequestPipeline"/> 的 IPC 请求体上限测试（RH-IPC-04）。
/// <para>
/// 背景：Wails v3 对齐项 P1-8「运行时请求 64 MiB 上限 → 413」原先实现在
/// <c>HttpTransport</c> 中，而该类在生产路径零装配，限制实际从未生效。
/// 独立 HTTP 传输删除后校验迁入本管线（桌面模式真正可达的 IPC 入口），
/// 本测试确保该行为不会再次退化为"写在死代码里"。
/// </para>
/// </summary>
public sealed class IpcBodyLimitTests
{
    private static WebviewRequestPipeline CreatePipeline(List<string> handledBodies)
    {
        return new WebviewRequestPipeline(
            ipcHandler: (body, _, _, _) =>
            {
                lock (handledBodies)
                {
                    handledBodies.Add(body);
                }

                return Task.FromResult<object?>(null);
            },
            assetHandler: (_, _, _) => Task.FromResult<byte[]?>(null));
    }

    private static WebviewRequest CreateIpcRequest(byte[] body) => new()
    {
        Method = "POST",
        Path = IpcProtocol.MessageEndpoint,
        RequestBodyBytes = body,
        WindowId = 1,
        WindowName = "main",
    };

    [Test]
    public async Task IpcBodyOverLimit_Returns413_AndHandlerNotCalled()
    {
        var handled = new List<string>();
        var pipeline = CreatePipeline(handled);

        // 64 MiB + 1 字节：刚好越界（一次性分配，测试内即释放）。
        var oversize = new byte[(int)IpcProtocol.MaxRuntimeBodyBytes + 1];
        var response = await pipeline.HandleAsync(CreateIpcRequest(oversize));

        await Assert.That(response.StatusCode).IsEqualTo(413);
        await Assert.That(handled.Count).IsEqualTo(0);
        await Assert.That(response.Body is not null && System.Text.Encoding.UTF8.GetString(response.Body).Contains("64 MiB", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task IpcBodyWithinLimit_ReachesHandler()
    {
        var handled = new List<string>();
        var pipeline = CreatePipeline(handled);

        var response = await pipeline.HandleAsync(CreateIpcRequest("""{"id":"1"}"""u8.ToArray()));

        await Assert.That(response.StatusCode).IsEqualTo(200);
        await Assert.That(handled.Count).IsEqualTo(1);
    }
}
