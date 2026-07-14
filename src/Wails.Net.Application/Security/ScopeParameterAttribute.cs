namespace Wails.Net.Application.Security;

/// <summary>
/// 标记命令方法的裸 <c>string</c> 参数为 Scope 校验源。
/// 对应 Tauri v2 的参数级 Scope：标记后，<see cref="Commands.CommandDispatcher"/>
/// 在调度时自动提取参数值并使用指定权限进行 Scope 校验。
/// <para>
/// 仅对方法组注册的命令有效（lambda 表达式参数无法加特性）。
/// 与 <see cref="IScopeParameter"/> 互补：
/// <list type="bullet">
/// <item><see cref="IScopeParameter"/>：用于 Options 类（封装多个字段）</item>
/// <item><see cref="ScopeParameterAttribute"/>：用于裸 string 参数（单值）</item>
/// </list>
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class ScopeParameterAttribute : Attribute
{
    /// <summary>
    /// 获取关联的权限标识（如 <c>fs:allow-read</c>）。
    /// </summary>
    public string PermissionId { get; }

    /// <summary>
    /// 获取或设置参数在 JSON 中的字段名。
    /// 默认使用参数名的 camelCase 形式。
    /// </summary>
    public string? JsonPropertyName { get; set; }

    /// <summary>
    /// 初始化 <see cref="ScopeParameterAttribute"/>。
    /// </summary>
    /// <param name="permissionId">关联的权限标识。</param>
    public ScopeParameterAttribute(string permissionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(permissionId);
        PermissionId = permissionId;
    }
}
