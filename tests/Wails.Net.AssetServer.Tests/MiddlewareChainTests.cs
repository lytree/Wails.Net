using System.Net;
using System.Text;
using TUnit.Core;
using Wails.Net.AssetServer.Middleware;

namespace Wails.Net.AssetServer.Tests;

/// <summary>
/// MiddlewareChain 的单元测试（TUnit）。
/// 覆盖中间件注册顺序、路径型与 HTTP 型中间件、短路行为。
/// 对应 Wails v3 Go 版本 middleware 链编排逻辑的测试。
/// </summary>
[NotInParallel]
public sealed class MiddlewareChainTests
{
    // ========== 基于路径的中间件测试 ==========

    [Test]
    public async Task ExecuteAsync_NoMiddleware_CallsFinalHandler()
    {
        var chain = new MiddlewareChain();
        var expected = Encoding.UTF8.GetBytes("final");

        var result = await chain.ExecuteAsync("/test", _ => Task.FromResult<byte[]?>(expected));

        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task ExecuteAsync_NoMiddleware_FinalReturnsNull_ReturnsNull()
    {
        var chain = new MiddlewareChain();

        var result = await chain.ExecuteAsync("/test", _ => Task.FromResult<byte[]?>(null));

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ExecuteAsync_SingleMiddleware_ExecutesBeforeFinalHandler()
    {
        var chain = new MiddlewareChain();
        var callOrder = new List<string>();
        chain.Use(new RecordingPathMiddleware("mw1", callOrder));
        var finalContent = Encoding.UTF8.GetBytes("final");

        var result = await chain.ExecuteAsync(
            "/test",
            _ =>
            {
                callOrder.Add("final");
                return Task.FromResult<byte[]?>(finalContent);
            });

        await Assert.That(result).IsEqualTo(finalContent);
        await Assert.That(callOrder[0]).IsEqualTo("mw1");
        await Assert.That(callOrder[1]).IsEqualTo("final");
    }

    [Test]
    public async Task ExecuteAsync_MultipleMiddlewares_ExecutesInRegistrationOrder()
    {
        var chain = new MiddlewareChain();
        var callOrder = new List<string>();
        chain.Use(new RecordingPathMiddleware("mw1", callOrder));
        chain.Use(new RecordingPathMiddleware("mw2", callOrder));
        chain.Use(new RecordingPathMiddleware("mw3", callOrder));

        await chain.ExecuteAsync("/test", _ => Task.FromResult<byte[]?>(null));

        await Assert.That(callOrder.Count).IsEqualTo(3);
        await Assert.That(callOrder[0]).IsEqualTo("mw1");
        await Assert.That(callOrder[1]).IsEqualTo("mw2");
        await Assert.That(callOrder[2]).IsEqualTo("mw3");
    }

    [Test]
    public async Task ExecuteAsync_MiddlewareShortCircuits_ReturnsMiddlewareContent()
    {
        var chain = new MiddlewareChain();
        var interceptContent = Encoding.UTF8.GetBytes("intercepted");
        chain.Use(new StubPathMiddleware(_ => interceptContent));
        var finalCalled = false;

        var result = await chain.ExecuteAsync("/test", _ =>
        {
            finalCalled = true;
            return Task.FromResult<byte[]?>(Encoding.UTF8.GetBytes("final"));
        });

        await Assert.That(result).IsEqualTo(interceptContent);
        await Assert.That(finalCalled).IsFalse();
    }

    [Test]
    public async Task ExecuteAsync_MiddlewareCallsNext_ModifiesPathAndPassesThrough()
    {
        var chain = new MiddlewareChain();
        string? receivedPathByFinal = null;
        chain.Use(new TransformingPathMiddleware((path, next) => next(path + "-modified")));

        await chain.ExecuteAsync("/test", p =>
        {
            receivedPathByFinal = p;
            return Task.FromResult<byte[]?>(null);
        });

        await Assert.That(receivedPathByFinal).IsEqualTo("/test-modified");
    }

    [Test]
    public async Task ExecuteAsync_Count_ReturnsRegisteredMiddlewareCount()
    {
        var chain = new MiddlewareChain();
        await Assert.That(chain.Count).IsEqualTo(0);

        chain.Use(new StubPathMiddleware(_ => null));
        await Assert.That(chain.Count).IsEqualTo(1);

        chain.Use(new StubPathMiddleware(_ => null));
        await Assert.That(chain.Count).IsEqualTo(2);
    }

    // ========== HTTP 中间件测试（使用 HttpListener + HttpClient） ==========

    /// <summary>
    /// 使用 HttpListener 启动本地监听，将请求委托给 MiddlewareChain.ExecuteHttpAsync 处理。
    /// 返回最终处理器是否被调用的标志。若 HttpListener 不可用则返回 null 标记跳过。
    /// </summary>
    private static async Task<bool?> ExecuteHttpWithListenerAsync(
        List<IHttpMiddleware> middlewares)
    {
        var port = GetFreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");

        try
        {
            listener.Start();
        }
        catch (HttpListenerException)
        {
            // CI 环境可能无权限启动监听
            return null;
        }

        var chain = new MiddlewareChain();
        foreach (var mw in middlewares)
        {
            chain.Use(mw);
        }

        var finalCalled = false;
        var listenerTask = Task.Run(async () =>
        {
            try
            {
                var ctx = await listener.GetContextAsync();
                await chain.ExecuteHttpAsync(ctx, () =>
                {
                    finalCalled = true;
                    ctx.Response.StatusCode = 200;
                    ctx.Response.Close();
                    return Task.CompletedTask;
                });
            }
            catch
            {
                // 忽略监听异常
            }
        });

        try
        {
            using var client = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}/test");
            using var response = await client.SendAsync(request);
        }
        catch
        {
            // 服务端可能返回非 2xx，忽略
        }

        await listenerTask;
        listener.Stop();

        return finalCalled;
    }

    private static int GetFreePort()
    {
        using var socket = new System.Net.Sockets.Socket(
            System.Net.Sockets.AddressFamily.InterNetwork,
            System.Net.Sockets.SocketType.Stream,
            System.Net.Sockets.ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    [Test]
    public async Task ExecuteHttpAsync_NoMiddleware_CallsFinalHandler()
    {
        var finalCalled = await ExecuteHttpWithListenerAsync(new List<IHttpMiddleware>());

        if (finalCalled is null)
        {
            return; // 跳过：HttpListener 不可用
        }

        await Assert.That(finalCalled.Value).IsTrue();
    }

    [Test]
    public async Task ExecuteHttpAsync_MiddlewareShortCircuits_SkipsFinalHandler()
    {
        var mw = new StubHttpMiddleware(async (ctx, next) =>
        {
            ctx.Response.StatusCode = 201;
            ctx.Response.Close();
            return true; // 短路
        });

        var finalCalled = await ExecuteHttpWithListenerAsync(new List<IHttpMiddleware> { mw });

        if (finalCalled is null)
        {
            return;
        }

        await Assert.That(finalCalled.Value).IsFalse();
    }

    [Test]
    public async Task ExecuteHttpAsync_MiddlewareCallsNext_FinalHandlerExecutes()
    {
        var mw = new StubHttpMiddleware(async (ctx, next) =>
        {
            await next();
            return false;
        });

        var finalCalled = await ExecuteHttpWithListenerAsync(new List<IHttpMiddleware> { mw });

        if (finalCalled is null)
        {
            return;
        }

        await Assert.That(finalCalled.Value).IsTrue();
    }

    [Test]
    public async Task ExecuteHttpAsync_MultipleMiddlewares_ExecutesInOrder()
    {
        var callOrder = new List<string>();
        var middlewares = new List<IHttpMiddleware>
        {
            new StubHttpMiddleware(async (ctx, next) =>
            {
                callOrder.Add("mw1-before");
                await next();
                callOrder.Add("mw1-after");
                return false;
            }),
            new StubHttpMiddleware(async (ctx, next) =>
            {
                callOrder.Add("mw2-before");
                await next();
                callOrder.Add("mw2-after");
                return false;
            })
        };

        await ExecuteHttpWithListenerAsync(middlewares);

        // 验证中间件按注册顺序执行，且 next() 之后的代码按逆序执行
        await Assert.That(callOrder.Count).IsEqualTo(4);
        await Assert.That(callOrder[0]).IsEqualTo("mw1-before");
        await Assert.That(callOrder[1]).IsEqualTo("mw2-before");
        await Assert.That(callOrder[2]).IsEqualTo("mw2-after");
        await Assert.That(callOrder[3]).IsEqualTo("mw1-after");
    }

    [Test]
    public async Task ExecuteHttpAsync_HttpMiddlewareCount_ReturnsRegisteredCount()
    {
        var chain = new MiddlewareChain();
        await Assert.That(chain.HttpMiddlewareCount).IsEqualTo(0);

        chain.Use(new StubHttpMiddleware((ctx, next) => Task.FromResult(false)));
        await Assert.That(chain.HttpMiddlewareCount).IsEqualTo(1);

        chain.Use(new StubHttpMiddleware((ctx, next) => Task.FromResult(false)));
        await Assert.That(chain.HttpMiddlewareCount).IsEqualTo(2);
    }

    // ========== Use 重载测试 ==========

    [Test]
    public async Task Use_PathMiddleware_AddsToPathChain()
    {
        var chain = new MiddlewareChain();
        chain.Use(new StubPathMiddleware(_ => null));

        await Assert.That(chain.Count).IsEqualTo(1);
        await Assert.That(chain.HttpMiddlewareCount).IsEqualTo(0);
    }

    [Test]
    public async Task Use_HttpMiddleware_AddsToHttpChain()
    {
        var chain = new MiddlewareChain();
        chain.Use(new StubHttpMiddleware((ctx, next) => Task.FromResult(false)));

        await Assert.That(chain.Count).IsEqualTo(0);
        await Assert.That(chain.HttpMiddlewareCount).IsEqualTo(1);
    }

    [Test]
    public async Task Use_NullPathMiddleware_ThrowsArgumentNullException()
    {
        var chain = new MiddlewareChain();
        await Assert.That(() => chain.Use((IMiddleware)null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task Use_NullHttpMiddleware_ThrowsArgumentNullException()
    {
        var chain = new MiddlewareChain();
        await Assert.That(() => chain.Use((IHttpMiddleware)null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task ExecuteAsync_NullFinalHandler_ThrowsArgumentNullException()
    {
        var chain = new MiddlewareChain();
        await Assert.That(() => chain.ExecuteAsync("/test", null!)).ThrowsExactly<ArgumentNullException>();
    }

    // ========== 辅助类型 ==========

    /// <summary>
    /// 记录调用顺序的基于路径的中间件，用于验证中间件链的执行顺序。
    /// </summary>
    private sealed class RecordingPathMiddleware : IMiddleware
    {
        private readonly string _name;
        private readonly List<string> _callOrder;

        public RecordingPathMiddleware(string name, List<string> callOrder)
        {
            _name = name;
            _callOrder = callOrder;
        }

        public async Task<byte[]?> ProcessAsync(string path, Func<string, Task<byte[]?>> next)
        {
            _callOrder.Add(_name);
            return await next(path);
        }
    }

    /// <summary>
    /// 使用委托控制行为的基于路径的中间件，直接返回指定内容（短路）。
    /// </summary>
    private sealed class StubPathMiddleware : IMiddleware
    {
        private readonly Func<string, byte[]?> _returnContent;

        public StubPathMiddleware(Func<string, byte[]?> returnContent)
        {
            _returnContent = returnContent;
        }

        public Task<byte[]?> ProcessAsync(string path, Func<string, Task<byte[]?>> next)
        {
            return Task.FromResult(_returnContent(path));
        }
    }

    /// <summary>
    /// 使用委托控制行为的基于路径的中间件，可调用 next 委托继续链。
    /// </summary>
    private sealed class TransformingPathMiddleware : IMiddleware
    {
        private readonly Func<string, Func<string, Task<byte[]?>>, Task<byte[]?>> _handler;

        public TransformingPathMiddleware(Func<string, Func<string, Task<byte[]?>>, Task<byte[]?>> handler)
        {
            _handler = handler;
        }

        public Task<byte[]?> ProcessAsync(string path, Func<string, Task<byte[]?>> next)
        {
            return _handler(path, next);
        }
    }

    /// <summary>
    /// 使用委托控制行为的 HTTP 中间件。
    /// </summary>
    private sealed class StubHttpMiddleware : IHttpMiddleware
    {
        private readonly Func<HttpListenerContext, Func<Task>, Task<bool>> _handler;

        public StubHttpMiddleware(Func<HttpListenerContext, Func<Task>, Task<bool>> handler)
        {
            _handler = handler;
        }

        public Task<bool> ProcessAsync(HttpListenerContext context, Func<Task> next)
        {
            return _handler(context, next);
        }
    }
}
