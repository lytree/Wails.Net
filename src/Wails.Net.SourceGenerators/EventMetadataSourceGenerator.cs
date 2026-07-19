using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Wails.Net.SourceGenerators;

/// <summary>
/// 源代码生成器，扫描编译中的所有公共枚举类型和 KnownEvents 风格常量类，
/// 生成元数据注册代码，替代运行时反射 Assembly.GetExportedTypes / FieldInfo.GetValue。
/// </summary>
/// <remarks>
/// 此生成器扫描的类型：
/// <list type="bullet">
/// <item>所有公共枚举类型（生成 <c>EventEnumInfo</c> 元数据）。</item>
/// <item>所有公共静态类中包含 <c>public const string</c> 字段的类型（生成 <c>KnownEventFieldInfo</c> 元数据）。</item>
/// </list>
/// 生成结果通过 <c>[ModuleInitializer]</c> 自动注册到 <c>GeneratedEventsMetadata</c>，
/// 供 <c>Wails.Net.Generator.EventGenerator</c> 使用，消除运行时反射。
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class EventMetadataSourceGenerator : IIncrementalGenerator
{
    /// <summary>
    /// 初始化生成器，注册编译提供者。
    /// </summary>
    /// <param name="context">增量生成器上下文。</param>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var eventMetadata = context.CompilationProvider
            .Select(static (compilation, _) => CollectEventMetadata(compilation));

        context.RegisterSourceOutput(eventMetadata, static (spc, metadata) =>
        {
            if (metadata is null)
            {
                return;
            }

            var code = GenerateCode(metadata);
            if (!string.IsNullOrEmpty(code))
            {
                spc.AddSource("WailsGeneratedEventsMetadata.g.cs", code);
            }
        });
    }

    /// <summary>
    /// 收集编译中所有需要导出的枚举类型和 KnownEvents 常量字段元数据。
    /// </summary>
    /// <param name="compilation">编译对象。</param>
    /// <returns>收集到的事件元数据，若无任何内容则返回 null。</returns>
    private static EventMetadata? CollectEventMetadata(Compilation compilation)
    {
        var enums = new List<EventEnumData>();
        var knownEventFields = new List<KnownEventFieldData>();

        // 遍历所有命名空间（包括全局命名空间）
        VisitNamespace(compilation.GlobalNamespace, enums, knownEventFields);

        if (enums.Count == 0 && knownEventFields.Count == 0)
        {
            return null;
        }

        return new EventMetadata(enums, knownEventFields);
    }

    /// <summary>
    /// 递归访问命名空间，收集枚举和 KnownEvents 类的元数据。
    /// </summary>
    /// <param name="namespaceSymbol">命名空间符号。</param>
    /// <param name="enums">收集到的枚举数据列表。</param>
    /// <param name="knownEventFields">收集到的 KnownEvents 字段数据列表。</param>
    private static void VisitNamespace(
        INamespaceSymbol namespaceSymbol,
        List<EventEnumData> enums,
        List<KnownEventFieldData> knownEventFields)
    {
        // 处理当前命名空间下的所有类型
        foreach (var typeMember in namespaceSymbol.GetTypeMembers())
        {
            // 跳过非公共类型
            if (typeMember.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }

            // 处理枚举类型
            if (typeMember.TypeKind == TypeKind.Enum)
            {
                var enumData = CollectEnumData(typeMember);
                if (enumData is not null)
                {
                    enums.Add(enumData);
                }
                continue;
            }

            // 处理公共静态类中包含 public const string 字段的情况
            if (typeMember.TypeKind == TypeKind.Class && typeMember.IsStatic)
            {
                var fields = CollectKnownEventFields(typeMember);
                if (fields.Count > 0)
                {
                    knownEventFields.AddRange(fields);
                }
            }
        }

        // 递归处理子命名空间
        foreach (var subNamespace in namespaceSymbol.GetNamespaceMembers())
        {
            VisitNamespace(subNamespace, enums, knownEventFields);
        }
    }

    /// <summary>
    /// 收集单个枚举类型的元数据。
    /// </summary>
    /// <param name="enumType">枚举类型符号。</param>
    /// <returns>枚举数据，若无法获取则返回 null。</returns>
    private static EventEnumData? CollectEnumData(INamedTypeSymbol enumType)
    {
        var underlyingType = enumType.EnumUnderlyingType;
        if (underlyingType is null)
        {
            return null;
        }

        var underlyingTypeName = MapUnderlyingTypeToName(underlyingType.SpecialType);
        var members = new List<EventEnumMemberData>();

        foreach (var member in enumType.GetMembers().OfType<IFieldSymbol>())
        {
            // 跳过特殊字段（如 value__ 字段）
            if (member.Name == "__value" || !member.IsStatic || !member.HasConstantValue)
            {
                continue;
            }

            // 将常量值转换为字符串形式
            var valueStr = FormatEnumValue(member.ConstantValue, underlyingType.SpecialType);
            members.Add(new EventEnumMemberData(member.Name, valueStr));
        }

        if (members.Count == 0)
        {
            return null;
        }

        var ns = enumType.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        var fullName = string.IsNullOrEmpty(ns) ? enumType.Name : $"{ns}.{enumType.Name}";

        return new EventEnumData(
            fullName,
            ns,
            enumType.Name,
            underlyingTypeName,
            members);
    }

    /// <summary>
    /// 收集 KnownEvents 风格类中的 public const string 字段。
    /// </summary>
    /// <param name="typeSymbol">类型符号。</param>
    /// <returns>字段数据列表。</returns>
    private static List<KnownEventFieldData> CollectKnownEventFields(INamedTypeSymbol typeSymbol)
    {
        var fields = new List<KnownEventFieldData>();

        foreach (var member in typeSymbol.GetMembers().OfType<IFieldSymbol>())
        {
            // 仅收集 public static const string 字段
            if (member.DeclaredAccessibility != Accessibility.Public
                || !member.IsConst
                || !member.IsStatic
                || member.Type.SpecialType != SpecialType.System_String
                || !member.HasConstantValue)
            {
                continue;
            }

            var value = member.ConstantValue as string ?? string.Empty;
            fields.Add(new KnownEventFieldData(typeSymbol.Name, member.Name, value));
        }

        return fields;
    }

    /// <summary>
    /// 将枚举底层类型的 SpecialType 映射为 TypeScript 表示名。
    /// </summary>
    /// <param name="specialType">底层类型的 SpecialType。</param>
    /// <returns>类型名字符串。</returns>
    private static string MapUnderlyingTypeToName(SpecialType specialType)
    {
        return specialType switch
        {
            SpecialType.System_Byte => "number",
            SpecialType.System_SByte => "number",
            SpecialType.System_Int16 => "number",
            SpecialType.System_UInt16 => "number",
            SpecialType.System_Int32 => "number",
            SpecialType.System_UInt32 => "number",
            SpecialType.System_Int64 => "number",
            SpecialType.System_UInt64 => "number",
            _ => "number"
        };
    }

    /// <summary>
    /// 将枚举成员的常量值格式化为字符串。
    /// </summary>
    /// <param name="value">常量值（可能是 int/uint/byte 等）。</param>
    /// <param name="specialType">底层类型的 SpecialType。</param>
    /// <returns>数值的字符串表示。</returns>
    private static string FormatEnumValue(object? value, SpecialType specialType)
    {
        if (value is null)
        {
            return "0";
        }

        // 对于无符号类型，需要转换为对应的字符串表示
        return specialType switch
        {
            SpecialType.System_Byte => ((byte)value).ToString(),
            SpecialType.System_SByte => ((sbyte)value).ToString(),
            SpecialType.System_Int16 => ((short)value).ToString(),
            SpecialType.System_UInt16 => ((ushort)value).ToString(),
            SpecialType.System_Int32 => ((int)value).ToString(),
            SpecialType.System_UInt32 => ((uint)value).ToString(),
            SpecialType.System_Int64 => ((long)value).ToString(),
            SpecialType.System_UInt64 => ((ulong)value).ToString(),
            _ => value.ToString() ?? "0"
        };
    }

    /// <summary>
    /// 生成元数据注册代码。
    /// </summary>
    /// <param name="metadata">收集到的事件元数据。</param>
    /// <returns>生成的 C# 代码。</returns>
    private static string GenerateCode(EventMetadata metadata)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// 此代码由 Wails.Net.SourceGenerators.EventMetadataSourceGenerator 自动生成，请勿手动修改。");
        sb.AppendLine("// 扫描所有公共枚举类型和 KnownEvents 风格常量类，");
        sb.AppendLine("// 生成元数据注册，替代运行时反射 Assembly.GetExportedTypes / FieldInfo.GetValue。");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using Wails.Net.Events;");
        sb.AppendLine();
        sb.AppendLine("namespace Wails.Net.Generated");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// 由源代码生成器自动生成的事件元数据注册入口。");
        sb.AppendLine("    /// 通过 [ModuleInitializer] 在模块加载时自动注册到 GeneratedEventsMetadata，");
        sb.AppendLine("    /// 替代运行时反射，消除反射开销并支持 AOT 裁剪。");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public static class GeneratedEventsRegistration");
        sb.AppendLine("    {");
        sb.AppendLine("        [global::System.Runtime.CompilerServices.ModuleInitializer]");
        sb.AppendLine("        public static void Register()");
        sb.AppendLine("        {");

        // 生成枚举元数据注册
        foreach (var enumData in metadata.Enums)
        {
            AppendEnumRegistration(sb, enumData);
        }

        // 生成 KnownEvents 字段元数据注册
        foreach (var fieldData in metadata.KnownEvents)
        {
            AppendKnownEventFieldRegistration(sb, fieldData);
        }

        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// 为单个枚举类型生成 GeneratedEventsMetadata.Register(EventEnumInfo) 调用。
    /// </summary>
    /// <param name="sb">字符串构建器。</param>
    /// <param name="enumData">枚举数据。</param>
    private static void AppendEnumRegistration(StringBuilder sb, EventEnumData enumData)
    {
        var nsEscaped = EscapeString(enumData.Namespace);
        var nameEscaped = EscapeString(enumData.Name);
        var fullNameEscaped = EscapeString(enumData.FullName);
        var underlyingEscaped = EscapeString(enumData.UnderlyingTypeName);

        sb.AppendLine($"            GeneratedEventsMetadata.Register(");
        sb.AppendLine($"                new EventEnumInfo(");
        sb.AppendLine($"                    FullName: \"{fullNameEscaped}\",");
        sb.AppendLine($"                    Namespace: \"{nsEscaped}\",");
        sb.AppendLine($"                    Name: \"{nameEscaped}\",");
        sb.AppendLine($"                    UnderlyingTypeName: \"{underlyingEscaped}\",");
        sb.Append("                    Members: new List<EventEnumMemberInfo>");
        sb.AppendLine();
        sb.Append("                    {");

        for (var i = 0; i < enumData.Members.Count; i++)
        {
            var member = enumData.Members[i];
            var comma = i < enumData.Members.Count - 1 ? "," : "";
            sb.AppendLine();
            sb.Append($"                        new EventEnumMemberInfo(\"{EscapeString(member.Name)}\", \"{EscapeString(member.Value)}\"){comma}");
        }

        if (enumData.Members.Count > 0)
        {
            sb.AppendLine();
            sb.Append("                    }");
        }
        else
        {
            sb.Append("}");
        }

        sb.AppendLine("));");
    }

    /// <summary>
    /// 为单个 KnownEvents 字段生成 GeneratedEventsMetadata.Register(KnownEventFieldInfo) 调用。
    /// </summary>
    /// <param name="sb">字符串构建器。</param>
    /// <param name="fieldData">字段数据。</param>
    private static void AppendKnownEventFieldRegistration(StringBuilder sb, KnownEventFieldData fieldData)
    {
        var classNameEscaped = EscapeString(fieldData.ClassName);
        var fieldNameEscaped = EscapeString(fieldData.FieldName);
        var valueEscaped = EscapeString(fieldData.Value);

        sb.AppendLine($"            GeneratedEventsMetadata.Register(");
        sb.AppendLine($"                new KnownEventFieldInfo(");
        sb.AppendLine($"                    ClassName: \"{classNameEscaped}\",");
        sb.AppendLine($"                    FieldName: \"{fieldNameEscaped}\",");
        sb.AppendLine($"                    Value: \"{valueEscaped}\"));");
    }

    /// <summary>
    /// 转义字符串中的特殊字符。
    /// </summary>
    /// <param name="s">要转义的字符串。</param>
    /// <returns>转义后的字符串。</returns>
    private static string EscapeString(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    /// <summary>
    /// 收集到的事件元数据。
    /// </summary>
    /// <param name="Enums">枚举列表。</param>
    /// <param name="KnownEvents">KnownEvents 字段列表。</param>
    private sealed record EventMetadata(
        List<EventEnumData> Enums,
        List<KnownEventFieldData> KnownEvents);

    /// <summary>
    /// 枚举类型数据（源生成器内部使用）。
    /// </summary>
    /// <param name="FullName">全限定名。</param>
    /// <param name="Namespace">命名空间。</param>
    /// <param name="Name">类型名。</param>
    /// <param name="UnderlyingTypeName">底层类型名。</param>
    /// <param name="Members">成员列表。</param>
    private sealed record EventEnumData(
        string FullName,
        string Namespace,
        string Name,
        string UnderlyingTypeName,
        List<EventEnumMemberData> Members);

    /// <summary>
    /// 枚举成员数据。
    /// </summary>
    /// <param name="Name">成员名。</param>
    /// <param name="Value">成员值。</param>
    private sealed record EventEnumMemberData(string Name, string Value);

    /// <summary>
    /// KnownEvents 字段数据。
    /// </summary>
    /// <param name="ClassName">类名。</param>
    /// <param name="FieldName">字段名。</param>
    /// <param name="Value">字段值。</param>
    private sealed record KnownEventFieldData(
        string ClassName,
        string FieldName,
        string Value);
}
