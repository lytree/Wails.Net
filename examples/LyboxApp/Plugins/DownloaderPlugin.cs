using LyboxApp.Services.Core;
using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Plugins;

namespace LyboxApp.Plugins;

/// <summary>
/// 下载器插件。演示通过 HttpClient 下载文件并实时报告进度到任务托盘。
/// 对应 LYBox 的 Downloader 功能项。
/// </summary>
public class DownloaderPlugin : IPlugin
{
    /// <summary>插件名称。</summary>
    public string Name => "plugin-downloader";

    /// <summary>注册服务、导航项与清单。</summary>
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<DownloaderService>();
        services.AddSingleton(new NavItem { Key = "demo-downloader", TitleKey = "nav.downloader", Icon = "download", Order = 150, PluginId = "plugin-downloader" });
        services.AddSingleton(new PluginManifest
        {
            Id = "plugin-downloader",
            Name = "下载器",
            Author = "LYBox",
            Version = "1.0.0",
            Description = "演示文件下载与进度上报（接入任务注册表，可取消）。",
            Category = "Demo",
            Route = "demo-downloader",
        });
    }

    /// <summary>无需额外配置。</summary>
    public void Configure(IPluginContext context)
    {
    }
}

/// <summary>
/// 下载器服务。使用 HttpClient 流式下载并上报进度。
/// </summary>
public class DownloaderService
{
    private static readonly HttpClient _http = new();
    private readonly TaskRegistry _tasks;

    /// <summary>初始化下载器服务。</summary>
    public DownloaderService(TaskRegistry tasks)
    {
        _tasks = tasks;
    }

    /// <summary>
    /// 下载指定 URL 到本地 downloads 目录，实时上报进度。
    /// </summary>
    /// <param name="url">下载地址。</param>
    /// <param name="destName">目标文件名（可选）。</param>
    /// <returns>包含任务 Id 与保存路径的字典。</returns>
    [Binding]
    public async Task<Dictionary<string, string>> Download(string url, string? destName)
    {
        string name;
        try
        {
            var uri = new Uri(url);
            name = string.IsNullOrWhiteSpace(destName)
                ? Path.GetFileName(uri.AbsolutePath)
                : destName!;
        }
        catch
        {
            name = destName ?? "download.bin";
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = "download.bin";
        }

        var task = _tasks.Start($"下载 {name}");
        var token = _tasks.Token(task.Id);
        var dir = Path.Combine(AppContext.BaseDirectory, "downloads");
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, name);

        try
        {
            using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            resp.EnsureSuccessStatusCode();

            var total = resp.Content.Headers.ContentLength ?? -1L;
            await using var stream = await resp.Content.ReadAsStreamAsync(token);
            await using var fs = File.Create(dest);

            var buffer = new byte[8192];
            long read = 0;
            var lastPct = -1;
            int n;
            while ((n = await stream.ReadAsync(buffer, token)) > 0)
            {
                await fs.WriteAsync(buffer.AsMemory(0, n), token);
                read += n;
                if (total > 0)
                {
                    var pct = (int)(read * 100 / total);
                    if (pct != lastPct)
                    {
                        lastPct = pct;
                        _tasks.Update(task.Id, pct, $"{read / 1024} / {total / 1024} KB");
                    }
                }
            }

            _tasks.Finish(task.Id, "done", $"已保存到 {dest}");
            return new Dictionary<string, string> { ["taskId"] = task.Id, ["path"] = dest, ["name"] = name };
        }
        catch (OperationCanceledException)
        {
            _tasks.Finish(task.Id, "canceled", "已取消");
            throw;
        }
        catch (Exception ex)
        {
            _tasks.Finish(task.Id, "failed", ex.Message);
            throw;
        }
    }
}
