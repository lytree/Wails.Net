using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Wails.Net.AssetServer;
using Wails.Net.Application.Services;

namespace Wails.Net.Application.Tests;

/// <summary>
/// ServiceRegistry 的单元测试（TUnit）。
/// </summary>
public sealed class ServiceRegistryTests
{
    /// <summary>
    /// 用于测试的简单服务类。
    /// </summary>
    private class TestService { }

    /// <summary>
    /// 用于测试的 HTTP 服务处理器，记录最后一次收到的上下文。
    /// </summary>
    private sealed class FakeHttpHandler : IHttpServiceHandler
    {
        public HttpListenerContext? LastContext { get; private set; }
        public int CallCount { get; private set; }

        public Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            LastContext = context;
            CallCount++;
            context.Response.StatusCode = 200;
            context.Response.Close();
            return Task.CompletedTask;
        }
    }

    [Test]
    public async Task Register_AddsServiceToCollection()
    {
        // 安排
        var registry = new ServiceRegistry();
        var service = new TestService();

        // 操作
        registry.Register(service);

        // 断言
        await Assert.That(registry.Services.Count).IsEqualTo(1);
        await Assert.That(registry.Services[0]).IsSameReferenceAs(service);
    }

    [Test]
    public async Task Unregister_RemovesServiceFromCollection()
    {
        // 安排
        var registry = new ServiceRegistry();
        var service = new TestService();
        registry.Register(service);

        // 操作
        registry.Unregister(service);

        // 断言
        await Assert.That(registry.Services.Count).IsEqualTo(0);
    }

    [Test]
    public async Task GetService_ReturnsCorrectType()
    {
        // 安排
        var registry = new ServiceRegistry();
        var service = new TestService();
        registry.Register(service);

        // 操作
        var result = registry.GetService<TestService>();

        // 断言
        await Assert.That(result).IsNotNull();
        await Assert.That(result).IsSameReferenceAs(service);
    }

    [Test]
    public async Task GetService_ReturnsNullWhenNotFound()
    {
        // 安排
        var registry = new ServiceRegistry();

        // 操作
        var result = registry.GetService<TestService>();

        // 断言
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetServices_ReturnsAllMatching()
    {
        // 安排
        var registry = new ServiceRegistry();
        var service1 = new TestService();
        var service2 = new TestService();
        registry.Register(service1);
        registry.Register(service2);

        // 操作
        var result = registry.GetServices<TestService>().ToList();

        // 断言
        await Assert.That(result.Count).IsEqualTo(2);
        await Assert.That(result[0]).IsSameReferenceAs(service1);
        await Assert.That(result[1]).IsSameReferenceAs(service2);
    }

    [Test]
    public async Task Clear_RemovesAllServices()
    {
        // 安排
        var registry = new ServiceRegistry();
        registry.Register(new TestService());
        registry.Register(new TestService());

        // 操作
        registry.Clear();

        // 断言
        await Assert.That(registry.Services.Count).IsEqualTo(0);
    }

    // ========== P1-6: ServiceOptions + Route 测试 ==========

    [Test]
    public async Task Register_WithOptions_StoresServiceAndOptions()
    {
        // 安排
        var registry = new ServiceRegistry();
        var service = new TestService();
        var options = new ServiceOptions { Name = "MyService", Route = "/api" };

        // 操作
        registry.Register(service, options);

        // 断言
        await Assert.That(registry.Services.Count).IsEqualTo(1);
        await Assert.That(registry.Services[0]).IsSameReferenceAs(service);
        var opts = registry.GetOptions(service);
        await Assert.That(opts).IsNotNull();
        await Assert.That(opts!.Name).IsEqualTo("MyService");
        await Assert.That(opts.Route).IsEqualTo("/api");
    }

    [Test]
    public async Task Register_WithNullOptions_UsesDefault()
    {
        // 安排
        var registry = new ServiceRegistry();
        var service = new TestService();

        // 操作
        registry.Register(service, null);

        // 断言
        var opts = registry.GetOptions(service);
        await Assert.That(opts).IsNotNull();
        await Assert.That(opts!.Name).IsNull();
        await Assert.That(opts.Route).IsNull();
    }

    [Test]
    public async Task Register_ServiceOnly_UsesDefaultOptions()
    {
        // 安排
        var registry = new ServiceRegistry();
        var service = new TestService();

        // 操作
        registry.Register(service);

        // 断言
        var opts = registry.GetOptions(service);
        await Assert.That(opts).IsNotNull();
        await Assert.That(opts!.Route).IsNull();
    }

    [Test]
    public async Task GetOptions_ReturnsNullForUnregisteredService()
    {
        // 安排
        var registry = new ServiceRegistry();
        var service = new TestService();

        // 操作
        var opts = registry.GetOptions(service);

        // 断言
        await Assert.That(opts).IsNull();
    }

    [Test]
    public async Task GetOptions_ThrowsOnNullService()
    {
        // 安排
        var registry = new ServiceRegistry();

        // 操作 + 断言
        await Assert.That(() => registry.GetOptions(null!)).Throws<ArgumentNullException>();
    }

    // ========== Route 挂载测试 ==========

    [Test]
    public async Task Register_WithRouteAndHandler_RegistersToRouteTable()
    {
        // 安排
        var registry = new ServiceRegistry();
        var handler = new FakeHttpHandler();
        var options = new ServiceOptions { Route = "/api" };

        // 操作
        registry.Register(handler, options);

        // 断言
        var routes = registry.GetServiceRoutes();
        await Assert.That(routes.Count).IsEqualTo(1);
        await Assert.That(routes.ContainsKey("/api")).IsTrue();
        await Assert.That(routes["/api"]).IsSameReferenceAs(handler);
    }

    [Test]
    public async Task Register_WithRouteButNotHandler_DoesNotRegisterToRouteTable()
    {
        // 安排
        var registry = new ServiceRegistry();
        var service = new TestService(); // 不实现 IHttpServiceHandler
        var options = new ServiceOptions { Route = "/api" };

        // 操作
        registry.Register(service, options);

        // 断言
        var routes = registry.GetServiceRoutes();
        await Assert.That(routes.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Register_WithHandlerButNoRoute_DoesNotRegisterToRouteTable()
    {
        // 安排
        var registry = new ServiceRegistry();
        var handler = new FakeHttpHandler();
        var options = new ServiceOptions { Route = null };

        // 操作
        registry.Register(handler, options);

        // 断言
        var routes = registry.GetServiceRoutes();
        await Assert.That(routes.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Register_NormalizesRoutePrefix()
    {
        // 安排
        var registry = new ServiceRegistry();
        var handler = new FakeHttpHandler();

        // 操作：注册时使用 "api/"（无前导斜杠，有尾随斜杠）
        registry.Register(handler, new ServiceOptions { Route = "api/" });

        // 断言：规范化为 "/api"
        var routes = registry.GetServiceRoutes();
        await Assert.That(routes.ContainsKey("/api")).IsTrue();
    }

    [Test]
    public async Task Register_DuplicateRoute_ReplacesPrevious()
    {
        // 安排
        var registry = new ServiceRegistry();
        var handler1 = new FakeHttpHandler();
        var handler2 = new FakeHttpHandler();

        // 操作
        registry.Register(handler1, new ServiceOptions { Route = "/api" });
        registry.Register(handler2, new ServiceOptions { Route = "/api" });

        // 断言：后注册的覆盖前者
        var routes = registry.GetServiceRoutes();
        await Assert.That(routes.Count).IsEqualTo(1);
        await Assert.That(routes["/api"]).IsSameReferenceAs(handler2);
    }

    [Test]
    public async Task Unregister_RemovesRouteFromRouteTable()
    {
        // 安排
        var registry = new ServiceRegistry();
        var handler = new FakeHttpHandler();
        registry.Register(handler, new ServiceOptions { Route = "/api" });

        // 操作
        registry.Unregister(handler);

        // 断言
        var routes = registry.GetServiceRoutes();
        await Assert.That(routes.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Clear_RemovesAllRoutes()
    {
        // 安排
        var registry = new ServiceRegistry();
        registry.Register(new FakeHttpHandler(), new ServiceOptions { Route = "/api" });
        registry.Register(new FakeHttpHandler(), new ServiceOptions { Route = "/webhook" });

        // 操作
        registry.Clear();

        // 断言
        await Assert.That(registry.GetServiceRoutes().Count).IsEqualTo(0);
    }

    // ========== TryMatchRoute 测试 ==========

    [Test]
    public async Task TryMatchRoute_ExactMatch_ReturnsHandler()
    {
        // 安排
        var registry = new ServiceRegistry();
        var handler = new FakeHttpHandler();
        registry.Register(handler, new ServiceOptions { Route = "/api" });

        // 操作
        var matched = registry.TryMatchRoute("/api", out var route, out var matchedHandler);

        // 断言
        await Assert.That(matched).IsTrue();
        await Assert.That(route).IsEqualTo("/api");
        await Assert.That(matchedHandler).IsSameReferenceAs(handler);
    }

    [Test]
    public async Task TryMatchRoute_PrefixMatch_ReturnsHandler()
    {
        // 安排
        var registry = new ServiceRegistry();
        var handler = new FakeHttpHandler();
        registry.Register(handler, new ServiceOptions { Route = "/api" });

        // 操作
        var matched = registry.TryMatchRoute("/api/users/123", out var route, out var matchedHandler);

        // 断言
        await Assert.That(matched).IsTrue();
        await Assert.That(route).IsEqualTo("/api");
        await Assert.That(matchedHandler).IsSameReferenceAs(handler);
    }

    [Test]
    public async Task TryMatchRoute_NonPrefixMatch_ReturnsFalse()
    {
        // 安排
        var registry = new ServiceRegistry();
        var handler = new FakeHttpHandler();
        registry.Register(handler, new ServiceOptions { Route = "/api" });

        // 操作：/apiv2 不应匹配 /api（避免前缀歧义）
        var matched = registry.TryMatchRoute("/apiv2", out var route, out var matchedHandler);

        // 断言
        await Assert.That(matched).IsFalse();
        await Assert.That(route).IsNull();
        await Assert.That(matchedHandler).IsNull();
    }

    [Test]
    public async Task TryMatchRoute_EmptyPath_ReturnsFalse()
    {
        // 安排
        var registry = new ServiceRegistry();
        registry.Register(new FakeHttpHandler(), new ServiceOptions { Route = "/api" });

        // 操作
        var matched = registry.TryMatchRoute("", out _, out _);

        // 断言
        await Assert.That(matched).IsFalse();
    }

    [Test]
    public async Task TryMatchRoute_NoRoutes_ReturnsFalse()
    {
        // 安排
        var registry = new ServiceRegistry();

        // 操作
        var matched = registry.TryMatchRoute("/api", out _, out _);

        // 断言
        await Assert.That(matched).IsFalse();
    }

    [Test]
    public async Task TryMatchRoute_MultipleMatches_ReturnsLongestMatch()
    {
        // 安排：注册 /api 和 /api/v2 两个路由
        var registry = new ServiceRegistry();
        var apiHandler = new FakeHttpHandler();
        var apiV2Handler = new FakeHttpHandler();
        registry.Register(apiHandler, new ServiceOptions { Route = "/api" });
        registry.Register(apiV2Handler, new ServiceOptions { Route = "/api/v2" });

        // 操作：请求 /api/v2/users 同时匹配两个路由，应返回最长的 /api/v2
        var matched = registry.TryMatchRoute("/api/v2/users", out var route, out var matchedHandler);

        // 断言
        await Assert.That(matched).IsTrue();
        await Assert.That(route).IsEqualTo("/api/v2");
        await Assert.That(matchedHandler).IsSameReferenceAs(apiV2Handler);
    }

    [Test]
    public async Task TryMatchRoute_RootRoute_MatchesAnyPath()
    {
        // 安排：注册根路由 "/"
        var registry = new ServiceRegistry();
        var rootHandler = new FakeHttpHandler();
        registry.Register(rootHandler, new ServiceOptions { Route = "/" });

        // 操作
        var matched = registry.TryMatchRoute("/any/path", out var route, out var matchedHandler);

        // 断言
        await Assert.That(matched).IsTrue();
        await Assert.That(route).IsEqualTo("/");
        await Assert.That(matchedHandler).IsSameReferenceAs(rootHandler);
    }

    [Test]
    public async Task CopyTo_PreservesAllEntries()
    {
        // 安排
        var registry = new ServiceRegistry();
        var service1 = new TestService();
        var service2 = new TestService();
        registry.Register(service1, new ServiceOptions { Name = "S1" });
        registry.Register(service2, new ServiceOptions { Name = "S2" });

        var services = new ServiceCollection();

        // 操作
        registry.CopyTo(services);

        // 断言
        await Assert.That(services.Count).IsEqualTo(2);
    }
}

