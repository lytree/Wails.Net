using LyboxApp.Services.Core;
using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Plugins;

namespace LyboxApp.Plugins;

/// <summary>
/// 按钮与输入控件插件。演示基础交互控件与后端绑定。
/// 对应 LYBox 的 ButtonsInputs 功能项。
/// </summary>
public class ButtonsInputsPlugin : IPlugin
{
    /// <summary>插件名称。</summary>
    public string Name => "plugin-buttons";

    /// <summary>注册服务、导航项与清单。</summary>
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ButtonsInputsService>();
        services.AddSingleton(new NavItem { Key = "demo-buttons", TitleKey = "nav.buttons", Icon = "button", Order = 120, PluginId = "plugin-buttons" });
        services.AddSingleton(new PluginManifest
        {
            Id = "plugin-buttons",
            Name = "按钮与输入控件",
            Author = "LYBox",
            Version = "1.0.0",
            Description = "演示按钮、文本框、滑块等输入控件与后端回显示例。",
            Category = "Demo",
            Route = "demo-buttons",
        });
    }

    /// <summary>无需额外配置。</summary>
    public void Configure(IPluginContext context)
    {
    }
}

/// <summary>
/// 按钮与输入控件服务。
/// </summary>
public class ButtonsInputsService
{
    /// <summary>回显输入文本。</summary>
    [Binding]
    public string Echo(string text) => $"你输入了：{text}";

    /// <summary>拼接多个输入项（文本 + 数字 + 开关）。</summary>
    [Binding]
    public string Combine(string text, double number, bool toggle)
    {
        return $"文本={text} | 数字={number} | 开关={(toggle ? "开" : "关")}";
    }
}
