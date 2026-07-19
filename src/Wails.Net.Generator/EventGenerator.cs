using System.Text;
using Wails.Net.Events;

namespace Wails.Net.Generator;

/// <summary>
/// 事件代码生成器，从 <see cref="GeneratedEventsMetadata"/> 生成 TypeScript 事件常量。
/// 对应 Wails v3 Go 版本 internal/generator/events.go。
/// </summary>
/// <remarks>
/// 此实现完全基于源代码生成器在编译期填充的元数据，
/// 不再使用 <see cref="System.Reflection"/> 进行运行时分析。
/// 当目标程序集加载到进程时，其 <c>[ModuleInitializer]</c> 会自动注册元数据。
/// </remarks>
public class EventGenerator
{
    /// <summary>
    /// 从 <see cref="GeneratedEventsMetadata.Enums"/> 生成 TypeScript 事件常量文件。
    /// </summary>
    /// <returns>TypeScript 事件常量文件内容。</returns>
    public string GenerateFromMetadata()
    {
        var sb = new StringBuilder();

        sb.AppendLine("// 自动生成的 Wails.Net 事件类型定义文件");
        sb.AppendLine("// 请勿手动修改，此文件由 wails.net generate 命令生成");
        sb.AppendLine($"// 生成时间: {DateTime.UtcNow:O}");
        sb.AppendLine();

        // 按全限定名排序，保证输出顺序稳定
        var enums = GeneratedEventsMetadata.Enums
            .OrderBy(e => e.FullName)
            .ToList();

        foreach (var enumInfo in enums)
        {
            GenerateEnum(sb, enumInfo);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// 从指定 <see cref="EventEnumInfo"/> 生成 TypeScript 枚举定义。
    /// </summary>
    /// <param name="enumInfo">枚举元数据。</param>
    /// <returns>TypeScript 枚举定义文件内容。</returns>
    public string GenerateFromEnum(EventEnumInfo enumInfo)
    {
        ArgumentNullException.ThrowIfNull(enumInfo);

        var sb = new StringBuilder();
        GenerateEnum(sb, enumInfo);
        return sb.ToString();
    }

    /// <summary>
    /// 从 <see cref="GeneratedEventsMetadata.KnownEvents"/> 生成 KnownEvents 常量类对应的 TypeScript 文件。
    /// </summary>
    /// <returns>TypeScript 事件名称常量文件内容。</returns>
    public string GenerateKnownEventsFromMetadata()
    {
        var sb = new StringBuilder();

        sb.AppendLine("// 自动生成的 Wails.Net 已知事件名称常量");
        sb.AppendLine("// 请勿手动修改，此文件由 wails.net generate 命令生成");
        sb.AppendLine($"// 生成时间: {DateTime.UtcNow:O}");
        sb.AppendLine();

        // 按类名分组，每个 KnownEvents 类生成一个 const 对象
        var groups = GeneratedEventsMetadata.KnownEvents
            .GroupBy(f => f.ClassName)
            .OrderBy(g => g.Key);

        foreach (var group in groups)
        {
            GenerateKnownEventsClass(sb, group.Key, group.OrderBy(f => f.FieldName).ToList());
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// 从指定 <see cref="KnownEventFieldInfo"/> 列表生成 TypeScript 常量对象。
    /// </summary>
    /// <param name="className">常量类名。</param>
    /// <param name="fields">字段列表。</param>
    /// <returns>TypeScript 事件名称常量文件内容。</returns>
    public string GenerateKnownEvents(string className, IReadOnlyList<KnownEventFieldInfo> fields)
    {
        ArgumentException.ThrowIfNullOrEmpty(className);
        ArgumentNullException.ThrowIfNull(fields);

        var sb = new StringBuilder();
        GenerateKnownEventsClass(sb, className, fields);
        return sb.ToString();
    }

    /// <summary>
    /// 为单个枚举类型生成 TypeScript 枚举定义。
    /// </summary>
    /// <param name="sb">字符串构建器。</param>
    /// <param name="enumInfo">枚举元数据。</param>
    private static void GenerateEnum(StringBuilder sb, EventEnumInfo enumInfo)
    {
        sb.AppendLine($"export enum {enumInfo.Name} {{");

        for (var i = 0; i < enumInfo.Members.Count; i++)
        {
            var member = enumInfo.Members[i];
            var comma = i < enumInfo.Members.Count - 1 ? "," : string.Empty;
            sb.AppendLine($"  {member.Name} = {member.Value}{comma}");
        }

        sb.AppendLine("}");
    }

    /// <summary>
    /// 为单个 KnownEvents 常量类生成 TypeScript const 对象。
    /// </summary>
    /// <param name="sb">字符串构建器。</param>
    /// <param name="className">类名。</param>
    /// <param name="fields">字段列表。</param>
    private static void GenerateKnownEventsClass(StringBuilder sb, string className, IReadOnlyList<KnownEventFieldInfo> fields)
    {
        sb.AppendLine($"export const {className} = {{");

        foreach (var field in fields)
        {
            sb.AppendLine($"  {field.FieldName}: \"{field.Value}\",");
        }

        sb.AppendLine("};");
        sb.AppendLine();
        sb.AppendLine($"export default {className};");
    }
}
