using System.Text.Json;
using Wails.Net.Application.Security;

namespace Wails.Net.Cli.Check;

/// <summary>
/// capability 文件构建期校验结果（OPT-SEC-004）。
/// </summary>
public sealed class CheckResult
{
    /// <summary>是否通过校验。</summary>
    public bool Success { get; set; } = true;

    /// <summary>校验错误列表（阻断构建）。</summary>
    public List<string> Errors { get; } = new();

    /// <summary>校验警告列表（不阻断，提示改进）。</summary>
    public List<string> Warnings { get; } = new();

    /// <summary>扫描的能力文件数量。</summary>
    public int FileCount { get; set; }

    /// <summary>已声明的权限标识总数（去重）。</summary>
    public int PermissionCount { get; set; }
}

/// <summary>
/// capability 文件构建期校验器（OPT-SEC-004，2026-08-15 新增）。
/// <para>
/// 对应 Tauri v2 构建期 ACL/capability 校验：在 <c>wails-net check</c>（或 build 前置步骤）中
/// 扫描 capabilities 目录，校验 JSON schema、平台名与权限标识格式/存在性，
/// 使拼写错误在构建期暴露，而非运行时静默失败。
/// </para>
/// <para>
/// 已知权限集合（<paramref name="knownPermissions"/>）来源：
/// 短期由各插件包随包发布权限清单（与 OPT-PLUGIN-003 衔接），长期由源生成器产出。
/// 未提供已知集合时仅校验权限标识格式（<c>!?plugin:name</c>）。
/// </para>
/// </summary>
public static class CapabilityChecker
{
    /// <summary>
    /// 与 <see cref="CapabilityFileLoader"/> 一致的 JSON 解析选项（允许注释与尾逗号）。
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// 权限标识格式（去 "!" 前缀后必须为 <c>plugin:name</c> 形式，name 可含多段冒号，如 <c>core:window:allow-close</c>）。
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex PermissionIdPattern =
        new(@"^[A-Za-z0-9_-]+:[A-Za-z0-9_.:-]+$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// 校验项目 capabilities 目录（OPT-SEC-004）。
    /// <para>
    /// capabilities 目录解析优先级：<paramref name="capabilitiesDirOverride"/>（显式指定）
    /// → <c>appsettings.json</c> 中 <c>Wails:Permissions:CapabilitiesDirectory</c>
    /// → 项目根目录下 <c>capabilities/</c>（与运行时默认一致）。
    /// 相对路径基于项目根目录（源码目录），与运行时基于输出目录的语义互补。
    /// </para>
    /// </summary>
    /// <param name="projectPath">项目文件（.csproj）路径。</param>
    /// <param name="capabilitiesDirOverride">显式指定的 capabilities 目录；可为 null。</param>
    /// <param name="knownPermissions">已知权限标识集合（可为 null，缺省仅校验格式）。</param>
    /// <returns>校验结果。</returns>
    public static CheckResult CheckProject(
        string projectPath,
        string? capabilitiesDirOverride = null,
        IReadOnlySet<string>? knownPermissions = null)
    {
        var projectDir = Path.GetDirectoryName(projectPath)
            ?? throw new ArgumentException("无法解析项目目录", nameof(projectPath));

        // 解析 capabilities 目录
        string? capabilitiesDir = capabilitiesDirOverride;
        if (string.IsNullOrEmpty(capabilitiesDir))
        {
            capabilitiesDir = ResolveCapabilitiesDirFromAppSettings(projectDir);
        }

        if (string.IsNullOrEmpty(capabilitiesDir))
        {
            capabilitiesDir = Path.Combine(projectDir, "capabilities");
        }
        else if (!Path.IsPathRooted(capabilitiesDir))
        {
            capabilitiesDir = Path.Combine(projectDir, capabilitiesDir);
        }

        return CheckDirectory(capabilitiesDir, knownPermissions);
    }

    /// <summary>
    /// 校验指定 capabilities 目录下的所有 .json 能力文件。
    /// </summary>
    /// <param name="capabilitiesDir">capabilities 目录路径。</param>
    /// <param name="knownPermissions">已知权限标识集合（可为 null，缺省仅校验格式）。</param>
    /// <returns>校验结果。</returns>
    public static CheckResult CheckDirectory(
        string capabilitiesDir,
        IReadOnlySet<string>? knownPermissions = null)
    {
        var result = new CheckResult();

        if (!Directory.Exists(capabilitiesDir))
        {
            result.Warnings.Add($"capabilities 目录不存在：{capabilitiesDir}（Strict 模式下未授权命令将被拒绝）");
            return result;
        }

        var files = Directory.EnumerateFiles(capabilitiesDir, "*.json", SearchOption.TopDirectoryOnly).ToList();
        result.FileCount = files.Count;

        if (files.Count == 0)
        {
            result.Warnings.Add($"capabilities 目录为空：{capabilitiesDir}（未声明任何能力）");
            return result;
        }

        var identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenPermissions = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            Capability? capability;
            try
            {
                var json = File.ReadAllText(file);
                capability = JsonSerializer.Deserialize<Capability>(json, JsonOptions);
            }
            catch (JsonException ex)
            {
                result.Success = false;
                result.Errors.Add($"{fileName}：JSON 解析失败（{ex.Message}）");
                continue;
            }
            catch (IOException ex)
            {
                result.Success = false;
                result.Errors.Add($"{fileName}：文件读取失败（{ex.Message}）");
                continue;
            }

            if (capability is null)
            {
                result.Success = false;
                result.Errors.Add($"{fileName}：内容为空或无法反序列化");
                continue;
            }

            // identifier 非空且唯一
            if (string.IsNullOrWhiteSpace(capability.Identifier))
            {
                result.Success = false;
                result.Errors.Add($"{fileName}：缺少必填字段 identifier");
            }
            else if (!identifiers.Add(capability.Identifier))
            {
                result.Success = false;
                result.Errors.Add($"{fileName}：identifier '{capability.Identifier}' 与其它能力文件重复");
            }

            // platforms 平台名校验（复用 WailsPlatform 常量，OPT-SEC-002）
            foreach (var platform in capability.Platforms)
            {
                if (!WailsPlatform.IsKnown(platform))
                {
                    result.Success = false;
                    result.Errors.Add(
                        $"{fileName}（{capability.Identifier}）：未知平台 '{platform}'。" +
                        $"已知平台：windows / linux / android / macos / server");
                }
            }

            // permissions 权限标识校验：格式 + 存在性（若有已知集合）
            foreach (var raw in capability.Permissions)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    result.Success = false;
                    result.Errors.Add($"{fileName}（{capability.Identifier}）：permissions 包含空字符串");
                    continue;
                }

                var permissionId = raw.StartsWith('!') ? raw[1..] : raw;
                if (!PermissionIdPattern.IsMatch(permissionId))
                {
                    result.Success = false;
                    result.Errors.Add(
                        $"{fileName}（{capability.Identifier}）：权限标识 '{raw}' 格式非法，" +
                        "应为 '插件:权限名'（如 window:allow-readonly），'!' 前缀表示显式拒绝");
                }
                else
                {
                    seenPermissions.Add(permissionId);
                }

                if (knownPermissions is not null && !knownPermissions.Contains(permissionId))
                {
                    result.Warnings.Add(
                        $"{fileName}（{capability.Identifier}）：权限 '{raw}' 不在已知权限集合中，" +
                        "请确认插件已安装且权限标识拼写正确");
                }
            }

            // windows 窗口名校验：非空字符串
            foreach (var window in capability.Windows)
            {
                if (string.IsNullOrWhiteSpace(window))
                {
                    result.Success = false;
                    result.Errors.Add($"{fileName}（{capability.Identifier}）：windows 包含空窗口名");
                }
            }

            // remote 远程 URL 模式校验：非空字符串
            foreach (var remote in capability.Remote)
            {
                if (string.IsNullOrWhiteSpace(remote))
                {
                    result.Success = false;
                    result.Errors.Add($"{fileName}（{capability.Identifier}）：remote 包含空 URL 模式");
                }
            }
        }

        result.PermissionCount = seenPermissions.Count;
        return result;
    }

    /// <summary>
    /// 从 appsettings.json 解析 <c>Wails:Permissions:CapabilitiesDirectory</c> 配置。
    /// </summary>
    /// <param name="projectDir">项目根目录（源码目录）。</param>
    /// <returns>配置的目录（相对或绝对路径）；未配置或解析失败返回 null。</returns>
    private static string? ResolveCapabilitiesDirFromAppSettings(string projectDir)
    {
        var appSettingsPath = Path.Combine(projectDir, "appsettings.json");
        if (!File.Exists(appSettingsPath))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(
                File.ReadAllText(appSettingsPath),
                new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });
            if (doc.RootElement.TryGetProperty("Wails", out var wails) &&
                wails.TryGetProperty("Permissions", out var permissions) &&
                permissions.TryGetProperty("CapabilitiesDirectory", out var dir))
            {
                var value = dir.GetString();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }
        catch
        {
            // appsettings.json 解析失败按未配置处理（不阻断 check，仅提示）
        }

        return null;
    }

    /// <summary>
    /// 从插件权限清单文件加载已知权限标识集合（OPT-PLUGIN-003 衔接）。
    /// <para>
    /// 清单目录约定：每个插件一个 JSON 文件（如 <c>window.json</c>），内容为权限标识数组：
    /// <code>
    /// ["window:default", "window:allow-readonly", "window:allow-dangerous"]
    /// </code>
    /// 也支持 <c>{ "plugin": "window", "permissions": [...] }</c> 对象形式。
    /// </para>
    /// </summary>
    /// <param name="manifestDir">清单目录路径；目录不存在时返回空集合。</param>
    /// <returns>已知权限标识集合。</returns>
    public static IReadOnlySet<string> LoadKnownPermissionsFromManifests(string manifestDir)
    {
        var known = new HashSet<string>(StringComparer.Ordinal);
        if (!Directory.Exists(manifestDir))
        {
            return known;
        }

        foreach (var file in Directory.EnumerateFiles(manifestDir, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var json = File.ReadAllText(file);
                using var doc = JsonDocument.Parse(
                    json,
                    new JsonDocumentOptions
                    {
                        CommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true,
                    });

                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in root.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            known.Add(item.GetString()!);
                        }
                    }
                }
                else if (root.ValueKind == JsonValueKind.Object &&
                         root.TryGetProperty("permissions", out var perms) &&
                         perms.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in perms.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            known.Add(item.GetString()!);
                        }
                    }
                }
            }
            catch
            {
                // 单个清单文件损坏不影响其他清单；该校验已足够宽松
            }
        }

        return known;
    }
}
