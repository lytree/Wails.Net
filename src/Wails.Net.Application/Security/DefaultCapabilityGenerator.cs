using System.Text;
using System.Text.Json;

namespace Wails.Net.Application.Security;

/// <summary>
/// 默认 capability JSON 生成器（R3-D.B）。
/// <para>
/// 对标 Tauri v2 <c>tauri init</c> / <c>cargo tauri init</c> 的能力模板体验：
/// 基于已注册插件清单（<see cref="PluginPermissionManifestRegistry"/>）自动产出
/// <c>&lt;plugin&gt;:default</c> 能力 JSON，作为 Strict 模式迁移的起点。
/// </para>
/// <para>
/// 设计取舍：
/// <list type="bullet">
/// <item>仅产出"最小可用"默认：包含命名权限集 + 全部已声明权限标识，
/// 不强行加 <c>!deny-*</c>（拒绝由调用方按需追加）。</item>
/// <item>生成 JSON 用手写缩进（2 空格）保证 AOT 兼容与可读性，
/// 避免 <c>System.Text.Json</c> 的 <c>WriteIndented</c>（序列化器选项在 AOT 下需谨慎）。</item>
/// </list>
/// </para>
/// </summary>
public static class DefaultCapabilityGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// 默认标识符约定：<c>&lt;plugin&gt;:default</c>，与 Tauri v2 plugin default capability 命名一致。
    /// </summary>
    /// <param name="pluginName">插件名（短名，如 <c>window</c>、<c>fs</c>）。</param>
    /// <returns>默认 capability 标识符字符串。</returns>
    public static string DefaultIdentifier(string pluginName)
    {
        ArgumentException.ThrowIfNullOrEmpty(pluginName);
        return pluginName + ":default";
    }

    /// <summary>
    /// 从清单 JSON 中提取细粒度权限标识集合。
    /// </summary>
    /// <param name="manifestJson">清单 JSON 字符串。</param>
    /// <returns>权限标识集合；解析失败返回空集合。</returns>
    public static IReadOnlyList<string> ExtractPermissions(string manifestJson)
    {
        ArgumentNullException.ThrowIfNull(manifestJson);
        var result = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(manifestJson, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            if (doc.RootElement.TryGetProperty("permissions", out var perms) &&
                perms.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in perms.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var s = item.GetString();
                        if (!string.IsNullOrEmpty(s))
                        {
                            result.Add(s);
                        }
                    }
                }
            }
        }
        catch
        {
            // 损坏的清单返回空集合（生成器容忍异常输入）。
        }
        return result;
    }

    /// <summary>
    /// 为指定插件生成默认 capability JSON。
    /// <para>
    /// 生成规则：
    /// <list type="bullet">
    /// <item><c>identifier</c>：<see cref="DefaultIdentifier"/>；</item>
    /// <item><c>description</c>：固定文案，指明自动生成与对应插件名；</item>
    /// <item><c>permissions</c>：清单中所有权限标识（含命名权限集与 allow-* 形式）。</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="pluginName">插件名。</param>
    /// <param name="manifestJson">清单 JSON；可为 null（视为空权限集）。</param>
    /// <returns>格式化的 capability JSON 字符串。</returns>
    public static string GenerateJson(string pluginName, string? manifestJson)
    {
        ArgumentException.ThrowIfNullOrEmpty(pluginName);

        var permissions = manifestJson is null
            ? new List<string>()
            : ExtractPermissions(manifestJson).ToList();

        // 确保命名权限集 <plugin>:default 在最前（与 Tauri 默认 capability 风格一致）
        var namedSet = DefaultIdentifier(pluginName);
        if (!permissions.Contains(namedSet, StringComparer.Ordinal))
        {
            permissions.Insert(0, namedSet);
        }

        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.Append("  \"identifier\": \"").Append(JsonEncodedText.Encode(namedSet)).AppendLine("\",");
        sb.Append("  \"description\": \"默认能力（自动生成于 R3-D.B）：包含 ").Append(pluginName).AppendLine(" 插件全部已声明权限\",");
        sb.AppendLine("  \"permissions\": [");
        for (int i = 0; i < permissions.Count; i++)
        {
            sb.Append("    \"").Append(JsonEncodedText.Encode(permissions[i])).Append('"');
            if (i < permissions.Count - 1)
            {
                sb.Append(',');
            }
            sb.AppendLine();
        }
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>
    /// 从 <see cref="PluginPermissionManifestRegistry"/> 读取已注册清单，
    /// 为每个插件生成默认 capability JSON。
    /// </summary>
    /// <returns>插件名 → 默认 capability JSON 的字典；注册表为空时返回空字典。</returns>
    public static IReadOnlyDictionary<string, string> GenerateAllDefaults()
    {
        var all = PluginPermissionManifestRegistry.GetAll();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in all)
        {
            result[kv.Key] = GenerateJson(kv.Key, kv.Value);
        }
        return result;
    }
}