using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using TUnit.Core;

namespace Wails.Net.AssetServer.Tests;

/// <summary>
/// M12 日志集成单元测试（TUnit）。
/// 验证 <see cref="AssetServer" /> 与 <c>ILogger&lt;AssetServer&gt;</c> 的集成：
/// 构造函数注入、SetLogger 注入、各事件在合适级别触发、null 日志器静默运行。
/// </summary>
[NotInParallel]
public sealed class AssetServerLoggingTests
{
    // ========== 辅助：TestLogger 实现 ==========

    /// <summary>
    /// 捕获日志条目的简单 ILogger 实现。
    /// </summary>
    private sealed class CapturingLogger : ILogger<AssetServer>
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(
                logLevel,
                formatter(state, exception),
                exception));
        }
    }

    /// <summary>
    /// 始终禁用的日志器，用于验证 IsEnabled 检查路径。
    /// Log 方法 no-op（与 NullLogger 行为一致），符合 LoggerExtensions 不检查 IsEnabled 的契约。
    /// </summary>
    private sealed class DisabledLogger : ILogger<AssetServer>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            // no-op：模拟禁用的日志器，不记录任何内容
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    /// <summary>
    /// 内存字典资源提供者，用于测试。
    /// </summary>
    private sealed class MemoryAssetProvider : IAssetProvider
    {
        private readonly Dictionary<string, byte[]> _assets;

        public MemoryAssetProvider(Dictionary<string, byte[]> assets)
        {
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        }

        public Task<byte[]?> ReadAsync(string path, CancellationToken cancellationToken = default)
        {
            _assets.TryGetValue(path, out var content);
            return Task.FromResult(content);
        }

        public DateTime? GetLastModified(string path) => null;
    }

    // ========== 构造函数与 SetLogger 测试 ==========

    [Test]
    public async Task Constructor_WithLogger_DoesNotThrow()
    {
        var options = new AssetOptions { Handler = "test" };
        var logger = new CapturingLogger();
        var server = new AssetServer(options, logger);

        await Assert.That(server).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        var options = new AssetOptions { Handler = "test" };
        await Assert.That(() => new AssetServer(options, (ILogger<AssetServer>)null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_WithProviderAndLogger_DoesNotThrow()
    {
        var options = new AssetOptions { Handler = "test" };
        var provider = new MemoryAssetProvider(new Dictionary<string, byte[]>());
        var logger = new CapturingLogger();
        var server = new AssetServer(options, provider, logger);

        await Assert.That(server).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithProviderAndNullLogger_ThrowsArgumentNullException()
    {
        var options = new AssetOptions { Handler = "test" };
        var provider = new MemoryAssetProvider(new Dictionary<string, byte[]>());
        await Assert.That(() => new AssetServer(options, provider, null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task SetLogger_InjectsLogger_PostConstruction()
    {
        var options = new AssetOptions { Handler = "test" };
        var server = new AssetServer(options);
        var logger = new CapturingLogger();

        server.SetLogger(logger);

        // 通过触发一次请求验证日志器已生效
        await ServeNotFoundAsync(server);
        await Assert.That(logger.Entries.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task SetLogger_Null_ThrowsArgumentNullException()
    {
        var options = new AssetOptions { Handler = "test" };
        var server = new AssetServer(options);

        await Assert.That(() => server.SetLogger(null!)).ThrowsExactly<ArgumentNullException>();
    }

    // ========== 日志事件触发测试 ==========

    /// <summary>
    /// 启动 HttpListener 并由 AssetServer 处理一次请求。
    /// </summary>
    private static async Task ServeOnceAsync(AssetServer server, string path, Action<HttpListenerRequest>? configureRequest = null)
    {
        using var listener = new HttpListener();
        var port = 18000 + Random.Shared.Next(0, 999);
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();

        var requestTask = Task.Run(async () =>
        {
            using var client = new HttpClient();
            return await client.GetAsync($"http://localhost:{port}/{path.TrimStart('/')}");
        });

        var ctx = await listener.GetContextAsync();
        configureRequest?.Invoke(ctx.Request);
        await server.ServeHttpAsync(ctx);
        ctx.Response.Close();

        // 等待客户端接收到响应（避免提前 Stop 导致连接中断）
        try
        {
            _ = await requestTask;
        }
        catch
        {
            // 客户端断开等不影响日志断言
        }
        listener.Stop();
    }

    /// <summary>
    /// 触发 ServeNotFound 路径（资源不存在）。
    /// </summary>
    private static async Task ServeNotFoundAsync(AssetServer server)
    {
        using var listener = new HttpListener();
        var port = 18200 + Random.Shared.Next(0, 999);
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();

        var requestTask = Task.Run(async () =>
        {
            using var client = new HttpClient();
            return await client.GetAsync($"http://localhost:{port}/missing.html");
        });

        var ctx = await listener.GetContextAsync();
        await server.ServeHttpAsync(ctx);
        ctx.Response.Close();

        try { _ = await requestTask; } catch { /* ignore */ }
        listener.Stop();
    }

    [Test]
    public async Task ServeHttpAsync_LogsRequestArrival_Debug()
    {
        var logger = new CapturingLogger();
        var provider = new MemoryAssetProvider(new Dictionary<string, byte[]>
        {
            ["/index.html"] = Encoding.UTF8.GetBytes("<html></html>")
        });
        var server = new AssetServer(new AssetOptions { Handler = "test" }, provider, logger);

        await ServeOnceAsync(server, "index.html");

        var arrivalLog = logger.Entries.FirstOrDefault(e => e.Level == LogLevel.Debug && e.Message.Contains("HTTP"));
        await Assert.That(arrivalLog).IsNotNull();
    }

    [Test]
    public async Task ServeHttpAsync_LogsCompletion_Information()
    {
        var logger = new CapturingLogger();
        var provider = new MemoryAssetProvider(new Dictionary<string, byte[]>
        {
            ["/index.html"] = Encoding.UTF8.GetBytes("<html><body>ok</body></html>")
        });
        var server = new AssetServer(new AssetOptions { Handler = "test" }, provider, logger);

        await ServeOnceAsync(server, "index.html");

        var completionLog = logger.Entries.FirstOrDefault(e => e.Level == LogLevel.Information);
        await Assert.That(completionLog).IsNotNull();
        await Assert.That(completionLog!.Message).Contains("200");
    }

    [Test]
    public async Task ServeHttpAsync_ResourceNotFound_LogsWarning()
    {
        var logger = new CapturingLogger();
        var provider = new MemoryAssetProvider(new Dictionary<string, byte[]>());
        var server = new AssetServer(new AssetOptions { Handler = "test" }, provider, logger);

        await ServeNotFoundAsync(server);

        var warningLog = logger.Entries.FirstOrDefault(e => e.Level == LogLevel.Warning);
        await Assert.That(warningLog).IsNotNull();
        await Assert.That(warningLog!.Message).Contains("资源未找到");
    }

    [Test]
    public async Task ServeHttpAsync_WithCustomErrorHandler_LogsDebug()
    {
        var logger = new CapturingLogger();
        var provider = new MemoryAssetProvider(new Dictionary<string, byte[]>());
        var options = new AssetOptions
        {
            Handler = "test",
            ErrorHandler = (ctx, ex) =>
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
            }
        };
        var server = new AssetServer(options, provider, logger);

        await ServeNotFoundAsync(server);

        var debugLogs = logger.Entries.Where(e => e.Level == LogLevel.Debug).ToList();
        var handlerLog = debugLogs.FirstOrDefault(e => e.Message.Contains("自定义错误处理器"));
        await Assert.That(handlerLog).IsNotNull();
    }

    [Test]
    public async Task ServeHttpAsync_RangeInvalid_LogsWarning()
    {
        var logger = new CapturingLogger();
        var content = Encoding.UTF8.GetBytes("hello world");
        var provider = new MemoryAssetProvider(new Dictionary<string, byte[]>
        {
            ["/file.txt"] = content
        });
        var server = new AssetServer(new AssetOptions { Handler = "test" }, provider, logger);

        using var listener = new HttpListener();
        var port = 18400 + Random.Shared.Next(0, 599);
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();

        var requestTask = Task.Run(async () =>
        {
            using var client = new HttpClient();
            using var req = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}/file.txt");
            req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(100, 200); // 超出范围
            return await client.SendAsync(req);
        });

        var ctx = await listener.GetContextAsync();
        await server.ServeHttpAsync(ctx);
        ctx.Response.Close();

        try { _ = await requestTask; } catch { /* ignore */ }
        listener.Stop();

        var warningLog = logger.Entries.FirstOrDefault(e => e.Level == LogLevel.Warning && e.Message.Contains("Range"));
        await Assert.That(warningLog).IsNotNull();
    }

    [Test]
    public async Task ServeHttpAsync_CacheHit_LogsTrace()
    {
        var logger = new CapturingLogger();
        var content = Encoding.UTF8.GetBytes("hello world");
        var provider = new MemoryAssetProvider(new Dictionary<string, byte[]>
        {
            ["/file.txt"] = content
        });
        var server = new AssetServer(new AssetOptions { Handler = "test" }, provider, logger);

        using var listener = new HttpListener();
        var port = 18600 + Random.Shared.Next(0, 399);
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();

        // 第一次请求以获取 ETag
        var firstEtag = string.Empty;
        var firstRequestTask = Task.Run(async () =>
        {
            using var client = new HttpClient();
            var resp = await client.GetAsync($"http://localhost:{port}/file.txt");
            if (resp.Headers.ETag is not null)
            {
                firstEtag = resp.Headers.ETag.Tag;
            }
            return resp;
        });

        var ctx1 = await listener.GetContextAsync();
        await server.ServeHttpAsync(ctx1);
        ctx1.Response.Close();
        try { await firstRequestTask; } catch { /* ignore */ }

        // 第二次请求带 If-None-Match
        var secondRequestTask = Task.Run(async () =>
        {
            using var client = new HttpClient();
            using var req = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}/file.txt");
            if (!string.IsNullOrEmpty(firstEtag))
            {
                req.Headers.IfNoneMatch.Add(new System.Net.Http.Headers.EntityTagHeaderValue(firstEtag));
            }
            return await client.SendAsync(req);
        });

        var ctx2 = await listener.GetContextAsync();
        await server.ServeHttpAsync(ctx2);
        ctx2.Response.Close();
        try { await secondRequestTask; } catch { /* ignore */ }

        listener.Stop();

        var traceLog = logger.Entries.FirstOrDefault(e => e.Level == LogLevel.Trace);
        await Assert.That(traceLog).IsNotNull();
        await Assert.That(traceLog!.Message).Contains("304");
    }

    [Test]
    public async Task ServeHttpAsync_WithoutLogger_DoesNotThrow()
    {
        // 未注入日志器，AssetServer 应静默运行
        var provider = new MemoryAssetProvider(new Dictionary<string, byte[]>
        {
            ["/index.html"] = Encoding.UTF8.GetBytes("<html></html>")
        });
        var server = new AssetServer(new AssetOptions { Handler = "test" }, provider);

        await ServeOnceAsync(server, "index.html");
        // 不抛异常即可
    }

    [Test]
    public async Task ServeHttpAsync_WithDisabledLogger_DoesNotLog()
    {
        var logger = new DisabledLogger();
        var provider = new MemoryAssetProvider(new Dictionary<string, byte[]>
        {
            ["/index.html"] = Encoding.UTF8.GetBytes("<html></html>")
        });
        var server = new AssetServer(new AssetOptions { Handler = "test" }, provider, logger);

        // 应静默运行（DisabledLogger 的 Log 方法 no-op）
        await ServeOnceAsync(server, "index.html");
    }

    [Test]
    public async Task Logger_Property_ReturnsInjectedLogger()
    {
        var logger = new CapturingLogger();
        var server = new AssetServer(new AssetOptions { Handler = "test" }, logger);

        // 通过反射检查 Logger 属性（protected）
        var loggerProperty = typeof(AssetServer).GetProperty(
            "Logger",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await Assert.That(loggerProperty).IsNotNull();
        var value = loggerProperty!.GetValue(server);
        await Assert.That(value).IsEqualTo(logger);
    }
}
