using System.Linq;
using Wails.Net.Application.Bindings;

namespace LyboxApp.Services.Core;

/// <summary>
/// 本地化服务。提供可用语言列表、当前语言获取与切换，
/// 对应 LYBox 的 ILocalizationService 功能项（中英文）。
/// </summary>
public class LocalizationService
{
    private static readonly List<LanguageInfo> _languages = new()
    {
        new LanguageInfo { Code = "zh-CN", Name = "简体中文" },
        new LanguageInfo { Code = "en", Name = "English" },
    };

    private readonly SettingsStore _settings;
    private readonly LyboxEventBus _bus;

    /// <summary>初始化本地化服务。</summary>
    public LocalizationService(SettingsStore settings, LyboxEventBus bus)
    {
        _settings = settings;
        _bus = bus;
    }

    /// <summary>获取可用语言列表。</summary>
    [Binding]
    public List<LanguageInfo> GetLanguages() => _languages;

    /// <summary>获取当前语言代码。</summary>
    [Binding]
    public string GetCurrentLanguage() => _settings.Language;

    /// <summary>切换语言并广播变更事件。</summary>
    [Binding]
    public bool SetLanguage(string code)
    {
        if (_languages.All(l => l.Code != code))
        {
            return false;
        }

        _settings.Language = code;
        _bus.Emit("lybox:language-changed", new Dictionary<string, object> { ["code"] = code });
        return true;
    }
}
