using System.Collections.Concurrent;
using Wails.Net.Application.Security;

namespace Wails.Net.Application.Plugins;

/// <summary>
/// 延迟权限声明器（A-2 / P1-5 审计项落地）。
/// <para>
/// 在 Host 构建前收集插件声明的权限集、细粒度权限与作用域绑定，
/// 替代「构建临时 ServiceProvider 解析 <see cref="PermissionManager"/>」的旧方案，
/// 由 <see cref="Hosting.DesktopApplicationBuilder"/> 在 Host 构建后统一
/// materialize（重放）到真实 <see cref="PermissionManager"/> 实例。
/// </para>
/// <para>
/// 收益：消除临时容器与最终 Host 容器的双 singleton 实例风险；
/// 插件 <c>ConfigureServices</c> 注册的有状态服务不再被临时容器提前实例化。
/// </para>
/// </summary>
internal sealed class DeferredPermissionRegistrar : IPermissionRegistrar
{
    private readonly List<(string Identifier, string Description, string[] Permissions)> _permissionSets = new();
    private readonly List<(string PermissionId, IScope Scope)> _scopes = new();
    private readonly ConcurrentDictionary<string, string> _declaredPermissions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 是否收集到任何声明（供构建器判断是否需要 materialize）。
    /// </summary>
    public bool HasDeclarations =>
        _permissionSets.Count > 0 || _scopes.Count > 0 || _declaredPermissions.Count > 0;

    /// <summary>
    /// 已声明的细粒度权限只读快照（权限标识 → 描述）。
    /// 供文档生成与审计在物化前查询（见 §2.5 卫生项）。
    /// </summary>
    public IReadOnlyDictionary<string, string> DeclaredPermissions
        => new Dictionary<string, string>(_declaredPermissions, StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public void RegisterPermissionSet(string identifier, string description, params string[] permissions)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        _permissionSets.Add((identifier, description, permissions ?? Array.Empty<string>()));
    }

    /// <inheritdoc />
    public void DeclarePermission(string identifier, string description)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        _declaredPermissions[identifier] = description;
    }

    /// <inheritdoc />
    public void BindScope(string permissionId, IScope scope)
    {
        ArgumentException.ThrowIfNullOrEmpty(permissionId);
        ArgumentNullException.ThrowIfNull(scope);
        _scopes.Add((permissionId, scope));
    }

    /// <summary>
    /// 将收集的权限声明物化（重放）到真实 <see cref="PermissionManager"/>。
    /// 由 <see cref="Hosting.DesktopApplicationBuilder.Build"/> 在 Host 构建完成后调用，
    /// 确保能力文件加载与命令调度时权限集、作用域均已就绪。
    /// </summary>
    /// <param name="permissionManager">已构建的权限管理器实例。</param>
    public void ReplayInto(PermissionManager permissionManager)
    {
        ArgumentNullException.ThrowIfNull(permissionManager);

        foreach (var (identifier, description, permissions) in _permissionSets)
        {
            permissionManager.RegisterPermissionSet(new PermissionSet(identifier)
            {
                Description = description,
                Permissions = permissions.ToList(),
            });
        }

        foreach (var (permissionId, scope) in _scopes)
        {
            permissionManager.SetScope(permissionId, scope);
        }

        // 将延迟收集的细粒度权限登记到运行时权限清单数据源（见 §2.5 卫生项：
        // 此前仅收集不重放，导致 DeclarePermission 的声明在物化后丢失）。
        foreach (var (identifier, description) in _declaredPermissions)
        {
            permissionManager.RegisterDeclaredPermission(identifier, description);
        }
    }
}
