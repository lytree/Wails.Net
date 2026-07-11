using System.Net;
using Wails.Net.Application.Options;

namespace Wails.Net.Application.Services;

/// <summary>
/// 文件服务，提供安全的本地文件读写与 HTTP 文件服务器功能。
/// 对应 Wails v3 Go 版本 pkg/services/fileserver。
/// 通过路径穿越防护确保所有文件操作限制在允许的根目录内；
/// HTTP 文件服务器将 URL 路径映射到根目录下的文件，支持 Content-Type 推断、Range 请求与中间件链。
/// </summary>
public class FileServerService : IServiceStartup, IServiceShutdown
{
    /// <summary>
    /// 允许的文件操作根目录。
    /// </summary>
    private string _rootPath;

    /// <summary>
    /// 线程安全锁，用于保护文件写入操作。
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// HTTP 监听器实例，未启动时为 null。
    /// </summary>
    private HttpListener? _httpListener;

    /// <summary>
    /// 已注册的中间件列表，按注册顺序执行。
    /// </summary>
    private readonly List<Func<HttpListenerContext, Func<Task>, Task>> _middlewares = new();

    /// <summary>
    /// 获取或设置允许的文件操作根目录。
    /// 在服务启动前设置以自定义根目录。
    /// </summary>
    public string RootPath
    {
        get => _rootPath;
        set => _rootPath = value;
    }

    /// <summary>
    /// 获取 HTTP 文件服务器是否正在运行。
    /// </summary>
    public bool IsHttpServerRunning => _httpListener is not null;

    /// <summary>
    /// 使用当前工作目录作为根目录构造文件服务实例。
    /// </summary>
    public FileServerService()
    {
        _rootPath = Directory.GetCurrentDirectory();
    }

    /// <summary>
    /// 使用指定根目录构造文件服务实例。
    /// </summary>
    /// <param name="rootPath">允许文件操作的根目录。</param>
    public FileServerService(string rootPath)
    {
        _rootPath = rootPath;
    }

    /// <summary>
    /// 服务启动，初始化根目录配置。
    /// </summary>
    /// <param name="options">应用选项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示启动操作的异步任务。</returns>
    public Task ServiceStartup(ApplicationOptions options, CancellationToken cancellationToken)
    {
        _rootPath = Path.GetFullPath(_rootPath);
        if (!Directory.Exists(_rootPath))
        {
            Directory.CreateDirectory(_rootPath);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 服务关闭，停止 HTTP 文件服务器（若正在运行）并清理资源。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示关闭操作的异步任务。</returns>
    public Task ServiceShutdown(CancellationToken cancellationToken)
    {
        StopHttpServer();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 读取指定路径的文件内容。
    /// </summary>
    /// <param name="path">相对于根目录的文件路径。</param>
    /// <returns>文件文本内容。</returns>
    /// <exception cref="UnauthorizedAccessException">路径超出根目录范围。</exception>
    /// <exception cref="FileNotFoundException">文件不存在。</exception>
    public string ReadFile(string path)
    {
        var fullPath = GetSafePath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"文件不存在: {path}", fullPath);
        }
        lock (_lock)
        {
            return File.ReadAllText(fullPath);
        }
    }

    /// <summary>
    /// 将内容写入指定路径的文件。
    /// 若文件已存在则覆盖。
    /// </summary>
    /// <param name="path">相对于根目录的文件路径。</param>
    /// <param name="content">要写入的内容。</param>
    /// <exception cref="UnauthorizedAccessException">路径超出根目录范围。</exception>
    public void WriteFile(string path, string content)
    {
        var fullPath = GetSafePath(path);
        var dir = Path.GetDirectoryName(fullPath);
        if (dir is not null && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        lock (_lock)
        {
            File.WriteAllText(fullPath, content);
        }
    }

    /// <summary>
    /// 检查指定路径的文件是否存在。
    /// </summary>
    /// <param name="path">相对于根目录的文件路径。</param>
    /// <returns>文件存在返回 true，否则返回 false。</returns>
    /// <exception cref="UnauthorizedAccessException">路径超出根目录范围。</exception>
    public bool FileExists(string path)
    {
        var fullPath = GetSafePath(path);
        return File.Exists(fullPath);
    }

    /// <summary>
    /// 删除指定路径的文件。
    /// 若文件不存在则不执行任何操作。
    /// </summary>
    /// <param name="path">相对于根目录的文件路径。</param>
    /// <exception cref="UnauthorizedAccessException">路径超出根目录范围。</exception>
    public void DeleteFile(string path)
    {
        var fullPath = GetSafePath(path);
        lock (_lock)
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
    }

    // ========================================================================
    // HTTP 文件服务器
    // ========================================================================

    /// <summary>
    /// 注册中间件。中间件按注册顺序执行，最后执行文件服务。
    /// 中间件可调用 <paramref name="next"/> 继续调用链，或直接返回以短路请求。
    /// </summary>
    /// <param name="middleware">中间件委托，接收 HTTP 上下文与下一个委托。</param>
    public void Use(Func<HttpListenerContext, Func<Task>, Task> middleware)
    {
        _middlewares.Add(middleware);
    }

    /// <summary>
    /// 启动 HTTP 文件服务器，监听指定端口。
    /// 监听地址为 <c>http://localhost:{port}/</c>，将 URL 路径映射到根目录下的文件。
    /// </summary>
    /// <param name="port">监听端口。</param>
    /// <exception cref="InvalidOperationException">HTTP 服务器已在运行。</exception>
    public void StartHttpServer(int port)
    {
        if (_httpListener is not null)
        {
            throw new InvalidOperationException("HTTP 文件服务器已在运行。");
        }

        var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        _httpListener = listener;

        _ = Task.Run(async () =>
        {
            while (true)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (HttpListenerException)
                {
                    // 监听器已停止
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                // 每个请求独立处理，支持并发
                _ = Task.Run(() => ProcessRequestAsync(context));
            }
        });
    }

    /// <summary>
    /// 停止 HTTP 文件服务器。若未运行则不执行任何操作。
    /// </summary>
    public void StopHttpServer()
    {
        if (_httpListener is null)
        {
            return;
        }

        try
        {
            _httpListener.Stop();
        }
        catch (ObjectDisposedException)
        {
            // 忽略已释放的监听器
        }

        try
        {
            _httpListener.Close();
        }
        catch (ObjectDisposedException)
        {
            // 忽略已释放的监听器
        }

        _httpListener = null;
    }

    /// <summary>
    /// 处理单个 HTTP 请求，按中间件注册顺序构建调用链，最后执行文件服务。
    /// </summary>
    /// <param name="context">HTTP 上下文。</param>
    /// <returns>表示请求处理的异步任务。</returns>
    private async Task ProcessRequestAsync(HttpListenerContext context)
    {
        try
        {
            Func<Task> pipeline = () => ServeFileAsync(context);

            // 按逆序构建中间件链，使注册顺序最早的中间件最先执行
            for (var i = _middlewares.Count - 1; i >= 0; i--)
            {
                var currentNext = pipeline;
                var currentMiddleware = _middlewares[i];
                pipeline = () => currentMiddleware(context, currentNext);
            }

            await pipeline();
        }
        catch (Exception)
        {
            try
            {
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            }
            catch (Exception)
            {
                // 响应已发送或连接已断开，忽略
            }
        }
        finally
        {
            try
            {
                context.Response.Close();
            }
            catch (Exception)
            {
                // 忽略关闭异常
            }
        }
    }

    /// <summary>
    /// 核心文件服务处理：将请求 URL 路径映射到根目录下的文件并返回内容。
    /// 支持路径穿越防护、Content-Type 推断、Range 请求与 404 处理。
    /// </summary>
    /// <param name="context">HTTP 上下文。</param>
    /// <returns>表示文件服务处理的异步任务。</returns>
    private async Task ServeFileAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        // 将 URL 路径映射到文件系统路径
        var urlPath = request.Url?.AbsolutePath ?? "/";
        urlPath = Uri.UnescapeDataString(urlPath).TrimStart('/');

        string fullPath;
        try
        {
            fullPath = GetSafePath(urlPath);
        }
        catch (UnauthorizedAccessException)
        {
            response.StatusCode = (int)HttpStatusCode.Forbidden;
            return;
        }

        if (!File.Exists(fullPath))
        {
            response.StatusCode = (int)HttpStatusCode.NotFound;
            return;
        }

        response.ContentType = GetContentType(fullPath);

        var fileLength = new FileInfo(fullPath).Length;

        // Range 请求支持（简化实现：bytes=start-end 或 bytes=start-）
        var rangeHeader = request.Headers["Range"];
        if (!string.IsNullOrEmpty(rangeHeader) &&
            rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            var rangeSpec = rangeHeader["bytes=".Length..];
            var dashIndex = rangeSpec.IndexOf('-');
            if (dashIndex > 0 &&
                long.TryParse(rangeSpec.AsSpan(0, dashIndex), out var start))
            {
                var endSpan = rangeSpec.AsSpan(dashIndex + 1);
                long end = endSpan.IsEmpty
                    ? fileLength - 1
                    : (long.TryParse(endSpan, out var parsedEnd) ? parsedEnd : fileLength - 1);

                if (start < fileLength && start <= end)
                {
                    if (end >= fileLength)
                    {
                        end = fileLength - 1;
                    }

                    var length = end - start + 1;
                    response.StatusCode = (int)HttpStatusCode.PartialContent;
                    response.ContentLength64 = length;
                    response.Headers["Content-Range"] = $"bytes {start}-{end}/{fileLength}";

                    await using var fs = new FileStream(
                        fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    fs.Seek(start, SeekOrigin.Begin);
                    var buffer = new byte[81920];
                    long remaining = length;
                    while (remaining > 0)
                    {
                        var toRead = (int)Math.Min(buffer.Length, remaining);
                        var read = await fs.ReadAsync(buffer.AsMemory(0, toRead));
                        if (read == 0)
                        {
                            break;
                        }

                        await response.OutputStream.WriteAsync(buffer.AsMemory(0, read));
                        remaining -= read;
                    }

                    return;
                }
            }
        }

        // 完整文件响应
        response.StatusCode = (int)HttpStatusCode.OK;
        response.ContentLength64 = fileLength;
        await using var fullStream = new FileStream(
            fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var buf = new byte[81920];
        int readCount;
        while ((readCount = await fullStream.ReadAsync(buf.AsMemory())) > 0)
        {
            await response.OutputStream.WriteAsync(buf.AsMemory(0, readCount));
        }
    }

    /// <summary>
    /// 根据文件扩展名推断 Content-Type。
    /// </summary>
    /// <param name="path">文件路径。</param>
    /// <returns>对应的 MIME 类型字符串。</returns>
    private static string GetContentType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".html" or ".htm" => "text/html",
            ".js" => "application/javascript",
            ".css" => "text/css",
            ".json" => "application/json",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };
    }

    /// <summary>
    /// 计算安全路径，防止路径穿越攻击。
    /// 确保解析后的完整路径位于根目录内。
    /// </summary>
    /// <param name="relativePath">相对路径。</param>
    /// <returns>解析后的完整路径。</returns>
    /// <exception cref="UnauthorizedAccessException">路径超出根目录范围。</exception>
    private string GetSafePath(string relativePath)
    {
        var root = Path.GetFullPath(_rootPath);
        var rootWithSep = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        var fullPath = Path.IsPathRooted(relativePath)
            ? Path.GetFullPath(relativePath)
            : Path.GetFullPath(relativePath, root);

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!fullPath.StartsWith(rootWithSep, comparison) && fullPath != root)
        {
            throw new UnauthorizedAccessException($"路径穿越攻击被阻止: {relativePath}");
        }

        return fullPath;
    }
}
