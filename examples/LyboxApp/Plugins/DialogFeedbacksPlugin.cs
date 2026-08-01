using LyboxApp.Services.Core;
using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Plugins;

namespace LyboxApp.Plugins;

/// <summary>
/// 对话框反馈插件。后端可向前端弹出信息/成功/警告/错误/确认对话框。
/// 对应 LYBox 的 DialogFeedbacks 功能项。
/// </summary>
public class DialogFeedbacksPlugin : IPlugin
{
    /// <summary>插件名称。</summary>
    public string Name => "plugin-dialog";

    /// <summary>注册服务、导航项与清单。</summary>
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<DialogFeedbacksService>();
        services.AddSingleton(new NavItem { Key = "demo-dialog", TitleKey = "nav.dialog", Icon = "chat", Order = 140, PluginId = "plugin-dialog" });
        services.AddSingleton(new PluginManifest
        {
            Id = "plugin-dialog",
            Name = "对话框反馈",
            Author = "LYBox",
            Version = "1.0.0",
            Description = "演示信息、成功、警告、错误与确认对话框（后端触发，前端事件驱动）。",
            Category = "Demo",
            Route = "demo-dialog",
        });
    }

    /// <summary>无需额外配置。</summary>
    public void Configure(IPluginContext context)
    {
    }
}

/// <summary>
/// 对话框反馈服务。通过事件总线向前端广播对话框请求。
/// </summary>
public class DialogFeedbacksService
{
    private readonly LyboxEventBus _bus;

    /// <summary>初始化对话框反馈服务。</summary>
    public DialogFeedbacksService(LyboxEventBus bus)
    {
        _bus = bus;
    }

    /// <summary>
    /// 弹出一个对话框。返回对话框 Id，前端通过 lybox:dialog-result 事件回传结果。
    /// </summary>
    [Binding]
    public string ShowDialog(string type, string title, string message, string? confirmText = null, string? cancelText = null)
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        _bus.Emit("lybox:dialog", new Dictionary<string, object?>
        {
            ["id"] = id,
            ["type"] = type,
            ["title"] = title,
            ["message"] = message,
            ["confirmText"] = confirmText,
            ["cancelText"] = cancelText,
        });
        return id;
    }
}
