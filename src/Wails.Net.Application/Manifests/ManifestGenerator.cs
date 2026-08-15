using System.Text.Json;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Security;

namespace Wails.Net.Application.Manifests;

/// <summary>
/// 命令清单条目，记录单个命令的元数据。
/// 对应审计 P1-2：以命令 manifest 为机器可读的单一来源，统一驱动前端 TypeScript
/// wrapper 生成、插件文档命令表与 capability 默认权限清单，消除前后端 API 漂移。
/// </summary>
/// <param name="Name">命令名称（前端调用名，如 <c>fs.readFile</c>）。</param>
/// <param name="RequiredCapabilities">命令所需能力标识列表；空表示无权限要求（匿名命令）。</param>
/// <param name="Anonymous">是否为匿名命令（无需 capability 覆盖）。<see cref="RequiredCapabilities"/> 为空时为 true。</param>
/// <param name="Parameters">业务参数类型声明列表（TypeScript 类型，如 <c>path: string</c>）；无业务参数时为空。</param>
public sealed record CommandManifestEntry(
    string Name,
    IReadOnlyList<string> RequiredCapabilities,
    bool Anonymous,
    IReadOnlyList<string> Parameters);

/// <summary>
/// 能力清单条目，记录单个 capability 声明的元数据。
/// </summary>
/// <param name="Identifier">能力标识符（如 <c>main-capability</c>）。</param>
/// <param name="Permissions">能力包含的权限标识列表（含可能的 <c>!</c> 前缀显式拒绝）。</param>
/// <param name="Windows">应用此能力的窗口名称列表。</param>
/// <param name="Remote">远程来源 URL 模式列表。</param>
/// <param name="Platforms">平台过滤列表。</param>
public sealed record CapabilityManifestEntry(
    string Identifier,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<string> Windows,
    IReadOnlyList<string> Remote,
    IReadOnlyList<string> Platforms);

/// <summary>
/// 命令清单（commands.manifest.json）。
/// </summary>
/// <param name="Version">清单版本号。</param>
/// <param name="GeneratedAt">生成时间（UTC）。</param>
/// <param name="Commands">命令条目列表。</param>
public sealed record CommandsManifest(
    string Version,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<CommandManifestEntry> Commands);

/// <summary>
/// 能力清单（capabilities.manifest.json）。
/// </summary>
/// <param name="Version">清单版本号。</param>
/// <param name="GeneratedAt">生成时间（UTC）。</param>
/// <param name="Capabilities">能力条目列表。</param>
public sealed record CapabilitiesManifest(
    string Version,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<CapabilityManifestEntry> Capabilities);

/// <summary>
/// 清单生成器（审计 P1-2 落地）。
/// <para>
/// 从 <see cref="CommandRegistry"/>（命令注册结果）与 capability 声明生成机器可读清单，
/// 并支持"敏感命令必须被 capability 覆盖"的一致性校验，供测试、文档与前端 API
/// 生成统一消费。纯数据序列化，零反射（遵循 AGENTS.md §3.4）。
/// </para>
/// </summary>
public static class ManifestGenerator
{
    /// <summary>清单版本号。</summary>
    public const string ManifestVersion = "1";

    /// <summary>
    /// 命令清单 JSON 文件名（<c>commands.manifest.json</c>）。
    /// 应用侧导出与 CLI 生成统一使用此文件名（P2-1 收尾）。
    /// </summary>
    public const string ManifestFileName = "commands.manifest.json";

    /// <summary>
    /// 从命令注册表生成命令清单。
    /// 命令按名称序排列；<see cref="CommandManifestEntry.Anonymous"/> 由
    /// <see cref="CommandRegistry.CommandEntry.RequiredCapabilities"/> 是否为空推导；
    /// <see cref="CommandManifestEntry.Parameters"/> 由命令的业务参数类型（P2-1 签名元数据）映射为
    /// TypeScript 类型声明（如 <c>path: string</c>）。
    /// </summary>
    /// <param name="registry">命令注册表（已由插件填充）。</param>
    /// <returns>命令清单。</returns>
    public static CommandsManifest GenerateCommandsManifest(CommandRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var entries = registry.GetCommandNames()
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name =>
            {
                var entry = registry.Find(name);
                var required = entry?.RequiredCapabilities ?? [];
                var parameters = (entry?.EffectiveParameterTypes ?? [])
                    .Select((type, index) => $"{ToParameterName(index)}: {TypeToTypeScript(type)}")
                    .ToArray();
                return new CommandManifestEntry(name, required, Anonymous: required.Count == 0, parameters);
            })
            .ToArray();

        return new CommandsManifest(ManifestVersion, DateTimeOffset.UtcNow, entries);
    }

    /// <summary>
    /// 将 .NET 类型映射为 TypeScript 类型（P2-1）。
    /// 基础映射：字符串/字符→string、整数/浮点/十进制→number、布尔→boolean、
    /// 枚举→string（前端以语义化字符串传参）、Guid/DateTime/TimeSpan→string、
    /// 数组/集合→数组、可空类型剥壳、其余复杂类型→object。
    /// </summary>
    /// <param name="type">.NET 类型。</param>
    /// <returns>TypeScript 类型字符串。</returns>
    public static string TypeToTypeScript(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        // 可空值类型剥壳：Nullable<T> → T
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
        {
            return TypeToTypeScript(underlying);
        }

        if (type == typeof(string) || type == typeof(char))
        {
            return "string";
        }

        if (type == typeof(bool))
        {
            return "boolean";
        }

        if (type == typeof(byte) || type == typeof(sbyte) ||
            type == typeof(short) || type == typeof(ushort) ||
            type == typeof(int) || type == typeof(uint) ||
            type == typeof(long) || type == typeof(ulong) ||
            type == typeof(float) || type == typeof(double) ||
            type == typeof(decimal))
        {
            return "number";
        }

        if (type == typeof(Guid) || type == typeof(DateTime) || type == typeof(DateTimeOffset) ||
            type == typeof(TimeSpan))
        {
            return "string";
        }

        if (type.IsEnum)
        {
            return "string";
        }

        if (type.IsArray)
        {
            return $"{TypeToTypeScript(type.GetElementType()!)}[]";
        }

        // 常见集合类型 → 数组（IEnumerable<T>、List<T>、IReadOnlyList<T> 等）
        if (type != typeof(string) &&
            type.GetInterfaces().Append(type).Any(t =>
                t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>)))
        {
            var elementType = type.GetGenericArguments()[0];
            return $"{TypeToTypeScript(elementType)}[]";
        }

        return "object";
    }

    /// <summary>
    /// 生成业务参数的 TypeScript 参数名（arg0/arg1…，与前端按位置传参的语义一致）。
    /// </summary>
    /// <param name="index">参数位置。</param>
    /// <returns>参数名。</returns>
    private static string ToParameterName(int index) => $"arg{index}";

    /// <summary>
    /// 从 capability 声明列表生成能力清单。
    /// 能力按标识符序排列。
    /// </summary>
    /// <param name="capabilities">能力声明列表（可来自 <see cref="CapabilityFileLoader.LoadFromDirectory"/>）。</param>
    /// <returns>能力清单。</returns>
    public static CapabilitiesManifest GenerateCapabilitiesManifest(IEnumerable<Capability> capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        var entries = capabilities
            .OrderBy(c => c.Identifier, StringComparer.Ordinal)
            .Select(c => new CapabilityManifestEntry(
                c.Identifier,
                c.Permissions.ToArray(),
                c.Windows.ToArray(),
                c.Remote.ToArray(),
                c.Platforms.ToArray()))
            .ToArray();

        return new CapabilitiesManifest(ManifestVersion, DateTimeOffset.UtcNow, entries);
    }

    /// <summary>
    /// 生成命令清单 JSON（commands.manifest.json 格式）。
    /// 使用源生成序列化器（NativeAOT 兼容，P2-4）。
    /// </summary>
    /// <param name="registry">命令注册表。</param>
    /// <returns>缩进格式的 JSON 字符串。</returns>
    public static string GenerateCommandsManifestJson(CommandRegistry registry)
        => JsonSerializer.Serialize(GenerateCommandsManifest(registry), ManifestsJsonSerializerContext.Default.CommandsManifest);

    /// <summary>
    /// 序列化已生成的命令清单为 JSON（commands.manifest.json 格式，P2-1 收尾）。
    /// 与 <see cref="GenerateCommandsManifestJson(CommandRegistry)"/> 的序列化格式一致，
    /// 用于应用侧导出已构建的清单（如 <see cref="Hosting.DesktopApplication.WriteCommandsManifestToDirectory"/>）。
    /// 使用源生成序列化器（NativeAOT 兼容，P2-4）。
    /// </summary>
    /// <param name="manifest">已生成的命令清单。</param>
    /// <returns>缩进格式的 JSON 字符串。</returns>
    public static string GenerateCommandsManifestJson(CommandsManifest manifest)
        => JsonSerializer.Serialize(manifest, ManifestsJsonSerializerContext.Default.CommandsManifest);

    /// <summary>
    /// 生成能力清单 JSON（capabilities.manifest.json 格式）。
    /// 使用源生成序列化器（NativeAOT 兼容，P2-4）。
    /// </summary>
    /// <param name="capabilities">能力声明列表。</param>
    /// <returns>缩进格式的 JSON 字符串。</returns>
    public static string GenerateCapabilitiesManifestJson(IEnumerable<Capability> capabilities)
        => JsonSerializer.Serialize(GenerateCapabilitiesManifest(capabilities), ManifestsJsonSerializerContext.Default.CapabilitiesManifest);

    /// <summary>
    /// 校验命令所需能力是否被 capability 声明覆盖（P1-2 一致性检查）。
    /// <para>
    /// 规则：
    /// <list type="bullet">
    /// <item>所有注册命令的 <see cref="CommandRegistry.CommandEntry.RequiredCapabilities"/>
    /// 必须出现在至少一个 capability 的 permissions 集合中；</item>
    /// <item>带 <c>!</c> 前缀的权限是显式拒绝，<b>不计入</b>覆盖集合；</item>
    /// <item>匿名命令（RequiredCapabilities 为空）不参与校验。</item>
    /// </list>
    /// 注意：permission set（如 <c>fs:default</c>）的字面展开由
    /// <see cref="PermissionManager"/> 在运行时处理，本校验只做字面匹配；
    /// 需要集合展开时由调用方在生成前先解析。
    /// </para>
    /// </summary>
    /// <param name="registry">命令注册表。</param>
    /// <param name="capabilities">能力声明列表。</param>
    /// <returns>未覆盖的命令与缺失能力描述列表；全部覆盖时返回空列表。</returns>
    public static IReadOnlyList<string> FindUncoveredCapabilities(
        CommandRegistry registry, IEnumerable<Capability> capabilities)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(capabilities);

        var granted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var capability in capabilities)
        {
            foreach (var permission in capability.Permissions)
            {
                // 剥离 "!" 前缀：显式拒绝不计入覆盖集合
                granted.Add(permission.StartsWith('!') ? permission[1..] : permission);
            }
        }

        var uncovered = new List<string>();
        foreach (var name in registry.GetCommandNames().OrderBy(n => n, StringComparer.Ordinal))
        {
            var required = registry.Find(name)?.RequiredCapabilities ?? [];
            if (required.Count == 0)
            {
                continue; // 匿名命令，跳过
            }

            foreach (var capabilityId in required)
            {
                if (!granted.Contains(capabilityId))
                {
                    uncovered.Add($"命令 '{name}' 需要能力 '{capabilityId}'，但未被任何 capability 覆盖");
                }
            }
        }

        return uncovered;
    }
}
