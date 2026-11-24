using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wails.Net.Application;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Options;
using Wails.Net.Demo.ResDownloader.Services;

// Demo: Wails.Net.Demo.ResDownloader
// 对标 https://github.com/putyy/res-downloader 的核心能力：
//   1. 本地 MITM 代理（HTTP/HTTPS 中间人），支持导出/安装自签根证书；
//   2. 按 content-type / 扩展名 / 平台域名嗅探视频、音频、图片、m3u8 资源；
//   3. 资源列表 + 直链下载 + m3u8（含 AES-128 解密）下载，进度事件推送；
//   4. 系统代理一键开关（退出自动还原）、资源类型筛选、保存目录等设置。
var isDebugMode = DebugMode.IsEnabled(args);

var builder = DesktopApplicationBuilder.CreateBuilder(args);

builder.Configure(options =>
{
    options.ApplicationName = "Wails.Net 资源嗅探下载器";
    options.SingleInstance = true;

    // 纯 HTML/CSS/JS 前端，无需构建
    options.Assets.RootPath = "frontend";
    options.Assets.DefaultDocument = "index.html";
    options.Assets.EnableSpaFallback = true;
});

// 业务服务注册到 DI 容器
builder.Services.AddSingleton<SettingsStore>();
builder.Services.AddSingleton<CertificateAuthority>();
builder.Services.AddSingleton<ResourceRepository>();
builder.Services.AddSingleton<LocalProxyServer>();
builder.Services.AddSingleton<DownloadManager>();
builder.Services.AddSingleton<ResDownloaderService>();

// 打开下载目录 / 在浏览器打开资源直链由 ResDownloaderService 内部调用系统默认程序完成，
// 不额外引入插件包，保持示例只依赖 Wails.Net.Sdk 一个 NuGet 包。

builder.Logging.SetMinimumLevel(isDebugMode ? LogLevel.Debug : LogLevel.Information);
builder.Logging.AddFilter("Microsoft", isDebugMode ? LogLevel.Information : LogLevel.Warning);

builder.UseWailsPlatform();

var desktopApp = builder.Build();
var app = desktopApp.Application;

app.Options.EnableDefaultContextMenu = true;

// 绑定服务：前端通过 wails.call('ResDownloaderService.方法名', [...]) 调用
app.RegisterBindings<ResDownloaderService>();

// 应用启动后创建主窗口
app.Options.OnAfterStart = () =>
{
    var mainWindow = app.CreateWebviewWindow(new WebviewWindowOptions
    {
        Name = "main",
        Title = isDebugMode
            ? "Wails.Net 资源嗅探下载器 (Debug)"
            : "Wails.Net 资源嗅探下载器",
        Width = 1280,
        Height = 820,
        MinWidth = 960,
        MinHeight = 640,
        Resizable = true,
        Maximisable = true,
        Minimisable = true,
    });

    if (isDebugMode)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(500);
            try
            {
                mainWindow.OpenDevTools();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ResDownloader] DevTools 打开失败：{ex.Message}");
            }
        });
    }
};

// 退出前还原系统代理，避免关闭应用后无法上网（res-downloader 的常见问题）
app.Options.OnShutdown = () =>
{
    try
    {
        desktopApp.Services.GetRequiredService<ResDownloaderService>().Shutdown();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ResDownloader] 退出清理失败：{ex.Message}");
    }
};

await desktopApp.RunAsync();
