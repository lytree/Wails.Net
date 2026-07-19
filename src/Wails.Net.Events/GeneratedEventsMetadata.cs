namespace Wails.Net.Events;

/// <summary>
/// 枚举成员元数据，由源代码生成器在编译期填充。
/// 对应 <c>EventGenerator</c> 生成 TypeScript 枚举时所需的成员信息。
/// </summary>
/// <param name="Name">枚举成员名称。</param>
/// <param name="Value">枚举成员的数值（字符串形式，保留原始进制表示）。</param>
public sealed record EventEnumMemberInfo(
    string Name,
    string Value);

/// <summary>
/// 事件枚举类型元数据，由源代码生成器在编译期填充。
/// 替代运行时反射 <see cref="System.Reflection.Assembly.GetExportedTypes"/> 提供的枚举信息，
/// 供 <c>Wails.Net.Generator.EventGenerator</c> 使用。
/// </summary>
/// <param name="FullName">枚举类型全限定名（Namespace.EnumName）。</param>
/// <param name="Namespace">命名空间。</param>
/// <param name="Name">枚举类型名称（不含命名空间）。</param>
/// <param name="UnderlyingTypeName">枚举底层类型的 TypeScript 表示（"number" 或具体类型如 "int"/"uint"/"byte"）。</param>
/// <param name="Members">枚举成员列表。</param>
public sealed record EventEnumInfo(
    string FullName,
    string Namespace,
    string Name,
    string UnderlyingTypeName,
    IReadOnlyList<EventEnumMemberInfo> Members);

/// <summary>
/// KnownEvents 常量字段元数据，由源代码生成器在编译期填充。
/// 替代运行时反射 <see cref="System.Reflection.FieldInfo.GetValue"/> 提供的常量值，
/// 供 <c>Wails.Net.Generator.EventGenerator.GenerateKnownEvents</c> 使用。
/// </summary>
/// <param name="ClassName">常量类名（如 "KnownEvents"）。</param>
/// <param name="FieldName">常量字段名。</param>
/// <param name="Value">常量字段值（字符串形式）。</param>
public sealed record KnownEventFieldInfo(
    string ClassName,
    string FieldName,
    string Value);

/// <summary>
/// 编译时生成的事件元数据注册表。
/// 源代码生成器 <c>Wails.Net.SourceGenerators.BindingSourceGenerator</c> 在编译期生成代码，
/// 通过 <see cref="Register(EventEnumInfo)"/> 和 <see cref="Register(KnownEventFieldInfo)"/>
/// 在模块加载时填充 <see cref="Enums"/> 和 <see cref="KnownEvents"/> 列表。
/// </summary>
/// <remarks>
/// 此注册表替代了原 <c>EventGenerator</c> 中基于 <see cref="System.Reflection"/> 的运行时分析，
/// 消除了反射开销并支持 AOT 裁剪。
/// </remarks>
public static class GeneratedEventsMetadata
{
    /// <summary>
    /// 已注册的枚举类型元数据列表。
    /// 由源生成器生成的 <c>[ModuleInitializer]</c> 方法在模块加载时填充。
    /// </summary>
    private static readonly List<EventEnumInfo> _enums = new();

    /// <summary>
    /// 已注册的 KnownEvents 常量字段元数据列表。
    /// 由源生成器生成的 <c>[ModuleInitializer]</c> 方法在模块加载时填充。
    /// </summary>
    private static readonly List<KnownEventFieldInfo> _knownEvents = new();

    /// <summary>
    /// 获取已注册的所有枚举类型元数据的只读视图。
    /// </summary>
    public static IReadOnlyList<EventEnumInfo> Enums => _enums;

    /// <summary>
    /// 获取已注册的所有 KnownEvents 常量字段元数据的只读视图。
    /// </summary>
    public static IReadOnlyList<KnownEventFieldInfo> KnownEvents => _knownEvents;

    /// <summary>
    /// 注册一个枚举类型元数据。
    /// 通常由源生成器生成的代码在 <c>[ModuleInitializer]</c> 中调用。
    /// </summary>
    /// <param name="enumInfo">要注册的枚举类型元数据。</param>
    public static void Register(EventEnumInfo enumInfo) => _enums.Add(enumInfo);

    /// <summary>
    /// 注册一个 KnownEvents 常量字段元数据。
    /// 通常由源生成器生成的代码在 <c>[ModuleInitializer]</c> 中调用。
    /// </summary>
    /// <param name="field">要注册的常量字段元数据。</param>
    public static void Register(KnownEventFieldInfo field) => _knownEvents.Add(field);

    /// <summary>
    /// 清除所有已注册的元数据（仅用于测试）。
    /// </summary>
    internal static void Clear()
    {
        _enums.Clear();
        _knownEvents.Clear();
    }
}
