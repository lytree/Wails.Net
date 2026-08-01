using LyboxApp.Services.Core;
using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Plugins;

namespace LyboxApp.Plugins;

/// <summary>
/// 日期时间插件。演示日期时间格式化与各部分提取。
/// 对应 LYBox 的 DateTime 功能项。
/// </summary>
public class DateTimePlugin : IPlugin
{
    /// <summary>插件名称。</summary>
    public string Name => "plugin-datetime";

    /// <summary>注册服务、导航项与清单。</summary>
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<DateTimeService>();
        services.AddSingleton(new NavItem { Key = "demo-datetime", TitleKey = "nav.datetime", Icon = "clock", Order = 130, PluginId = "plugin-datetime" });
        services.AddSingleton(new PluginManifest
        {
            Id = "plugin-datetime",
            Name = "日期时间",
            Author = "LYBox",
            Version = "1.0.0",
            Description = "演示本地时间、UTC 时间与自定义格式化的日期时间绑定。",
            Category = "Demo",
            Route = "demo-datetime",
        });
    }

    /// <summary>无需额外配置。</summary>
    public void Configure(IPluginContext context)
    {
    }
}

/// <summary>
/// 日期时间服务。
/// </summary>
public class DateTimeService
{
    /// <summary>按指定格式返回当前本地时间。</summary>
    [Binding]
    public string Now(string format) => DateTime.Now.ToString(string.IsNullOrWhiteSpace(format) ? "yyyy-MM-dd HH:mm:ss" : format);

    /// <summary>返回 UTC 时间（ISO 8601）。</summary>
    [Binding]
    public string UtcNow() => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

    /// <summary>返回日期各组成部分。</summary>
    [Binding]
    public Dictionary<string, int> Parts() => new()
    {
        ["year"] = DateTime.Now.Year,
        ["Month"] = DateTime.Now.Month,
        ["day"] = DateTime.Now.Day,
        ["hour"] = DateTime.Now.Hour,
        ["minute"] = DateTime.Now.Minute,
        ["second"] = DateTime.Now.Second,
        ["dayOfWeek"] = (int)DateTime.Now.DayOfWeek,
    };
}
