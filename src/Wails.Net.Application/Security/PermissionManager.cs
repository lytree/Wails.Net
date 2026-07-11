using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Wails.Net.Application.Security;

/// <summary>
/// 权限管理器，运行时校验命令调用是否有权限。
/// </summary>
public sealed class PermissionManager
{
    private readonly HashSet<string> _grantedCapabilities = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Capability> _declaredCapabilities = new(StringComparer.OrdinalIgnoreCase);
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

        foreach (var perm in _options.Permissions)
        {
            _grantedCapabilities.Add(perm);
        }
    }

    /// <summary>
    /// 声明能力。
    /// </summary>
    /// <param name="capability">要声明的能力。</param>
    public void DeclareCapability(Capability capability)
    {
        _declaredCapabilities[capability.Id] = capability;
    }

    /// <summary>
    /// 授权能力。
    /// </summary>
    /// <param name="capabilityId">要授权的能力标识。</param>
    public void Grant(string capabilityId)
    {
        _grantedCapabilities.Add(capabilityId);
        _logger?.LogInformation("已授权能力: {Capability}", capabilityId);
    }

    /// <summary>
    /// 撤销能力。
    /// </summary>
    /// <param name="capabilityId">要撤销的能力标识。</param>
    public void Revoke(string capabilityId)
    {
        _grantedCapabilities.Remove(capabilityId);
    }

    /// <summary>
    /// 检查是否已授权指定能力。
    /// </summary>
    /// <param name="capabilityId">能力标识。</param>
    /// <returns>已授权返回 true；未启用权限检查时全部放行。</returns>
    public bool IsGranted(string capabilityId)
    {
        if (!_options.Enabled) return true; // 未启用权限检查时全部放行
        return _grantedCapabilities.Contains(capabilityId);
    }

    /// <summary>
    /// 校验命令方法是否有权限执行。
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
    /// 获取所有已声明的能力。
    /// </summary>
    /// <returns>已声明能力的只读集合。</returns>
    public IReadOnlyCollection<Capability> GetDeclaredCapabilities() => _declaredCapabilities.Values;

    /// <summary>
    /// 获取所有已授权的能力。
    /// </summary>
    /// <returns>已授权能力标识的只读集合。</returns>
    public IReadOnlyCollection<string> GetGrantedCapabilities() => _grantedCapabilities;
}
