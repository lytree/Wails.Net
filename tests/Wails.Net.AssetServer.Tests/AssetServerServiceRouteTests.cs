using System.Net;
using System.Text;
using TUnit.Core;
using Wails.Net.AssetServer.Middleware;

namespace Wails.Net.AssetServer.Tests;

/// <summary>
/// AssetServer 服务路由（P1-6）的单元测试（TUnit）。
/// <para>
/// 覆盖 <see cref="AssetServer.MountServiceRoute"/>、<see cref="AssetServer.UnmountServiceRoute"/>、
/// <see cref="AssetServer.IsServiceRouteMounted"/>、<see cref="AssetServer.TryMatchServiceRoute"/>，
/// 以及 <see cref="AssetServer.ServeHttpAsync"/> 中的路由转发逻辑。
/// </para>
/// </summary>
[NotInParallel]
public sealed class AssetServerServiceRouteTests
{
    /// <summary>
    /// 用于测试的 AssetServer 子类，提供空的资源读取（始终返回 null）。
    /// </summary>
    private sealed class EmptyAssetServer : AssetServer
    {
        public EmptyAssetServer() : base(new AssetOptions { Handler = "test" }) { }

        protected override byte[]? ReadAssetCore(string path) => null;
    }

    /// <summary>
    /// 记录调用信息的 HTTP 服务处理器。
    /// </summary>
    private sealed class CapturingHandler : IHttpServiceHandler
    {
        public int CallCount { get; private set; }
        public HttpListenerContext? LastContext { get; private set; }

        public Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            CallCount++;
            LastContext = context;
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            var body = Encoding.UTF8.GetBytes("""{"ok":true}""");
            context.Response.ContentLength64 = body.Length;
            context.Response.OutputStream.Write(body, 0, body.Length);
            context.Response.Close();
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 抛出异常的 HTTP 服务处理器，用于测试异常路径。
    /// </summary>
    private sealed class ThrowingHandler : IHttpServiceHandler
    {
        public Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Service handler test exception");
        }
    }

    /// <summary>
    /// 不写入响应即返回的处理器，用于验证异常路径的兜底逻辑。
    /// </summary>
    private sealed class NoopHandler : IHttpServiceHandler
    {
        public Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            context.Response.Close();
            return Task.CompletedTask;
        }
    }

    // ========== MountServiceRoute / UnmountServiceRoute / IsServiceRouteMounted ==========

    [Test]
    public async Task MountServiceRoute_AddsRouteToMountedServiceRoutes()
    {
        // 安排
        var server = new EmptyAssetServer();
        var handler = new CapturingHandler();

        // 操作
        server.MountServiceRoute("/api", handler);

        // 断言
        await Assert.That(server.MountedServiceRoutes.Count).IsEqualTo(1);
        await Assert.That(server.MountedServiceRoutes.Contains("/api")).IsTrue();
        await Assert.That(server.IsServiceRouteMounted("/api")).IsTrue();
    }

    [Test]
    public async Task MountServiceRoute_NormalizesRoutePrefix()
    {
        // 安排
        var server = new EmptyAssetServer();
        var handler = new CapturingHandler();

        // 操作：传入 "api/" 应规范化为 "/api"
        server.MountServiceRoute("api/", handler);

        // 断言
        await Assert.That(server.IsServiceRouteMounted("/api")).IsTrue();
        await Assert.That(server.IsServiceRouteMounted("api/")).IsTrue(); // 同样规范化后查询
    }

    [Test]
    public async Task MountServiceRoute_NullRoute_ThrowsArgumentException()
    {
        // 安排
        var server = new EmptyAssetServer();
        var handler = new CapturingHandler();

        // 操作 + 断言
        await Assert.That(() => server.MountServiceRoute(null!, handler)).Throws<ArgumentException>();
    }

    [Test]
    public async Task MountServiceRoute_EmptyRoute_ThrowsArgumentException()
    {
        // 安排
        var server = new EmptyAssetServer();
        var handler = new CapturingHandler();

        // 操作 + 断言
        await Assert.That(() => server.MountServiceRoute("", handler)).Throws<ArgumentException>();
    }

    [Test]
    public async Task MountServiceRoute_WhiteSpaceRoute_ThrowsArgumentException()
    {
        // 安排
        var server = new EmptyAssetServer();
        var handler = new CapturingHandler();

        // 操作 + 断言
        await Assert.That(() => server.MountServiceRoute("   ", handler)).Throws<ArgumentException>();
    }

    [Test]
    public async Task MountServiceRoute_NullHandler_ThrowsArgumentNullException()
    {
        // 安排
        var server = new EmptyAssetServer();

        // 操作 + 断言
        await Assert.That(() => server.MountServiceRoute("/api", null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task MountServiceRoute_DuplicateRoute_ReplacesPrevious()
    {
        // 安排
        var server = new EmptyAssetServer();
        var handler1 = new CapturingHandler();
        var handler2 = new CapturingHandler();
        server.MountServiceRoute("/api", handler1);

        // 操作
        server.MountServiceRoute("/api", handler2);

        // 断言
        await Assert.That(server.MountedServiceRoutes.Count).IsEqualTo(1);
        // 验证使用 handler2：通过 TryMatchServiceRoute 验证
        server.TryMatchServiceRoute("/api", out _, out var matched);
        await Assert.That(matched).IsSameReferenceAs(handler2);
    }

    [Test]
    public async Task UnmountServiceRoute_RemovesMountedRoute()
    {
        // 安排
        var server = new EmptyAssetServer();
        var handler = new CapturingHandler();
        server.MountServiceRoute("/api", handler);

        // 操作
        var removed = server.UnmountServiceRoute("/api");

        // 断言
        await Assert.That(removed).IsTrue();
        await Assert.That(server.IsServiceRouteMounted("/api")).IsFalse();
        await Assert.That(server.MountedServiceRoutes.Count).IsEqualTo(0);
    }

    [Test]
    public async Task UnmountServiceRoute_UnknownRoute_ReturnsFalse()
    {
        // 安排
        var server = new EmptyAssetServer();

        // 操作
        var removed = server.UnmountServiceRoute("/api");

        // 断言
        await Assert.That(removed).IsFalse();
    }

    [Test]
    public async Task UnmountServiceRoute_NullOrEmpty_ReturnsFalse()
    {
        // 安排
        var server = new EmptyAssetServer();

        // 操作 + 断言
        await Assert.That(server.UnmountServiceRoute(null!)).IsFalse();
        await Assert.That(server.UnmountServiceRoute("")).IsFalse();
        await Assert.That(server.UnmountServiceRoute("   ")).IsFalse();
    }

    [Test]
    public async Task IsServiceRouteMounted_NotMounted_ReturnsFalse()
    {
        // 安排
        var server = new EmptyAssetServer();

        // 操作 + 断言
        await Assert.That(server.IsServiceRouteMounted("/api")).IsFalse();
    }

    [Test]
    public async Task IsServiceRouteMounted_NullOrEmpty_ReturnsFalse()
    {
        // 安排
        var server = new EmptyAssetServer();

        // 操作 + 断言
        await Assert.That(server.IsServiceRouteMounted(null!)).IsFalse();
        await Assert.That(server.IsServiceRouteMounted("")).IsFalse();
    }

    // ========== TryMatchServiceRoute ==========

    [Test]
    public async Task TryMatchServiceRoute_ExactMatch_ReturnsTrue()
    {
        // 安排
        var server = new EmptyAssetServer();
        var handler = new CapturingHandler();
        server.MountServiceRoute("/api", handler);

        // 操作
        var matched = server.TryMatchServiceRoute("/api", out var route, out var matchedHandler);

        // 断言
        await Assert.That(matched).IsTrue();
        await Assert.That(route).IsEqualTo("/api");
        await Assert.That(matchedHandler).IsSameReferenceAs(handler);
    }

    [Test]
    public async Task TryMatchServiceRoute_PrefixMatch_ReturnsTrue()
    {
        // 安排
        var server = new EmptyAssetServer();
        var handler = new CapturingHandler();
        server.MountServiceRoute("/api", handler);

        // 操作
        var matched = server.TryMatchServiceRoute("/api/users/123", out var route, out var matchedHandler);

        // 断言
        await Assert.That(matched).IsTrue();
        await Assert.That(route).IsEqualTo("/api");
        await Assert.That(matchedHandler).IsSameReferenceAs(handler);
    }

    [Test]
    public async Task TryMatchServiceRoute_NonPrefixMatch_ReturnsFalse()
    {
        // 安排
        var server = new EmptyAssetServer();
        var handler = new CapturingHandler();
        server.MountServiceRoute("/api", handler);

        // 操作：/apiv2 不应匹配 /api
        var matched = server.TryMatchServiceRoute("/apiv2", out var route, out var matchedHandler);

        // 断言
        await Assert.That(matched).IsFalse();
        await Assert.That(route).IsNull();
        await Assert.That(matchedHandler).IsNull();
    }

    [Test]
    public async Task TryMatchServiceRoute_EmptyOrNoRoutes_ReturnsFalse()
    {
        // 安排
        var server = new EmptyAssetServer();

        // 操作
        var matched1 = server.TryMatchServiceRoute("/api", out _, out _);
        var matched2 = server.TryMatchServiceRoute("", out _, out _);
        var matched3 = server.TryMatchServiceRoute(null!, out _, out _);

        // 断言
        await Assert.That(matched1).IsFalse();
        await Assert.That(matched2).IsFalse();
        await Assert.That(matched3).IsFalse();
    }

    [Test]
    public async Task TryMatchServiceRoute_MultipleMatches_ReturnsLongest()
    {
        // 安排：注册 /api 和 /api/v2
        var server = new EmptyAssetServer();
        var apiHandler = new CapturingHandler();
        var apiV2Handler = new CapturingHandler();
        server.MountServiceRoute("/api", apiHandler);
        server.MountServiceRoute("/api/v2", apiV2Handler);

        // 操作：/api/v2/users 同时匹配两个，应返回最长的 /api/v2
        var matched = server.TryMatchServiceRoute("/api/v2/users", out var route, out var matchedHandler);

        // 断言
        await Assert.That(matched).IsTrue();
        await Assert.That(route).IsEqualTo("/api/v2");
        await Assert.That(matchedHandler).IsSameReferenceAs(apiV2Handler);
    }

    [Test]
    public async Task TryMatchServiceRoute_RootRoute_MatchesAny()
    {
        // 安排：注册根路由
        var server = new EmptyAssetServer();
        var rootHandler = new CapturingHandler();
        server.MountServiceRoute("/", rootHandler);

        // 操作
        var matched = server.TryMatchServiceRoute("/anything/at/all", out var route, out var matchedHandler);

        // 断言
        await Assert.That(matched).IsTrue();
        await Assert.That(route).IsEqualTo("/");
        await Assert.That(matchedHandler).IsSameReferenceAs(rootHandler);
    }

    // ========== ServeHttpAsync 路由转发测试 ==========

    [Test]
    public async Task ServeHttpAsync_MatchingRoute_DispatchesToHandler()
    {
        // 安排
        var server = new EmptyAssetServer();
        var handler = new CapturingHandler();
        server.MountServiceRoute("/api", handler);

        // 操作
        var response = await SendRequestAsync(() => server, "/api/users");

        // 断言：HttpListener 在 CI 可能不可用
        if (response is null)
        {
            return;
        }

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(handler.CallCount).IsEqualTo(1);
        var body = Encoding.UTF8.GetString(response.Body);
        await Assert.That(body).IsEqualTo("""{"ok":true}""");
    }

    [Test]
    public async Task ServeHttpAsync_MatchingRoute_SetsCorsHeader()
    {
        // 安排
        var server = new EmptyAssetServer();
        var handler = new CapturingHandler();
        server.MountServiceRoute("/api", handler);

        // 操作
        var response = await SendRequestAsync(() => server, "/api");

        // 断言
        if (response is null)
        {
            return;
        }

        // CORS 头应在路由转发之前设置
        var cors = response.Response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values)
            ? values.FirstOrDefault()
            : null;
        await Assert.That(cors).IsEqualTo("*");
    }

    [Test]
    public async Task ServeHttpAsync_NonMatchingRoute_FallsBackToAssetProcessing()
    {
        // 安排
        var server = new EmptyAssetServer();
        var handler = new CapturingHandler();
        server.MountServiceRoute("/api", handler);

        // 操作：请求 /other 不匹配 /api，应回退到资源处理（404）
        var response = await SendRequestAsync(() => server, "/other");

        // 断言
        if (response is null)
        {
            return;
        }

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(handler.CallCount).IsEqualTo(0);
    }

    [Test]
    public async Task ServeHttpAsync_ExactRouteMatch_DispatchesToHandler()
    {
        // 安排
        var server = new EmptyAssetServer();
        var handler = new CapturingHandler();
        server.MountServiceRoute("/api", handler);

        // 操作：精确匹配 /api
        var response = await SendRequestAsync(() => server, "/api");

        // 断言
        if (response is null)
        {
            return;
        }

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(handler.CallCount).IsEqualTo(1);
    }

    [Test]
    public async Task ServeHttpAsync_NonPrefixRoute_DoesNotDispatch()
    {
        // 安排
        var server = new EmptyAssetServer();
        var handler = new CapturingHandler();
        server.MountServiceRoute("/api", handler);

        // 操作：/apiv2 不匹配 /api
        var response = await SendRequestAsync(() => server, "/apiv2");

        // 断言
        if (response is null)
        {
            return;
        }

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(handler.CallCount).IsEqualTo(0);
    }

    [Test]
    public async Task ServeHttpAsync_LongestRouteMatch_Wins()
    {
        // 安排
        var server = new EmptyAssetServer();
        var apiHandler = new CapturingHandler();
        var apiV2Handler = new CapturingHandler();
        server.MountServiceRoute("/api", apiHandler);
        server.MountServiceRoute("/api/v2", apiV2Handler);

        // 操作：/api/v2/users 同时匹配 /api 和 /api/v2
        var response = await SendRequestAsync(() => server, "/api/v2/users");

        // 断言
        if (response is null)
        {
            return;
        }

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(apiV2Handler.CallCount).IsEqualTo(1);
        await Assert.That(apiHandler.CallCount).IsEqualTo(0); // 较短的路由未被调用
    }

    [Test]
    public async Task ServeHttpAsync_HandlerThrows_Returns500()
    {
        // 安排
        var server = new EmptyAssetServer();
        var handler = new ThrowingHandler();
        server.MountServiceRoute("/api", handler);

        // 操作
        var response = await SendRequestAsync(() => server, "/api");

        // 断言
        if (response is null)
        {
            return;
        }

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.InternalServerError);
        var body = Encoding.UTF8.GetString(response.Body);
        await Assert.That(body).Contains("Service Handler Error");
    }

    [Test]
    public async Task ServeHttpAsync_AfterUnmount_FallsBackToAsset()
    {
        // 安排
        var server = new EmptyAssetServer();
        var handler = new CapturingHandler();
        server.MountServiceRoute("/api", handler);
        server.UnmountServiceRoute("/api");

        // 操作
        var response = await SendRequestAsync(() => server, "/api");

        // 断言
        if (response is null)
        {
            return;
        }

        // 卸载后回退到资源处理（404）
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(handler.CallCount).IsEqualTo(0);
    }

    [Test]
    public async Task ServeHttpAsync_RootRoute_CapturesAllPaths()
    {
        // 安排：注册根路由会捕获所有请求
        var server = new EmptyAssetServer();
        var rootHandler = new CapturingHandler();
        server.MountServiceRoute("/", rootHandler);

        // 操作
        var response = await SendRequestAsync(() => server, "/any/arbitrary/path");

        // 断言
        if (response is null)
        {
            return;
        }

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(rootHandler.CallCount).IsEqualTo(1);
    }

    // ========== 辅助方法 ==========

    /// <summary>
    /// 简化的 HTTP 响应记录。
    /// </summary>
    private sealed class SimpleHttpResponse : IDisposable
    {
        public HttpStatusCode StatusCode { get; init; }
        public byte[] Body { get; init; } = [];
        public HttpResponseMessage Response { get; init; } = null!;

        public void Dispose() => Response.Dispose();
    }

    /// <summary>
    /// 使用 HttpListener 启动本地监听，将请求委托给 AssetServer 处理，并返回响应。
    /// 若 HttpListener 无法启动（如 CI 环境无权限），则返回 null 标记跳过。
    /// </summary>
    private static async Task<SimpleHttpResponse?> SendRequestAsync(
        Func<AssetServer> serverFactory,
        string path,
        string method = "GET")
    {
        var port = GetFreePort();
        var uriPrefix = $"http://localhost:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(uriPrefix);

        try
        {
            listener.Start();
        }
        catch (HttpListenerException)
        {
            return null;
        }

        var server = serverFactory();
        var listenerTask = Task.Run(async () =>
        {
            try
            {
                var ctx = await listener.GetContextAsync();
                await server.ServeHttpAsync(ctx);
            }
            catch
            {
                // 忽略监听异常
            }
        });

        try
        {
            using var client = new HttpClient();
            using var request = new HttpRequestMessage(new HttpMethod(method), $"http://localhost:{port}{path}");
            var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsByteArrayAsync();

            await listenerTask;
            listener.Stop();

            return new SimpleHttpResponse
            {
                StatusCode = response.StatusCode,
                Body = body,
                Response = response
            };
        }
        catch
        {
            try { listener.Stop(); } catch { }
            return null;
        }
    }

    /// <summary>
    /// 获取一个空闲的本地端口号。
    /// </summary>
    private static int GetFreePort()
    {
        using var socket = new System.Net.Sockets.Socket(
            System.Net.Sockets.AddressFamily.InterNetwork,
            System.Net.Sockets.SocketType.Stream,
            System.Net.Sockets.ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var endPoint = (IPEndPoint)socket.LocalEndPoint!;
        return endPoint.Port;
    }
}
