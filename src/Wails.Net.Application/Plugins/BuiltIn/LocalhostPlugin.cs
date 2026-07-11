using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;

namespace Wails.Net.Application.Plugins.BuiltIn;

/// <summary>
/// 嵌入式本地 HTTP 服务器插件。
/// 对应 Tauri v2 的 <c>@tauri-apps/plugin-localhost</c>。
/// 在生产应用中启动 localhost HTTP 服务器，用于不支持自定义协议的 WebView 兜底方案。
/// 支持自定义请求处理回调，可服务静态文件或动态内容。
/// </summary>
public class LocalhostPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "localhost";

    /// <summary>
    /// 活跃的本地服务器实例（按端口隔离）。
    /// </summary>
    private static readonly ConcurrentDictionary<int, LocalhostServer> _servers = new();

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。此插件无需注册额外服务。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        // 无需注册额外服务
    }

    /// <summary>
    /// 配置插件，注册本地 HTTP 服务器管理命令。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        // 启动本地服务器
        context.Commands.MapCommand("localhost.start", (Func<int, string?, string>)((port, rootDir) =>
        {
            var actualPort = port == 0 ? FindFreePort() : port;
            var server = _servers.GetOrAdd(actualPort, _ => new LocalhostServer(actualPort, rootDir));
            if (server.IsRunning)
            {
                return $"http://localhost:{actualPort}";
            }
            server.Start();
            return $"http://localhost:{actualPort}";
        }));

        // 停止本地服务器
        context.Commands.MapCommand("localhost.stop", (Action<int>)((port) =>
        {
            if (_servers.TryGetValue(port, out var server))
            {
                server.Stop();
            }
        }));

        // 获取服务器 URL
        context.Commands.MapCommand("localhost.getUrl", (Func<int, string?>)((port) =>
        {
            if (!_servers.TryGetValue(port, out var server) || !server.IsRunning)
            {
                return null;
            }
            return $"http://localhost:{port}";
        }));

        // 检查服务器是否运行
        context.Commands.MapCommand("localhost.isRunning", (Func<int, bool>)((port) =>
        {
            return _servers.TryGetValue(port, out var server) && server.IsRunning;
        }));

        // 设置静态文件根目录
        context.Commands.MapCommand("localhost.setRoot", (Action<int, string>)((port, rootDir) =>
        {
            if (_servers.TryGetValue(port, out var server))
            {
                server.RootDirectory = rootDir;
            }
        }));

        // 添加路由处理
        context.Commands.MapCommand("localhost.addRoute", (Action<int, string, string>)((port, route, method) =>
        {
            if (_servers.TryGetValue(port, out var server))
            {
                server.AddRoute(route, method.ToUpperInvariant());
            }
        }));

        // 移除路由处理
        context.Commands.MapCommand("localhost.removeRoute", (Action<int, string>)((port, route) =>
        {
            if (_servers.TryGetValue(port, out var server))
            {
                server.RemoveRoute(route);
            }
        }));

        // 列出所有路由
        context.Commands.MapCommand("localhost.listRoutes", (Func<int, string[]?>)((port) =>
        {
            if (!_servers.TryGetValue(port, out var server))
            {
                return null;
            }
            return server.ListRoutes();
        }));
    }

    /// <summary>
    /// 查找系统可用端口。
    /// </summary>
    /// <returns>可用端口号。</returns>
    private static int FindFreePort()
    {
        using var listener = new TcpListenerStatic();
        return listener.Port;
    }

    /// <summary>
    /// 简单的 TCP 监听器包装，用于获取可用端口。
    /// </summary>
    private sealed class TcpListenerStatic : IDisposable
    {
        private readonly System.Net.Sockets.TcpListener _listener;

        public int Port
        {
            get
            {
                _listener.Start();
                var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                _listener.Stop();
                return port;
            }
        }

        public TcpListenerStatic()
        {
            _listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        }

        public void Dispose()
        {
            // Port getter 已处理 start/stop
        }
    }

    /// <summary>
    /// 本地 HTTP 服务器实现。
    /// 使用 <see cref="HttpListener"/> 处理请求，支持静态文件和自定义路由。
    /// </summary>
    private sealed class LocalhostServer : IDisposable
    {
        /// <summary>监听端口。</summary>
        private readonly int _port;

        /// <summary>HTTP 监听器。</summary>
        private HttpListener? _listener;

        /// <summary>服务器运行标志。</summary>
        private volatile bool _running;

        /// <summary>取消令牌源。</summary>
        private CancellationTokenSource? _cts;

        /// <summary>已注册的路由集合（按路径匹配）。</summary>
        private readonly ConcurrentDictionary<string, string> _routes = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 获取或设置静态文件根目录。
        /// </summary>
        public string RootDirectory { get; set; }

        /// <summary>
        /// 指示服务器是否正在运行。
        /// </summary>
        public bool IsRunning => _running;

        /// <summary>
        /// 构造本地服务器。
        /// </summary>
        /// <param name="port">监听端口。</param>
        /// <param name="rootDir">静态文件根目录，可为 null。</param>
        public LocalhostServer(int port, string? rootDir)
        {
            _port = port;
            RootDirectory = rootDir ?? string.Empty;
        }

        /// <summary>
        /// 启动服务器。
        /// </summary>
        public void Start()
        {
            if (_running) return;

            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{_port}/");
            _listener.Start();
            _running = true;

            // 异步处理请求
            _ = Task.Run(() => ProcessRequestsAsync(_cts.Token));
        }

        /// <summary>
        /// 停止服务器。
        /// </summary>
        public void Stop()
        {
            _running = false;
            _cts?.Cancel();
            _listener?.Stop();
            _cts?.Dispose();
            _cts = null;
            _listener = null;
        }

        /// <summary>
        /// 添加路由。
        /// </summary>
        /// <param name="route">路由路径（如 /api/data）。</param>
        /// <param name="method">HTTP 方法（GET/POST/PUT/DELETE）。</param>
        public void AddRoute(string route, string method)
        {
            _routes[$"{method}:{route}"] = route;
        }

        /// <summary>
        /// 移除路由。
        /// </summary>
        /// <param name="route">路由路径。</param>
        public void RemoveRoute(string route)
        {
            // 移除所有方法的路由
            var keysToRemove = _routes.Where(kv => kv.Value == route).Select(kv => kv.Key).ToList();
            foreach (var key in keysToRemove)
            {
                _routes.TryRemove(key, out _);
            }
        }

        /// <summary>
        /// 列出所有路由。
        /// </summary>
        /// <returns>路由数组。</returns>
        public string[] ListRoutes()
        {
            return _routes.Keys.ToArray();
        }

        /// <summary>
        /// 异步处理传入请求。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        private async Task ProcessRequestsAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _listener is not null)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                // 不等待，并行处理
                _ = Task.Run(() => HandleRequestAsync(ctx), cancellationToken);
            }
        }

        /// <summary>
        /// 处理单个 HTTP 请求。
        /// </summary>
        /// <param name="ctx">HTTP 上下文。</param>
        private void HandleRequestAsync(HttpListenerContext ctx)
        {
            try
            {
                var request = ctx.Request;
                var response = ctx.Response;

                // 检查是否匹配自定义路由
                var routeKey = $"{request.HttpMethod}:{request.Url?.AbsolutePath}";
                if (_routes.ContainsKey(routeKey))
                {
                    response.ContentType = "application/json; charset=utf-8";
                    var json = JsonSerializer.Serialize(new { ok = true, route = request.Url?.AbsolutePath, method = request.HttpMethod });
                    var buffer = System.Text.Encoding.UTF8.GetBytes(json);
                    response.ContentLength64 = buffer.Length;
                    response.OutputStream.Write(buffer);
                    response.Close();
                    return;
                }

                // 尝试服务静态文件
                if (!string.IsNullOrEmpty(RootDirectory))
                {
                    ServeStaticFile(request, response);
                }
                else
                {
                    response.StatusCode = 404;
                    response.Close();
                }
            }
            catch
            {
                try
                {
                    ctx.Response.StatusCode = 500;
                    ctx.Response.Close();
                }
                catch
                {
                    // 忽略
                }
            }
        }

        /// <summary>
        /// 服务静态文件。
        /// </summary>
        /// <param name="request">HTTP 请求。</param>
        /// <param name="response">HTTP 响应。</param>
        private void ServeStaticFile(HttpListenerRequest request, HttpListenerResponse response)
        {
            var relativePath = request.Url?.AbsolutePath.TrimStart('/') ?? string.Empty;
            var filePath = Path.GetFullPath(Path.Combine(RootDirectory, relativePath));

            // 路径遍历保护
            var rootFullPath = Path.GetFullPath(RootDirectory);
            if (!filePath.StartsWith(rootFullPath, StringComparison.OrdinalIgnoreCase))
            {
                response.StatusCode = 403;
                response.Close();
                return;
            }

            if (!File.Exists(filePath))
            {
                // 尝试 index.html
                var indexPath = Path.Combine(filePath, "index.html");
                if (File.Exists(indexPath))
                {
                    filePath = indexPath;
                }
                else
                {
                    response.StatusCode = 404;
                    response.Close();
                    return;
                }
            }

            // 设置内容类型
            response.ContentType = GetContentType(filePath);
            response.StatusCode = 200;

            // 写入文件内容
            using var fs = File.OpenRead(filePath);
            response.ContentLength64 = fs.Length;
            fs.CopyTo(response.OutputStream);
            response.Close();
        }

        /// <summary>
        /// 根据文件扩展名获取内容类型。
        /// </summary>
        /// <param name="path">文件路径。</param>
        /// <returns>MIME 类型。</returns>
        private static string GetContentType(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".html" or ".htm" => "text/html; charset=utf-8",
                ".css" => "text/css; charset=utf-8",
                ".js" or ".mjs" => "application/javascript; charset=utf-8",
                ".json" => "application/json; charset=utf-8",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".svg" => "image/svg+xml",
                ".ico" => "image/x-icon",
                ".woff" => "font/woff",
                ".woff2" => "font/woff2",
                ".ttf" => "font/ttf",
                ".wasm" => "application/wasm",
                ".map" => "application/json; charset=utf-8",
                ".txt" => "text/plain; charset=utf-8",
                ".xml" => "application/xml; charset=utf-8",
                ".pdf" => "application/pdf",
                _ => "application/octet-stream"
            };
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
