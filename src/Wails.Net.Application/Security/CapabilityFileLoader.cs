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
            // 展开权限集并授权
            foreach (var permission in capability.Permissions)
            {
                manager.Grant(permission);
            }
        }
    }
}
