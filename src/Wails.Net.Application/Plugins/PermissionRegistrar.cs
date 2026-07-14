using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Wails.Net.Application.Security;

namespace Wails.Net.Application.Plugins;

/// <summary>
/// 权限声明器实现，将插件声明的权限集、细粒度权限和作用域委托给 <see cref="PermissionManager"/>。
/// 对应 Tauri v2 的 <c>PermissionRegistrar</c>：插件在 <see cref="IPlugin.Configure"/> 中通过此声明器
/// 声明自身权限，运行时校验命令调用时即可识别这些权限标识。
/// </summary>
internal sealed class PermissionRegistrar : IPermissionRegistrar
{
    private readonly PermissionManager _permissionManager;
    private readonly ILogger? _logger;

    /// <summary>
    /// 已声明的细粒度权限字典（线程安全），键为权限标识，值为权限描述。
    /// 仅用于记录和文档生成，不参与运行时权限校验决策。
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _declaredPermissions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 构造权限声明器。
    /// </summary>
    /// <param name="permissionManager">权限管理器，所有声明将委托到此实例。</param>
    /// <param name="logger">日志记录器，可为 null。</param>
    public PermissionRegistrar(PermissionManager permissionManager, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(permissionManager);
        _permissionManager = permissionManager;
        _logger = logger;
    }

    /// <summary>
    /// 注册插件权限集。
    /// 将权限集委托给 <see cref="PermissionManager.RegisterPermissionSet"/> 注册，
    /// 使能力声明中引用权限集标识后能展开为集内所有细粒度权限。
    /// </summary>
    public void RegisterPermissionSet(string identifier, string description, params string[] permissions)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);

        var set = new PermissionSet(identifier)
        {
            Description = description,
            Permissions = permissions.ToList()
        };
        _permissionManager.RegisterPermissionSet(set);
        _logger?.LogDebug("插件权限集已注册: {Identifier}（{Count} 个权限）", identifier, permissions.Length);
    }

    /// <summary>
    /// 声明细粒度权限。
    /// 记录到内部字典，便于文档生成和运行时权限标识查询。
    /// </summary>
    public void DeclarePermission(string identifier, string description)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        _declaredPermissions[identifier] = description;
        _logger?.LogDebug("权限已声明: {Identifier} - {Description}", identifier, description);
    }

    /// <summary>
    /// 为权限绑定作用域。
    /// 委托给 <see cref="PermissionManager.SetScope"/>，绑定后命令调度时会自动校验参数值是否在作用域内。
    /// </summary>
    public void BindScope(string permissionId, IScope scope)
    {
        _permissionManager.SetScope(permissionId, scope);
        _logger?.LogDebug("权限作用域已绑定: {PermissionId}", permissionId);
    }

    /// <summary>
    /// 获取所有已声明的细粒度权限标识（用于测试和文档生成）。
    /// </summary>
    /// <returns>已声明权限标识的只读集合。</returns>
    public IReadOnlyCollection<string> GetDeclaredPermissionIds() => _declaredPermissions.Keys.ToList().AsReadOnly();
}

/// <summary>
/// 空权限声明器，所有操作均为 no-op。
/// 用于 PermissionManager 不可用的场景（如未注册权限管理器的测试环境），
/// 确保插件代码无需感知权限管理器是否存在。
/// </summary>
internal sealed class NullPermissionRegistrar : IPermissionRegistrar
{
    /// <summary>单例实例，避免重复分配。</summary>
    public static NullPermissionRegistrar Instance { get; } = new();

    private NullPermissionRegistrar() { }

    /// <inheritdoc />
    public void RegisterPermissionSet(string identifier, string description, params string[] permissions) { }

    /// <inheritdoc />
    public void DeclarePermission(string identifier, string description) { }

    /// <inheritdoc />
    public void BindScope(string permissionId, IScope scope) { }
}
