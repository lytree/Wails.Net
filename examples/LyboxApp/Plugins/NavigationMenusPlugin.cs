using LyboxApp.Services.Core;
using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Plugins;

namespace LyboxApp.Plugins;

/// <summary>
/// 导航菜单插件。演示菜单树结构与导航项的声明式贡献。
/// 对应 LYBox 的 NavigationMenus 功能项。
/// </summary>
public class NavigationMenusPlugin : IPlugin
{
    /// <summary>插件名称。</summary>
    public string Name => "plugin-navmenus";

    /// <summary>注册服务、导航项与清单。</summary>
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<NavigationMenusService>();
        services.AddSingleton(new NavItem { Key = "demo-navmenus", TitleKey = "nav.navmenus", Icon = "menu", Order = 160, PluginId = "plugin-navmenus" });
        services.AddSingleton(new PluginManifest
        {
            Id = "plugin-navmenus",
            Name = "导航菜单",
            Author = "LYBox",
            Version = "1.0.0",
            Description = "演示分组菜单树（文件 / 编辑 / 帮助）与子项结构。",
            Category = "Demo",
            Route = "demo-navmenus",
        });
    }

    /// <summary>无需额外配置。</summary>
    public void Configure(IPluginContext context)
    {
    }
}

/// <summary>
/// 菜单节点。
/// </summary>
public class MenuNode
{
    /// <summary>节点 Key。</summary>
    public string Key { get; set; } = "";

    /// <summary>显示标签。</summary>
    public string Label { get; set; } = "";

    /// <summary>子节点。</summary>
    public List<MenuNode> Children { get; set; } = new();
}

/// <summary>
/// 导航菜单服务。提供示例菜单树。
/// </summary>
public class NavigationMenusService
{
    /// <summary>返回示例菜单树。</summary>
    [Binding]
    public List<MenuNode> GetMenuTree() => new()
    {
        new MenuNode
        {
            Key = "file",
            Label = "文件",
            Children = new()
            {
                new MenuNode { Key = "file.new", Label = "新建" },
                new MenuNode { Key = "file.open", Label = "打开" },
                new MenuNode { Key = "file.save", Label = "保存" },
            },
        },
        new MenuNode
        {
            Key = "edit",
            Label = "编辑",
            Children = new()
            {
                new MenuNode { Key = "edit.undo", Label = "撤销" },
                new MenuNode { Key = "edit.redo", Label = "重做" },
            },
        },
        new MenuNode
        {
            Key = "help",
            Label = "帮助",
            Children = new()
            {
                new MenuNode { Key = "help.about", Label = "关于" },
            },
        },
    };
}
