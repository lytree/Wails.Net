using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Wails.Net.Application.Security;

/// <summary>
/// 权限管理器，运行时校验命令调用是否有权限。
/// 对应 Tauri v2 的权限模型：基于能力（Capability）和权限集（PermissionSet）的细粒度访问控制。
/// </summary>
public sealed class PermissionManager
{
    /// <summary>
    /// 已授权的权限标识集合（线程安全）。
    /// 包含直接授权的权限和从 PermissionSet 展开后的细粒度权限。
    /// </summary>
    private readonly ConcurrentDictionary<string, byte> _grantedPermissions = new(StringComparer.OrdinalIgnoreCase);

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

        // 从配置的 Permissions 列表授权初始权限
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
        _logger?.LogDebug("已声明能力: {Identifier}（{Count} 个权限）", capability.Identifier, capability.Permissions.Count);
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
    /// 授权权限。
    /// 若 <paramref name="permissionId"/> 是已注册的权限集标识，则展开为集内所有权限逐个授权。
    /// </summary>
    /// <param name="permissionId">要授权的权限标识或权限集标识。</param>
    public void Grant(string permissionId)
    {
        // 若是权限集，展开为集内所有权限
        if (_permissionSets.TryGetValue(permissionId, out var set))
        {
            foreach (var perm in set.Permissions)
            {
                _grantedPermissions.TryAdd(perm, 0);
            }
            _logger?.LogInformation("已授权权限集: {Set}（展开为 {Count} 个权限）", permissionId, set.Permissions.Count);
        }
        else
        {
            _grantedPermissions.TryAdd(permissionId, 0);
            _logger?.LogInformation("已授权权限: {Permission}", permissionId);
        }
    }

    /// <summary>
    /// 撤销权限。
    /// </summary>
    /// <param name="permissionId">要撤销的权限标识。</param>
    public void Revoke(string permissionId)
    {
        _grantedPermissions.TryRemove(permissionId, out _);
        _logger?.LogInformation("已撤销权限: {Permission}", permissionId);
    }

    /// <summary>
    /// 检查是否已授权指定权限。
    /// 强制执行 <see cref="PermissionOptions.DenyByDefault"/> 语义：
    /// 当 DenyByDefault=true 时，未授权的权限一律拒绝；
    /// 当 DenyByDefault=false 时，未授权的权限默认放行（仅在显式撤销时拒绝）。
    /// </summary>
    /// <param name="permissionId">权限标识。</param>
    /// <returns>已授权返回 true；未启用权限检查时全部放行。</returns>
    public bool IsGranted(string permissionId)
    {
        // 未启用权限检查时全部放行
        if (!_options.Enabled) return true;

        var granted = _grantedPermissions.ContainsKey(permissionId);

        // 强制 DenyByDefault 语义
        if (!granted && _options.DenyByDefault)
        {
            return false;
        }

        return granted || !_options.DenyByDefault;
    }

    /// <summary>
    /// 校验命令方法是否有权限执行。
    /// 检查方法上的 <see cref="RequireCapabilityAttribute"/> 特性，
    /// 所有标记的能力都必须已授权才能执行。
    /// </summary>
    /// <param name="method">要校验的方法信息。</param>
    /// <returns>允许执行返回 true；权限不足返回 false。</returns>
    public bool ValidateCommand(MethodInfo method)
    {
        if (!_options.Enabled) return true;

        var attrs = method.GetCustomAttributes<RequireCapabilityAttribute>();
        foreach (var attr in attrs)
        {
            if (!IsGranted(attr.Capability))
            {
                _logger?.LogWarning("权限拒绝: 命令 {Method} 需要能力 {Capability}", method.Name, attr.Capability);
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
    /// 获取所有已授权的权限标识。
    /// </summary>
    /// <returns>已授权权限标识的只读集合。</returns>
    public IReadOnlyCollection<string> GetGrantedPermissions() => _grantedPermissions.Keys.ToList().AsReadOnly();

    /// <summary>
    /// 获取所有已注册的权限集。
    /// </summary>
    /// <returns>已注册权限集的只读集合。</returns>
    public IReadOnlyCollection<PermissionSet> GetPermissionSets() => _permissionSets.Values.ToList().AsReadOnly();
}
