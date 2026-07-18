using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Wails.Net.Application.Security;

/// <summary>
/// 权限管理器，运行时校验命令调用是否有权限。
/// 对应 Tauri v2 的权限模型：基于能力（Capability）和权限集（PermissionSet）的细粒度访问控制。
/// 支持窗口级能力隔离：权限可全局授权（Window=null）或绑定到特定窗口名。
/// 对应 Tauri v2 Capability.Windows 字段运行时校验。
/// </summary>
public sealed class PermissionManager
{
    /// <summary>
    /// 已授权的权限键集合（线程安全）。
    /// 键为 (Permission, Window) 复合键：Window=null 表示全局授权（应用于所有窗口），
    /// Window="windowName" 表示仅授权给该窗口。
    /// 包含直接授权的权限和从 PermissionSet 展开后的细粒度权限。
    /// </summary>
    private readonly ConcurrentDictionary<PermissionKey, byte> _grantedPermissions = new();

    /// <summary>
    /// 已声明的能力字典（线程安全），键为能力标识符。
    /// </summary>
    private readonly ConcurrentDictionary<string, Capability> _declaredCapabilities = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 已注册的权限集字典（线程安全），键为权限集标识（如 "core:default"）。
    /// </summary>
    private readonly ConcurrentDictionary<string, PermissionSet> _permissionSets = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 已声明的权限作用域字典（线程安全），键为权限标识。
    /// 用于参数级 scope 校验（如文件系统路径范围）。
    /// </summary>
    private readonly ConcurrentDictionary<string, IScope> _permissionScopes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 远程 URL 限制字典（线程安全）。
    /// 键为 (Permission, Window) 复合键，值为该权限-窗口对的远程 URL 模式集合。
    /// 当权限被授予时附带非空 Remote 模式列表时，远程调用必须匹配其中一个模式才能放行。
    /// 本地源（wails://, http://localhost, null）总是放行，不受 Remote 限制。
    /// 对应 Tauri v2 Capability.remote 字段的运行时校验。
    /// </summary>
    private readonly ConcurrentDictionary<PermissionKey, UrlWhitelist> _remoteUrlScopes = new();

    /// <summary>
    /// 已显式拒绝的权限键集合（线程安全）。
    /// 键为 (Permission, Window) 复合键：Window=null 表示全局拒绝（所有窗口禁用），
    /// Window="windowName" 表示仅该窗口禁用。
    /// 对应 Tauri v2 的 deny 语义：deny 始终优先于 grant，
    /// 即便权限被授权，只要匹配到 deny 条目即视为拒绝。
    /// Capability.Permissions 中以 "!" 前缀的权限标识会被加载为 deny 条目。
    /// </summary>
    private readonly ConcurrentDictionary<PermissionKey, byte> _deniedPermissions = new();

    private readonly PermissionOptions _options;
    private readonly ILogger<PermissionManager>? _logger;

    /// <summary>是否启用权限检查</summary>
    public bool Enabled => _options.Enabled;

    /// <summary>
    /// 构造权限管理器。
    /// </summary>
    /// <param name="options">权限配置选项。</param>
    /// <param name="logger">日志记录器，可为 null。</param>
    public PermissionManager(IOptions<PermissionOptions> options, ILogger<PermissionManager>? logger = null)
    {
        _options = options.Value;
        _logger = logger;

        // 从配置的 Permissions 列表授权初始权限（全局授权，Window=null）
        foreach (var perm in _options.Permissions)
        {
            Grant(perm);
        }
    }

    /// <summary>
    /// 声明能力。
    /// </summary>
    /// <param name="capability">要声明的能力。</param>
    public void DeclareCapability(Capability capability)
    {
        ArgumentException.ThrowIfNullOrEmpty(capability.Identifier);
        _declaredCapabilities[capability.Identifier] = capability;
        _logger?.LogDebug("已声明能力: {Identifier}（{Count} 个权限，{WindowCount} 个窗口）",
            capability.Identifier, capability.Permissions.Count,
            capability.Windows.Count == 0 ? "全局" : string.Join(",", capability.Windows));
    }

    /// <summary>
    /// 注册权限集。
    /// 权限集是命名权限组合（如 "core:default"），可在能力声明中引用。
    /// </summary>
    /// <param name="permissionSet">要注册的权限集。</param>
    public void RegisterPermissionSet(PermissionSet permissionSet)
    {
        ArgumentException.ThrowIfNullOrEmpty(permissionSet.Identifier);
        _permissionSets[permissionSet.Identifier] = permissionSet;
        _logger?.LogDebug("已注册权限集: {Identifier}（{Count} 个权限）", permissionSet.Identifier, permissionSet.Permissions.Count);
    }

    /// <summary>
    /// 为权限绑定作用域。
    /// </summary>
    /// <param name="permissionId">权限标识。</param>
    /// <param name="scope">作用域实例。</param>
    public void SetScope(string permissionId, IScope scope)
    {
        ArgumentException.ThrowIfNullOrEmpty(permissionId);
        ArgumentNullException.ThrowIfNull(scope);
        _permissionScopes[permissionId] = scope;
    }

    /// <summary>
    /// 获取权限的作用域（若已绑定）。
    /// </summary>
    /// <param name="permissionId">权限标识。</param>
    /// <returns>作用域实例，未绑定时返回 null。</returns>
    public IScope? GetScope(string permissionId)
    {
        return _permissionScopes.TryGetValue(permissionId, out var scope) ? scope : null;
    }

    /// <summary>
    /// 全局授权权限（应用于所有窗口）。
    /// 等效于 <see cref="Grant(string, string?)"/> 传入 <c>windowName=null</c>。
    /// 若 <paramref name="permissionId"/> 是已注册的权限集标识，则展开为集内所有权限逐个授权。
    /// </summary>
    /// <param name="permissionId">要授权的权限标识或权限集标识。</param>
    public void Grant(string permissionId) => Grant(permissionId, windowName: null, remotePatterns: null);

    /// <summary>
    /// 授权权限到指定窗口（或全局）。
    /// 若 <paramref name="permissionId"/> 是已注册的权限集标识，则展开为集内所有权限逐个授权。
    /// 对应 Tauri v2 的窗口级 Capability：当 Capability.Windows 非空时，权限仅授权给指定窗口。
    /// </summary>
    /// <param name="permissionId">要授权的权限标识或权限集标识。</param>
    /// <param name="windowName">
    /// 目标窗口名称。传入 <c>null</c> 表示全局授权（应用于所有窗口）；
    /// 传入具体窗口名表示仅授权给该窗口（窗口级隔离）。
    /// </param>
    public void Grant(string permissionId, string? windowName)
        => Grant(permissionId, windowName, remotePatterns: null);

    /// <summary>
    /// 授权权限到指定窗口（或全局），并附带远程 URL 限制模式。
    /// 对应 Tauri v2 Capability.remote 字段：
    /// 仅当远程调用来源 URL 匹配 <paramref name="remotePatterns"/> 之一时才放行；
    /// 本地源总是放行。
    /// </summary>
    /// <param name="permissionId">要授权的权限标识或权限集标识。</param>
    /// <param name="windowName">目标窗口名称；<c>null</c> 表示全局授权。</param>
    /// <param name="remotePatterns">
    /// 远程 URL 模式列表（如 "https://*.example.com"）。
    /// 传入 <c>null</c> 或空列表表示不限制远程来源。
    /// </param>
    public void Grant(string permissionId, string? windowName, IReadOnlyCollection<string>? remotePatterns)
    {
        // 若是权限集，展开为集内所有权限，并透传 remotePatterns
        if (_permissionSets.TryGetValue(permissionId, out var set))
        {
            foreach (var perm in set.Permissions)
            {
                GrantSingle(perm, windowName, remotePatterns);
            }
            _logger?.LogInformation("已授权权限集: {Set}（展开为 {Count} 个权限，窗口={Window}，远程={Remote}）",
                permissionId, set.Permissions.Count, windowName ?? "全局",
                remotePatterns is null || remotePatterns.Count == 0 ? "无限制" : $"{remotePatterns.Count} 个模式");
        }
        else
        {
            GrantSingle(permissionId, windowName, remotePatterns);
            _logger?.LogInformation("已授权权限: {Permission}（窗口={Window}，远程={Remote}）",
                permissionId, windowName ?? "全局",
                remotePatterns is null || remotePatterns.Count == 0 ? "无限制" : $"{remotePatterns.Count} 个模式");
        }
    }

    /// <summary>
    /// 实际授权单个权限到 (permission, window)，并附带 remotePatterns。
    /// 内部使用，由 <see cref="Grant(string, string?, IReadOnlyCollection{string}?)"/> 调用。
    /// </summary>
    private void GrantSingle(string permissionId, string? windowName, IReadOnlyCollection<string>? remotePatterns)
    {
        _grantedPermissions.TryAdd(new PermissionKey(permissionId, windowName), 0);

        // 若附带 remotePatterns，注册到 _remoteUrlScopes
        if (remotePatterns is not null && remotePatterns.Count > 0)
        {
            var key = new PermissionKey(permissionId, windowName);
            var whitelist = _remoteUrlScopes.GetOrAdd(key, _ => new UrlWhitelist());
            foreach (var pattern in remotePatterns)
            {
                if (!string.IsNullOrEmpty(pattern))
                {
                    whitelist.Add(pattern);
                }
            }
        }
    }

    /// <summary>
    /// 撤销全局权限。
    /// 等效于 <see cref="Revoke(string, string?)"/> 传入 <c>windowName=null</c>。
    /// </summary>
    /// <param name="permissionId">要撤销的权限标识。</param>
    public void Revoke(string permissionId) => Revoke(permissionId, windowName: null);

    /// <summary>
    /// 撤销权限（指定窗口或全局）。
    /// </summary>
    /// <param name="permissionId">要撤销的权限标识。</param>
    /// <param name="windowName">目标窗口名称；<c>null</c> 表示撤销全局授权。</param>
    public void Revoke(string permissionId, string? windowName)
    {
        _grantedPermissions.TryRemove(new PermissionKey(permissionId, windowName), out _);
        _logger?.LogInformation("已撤销权限: {Permission}（窗口={Window}）", permissionId, windowName ?? "全局");
    }

    /// <summary>
    /// 全局显式拒绝权限（应用于所有窗口，deny 优先于 grant）。
    /// 等效于 <see cref="Deny(string, string?)"/> 传入 <c>windowName=null</c>。
    /// 若 <paramref name="permissionId"/> 是已注册的权限集标识，则展开为集内所有权限逐个拒绝。
    /// 对应 Tauri v2 的 deny 语义。
    /// </summary>
    /// <param name="permissionId">要拒绝的权限标识或权限集标识。</param>
    public void Deny(string permissionId) => Deny(permissionId, windowName: null);

    /// <summary>
    /// 显式拒绝权限（指定窗口或全局）。
    /// deny 始终优先于 grant：即便权限被授权，只要匹配到 deny 条目即视为拒绝。
    /// 对应 Tauri v2 的 deny 语义和 Capability.Permissions 中的 "!" 前缀语法。
    /// 若 <paramref name="permissionId"/> 是已注册的权限集标识，则展开为集内所有权限逐个拒绝。
    /// </summary>
    /// <param name="permissionId">要拒绝的权限标识或权限集标识。</param>
    /// <param name="windowName">
    /// 目标窗口名称。传入 <c>null</c> 表示全局拒绝（应用于所有窗口）；
    /// 传入具体窗口名表示仅拒绝该窗口（窗口级隔离）。
    /// </param>
    public void Deny(string permissionId, string? windowName)
    {
        // 若是权限集，展开为集内所有权限逐个拒绝
        if (_permissionSets.TryGetValue(permissionId, out var set))
        {
            foreach (var perm in set.Permissions)
            {
                _deniedPermissions.TryAdd(new PermissionKey(perm, windowName), 0);
            }
            _logger?.LogInformation("已拒绝权限集: {Set}（展开为 {Count} 个权限，窗口={Window}）",
                permissionId, set.Permissions.Count, windowName ?? "全局");
        }
        else
        {
            _deniedPermissions.TryAdd(new PermissionKey(permissionId, windowName), 0);
            _logger?.LogInformation("已拒绝权限: {Permission}（窗口={Window}）",
                permissionId, windowName ?? "全局");
        }
    }

    /// <summary>
    /// 撤销显式拒绝条目（指定窗口或全局）。
    /// 仅移除 <see cref="Deny"/> 添加的拒绝条目，不影响授权状态。
    /// </summary>
    /// <param name="permissionId">要撤销拒绝的权限标识。</param>
    /// <param name="windowName">目标窗口名称；<c>null</c> 表示撤销全局拒绝。</param>
    public void Undeny(string permissionId, string? windowName)
    {
        _deniedPermissions.TryRemove(new PermissionKey(permissionId, windowName), out _);
        _logger?.LogInformation("已撤销拒绝: {Permission}（窗口={Window}）", permissionId, windowName ?? "全局");
    }

    /// <summary>
    /// 检查权限是否被显式拒绝。
    /// 同时检查全局拒绝（Window=null）和窗口级拒绝（Window=windowName）：
    /// 任一存在即视为拒绝。deny 始终优先于 grant。
    /// </summary>
    /// <param name="permissionId">权限标识。</param>
    /// <param name="windowName">当前调用来源窗口名；<c>null</c> 仅检查全局拒绝。</param>
    /// <returns>已拒绝返回 true；未拒绝返回 false。</returns>
    public bool IsDenied(string permissionId, string? windowName)
    {
        // 全局拒绝优先
        if (_deniedPermissions.ContainsKey(new PermissionKey(permissionId, null)))
        {
            return true;
        }

        // 窗口级拒绝
        if (!string.IsNullOrEmpty(windowName) &&
            _deniedPermissions.ContainsKey(new PermissionKey(permissionId, windowName)))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 获取所有已拒绝的权限条目（含窗口信息）。
    /// 用于审计和调试 deny 配置。
    /// </summary>
    /// <returns>已拒绝权限条目的只读集合，每项包含权限标识和窗口名（null 表示全局）。</returns>
    public IReadOnlyCollection<(string Permission, string? Window)> GetDeniedPermissions()
        => _deniedPermissions.Keys.Select(k => (k.Permission, k.Window)).ToList().AsReadOnly();

    /// <summary>
    /// 检查是否已全局授权指定权限。
    /// 等效于 <see cref="IsGranted(string, string?)"/> 传入 <c>windowName=null</c>。
    /// 强制执行 <see cref="PermissionOptions.DenyByDefault"/> 语义：
    /// 当 DenyByDefault=true 时，未授权的权限一律拒绝；
    /// 当 DenyByDefault=false 时，未授权的权限默认放行（仅在显式撤销时拒绝）。
    /// </summary>
    /// <param name="permissionId">权限标识。</param>
    /// <returns>已授权返回 true；未启用权限检查时全部放行。</returns>
    public bool IsGranted(string permissionId) => IsGranted(permissionId, windowName: null);

    /// <summary>
    /// 检查指定窗口是否已授权该权限。
    /// 同时检查全局授权（Window=null）和窗口级授权（Window=windowName）：
    /// 任一存在即视为已授权。对应 Tauri v2 的窗口级 Capability 运行时隔离。
    /// 强制执行 <see cref="PermissionOptions.DenyByDefault"/> 语义。
    /// <para>
    /// <b>deny 优先</b>：若权限被 <see cref="Deny(string, string?)"/> 显式拒绝（全局或窗口级），
    /// 即使已授权也返回 false。对应 Tauri v2 的 deny 语义。
    /// </para>
    /// </summary>
    /// <param name="permissionId">权限标识。</param>
    /// <param name="windowName">
    /// 当前调用来源窗口名。传入 <c>null</c> 时仅检查全局授权（保持向后兼容）；
    /// 传入具体窗口名时同时检查全局授权和该窗口级授权。
    /// </param>
    /// <returns>已授权返回 true；未启用权限检查时全部放行。</returns>
    public bool IsGranted(string permissionId, string? windowName)
    {
        // 未启用权限检查时全部放行
        if (!_options.Enabled) return true;

        // deny 优先：显式拒绝的权限一律不可用（即使已授权）
        if (IsDenied(permissionId, windowName))
        {
            return false;
        }

        // 优先检查全局授权（Window=null）
        var granted = _grantedPermissions.ContainsKey(new PermissionKey(permissionId, null));

        // 若提供了 windowName，再检查窗口级授权
        if (!granted && !string.IsNullOrEmpty(windowName))
        {
            granted = _grantedPermissions.ContainsKey(new PermissionKey(permissionId, windowName));
        }

        // 强制 DenyByDefault 语义
        if (!granted && _options.DenyByDefault)
        {
            return false;
        }

        return granted || !_options.DenyByDefault;
    }

    /// <summary>
    /// 检查指定窗口是否已授权该权限，并校验调用来源 URL。
    /// 对应 Tauri v2 Capability.remote 字段的运行时校验：
    /// <list type="bullet">
    /// <item>权限未授权 → 返回 false。</item>
    /// <item>权限已授权且无任何远程 URL 限制 → 始终允许（本地与远程均可）。</item>
    /// <item>权限已授权且至少一处授权附带 remotePatterns：
    /// 本地源（wails://、http(s)://localhost、http(s)://127.0.0.1、null/空）始终允许；
    /// 远程源必须匹配至少一个已注册的 URL 模式（合并全局和窗口级模式集合）。
    /// </item>
    /// </list>
    /// </summary>
    /// <param name="permissionId">权限标识。</param>
    /// <param name="windowName">当前调用来源窗口名；<c>null</c> 仅检查全局授权。</param>
    /// <param name="origin">
    /// 调用来源 URL（如前端页面 origin）。传入 <c>null</c> 或空字符串视为本地源。
    /// </param>
    /// <returns>已授权且来源允许返回 true；否则返回 false。</returns>
    public bool IsGranted(string permissionId, string? windowName, string? origin)
    {
        // 未启用权限检查时全部放行
        if (!_options.Enabled) return true;

        // 先执行基础 IsGranted 校验（窗口级和全局授权）
        if (!IsGranted(permissionId, windowName)) return false;

        // 校验调用来源 URL
        if (!IsOriginAllowed(permissionId, windowName, origin))
        {
            _logger?.LogWarning("权限拒绝: 权限 {Permission} 不允许来自来源 {Origin}（窗口={Window}）",
                permissionId, string.IsNullOrEmpty(origin) ? "未知" : origin, windowName ?? "全局");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 检查调用来源是否被允许调用指定权限。
    /// 合并 (permission, null) 全局授权和 (permission, windowName) 窗口级授权的远程 URL 模式集合：
    /// 任一集合允许该来源即放行。
    /// </summary>
    /// <param name="permissionId">权限标识。</param>
    /// <param name="windowName">窗口名；可为 null。</param>
    /// <param name="origin">调用来源 URL；可为 null。</param>
    /// <returns>来源允许返回 true；否则返回 false。</returns>
    private bool IsOriginAllowed(string permissionId, string? windowName, string? origin)
    {
        // 本地源始终允许（不受 Remote 限制）
        if (IsLocalOrigin(origin)) return true;

        // 远程源：收集所有适用的远程 URL 限制模式
        UrlWhitelist? globalWhitelist =
            _remoteUrlScopes.TryGetValue(new PermissionKey(permissionId, null), out var gw) ? gw : null;
        UrlWhitelist? windowWhitelist = null;
        if (!string.IsNullOrEmpty(windowName) &&
            _remoteUrlScopes.TryGetValue(new PermissionKey(permissionId, windowName), out var ww))
        {
            windowWhitelist = ww;
        }

        // 无任何远程限制模式 → 不限制来源（远程源也允许）
        if (globalWhitelist is null && windowWhitelist is null) return true;

        // 远程源必须匹配至少一个已注册的 URL 模式
        if (globalWhitelist is not null && globalWhitelist.IsAllowed(origin!)) return true;
        if (windowWhitelist is not null && windowWhitelist.IsAllowed(origin!)) return true;

        return false;
    }

    /// <summary>
    /// 判断来源是否为本地源（不受 Remote 限制）。
    /// 本地源包括：null/空、wails:// 协议、http(s)://localhost、http(s)://127.0.0.1。
    /// </summary>
    /// <param name="origin">调用来源 URL。</param>
    /// <returns>本地源返回 true；远程源或未知返回 false。</returns>
    private static bool IsLocalOrigin(string? origin)
    {
        if (string.IsNullOrEmpty(origin)) return true;

        // 大小写不敏感比较前缀
        if (origin.StartsWith("wails://", StringComparison.OrdinalIgnoreCase)) return true;
        if (origin.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (origin.StartsWith("https://localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (origin.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase)) return true;
        if (origin.StartsWith("https://127.0.0.1", StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }

    /// <summary>
    /// 校验命令方法是否有权限执行（全局）。
    /// 等效于 <see cref="ValidateCommand(MethodInfo, string?)"/> 传入 <c>windowName=null</c>。
    /// </summary>
    /// <param name="method">要校验的方法信息。</param>
    /// <returns>允许执行返回 true；权限不足返回 false。</returns>
    public bool ValidateCommand(MethodInfo method) => ValidateCommand(method, windowName: null);

    /// <summary>
    /// 校验命令方法在指定窗口上下文中是否有权限执行。
    /// 检查方法上的 <see cref="RequireCapabilityAttribute"/> 特性，
    /// 所有标记的能力都必须已授权（全局或窗口级）才能执行。
    /// </summary>
    /// <param name="method">要校验的方法信息。</param>
    /// <param name="windowName">当前调用来源窗口名；<c>null</c> 仅检查全局授权。</param>
    /// <returns>允许执行返回 true；权限不足返回 false。</returns>
    public bool ValidateCommand(MethodInfo method, string? windowName)
    {
        if (!_options.Enabled) return true;

        var attrs = method.GetCustomAttributes<RequireCapabilityAttribute>();
        foreach (var attr in attrs)
        {
            if (!IsGranted(attr.Capability, windowName))
            {
                _logger?.LogWarning("权限拒绝: 命令 {Method} 需要能力 {Capability}（窗口={Window}）",
                    method.Name, attr.Capability, windowName ?? "未知");
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 校验命令方法在指定窗口上下文和来源 URL 下是否有权限执行。
    /// 同时检查 <see cref="RequireCapabilityAttribute"/> 标记的能力授权状态和
    /// Capability.remote 远程 URL 限制。
    /// </summary>
    /// <param name="method">要校验的方法信息。</param>
    /// <param name="windowName">当前调用来源窗口名；<c>null</c> 仅检查全局授权。</param>
    /// <param name="origin">调用来源 URL；<c>null</c> 视为本地源。</param>
    /// <returns>允许执行返回 true；权限不足或来源不允许返回 false。</returns>
    public bool ValidateCommand(MethodInfo method, string? windowName, string? origin)
    {
        if (!_options.Enabled) return true;

        var attrs = method.GetCustomAttributes<RequireCapabilityAttribute>();
        foreach (var attr in attrs)
        {
            if (!IsGranted(attr.Capability, windowName, origin))
            {
                _logger?.LogWarning("权限拒绝: 命令 {Method} 需要能力 {Capability}（窗口={Window}，来源={Origin}）",
                    method.Name, attr.Capability, windowName ?? "未知",
                    string.IsNullOrEmpty(origin) ? "本地" : origin);
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 校验指定能力标识列表是否全部已全局授权。
    /// 等效于 <see cref="ValidateCapabilities(IEnumerable{string}, string?)"/> 传入 <c>windowName=null</c>。
    /// </summary>
    /// <param name="requiredCapabilities">所需能力标识列表。</param>
    /// <returns>全部已授权返回 true；权限不足返回 false。</returns>
    public bool ValidateCapabilities(IEnumerable<string> requiredCapabilities)
        => ValidateCapabilities(requiredCapabilities, windowName: null);

    /// <summary>
    /// 校验指定能力标识列表在指定窗口上下文中是否全部已授权。
    /// 用于检查通过 <see cref="Commands.CommandRegistry.CommandEntry.RequiredCapabilities"/> 声明的能力。
    /// </summary>
    /// <param name="requiredCapabilities">所需能力标识列表。</param>
    /// <param name="windowName">当前调用来源窗口名；<c>null</c> 仅检查全局授权。</param>
    /// <returns>全部已授权返回 true；权限不足返回 false。</returns>
    public bool ValidateCapabilities(IEnumerable<string> requiredCapabilities, string? windowName)
    {
        if (!_options.Enabled) return true;

        foreach (var capability in requiredCapabilities)
        {
            if (!IsGranted(capability, windowName))
            {
                _logger?.LogWarning("权限拒绝: 命令需要能力 {Capability}（窗口={Window}）",
                    capability, windowName ?? "未知");
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 校验指定能力标识列表在指定窗口上下文和来源 URL 下是否全部已授权。
    /// 同时检查能力授权状态和 Capability.remote 远程 URL 限制。
    /// </summary>
    /// <param name="requiredCapabilities">所需能力标识列表。</param>
    /// <param name="windowName">当前调用来源窗口名；<c>null</c> 仅检查全局授权。</param>
    /// <param name="origin">调用来源 URL；<c>null</c> 视为本地源。</param>
    /// <returns>全部已授权且来源允许返回 true；否则返回 false。</returns>
    public bool ValidateCapabilities(IEnumerable<string> requiredCapabilities, string? windowName, string? origin)
    {
        if (!_options.Enabled) return true;

        foreach (var capability in requiredCapabilities)
        {
            if (!IsGranted(capability, windowName, origin))
            {
                _logger?.LogWarning("权限拒绝: 命令需要能力 {Capability}（窗口={Window}，来源={Origin}）",
                    capability, windowName ?? "未知",
                    string.IsNullOrEmpty(origin) ? "本地" : origin);
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 校验命令参数的 Scope 约束。
    /// 对每个 (permissionId, value) 对，检查权限是否已授权，
    /// 若绑定了 IScope 则调用 Allows(value) 校验值是否在允许范围内。
    /// </summary>
    /// <param name="scopeValues">待校验的 (权限标识, 值) 对列表。</param>
    /// <returns>全部通过返回 true，任一不通过返回 false。</returns>
    public bool ValidateScopes(IEnumerable<(string PermissionId, string Value)> scopeValues)
    {
        if (!_options.Enabled) return true;

        foreach (var (permissionId, value) in scopeValues)
        {
            if (!IsGranted(permissionId))
            {
                _logger?.LogWarning("Scope 校验失败：权限 {Permission} 未授权", permissionId);
                return false;
            }
            var scope = GetScope(permissionId);
            if (scope is not null && !scope.Allows(value))
            {
                _logger?.LogWarning("Scope 校验失败：值 {Value} 不在权限 {Permission} 的允许范围内", value, permissionId);
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 检查指定窗口是否匹配能力声明的窗口范围。
    /// 能力的 Windows 列表为空表示应用于所有窗口。
    /// </summary>
    /// <param name="capability">能力声明。</param>
    /// <param name="windowName">窗口名称，null 表示未知窗口（仅匹配全局能力）。</param>
    /// <returns>能力应用于该窗口返回 true。</returns>
    public static bool IsCapabilityApplicableToWindow(Capability capability, string? windowName)
    {
        // 空窗口列表表示全局能力，应用于所有窗口
        if (capability.Windows.Count == 0) return true;

        // 未知窗口仅匹配全局能力
        if (string.IsNullOrEmpty(windowName)) return false;

        return capability.Windows.Contains(windowName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 获取所有已声明的能力。
    /// </summary>
    /// <returns>已声明能力的只读集合。</returns>
    public IReadOnlyCollection<Capability> GetDeclaredCapabilities() => _declaredCapabilities.Values.ToList().AsReadOnly();

    /// <summary>
    /// 获取所有已授权的权限标识（去重，不含窗口信息）。
    /// 兼容旧 API：返回所有全局和窗口级授权的权限名集合。
    /// </summary>
    /// <returns>已授权权限标识的只读集合。</returns>
    public IReadOnlyCollection<string> GetGrantedPermissions()
        => _grantedPermissions.Keys.Select(k => k.Permission).Distinct().ToList().AsReadOnly();

    /// <summary>
    /// 获取所有已授权的权限条目（含窗口信息）。
    /// 用于审计和调试窗口级隔离。
    /// </summary>
    /// <returns>已授权权限条目的只读集合，每项包含权限标识和窗口名（null 表示全局）。</returns>
    public IReadOnlyCollection<(string Permission, string? Window)> GetGrantedPermissionsWithWindows()
        => _grantedPermissions.Keys.Select(k => (k.Permission, k.Window)).ToList().AsReadOnly();

    /// <summary>
    /// 获取所有已注册的权限集。
    /// </summary>
    /// <returns>已注册权限集的只读集合。</returns>
    public IReadOnlyCollection<PermissionSet> GetPermissionSets() => _permissionSets.Values.ToList().AsReadOnly();

    /// <summary>
    /// 权限键复合结构：权限标识 + 窗口名。
    /// Window=null 表示全局授权；非 null 表示窗口级授权。
    /// </summary>
    private readonly record struct PermissionKey(string Permission, string? Window)
    {
        /// <summary>
        /// 大小写不敏感比较窗口名，权限标识符大小写敏感（与 Tauri v2 行为一致）。
        /// </summary>
        public bool Equals(PermissionKey other)
            => string.Equals(Permission, other.Permission, StringComparison.Ordinal)
               && string.Equals(Window, other.Window, StringComparison.OrdinalIgnoreCase);

        public override int GetHashCode()
            => HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(Permission),
                Window is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Window));
    }
}
