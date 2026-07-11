using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Windows;
using Wails.Net.Events;

namespace Wails.Net.Application.Plugins.BuiltIn;

/// <summary>
/// 窗口状态持久化插件。
/// 保存和恢复窗口位置、大小和窗口状态（最大化/最小化/普通）。
/// 对应 Tauri v2 的 <c>@tauri-apps/plugin-window-state</c>。
/// 状态文件默认保存到用户配置目录。
/// </summary>
public class WindowStatePlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "window-state";

    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _stateFilePath;
    private readonly bool _autoRestore;

    /// <summary>
    /// 初始化窗口状态持久化插件实例。
    /// </summary>
    /// <param name="stateFileName">状态文件名（不含路径），默认为 "window-state.json"。</param>
    /// <param name="autoRestore">是否在窗口创建时自动恢复状态。</param>
    public WindowStatePlugin(string stateFileName = "window-state.json", bool autoRestore = true)
    {
        var configDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appDir = Path.Combine(configDir, AppDomain.CurrentDomain.FriendlyName);
        _stateFilePath = Path.Combine(appDir, stateFileName);
        _autoRestore = autoRestore;
    }

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        // 无需注册额外服务
    }

    /// <summary>
    /// 配置插件，注册命令并在启用自动恢复时恢复窗口状态。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        context.Commands.MapCommand("windowstate.save", (Action)(() => SaveState()));
        context.Commands.MapCommand("windowstate.restore", (Action)(() => RestoreState()));
        context.Commands.MapCommand("windowstate.clear", (Action)(() => ClearState()));
    }

    /// <summary>
    /// 保存所有窗口的当前状态到状态文件。
    /// </summary>
    public void SaveState()
    {
        var app = Application.Get();
        if (app is null)
        {
            return;
        }

        var windowManager = app.WindowManager;
        if (windowManager is null)
        {
            return;
        }

        var states = new Dictionary<string, WindowStateData>();
        foreach (var window in windowManager.GetAllWindows())
        {
            var name = window.Name;
            var (x, y) = window.GetPosition();
            var (width, height) = window.GetSize();

            states[name] = new WindowStateData
            {
                X = x,
                Y = y,
                Width = width,
                Height = height,
                IsMaximised = window.IsMaximised(),
                IsMinimised = window.IsMinimised()
            };
        }

        try
        {
            var dir = Path.GetDirectoryName(_stateFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(states, s_jsonOptions);
            File.WriteAllText(_stateFilePath, json);
        }
        catch
        {
            // 保存失败不阻塞应用。
        }
    }

    /// <summary>
    /// 从状态文件恢复所有窗口的状态。
    /// </summary>
    public void RestoreState()
    {
        if (!File.Exists(_stateFilePath))
        {
            return;
        }

        Dictionary<string, WindowStateData> states;
        try
        {
            var json = File.ReadAllText(_stateFilePath);
            states = JsonSerializer.Deserialize<Dictionary<string, WindowStateData>>(json, s_jsonOptions)
                     ?? new Dictionary<string, WindowStateData>();
        }
        catch
        {
            return;
        }

        var app = Application.Get();
        if (app is null)
        {
            return;
        }

        var windowManager = app.WindowManager;
        if (windowManager is null)
        {
            return;
        }

        foreach (var window in windowManager.GetAllWindows())
        {
            var name = window.Name;
            if (!states.TryGetValue(name, out var state))
            {
                continue;
            }

            window.SetSize(state.Width, state.Height);
            window.SetPosition(state.X, state.Y);

            if (state.IsMaximised)
            {
                window.SetMaximised();
            }
            else if (state.IsMinimised)
            {
                window.SetMinimised();
            }
        }
    }

    /// <summary>
    /// 清除保存的窗口状态文件。
    /// </summary>
    public void ClearState()
    {
        if (File.Exists(_stateFilePath))
        {
            try
            {
                File.Delete(_stateFilePath);
            }
            catch
            {
                // 忽略删除错误。
            }
        }
    }

    /// <summary>
    /// 获取状态文件路径。
    /// </summary>
    public string StateFilePath => _stateFilePath;

    /// <summary>
    /// 获取是否启用自动恢复。
    /// </summary>
    public bool AutoRestore => _autoRestore;
}

/// <summary>
/// 窗口状态数据。
/// </summary>
public sealed class WindowStateData
{
    /// <summary>窗口 X 坐标</summary>
    public int X { get; set; }

    /// <summary>窗口 Y 坐标</summary>
    public int Y { get; set; }

    /// <summary>窗口宽度</summary>
    public int Width { get; set; }

    /// <summary>窗口高度</summary>
    public int Height { get; set; }

    /// <summary>是否最大化</summary>
    public bool IsMaximised { get; set; }

    /// <summary>是否最小化</summary>
    public bool IsMinimised { get; set; }
}
