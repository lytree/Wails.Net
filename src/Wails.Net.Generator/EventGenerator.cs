using System.Reflection;
using System.Text;

namespace Wails.Net.Generator;

/// <summary>
/// 事件代码生成器，从 C# 事件枚举生成 TypeScript 事件常量。
/// 对应 Wails v3 Go 版本 internal/generator/events.go。
/// </summary>
public class EventGenerator
{
    /// <summary>
    /// 从指定程序集中的事件枚举生成 TypeScript 事件常量文件。
    /// </summary>
    /// <param name="assembly">包含事件枚举的程序集。</param>
    /// <returns>TypeScript 事件常量文件内容。</returns>
    public string GenerateFromAssembly(Assembly assembly)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// 自动生成的 Wails.Net 事件类型定义文件");
        sb.AppendLine("// 请勿手动修改，此文件由 wails.net generate 命令生成");
        sb.AppendLine($"// 生成时间: {DateTime.UtcNow:O}");
        sb.AppendLine();

        // 查找所有枚举类型
        var enumTypes = assembly.GetExportedTypes()
            .Where(t => t.IsEnum)
            .OrderBy(t => t.FullName)
            .ToList();

        foreach (var enumType in enumTypes)
        {
            GenerateEnum(sb, enumType);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// 从指定枚举类型生成 TypeScript 枚举定义。
    /// </summary>
    /// <param name="enumType">枚举类型。</param>
    /// <returns>TypeScript 枚举定义文件内容。</returns>
    public string GenerateFromEnum(Type enumType)
    {
        var sb = new StringBuilder();
        GenerateEnum(sb, enumType);
        return sb.ToString();
    }

    /// <summary>
    /// 从 KnownEvents 常量类生成事件名称常量。
    /// </summary>
    /// <param name="knownEventsType">KnownEvents 类型。</param>
    /// <returns>TypeScript 事件名称常量文件内容。</returns>
    public string GenerateKnownEvents(Type knownEventsType)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// 自动生成的 Wails.Net 已知事件名称常量");
        sb.AppendLine("// 请勿手动修改，此文件由 wails.net generate 命令生成");
        sb.AppendLine($"// 生成时间: {DateTime.UtcNow:O}");
        sb.AppendLine();

        sb.AppendLine($"export const {knownEventsType.Name} = {{");

        var fields = knownEventsType.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && !f.IsInitOnly)
            .OrderBy(f => f.Name);

        foreach (var field in fields)
        {
            var value = field.GetValue(null)?.ToString() ?? string.Empty;
            sb.AppendLine($"  {field.Name}: \"{value}\",");
        }

        sb.AppendLine("};");
        sb.AppendLine();
        sb.AppendLine($"export default {knownEventsType.Name};");

        return sb.ToString();
    }

    /// <summary>
    /// 为单个枚举类型生成 TypeScript 枚举定义。
    /// </summary>
    /// <param name="sb">字符串构建器。</param>
    /// <param name="enumType">枚举类型。</param>
    private static void GenerateEnum(StringBuilder sb, Type enumType)
    {
        var underlyingType = Enum.GetUnderlyingType(enumType);

        sb.AppendLine($"export enum {enumType.Name} {{");

        var names = Enum.GetNames(enumType);
        var values = Enum.GetValues(enumType);

        for (var i = 0; i < names.Length; i++)
        {
            var value = Convert.ChangeType(values.GetValue(i), underlyingType);
            var comma = i < names.Length - 1 ? "," : string.Empty;
            sb.AppendLine($"  {names[i]} = {value}{comma}");
        }

        sb.AppendLine("}");
    }
}
