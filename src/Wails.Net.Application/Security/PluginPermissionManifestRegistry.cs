using System.Collections.Concurrent;

namespace Wails.Net.Application.Security;

/// <summary>
/// 插件权限清单运行时注册表（OPT-PLUGIN-003）。
/// <para>
/// 编译期由 <c>Wails.Net.SourceGenerators.PluginPermissionManifestGenerator</c>
/// 在每个插件 assembly 中生成 <c>Wails.Generated.WailsGeneratedPermissionManifest</c>
/// 静态类，通过 <c>[ModuleInitializer]</c> 在模块加载时调用本类的
/// <see cref="Register"/> 方法，将插件名与 JSON 清单写入注册表。
/// </para>
/// <para>
/// 清单数据用于：
/// <list type="bullet">
/// <item><c>wails-net check --permissions-manifest</c> 编译期校验已知权限集合；</item>
/// <item>运行时权限审计（查询插件已声明哪些权限集/细粒度权限）；</item>
/// <item>文档生成（输出每个插件的 permissions.md 索引）。</item>
/// </list>
/// </para>
/// </summary>
public static class PluginPermissionManifestRegistry
{
    /// <summary>
    /// 已注册清单的线程安全字典（键为插件名）。
    /// </summary>
    private static readonly ConcurrentDictionary<string, string> _manifests = new(StringComparer.Ordinal);

    /// <summary>
    /// 注册单个插件清单。
    /// 由生成代码 <c>[ModuleInitializer]</c> 在模块加载时自动调用。
    /// </summary>
    /// <param name="pluginName">插件名（如 <c>window</c>、<c>fs</c>）。</param>
    /// <param name="manifestJson">清单 JSON 字符串。</param>
    public static void Register(string pluginName, string manifestJson)
    {
        ArgumentException.ThrowIfNullOrEmpty(pluginName);
        ArgumentNullException.ThrowIfNull(manifestJson);

        // 后注册者覆盖先注册者（晚加载的插件优先）。
        // 实践中每个插件只声明一次自己的清单，无冲突预期。
        _manifests[pluginName] = manifestJson;
    }

    /// <summary>
    /// 获取所有已注册清单（只读快照）。
    /// </summary>
    /// <returns>插件名 → JSON 清单的只读字典。</returns>
    public static IReadOnlyDictionary<string, string> GetAll()
    {
        return _manifests.ToArray().ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
    }

    /// <summary>
    /// 获取指定插件的清单 JSON。
    /// </summary>
    /// <param name="pluginName">插件名。</param>
    /// <returns>JSON 字符串；未注册时返回 null。</returns>
    public static string? Get(string pluginName)
    {
        return _manifests.TryGetValue(pluginName, out var json) ? json : null;
    }

    /// <summary>
    /// 汇总所有清单中的细粒度权限标识集合（去除命名权限集，仅保留
    /// <c>&lt;plugin&gt;:allow-*</c> 形式或命名集标识）。
    /// </summary>
    /// <returns>所有已声明权限标识的去重集合。</returns>
    public static IReadOnlySet<string> CollectAllPermissions()
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var json in _manifests.Values)
        {
            ExtractPermissionsFromJson(json, result);
        }
        return result;
    }

    /// <summary>
    /// 清空所有已注册清单（仅用于测试）。
    /// </summary>
    internal static void ClearForTests()
    {
        _manifests.Clear();
    }

    /// <summary>
    /// 从清单 JSON 中提取权限标识（顶层 permissions 数组）。
    /// </summary>
    private static void ExtractPermissionsFromJson(string json, HashSet<string> target)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("permissions", out var perms) &&
                perms.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in perms.EnumerateArray())
                {
                    if (item.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var s = item.GetString();
                        if (!string.IsNullOrEmpty(s))
                        {
                            target.Add(s);
                        }
                    }
                }
            }
        }
        catch
        {
            // 损坏的清单跳过（不影响其他清单）。
        }
    }
}