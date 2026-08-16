using System.Collections.Generic;

namespace Wails.Net.Application.Commands;

/// <summary>
/// 命令安全声明：所需 capability、参数级 Scope 提取规则与公开性标记。
/// 作为 <see cref="CommandRegistry.CommandEntry.Security"/> 的运行时数据来源，
/// 让 <see cref="CommandDispatcher"/> 在 MapCommand 注册路径下真正接上 capability/scope 校验。
/// 对应 Tauri v2 command 的 permission/scope 声明模型（学习自 Tauri v2 capability）。
/// <para>
/// 与 <c>CommandEntry.RequiredCapabilities</c>（旧 fields-only API）的关系：
/// <c>Security</c> 为 null 时完全走旧路径，行为兼容（不参与额外校验）；
/// 设置 <c>Security</c> 后 capability、ScopeMapping、IsPublic 三个字段都会被 <see cref="CommandDispatcher"/> 读取。
/// </para>
/// <para>
/// 阶段二（B3 后）源生成器 <c>Wails.Net.SourceGenerators</c> 会针对带 <c>[ScopeParameter]</c> / <c>[RequireCapability]</c>
/// 特性的方法在调用 MapCommand 时未传 <see cref="CommandSecurity"/> 的场景发射 WNET001/WNET002 诊断警告
///（在 <c>Directory.Build.props</c> 的 <c>TreatWarningsAsErrors=true</c> 下即构建失败）。
/// </para>
/// </summary>
public sealed class CommandSecurity
{
    /// <summary>调用该命令所需的最小 capability / permission 标识列表。</summary>
    public IReadOnlyList<string> RequiredCapabilities { get; init; } = System.Array.Empty<string>();

    /// <summary>参数名 → 权限标识的映射。运行时从 JSON 参数按名取值做 Scope 校验（零反射：按属性名取 JsonElement）。</summary>
    public IReadOnlyList<ScopeMapping> ScopeMappings { get; init; } = System.Array.Empty<ScopeMapping>();

    /// <summary>显式声明为公开命令：Strict + DenyByDefault 下仍允许匿名调用（如只读的应用信息类命令）。</summary>
    public bool IsPublic { get; init; }

    /// <summary>
    /// 要求至少一个 capability 才能调用。
    /// </summary>
    /// <param name="capabilities">所需 capability 标识列表（如 <c>fs:allow-read</c>）。</param>
    /// <returns>新的 <see cref="CommandSecurity"/> 实例。</returns>
    public static CommandSecurity Require(params string[] capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        return new CommandSecurity
        {
            RequiredCapabilities = capabilities,
        };
    }

    /// <summary>
    /// 显式公开命令（如只读的应用信息类命令），不参与 DenyByDefault 拦截。
    /// </summary>
    /// <returns>标记为 <see cref="IsPublic"/> = true 的 <see cref="CommandSecurity"/> 实例。</returns>
    public static CommandSecurity Public()
    {
        return new CommandSecurity
        {
            IsPublic = true,
        };
    }

    /// <summary>
    /// 追加参数级 Scope 声明：从名为 <paramref name="parameterName"/> 的 JSON 参数取字符串值，
    /// 交给 <paramref name="permissionId"/> 绑定的 IScope 校验。
    /// </summary>
    /// <remarks>
    /// 零反射：<see cref="CommandDispatcher.ExtractScopeValues"/> 按 camelCase 参数名从 JsonElement 取值。
    /// 与 <c>JsonSerializerDefaults.Web</c> 的命名策略一致（与 MapCommandExtensions 的 ToCamelCase 同步）。
    /// </remarks>
    /// <param name="parameterName">参数名（C# 端 PascalCase，运行时按 camelCase 查找 JSON 属性）。</param>
    /// <param name="permissionId">绑定的权限标识，对应 <see cref="CommandSecurity.Require"/> 列表中的 capability。</param>
    /// <returns>当前实例，便于链式调用。</returns>
    public CommandSecurity WithScope(string parameterName, string permissionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(parameterName);
        ArgumentException.ThrowIfNullOrEmpty(permissionId);
        var merged = new List<ScopeMapping>(ScopeMappings.Count + 1)
        {
        };
        merged.AddRange(ScopeMappings);
        merged.Add(new ScopeMapping(parameterName, permissionId));
        return new CommandSecurity
        {
            RequiredCapabilities = RequiredCapabilities,
            ScopeMappings = merged,
            IsPublic = IsPublic,
        };
    }
}

/// <summary>
/// 参数级 Scope 声明：将命令的某个 JSON 参数与特定权限的 IScope 校验绑定。
/// 由 <see cref="CommandSecurity.WithScope"/> 构造，在 <see cref="CommandDispatcher.ExtractScopeValues"/> 中按名取值。
/// 对应 Tauri v2 command 的参数级 scope 声明。
/// </summary>
/// <param name="ParameterName">C# 端参数名（PascalCase）。运行时按 camelCase 形式查找 JSON 属性。</param>
/// <param name="PermissionId">绑定的权限标识。对应 <see cref="CommandSecurity.Require"/> 列表中的 capability。</param>
public sealed record ScopeMapping(string ParameterName, string PermissionId);
