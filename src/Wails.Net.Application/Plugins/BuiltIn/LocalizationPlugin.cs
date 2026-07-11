using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;

namespace Wails.Net.Application.Plugins.BuiltIn;

/// <summary>
/// 本地化插件，提供多语言资源管理和翻译 API。
/// 对应 Tauri v2 的 <c>@tauri-apps/plugin-localization</c>。
/// 支持注册翻译资源、切换语言、按键查询翻译。
/// </summary>
public class LocalizationPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "localization";

    /// <summary>
    /// 翻译资源表，按 locale 索引，每个 locale 下为 key→value 字典。
    /// 使用并发字典保证线程安全。
    /// </summary>
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _translations = new();

    /// <summary>
    /// 当前 locale（如 "en-US"、"zh-CN"）。
    /// </summary>
    private static string _currentLocale = CultureInfo.CurrentCulture.Name;

    /// <summary>
    /// 默认 locale（回退语言）。
    /// </summary>
    private const string DefaultLocale = "en-US";

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。此插件无需注册额外服务。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        // 无需注册额外服务
    }

    /// <summary>
    /// 配置插件，注册本地化相关命令。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        // 设置当前 locale
        context.Commands.MapCommand("localization.setLocale",
            (Action<string>)(locale => SetLocale(locale)));

        // 获取当前 locale
        context.Commands.MapCommand("localization.getLocale",
            (Func<string>)(() => _currentLocale));

        // 翻译指定 key
        context.Commands.MapCommand("localization.t",
            (Func<string, string?, string>)((key, paramsJson) => Translate(key, paramsJson)));

        // 注册翻译资源
        context.Commands.MapCommand("localization.registerTranslations",
            (Action<string, string>)((locale, translationsJson) =>
                RegisterTranslations(locale, translationsJson)));

        // 获取已注册的所有 locale
        context.Commands.MapCommand("localization.getAvailableLocales",
            (Func<string[]>)(() => _translations.Keys.ToArray()));
    }

    /// <summary>
    /// 设置当前 locale。
    /// </summary>
    /// <param name="locale">locale 字符串（如 "en-US"、"zh-CN"）。</param>
    public static void SetLocale(string locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return;
        }

        _currentLocale = locale;
    }

    /// <summary>
    /// 翻译指定 key 到当前 locale，支持参数插值。
    /// </summary>
    /// <param name="key">翻译键。</param>
    /// <param name="paramsJson">参数 JSON 字符串（可为 null）。</param>
    /// <returns>翻译后的字符串，未找到则返回 key 本身。</returns>
    public static string Translate(string key, string? paramsJson = null)
    {
        var value = LookupTranslation(key, _currentLocale) ??
                    LookupTranslation(key, DefaultLocale) ??
                    key;

        // 参数插值：将 {param} 替换为实际值
        if (!string.IsNullOrEmpty(paramsJson))
        {
            try
            {
                var parameters = System.Text.Json.JsonSerializer
                    .Deserialize<Dictionary<string, string>>(paramsJson);
                if (parameters is not null)
                {
                    foreach (var kv in parameters)
                    {
                        value = value.Replace($"{{{kv.Key}}}", kv.Value);
                    }
                }
            }
            catch
            {
                // 参数解析失败，返回未插值的翻译
            }
        }

        return value;
    }

    /// <summary>
    /// 注册翻译资源。
    /// </summary>
    /// <param name="locale">locale 字符串。</param>
    /// <param name="translationsJson">翻译资源 JSON（key→value 字典）。</param>
    public static void RegisterTranslations(string locale, string translationsJson)
    {
        if (string.IsNullOrWhiteSpace(locale) || string.IsNullOrEmpty(translationsJson))
        {
            return;
        }

        try
        {
            var entries = System.Text.Json.JsonSerializer
                .Deserialize<Dictionary<string, string>>(translationsJson);
            if (entries is null)
            {
                return;
            }

            var localeDict = _translations.GetOrAdd(locale, _ => new ConcurrentDictionary<string, string>());
            foreach (var kv in entries)
            {
                localeDict[kv.Key] = kv.Value;
            }
        }
        catch
        {
            // JSON 解析失败，忽略
        }
    }

    /// <summary>
    /// 在指定 locale 中查找翻译键。
    /// </summary>
    /// <param name="key">翻译键。</param>
    /// <param name="locale">locale。</param>
    /// <returns>翻译值，未找到返回 null。</returns>
    private static string? LookupTranslation(string key, string locale)
    {
        if (_translations.TryGetValue(locale, out var dict))
        {
            return dict.TryGetValue(key, out var value) ? value : null;
        }

        return null;
    }
}
