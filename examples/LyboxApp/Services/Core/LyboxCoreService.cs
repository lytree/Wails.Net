using System.Collections.Generic;
using Wails.Net.Application.Bindings;

namespace LyboxApp.Services.Core;

/// <summary>
/// 核心服务。聚合插件清单、导航树、任务与基础应用信息，
/// 对应 LYBox 的插件管理 / 导航 / 任务托盘等核心功能项。
/// </summary>
public class LyboxCoreService
{
    private readonly SettingsStore _settings;
    private readonly LyboxEventBus _bus;
    private readonly TaskRegistry _tasks;
    private readonly IEnumerable<PluginManifest> _manifests;
    private readonly IEnumerable<NavItem> _navItems;

    /// <summary>
    /// 通过 DI 注入依赖。
    /// </summary>
    public LyboxCoreService(
        SettingsStore settings,
        LyboxEventBus bus,
        TaskRegistry tasks,
        IEnumerable<PluginManifest> manifests,
        IEnumerable<NavItem> navItems)
    {
        _settings = settings;
        _bus = bus;
        _tasks = tasks;
        _manifests = manifests;
        _navItems = navItems;
    }

    /// <summary>获取所有插件清单（合并启用状态）。</summary>
    [Binding]
    public List<PluginView> GetPlugins()
    {
        return _manifests.Select(m => new PluginView
        {
            Id = m.Id,
            Name = m.Name,
            Author = m.Author,
            Version = m.Version,
            Description = m.Description,
            Category = m.Category,
            Enabled = _settings.IsPluginEnabled(m.Id),
        }).ToList();
    }

    /// <summary>获取导航树（仅返回已启用插件的导航项）。</summary>
    [Binding]
    public List<NavItem> GetNavigation()
    {
        return _navItems
            .Where(n => _settings.IsPluginEnabled(n.PluginId))
            .OrderBy(n => n.Order)
            .ToList();
    }

    /// <summary>设置插件启用状态，并广播变更事件。</summary>
    [Binding]
    public bool SetPluginEnabled(string id, bool enabled)
    {
        _settings.SetPluginEnabled(id, enabled);
        _bus.Emit("lybox:plugins-changed", new Dictionary<string, object> { ["id"] = id, ["enabled"] = enabled });
        return true;
    }

    /// <summary>获取当前任务列表。</summary>
    [Binding]
    public List<TaskInfo> GetTasks() => _tasks.List().ToList();

    /// <summary>获取应用基础信息。</summary>
    [Binding]
    public Dictionary<string, string> GetAppInfo()
    {
        return new Dictionary<string, string>
        {
            ["framework"] = "Wails.Net",
            ["frontend"] = "Vue 3 + vue-jsx-vapor + TailwindCSS",
            ["runtime"] = Environment.Version.ToString(),
            ["osVersion"] = Environment.OSVersion.ToString(),
            ["machineName"] = Environment.MachineName,
            ["processorCount"] = Environment.ProcessorCount.ToString(),
        };
    }
}
