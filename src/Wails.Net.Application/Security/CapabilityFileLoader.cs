using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Wails.Net.Application.Security;

/// <summary>
/// 能力文件加载器，从 JSON 文件扫描加载能力声明。
/// 对应 Tauri v2 的 capabilities/*.json 文件加载机制：
/// 应用可在指定目录放置 JSON 文件声明窗口所需的权限，启动时自动扫描加载。
/// </summary>
public static class CapabilityFileLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// 从指定目录加载所有 .json 能力文件。
    /// 文件格式示例：
    /// <code>
    /// {
    ///   "identifier": "main-capability",
    ///   "description": "主窗口能力",
    ///   "permissions": ["core:default", "fs:allow-read"],
    ///   "windows": ["main"]
    /// }
    /// </code>
    /// </summary>
    /// <param name="directoryPath">能力文件目录路径。</param>
    /// <param name="logger">日志记录器，可为 null。</param>
    /// <returns>加载的能力声明列表。若目录不存在或无文件返回空列表。</returns>
    public static List<Capability> LoadFromDirectory(string directoryPath, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(directoryPath);

        var capabilities = new List<Capability>();

        if (!Directory.Exists(directoryPath))
        {
            logger?.LogDebug("能力文件目录不存在: {Path}", directoryPath);
            return capabilities;
        }

        foreach (var file in Directory.EnumerateFiles(directoryPath, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var json = File.ReadAllText(file);
                var capability = JsonSerializer.Deserialize<Capability>(json, JsonOptions);
                if (capability is not null && !string.IsNullOrEmpty(capability.Identifier))
                {
                    capabilities.Add(capability);
                    logger?.LogDebug("已加载能力文件: {File}（{Identifier}）", file, capability.Identifier);
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "加载能力文件失败: {File}", file);
            }
        }

        logger?.LogInformation("从目录 {Path} 加载了 {Count} 个能力声明", directoryPath, capabilities.Count);
        return capabilities;
    }

    /// <summary>
    /// 从单个 JSON 文件加载能力声明。
    /// </summary>
    /// <param name="filePath">能力文件路径。</param>
    /// <param name="logger">日志记录器，可为 null。</param>
    /// <returns>加载的能力声明；文件不存在或解析失败返回 null。</returns>
    public static Capability? LoadFromFile(string filePath, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        if (!File.Exists(filePath))
        {
            logger?.LogDebug("能力文件不存在: {Path}", filePath);
            return null;
        }

        try
        {
            var json = File.ReadAllText(filePath);
            var capability = JsonSerializer.Deserialize<Capability>(json, JsonOptions);
            if (capability is not null && !string.IsNullOrEmpty(capability.Identifier))
            {
                logger?.LogDebug("已加载能力文件: {File}（{Identifier}）", filePath, capability.Identifier);
                return capability;
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "加载能力文件失败: {File}", filePath);
        }

        return null;
    }

    /// <summary>
    /// 将加载的能力声明注册到权限管理器。
    /// 同时展开能力中的权限集引用，将细粒度权限授权给管理器。
    /// 对应 Tauri v2 的窗口级 Capability 隔离和远程 URL 限制：
    /// <list type="bullet">
    /// <item>当 <see cref="Capability.Windows"/> 为空时，权限全局授权（应用于所有窗口）。</item>
    /// <item>当 <see cref="Capability.Windows"/> 非空时，权限仅授权给列出的窗口（窗口级隔离）。</item>
    /// <item>当 <see cref="Capability.Remote"/> 非空时，权限附带远程 URL 模式限制：
    /// 远程调用来源必须匹配其中一个模式才能放行；本地源始终放行。
    /// 对应 Tauri v2 Capability.remote 字段。</item>
    /// </list>
    /// </summary>
    /// <param name="manager">权限管理器。</param>
    /// <param name="capabilities">要注册的能力声明列表。</param>
    public static void RegisterToManager(PermissionManager manager, IEnumerable<Capability> capabilities)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(capabilities);

        foreach (var capability in capabilities)
        {
            manager.DeclareCapability(capability);

            // 提取远程 URL 模式集合（可能为空，表示不限制远程来源）
            var remotePatterns = capability.Remote is { Count: > 0 } remote
                ? remote
                : null;

            // 按 capability.Windows 分发授权或拒绝：
            // - Windows 为空 → 全局（所有窗口可用/拒绝）
            // - Windows 非空 → 窗口级（仅列出的窗口可用/拒绝）
            // 同时透传 Remote 模式集合（仅 grant 时有效），使运行时校验远程来源是否匹配。
            //
            // 权限标识支持 "!" 前缀语法（对应 Tauri v2 Capability.Permissions）：
            // - "fs:allow-read" → 调用 manager.Grant 授权该权限
            // - "!fs:allow-read" → 调用 manager.Deny 显式拒绝该权限（deny 优先于 grant）
            if (capability.Windows.Count == 0)
            {
                foreach (var permission in capability.Permissions)
                {
                    if (permission.StartsWith("!"))
                    {
                        // 显式拒绝：剥离 "!" 前缀后调用 Deny
                        // 注意：Deny 不需要 remotePatterns，因为拒绝本身就是无条件的
                        manager.Deny(permission[1..], windowName: null);
                    }
                    else
                    {
                        manager.Grant(permission, windowName: null, remotePatterns);
                    }
                }
            }
            else
            {
                foreach (var windowName in capability.Windows)
                {
                    foreach (var permission in capability.Permissions)
                    {
                        if (permission.StartsWith("!"))
                        {
                            manager.Deny(permission[1..], windowName);
                        }
                        else
                        {
                            manager.Grant(permission, windowName, remotePatterns);
                        }
                    }
                }
            }
        }
    }
}
