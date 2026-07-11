using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;

namespace Wails.Net.Application.Plugins.BuiltIn;

/// <summary>
/// 文件上传/下载插件，提供带进度回调的文件传输能力。
/// 对应 Tauri v2 的 <c>@tauri-apps/plugin-upload</c>。
/// 支持从 URL 下载文件到本地路径，以及上传本地文件到 URL。
/// 传输过程中通过事件分发进度更新。
/// </summary>
public class UploadPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "upload";

    /// <summary>
    /// 静态 HttpClient 实例，复用连接池。
    /// </summary>
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(30) };

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。此插件无需注册额外服务。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        // 无需注册额外服务
    }

    /// <summary>
    /// 配置插件，注册文件上传/下载相关命令。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        // 下载文件到本地路径
        context.Commands.MapCommand("upload.download",
            (Func<ICommandContext, string, string, Task<bool>>)(async (ctx, url, path) =>
        {
            return await DownloadFileAsync(url, path);
        }));

        // 上传本地文件到 URL
        context.Commands.MapCommand("upload.upload",
            (Func<ICommandContext, string, string, Task<bool>>)(async (ctx, url, filePath) =>
        {
            return await UploadFileAsync(url, filePath);
        }));

        // 下载文件到本地路径（带进度回调）
        context.Commands.MapCommand("upload.downloadWithProgress",
            (Func<ICommandContext, string, string, Task<bool>>)(async (ctx, url, path) =>
        {
            return await DownloadWithProgressAsync(url, path);
        }));

        // 上传本地文件到 URL（带进度回调）
        context.Commands.MapCommand("upload.uploadWithProgress",
            (Func<ICommandContext, string, string, Task<bool>>)(async (ctx, url, filePath) =>
        {
            return await UploadWithProgressAsync(url, filePath);
        }));
    }

    /// <summary>
    /// 从 URL 下载文件到本地路径。
    /// </summary>
    /// <param name="url">下载 URL。</param>
    /// <param name="path">本地文件路径。</param>
    /// <returns>成功返回 true，失败返回 false。</returns>
    public static async Task<bool> DownloadFileAsync(string url, string path)
    {
        try
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await using var fs = File.Create(path);
            await response.Content.CopyToAsync(fs);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 上传本地文件到 URL。
    /// </summary>
    /// <param name="url">上传目标 URL。</param>
    /// <param name="filePath">本地文件路径。</param>
    /// <returns>成功返回 true，失败返回 false。</returns>
    public static async Task<bool> UploadFileAsync(string url, string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return false;
            }

            await using var fileStream = File.OpenRead(filePath);
            using var content = new StreamContent(fileStream);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            using var response = await _httpClient.PostAsync(url, content);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 从 URL 下载文件到本地路径，并通过事件分发下载进度。
    /// </summary>
    /// <param name="url">下载 URL。</param>
    /// <param name="path">本地文件路径。</param>
    /// <returns>成功返回 true，失败返回 false。</returns>
    public static async Task<bool> DownloadWithProgressAsync(string url, string path)
    {
        try
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await using var fs = File.Create(path);
            await using var stream = await response.Content.ReadAsStreamAsync();

            var buffer = new byte[8192];
            long bytesRead = 0;
            int read;

            while ((read = await stream.ReadAsync(buffer)) > 0)
            {
                await fs.WriteAsync(buffer.AsMemory(0, read));
                bytesRead += read;

                // 通过事件分发下载进度
                var progress = totalBytes > 0
                    ? (double)bytesRead / totalBytes
                    : 0;

                EmitProgress("download", path, bytesRead, totalBytes, progress);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 上传本地文件到 URL，并通过事件分发上传进度。
    /// </summary>
    /// <param name="url">上传目标 URL。</param>
    /// <param name="filePath">本地文件路径。</param>
    /// <returns>成功返回 true，失败返回 false。</returns>
    public static async Task<bool> UploadWithProgressAsync(string url, string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return false;
            }

            var fileInfo = new FileInfo(filePath);
            var totalBytes = fileInfo.Length;

            using var fileStream = File.OpenRead(filePath);
            using var progressStream = new ProgressStream(fileStream, (bytesSent) =>
            {
                var progress = totalBytes > 0 ? (double)bytesSent / totalBytes : 0;
                EmitProgress("upload", filePath, bytesSent, (long)totalBytes, progress);
            });

            using var content = new StreamContent(progressStream);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            using var response = await _httpClient.PostAsync(url, content);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 分发传输进度事件到应用事件系统。
    /// </summary>
    /// <param name="type">传输类型（"download" 或 "upload"）。</param>
    /// <param name="path">文件路径。</param>
    /// <param name="transferred">已传输字节数。</param>
    /// <param name="total">总字节数（未知为 -1）。</param>
    /// <param name="progress">进度比例（0.0-1.0）。</param>
    private static void EmitProgress(string type, string path, long transferred, long total, double progress)
    {
        var data = JsonSerializer.Serialize(new
        {
            type,
            path,
            transferred,
            total,
            progress = Math.Round(progress, 4)
        });

        Application.Get()?.Events.Emit("upload:progress", data, null);
    }

    /// <summary>
    /// 带进度回调的流包装器，读取时触发进度回调。
    /// </summary>
    private sealed class ProgressStream : Stream
    {
        private readonly Stream _inner;
        private readonly Action<long> _onProgress;
        private long _bytesRead;

        public ProgressStream(Stream inner, Action<long> onProgress)
        {
            _inner = inner;
            _onProgress = onProgress;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, count);
            _bytesRead += read;
            _onProgress(_bytesRead);
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await _inner.ReadAsync(buffer, cancellationToken);
            _bytesRead += read;
            _onProgress(_bytesRead);
            return read;
        }

        public override void Flush() => _inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
