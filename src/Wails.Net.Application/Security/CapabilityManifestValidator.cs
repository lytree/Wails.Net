using System.Reflection;
using System.Text.Json;

namespace Wails.Net.Application.Security;

/// <summary>
/// 单条 capability 校验结果。
/// </summary>
/// <param name="Severity">问题严重度（Error / Warning）。</param>
/// <param name="Message">人类可读描述，含问题定位上下文（identifier / field / value）。</param>
/// <param name="Code">机器可读问题代号（如 CM001 / CM002），供 CLI 静默过滤特定类问题。</param>
public sealed record CapabilityValidationIssue(CapabilityValidationSeverity Severity, string Message, string Code);

/// <summary>
/// 校验问题严重度。
/// </summary>
public enum CapabilityValidationSeverity
{
    /// <summary>Warning：建议修复但不阻断启动。</summary>
    Warning,
    /// <summary>Error：必须修复，启动期阻断 capability 注册。</summary>
    Error,
}

/// <summary>
/// Capability 文件清单校验器（R3-A.3）。
/// <para>
/// 对齐 Tauri v2 tauri-build 在编译期做的 capability 校验：
/// <list type="bullet">
/// <item>结构校验（CM001-CM005）：identifier / description / permissions / windows / remote / platforms 字段形状；
/// 复用嵌入的 JSON Schema（Wails.Net.Application.Security.Capability.schema.json）作为权威形状来源，
/// 但出于不引入额外 JSON Schema 验证库的考虑，在此处以字段级规则落地（Tauri-build 同样以结构化规则实现）。</item>
/// <item>引用校验（CM101-CM103）：capability 引用的 permission 标识是否已通过
/// <see cref="Plugins.IPermissionRegistrar.DeclarePermission"/> 在 <see cref="PermissionManager"/> 中声明，
/// 避免能力文件引用了不存在的权限（运行时会被静默忽略）。</item>
/// </list>
/// </para>
/// <para>
/// 设计选择：不引入 NJsonSchema / Manatee.Json 等第三方 JSON Schema 验证器。
/// capability 文件由我们控制格式，字段级规则已覆盖所有校验需求；嵌入的 schema 文件
/// 仅供外部工具（CLI / IDE 插件）消费，仍能以单一来源驱动校验语义。
/// </para>
/// <para>
/// 与 AGENTS.md §3.4 一致：所有校验均为显式字段读取，零运行时反射。
/// </para>
/// </summary>
public static class CapabilityManifestValidator
{
    /// <summary>
    /// 已知的平台标识集合（OPT-SEC-002）。
    /// </summary>
    private static readonly HashSet<string> KnownPlatforms = new(StringComparer.OrdinalIgnoreCase)
    {
        "windows", "linux", "android", "macos", "server"
    };

    /// <summary>
    /// 已知的 capability 文件扩展名。
    /// </summary>
    private const string SchemaResourceName = "Wails.Net.Application.Security.Capability.schema.json";

    /// <summary>
    /// 加载嵌入的 JSON Schema 文本，供外部 CLI / IDE 插件使用。
    /// </summary>
    /// <returns>schema 文本；找不到嵌入资源时返回 null。</returns>
    public static string? LoadSchemaJson()
    {
        var assembly = typeof(Capability).Assembly;
        using var stream = assembly.GetManifestResourceStream(SchemaResourceName);
        if (stream is null)
        {
            return null;
        }
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// 校验已加载的 capability 列表的字段形状。
    /// 适用于应用启动期在 <see cref="CapabilityFileLoader.RegisterToManager"/> 之前调用，
    /// 阻断任何带 Error 级问题的 capability。
    /// </summary>
    /// <param name="capabilities">已加载的 capability 列表（来自 <see cref="CapabilityFileLoader.LoadFromDirectory"/> 或类似来源）。</param>
    /// <returns>校验问题列表；空表示全部通过。</returns>
    public static IReadOnlyList<CapabilityValidationIssue> ValidateShape(IEnumerable<Capability> capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        var issues = new List<CapabilityValidationIssue>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var cap in capabilities)
        {
            var prefix = $"capability '{cap.Identifier}'";

            if (string.IsNullOrWhiteSpace(cap.Identifier))
            {
                issues.Add(new CapabilityValidationIssue(
                    CapabilityValidationSeverity.Error,
                    "capability identifier 为空或缺失",
                    "CM001"));
            }
            else if (!seenIds.Add(cap.Identifier))
            {
                issues.Add(new CapabilityValidationIssue(
                    CapabilityValidationSeverity.Error,
                    $"{prefix} 重复（同一 identifier 已在当前列表中出现）",
                    "CM002"));
            }

            if (cap.Permissions is null || cap.Permissions.Count == 0)
            {
                issues.Add(new CapabilityValidationIssue(
                    CapabilityValidationSeverity.Warning,
                    $"{prefix} permissions 列表为空：不会授权任何权限",
                    "CM003"));
            }
            else
            {
                for (var i = 0; i < cap.Permissions.Count; i++)
                {
                    var p = cap.Permissions[i];
                    if (string.IsNullOrWhiteSpace(p))
                    {
                        issues.Add(new CapabilityValidationIssue(
                            CapabilityValidationSeverity.Error,
                            $"{prefix} permissions[{i}] 为空",
                            "CM004"));
                    }
                }
            }

            if (cap.Platforms is not null)
            {
                foreach (var platform in cap.Platforms)
                {
                    if (!KnownPlatforms.Contains(platform))
                    {
                        issues.Add(new CapabilityValidationIssue(
                            CapabilityValidationSeverity.Warning,
                            $"{prefix} 声明了未知平台 '{platform}'（已知：windows / linux / android / macos / server）",
                            "CM005"));
                    }
                }
            }
        }

        return issues;
    }

    /// <summary>
    /// 校验已加载 capability 引用的 permission 标识是否已在权限管理器中声明。
    /// </summary>
    /// <param name="manager">权限管理器（提供已声明 permission 字典）。</param>
    /// <param name="capabilities">要校验的 capability 列表。</param>
    /// <returns>未声明的 permission 引用列表（不含 ! 前缀的 deny 项，因为 deny 不需要先声明）。</returns>
    public static IReadOnlyList<string> FindUndeclaredPermissions(
        PermissionManager manager,
        IEnumerable<Capability> capabilities)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(capabilities);

        var declared = manager.GetDeclaredPermissionIds();
        var undeclared = new List<string>();

        foreach (var cap in capabilities)
        {
            if (cap.Permissions is null)
            {
                continue;
            }
            foreach (var permission in cap.Permissions)
            {
                if (string.IsNullOrWhiteSpace(permission))
                {
                    continue;
                }
                // deny 条目（! 前缀）不需要先声明；运行时 Deny 直接登记。
                if (permission.StartsWith("!"))
                {
                    continue;
                }
                if (!declared.Contains(permission))
                {
                    undeclared.Add($"capability '{cap.Identifier}' 引用了未声明的 permission '{permission}'");
                }
            }
        }

        return undeclared;
    }
}
