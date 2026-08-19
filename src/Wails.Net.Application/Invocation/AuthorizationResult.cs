namespace Wails.Net.Application.Invocation;

/// <summary>
/// 统一授权结果（R2-1 占位，R3 将扩展为包含 Capability/Scope/Remote 三维判定）。
/// 对应融合审计 §10.3 / R2-1 + §10.4 / R3 ACL 收口。
/// </summary>
/// <param name="IsAllowed">是否允许调用。</param>
/// <param name="Reason">拒绝原因（允许时为空）。</param>
public sealed record AuthorizationResult(bool IsAllowed, string? Reason)
{
    /// <summary>
    /// 默认允许结果（无 Reason）。R2-1 阶段 <see cref="InvocationGateway.Authorize"/> 直接返回此值。
    /// </summary>
    public static readonly AuthorizationResult Allow = new(true, null);

    /// <summary>
    /// 创建拒绝结果。
    /// </summary>
    /// <param name="reason">拒绝原因，描述哪个维度（Capability / Scope / Remote / 显式黑名单）失败。</param>
    /// <returns>对应的 <see cref="AuthorizationResult"/> 实例。</returns>
    public static AuthorizationResult Deny(string reason) => new(false, reason);
}