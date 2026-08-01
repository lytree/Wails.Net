using System.Text.Json;

namespace LyboxApp.Services.Core;

/// <summary>
/// 设置存储。将设置持久化到应用程序基目录下的 lybox-settings.json。
/// 线程安全（读写均加锁）。
/// </summary>
public class SettingsStore
{
    private readonly string _path;
    private readonly object _lock = new();
    private SettingsDto _settings;

    /// <summary>
    /// 初始化设置存储并加载已有设置。
    /// </summary>
    public SettingsStore()
    {
        _path = Path.Combine(AppContext.BaseDirectory, "lybox-settings.json");
        _settings = Load();
    }

    private SettingsDto Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                var dto = JsonSerializer.Deserialize<SettingsDto>(json);
                if (dto is not null)
                {
                    return dto;
                }
            }
        }
        catch
        {
            // 损坏的配置忽略，回退默认
        }

        return new SettingsDto();
    }

    /// <summary>持久化当前设置。</summary>
    public void Save()
    {
        lock (_lock)
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    /// <summary>当前语言代码。</summary>
    public string Language
    {
        get { lock (_lock) return _settings.Language; }
        set { lock (_lock) { _settings.Language = value; } }
    }

    /// <summary>当前主题（light / dark）。</summary>
    public string Theme
    {
        get { lock (_lock) return _settings.Theme; }
        set { lock (_lock) { _settings.Theme = value; } }
    }

    /// <summary>插件是否启用（缺省 true）。</summary>
    public bool IsPluginEnabled(string id)
    {
        lock (_lock)
        {
            return _settings.PluginEnabled.TryGetValue(id, out var v) ? v : true;
        }
    }

    /// <summary>设置插件启用状态。</summary>
    public void SetPluginEnabled(string id, bool enabled)
    {
        lock (_lock)
        {
            _settings.PluginEnabled[id] = enabled;
        }

        Save();
    }

    /// <summary>返回设置的深拷贝快照（避免前端持有可变引用）。</summary>
    public SettingsDto Snapshot()
    {
        lock (_lock)
        {
            var json = JsonSerializer.Serialize(_settings);
            return JsonSerializer.Deserialize<SettingsDto>(json)!;
        }
    }

    /// <summary>应用并持久化前端回传的设置。</summary>
    public void Apply(SettingsDto dto)
    {
        lock (_lock)
        {
            _settings = dto;
        }

        Save();
    }
}
