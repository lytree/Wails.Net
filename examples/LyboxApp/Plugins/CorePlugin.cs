using LyboxApp.Services.Core;
using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Plugins;

namespace LyboxApp.Plugins;

/// <summary>
/// 核心插件。注册设置存储、事件总线、任务注册表与核心 [Binding] 服务，
/// 并贡献系统级导航项（仪表盘 / 插件 / 设置 / 任务）。
/// </summary>
public class CorePlugin : IPlugin
{
    /// <summary>插件名称。</summary>
    public string Name => "lybox-core";

    /// <summary>注册核心 DI 服务与导航项。</summary>
    public void ConfigureServices(IServiceCollection services)
    {
        // 核心单例
        services.AddSingleton<SettingsStore>();
        services.AddSingleton<LyboxEventBus>();
        services.AddSingleton<TaskRegistry>();
        services.AddSingleton<LyboxCoreService>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<LocalizationService>();

        // 系统导航项
        services.AddSingleton(new NavItem { Key = "dashboard", TitleKey = "nav.dashboard", Icon = "home", Order = 10, PluginId = "core" });
        services.AddSingleton(new NavItem { Key = "plugins", TitleKey = "nav.plugins", Icon = "puzzle", Order = 20, PluginId = "core" });
        services.AddSingleton(new NavItem { Key = "tasks", TitleKey = "nav.tasks", Icon = "tasks", Order = 80, PluginId = "core" });
        services.AddSingleton(new NavItem { Key = "settings", TitleKey = "nav.settings", Icon = "settings", Order = 90, PluginId = "core" });

        // 核心清单（出现在插件管理页）
        services.AddSingleton(new PluginManifest
        {
            Id = "core",
            Name = "核心框架",
            Author = "LYBox",
            Version = "1.0.0",
            Description = "插件管理、导航注册、设置存储、本地化与任务注册表等基础设施。",
            Category = "System",
            Route = "dashboard",
        });
    }

    /// <summary>核心插件无需额外配置。</summary>
    public void Configure(IPluginContext context)
    {
    }
}
