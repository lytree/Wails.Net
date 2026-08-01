using Wails.Net.Application.Bindings;

namespace LyboxApp.Services.Core;

/// <summary>
/// 设置服务。提供设置的读取与保存，对应 LYBox 的 ISettingsService 功能项。
/// </summary>
public class SettingsService
{
    private readonly SettingsStore _settings;
    private readonly LyboxEventBus _bus;

    /// <summary>初始化设置服务。</summary>
    public SettingsService(SettingsStore settings, LyboxEventBus bus)
    {
        _settings = settings;
        _bus = bus;
    }

    /// <summary>获取当前设置快照。</summary>
    [Binding]
    public SettingsDto GetSettings() => _settings.Snapshot();

    /// <summary>保存设置并广播变更事件。</summary>
    [Binding]
    public bool SaveSettings(string language, string theme, Dictionary<string, bool>? pluginEnabled)
    {
        var dto = new SettingsDto
        {
            Language = language,
            Theme = theme,
            PluginEnabled = pluginEnabled ?? new Dictionary<string, bool>(),
        };
        _settings.Apply(dto);
        _bus.Emit("lybox:settings-changed", new Dictionary<string, object> { ["ok"] = true });
        return true;
    }
}
