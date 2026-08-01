using LyboxApp.Services.Core;
using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Plugins;

namespace LyboxApp.Plugins;

/// <summary>
/// 模板插件。演示最小插件结构：注册导航项与一个 [Binding] 服务。
/// 对应 LYBox 的 Template 示例插件。
/// </summary>
public class TemplatePlugin : IPlugin
{
    /// <summary>插件名称。</summary>
    public string Name => "plugin-template";

    /// <summary>注册服务、导航项与清单。</summary>
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<TemplateService>();
        services.AddSingleton(new NavItem { Key = "demo-template", TitleKey = "nav.template", Icon = "document", Order = 110, PluginId = "plugin-template" });
        services.AddSingleton(new PluginManifest
        {
            Id = "plugin-template",
            Name = "模板",
            Author = "LYBox",
            Version = "1.0.0",
            Description = "最小插件示例：展示一个页面与一个回显绑定方法。",
            Category = "Demo",
            Route = "demo-template",
        });
    }

    /// <summary>无需额外配置。</summary>
    public void Configure(IPluginContext context)
    {
    }
}

/// <summary>
/// 模板服务。提供回显与示例信息。
/// </summary>
public class TemplateService
{
    /// <summary>回显输入文本。</summary>
    [Binding]
    public string Echo(string text) => $"回显: {text}";

    /// <summary>返回示例信息。</summary>
    [Binding]
    public Dictionary<string, string> GetInfo() => new()
    {
        ["plugin"] = "plugin-template",
        ["purpose"] = "最小插件模板",
        ["hint"] = "复制此插件可快速创建新功能项。",
    };
}
