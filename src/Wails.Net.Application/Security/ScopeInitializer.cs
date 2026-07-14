using Microsoft.Extensions.Logging;

namespace Wails.Net.Application.Security;

/// <summary>
/// Scope 初始化器，从 <see cref="PermissionOptions.Scopes"/> 配置创建 Scope 实例并绑定到 <see cref="PermissionManager"/>。
/// 在 <see cref="Hosting.DesktopApplicationBuilder.Build"/> 时调用，将 appsettings.json 中的路径/URL 白名单转换为运行时 Scope 约束。
/// 对应 Tauri v2 的 Scope 配置加载机制。
/// </summary>
public static class ScopeInitializer
{
    /// <summary>
    /// 从权限配置初始化所有 Scope 绑定。
    /// 对每个 ScopeConfig 条目：
    /// - 若 Paths 非空，创建 <see cref="FileSystemScope"/> 并绑定
    /// - 若 Urls 非空，创建 <see cref="UrlScope"/> 并绑定
    /// </summary>
    /// <param name="permissionManager">权限管理器。</param>
    /// <param name="options">权限配置选项。</param>
    /// <param name="logger">日志记录器，可为 null。</param>
    public static void Initialize(PermissionManager permissionManager, PermissionOptions options, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(permissionManager);
        ArgumentNullException.ThrowIfNull(options);

        if (options.Scopes.Count == 0) return;

        foreach (var (permissionId, scopeConfig) in options.Scopes)
        {
            if (scopeConfig.Paths.Count > 0)
            {
                var fsScope = new FileSystemScope();
                foreach (var path in scopeConfig.Paths)
                {
                    fsScope.AddPath(path);
                }
                permissionManager.SetScope(permissionId, fsScope);
                logger?.LogDebug("已绑定文件系统 Scope 到权限 {Permission}（{Count} 个路径）", permissionId, scopeConfig.Paths.Count);
            }

            if (scopeConfig.Urls.Count > 0)
            {
                var urlScope = new UrlScope();
                foreach (var url in scopeConfig.Urls)
                {
                    urlScope.AddPattern(url);
                }
                permissionManager.SetScope(permissionId, urlScope);
                logger?.LogDebug("已绑定 URL Scope 到权限 {Permission}（{Count} 个模式）", permissionId, scopeConfig.Urls.Count);
            }
        }
    }
}
