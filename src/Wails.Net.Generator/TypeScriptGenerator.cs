using System.Text;
using Wails.Net.Generator.Models;

namespace Wails.Net.Generator;

/// <summary>
/// TypeScript 绑定代码生成器，根据绑定方法模型生成 TypeScript 定义文件和调用封装。
/// 对应 Wails v3 Go 版本 internal/generator/generator.go。
/// 生成产物：
/// - bindings.d.ts：类型定义文件（接口、方法签名）
/// - bindings.js：调用封装（通过 window.wails.Bindings.Call 调用后端）
/// </summary>
public class TypeScriptGenerator
{
    /// <summary>
    /// 生成 TypeScript 类型定义文件内容（.d.ts）。
    /// </summary>
    /// <param name="methods">绑定方法模型列表。</param>
    /// <returns>TypeScript 定义文件内容。</returns>
    public string GenerateDefinitions(List<BoundMethodModel> methods)
    {
        var sb = new StringBuilder();

        // 文件头注释
        sb.AppendLine("// 自动生成的 Wails.Net 绑定类型定义文件");
        sb.AppendLine("// 请勿手动修改，此文件由 wails.net generate 命令生成");
        sb.AppendLine($"// 生成时间: {DateTime.UtcNow:O}");
        sb.AppendLine();

        if (methods.Count == 0)
        {
            sb.AppendLine("// 未发现绑定方法");
            return sb.ToString();
        }

        // 按命名空间和类名分组
        var groups = methods
            .GroupBy(m => (m.Namespace, m.ClassName))
            .OrderBy(g => g.Key.Namespace)
            .ThenBy(g => g.Key.ClassName);

        foreach (var group in groups)
        {
            var ns = group.Key.Namespace;
            var className = group.Key.ClassName;

            // 生成命名空间声明
            if (!string.IsNullOrEmpty(ns))
            {
                sb.AppendLine($"declare namespace {ns} {{");
                GenerateClassDefinition(sb, className, group.ToList(), indent: "  ");
                sb.AppendLine("}");
            }
            else
            {
                GenerateClassDefinition(sb, className, group.ToList(), indent: string.Empty);
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// 生成 TypeScript 调用封装文件内容（.ts）。
    /// 调用通过 wails.bindings.call(bindingId, args) 发送到后端。
    /// </summary>
    /// <param name="methods">绑定方法模型列表。</param>
    /// <returns>TypeScript 调用封装文件内容。</returns>
    public string GenerateCaller(List<BoundMethodModel> methods)
    {
        var sb = new StringBuilder();

        // 文件头注释
        sb.AppendLine("// 自动生成的 Wails.Net 绑定调用封装文件");
        sb.AppendLine("// 请勿手动修改，此文件由 wails.net generate 命令生成");
        sb.AppendLine($"// 生成时间: {DateTime.UtcNow:O}");
        sb.AppendLine();

        // 导入 wails 运行时
        sb.AppendLine("import { wails } from '@wails/runtime';");
        sb.AppendLine();

        if (methods.Count == 0)
        {
            sb.AppendLine("// 未发现绑定方法");
            return sb.ToString();
        }

        // 按命名空间和类名分组
        var groups = methods
            .GroupBy(m => (m.Namespace, m.ClassName))
            .OrderBy(g => g.Key.Namespace)
            .ThenBy(g => g.Key.ClassName);

        foreach (var group in groups)
        {
            var ns = group.Key.Namespace;
            var className = group.Key.ClassName;

            // 生成导出类
            sb.AppendLine($"export class {className} {{");

            foreach (var method in group)
            {
                GenerateMethodCaller(sb, method, indent: "  ");
                sb.AppendLine();
            }

            sb.AppendLine("}");
            sb.AppendLine();

            // 若有命名空间，导出到命名空间对象
            if (!string.IsNullOrEmpty(ns))
            {
                sb.AppendLine($"export const {ns.Replace('.', '_')}_{className} = {className};");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 生成绑定 ID 映射文件内容。
    /// 提供绑定 ID 到全限定名的映射，便于调试。
    /// </summary>
    /// <param name="methods">绑定方法模型列表。</param>
    /// <returns>映射文件内容。</returns>
    public string GenerateIdMap(List<BoundMethodModel> methods)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// 自动生成的 Wails.Net 绑定 ID 映射文件");
        sb.AppendLine("// 请勿手动修改，此文件由 wails.net generate 命令生成");
        sb.AppendLine($"// 生成时间: {DateTime.UtcNow:O}");
        sb.AppendLine();
        sb.AppendLine("export const bindingIds = {");

        foreach (var method in methods.OrderBy(m => m.FullName))
        {
            sb.AppendLine($"  \"{method.FullName}\": {method.ID},");
        }

        sb.AppendLine("};");
        sb.AppendLine();
        sb.AppendLine("export default bindingIds;");

        return sb.ToString();
    }

    /// <summary>
    /// 为单个类生成类型定义。
    /// </summary>
    /// <param name="sb">字符串构建器。</param>
    /// <param name="className">类名。</param>
    /// <param name="methods">该类的方法列表。</param>
    /// <param name="indent">缩进字符串。</param>
    private static void GenerateClassDefinition(StringBuilder sb, string className, List<BoundMethodModel> methods, string indent)
    {
        sb.AppendLine($"{indent}interface {className} {{");

        foreach (var method in methods)
        {
            GenerateMethodSignature(sb, method, indent + "  ");
        }

        sb.AppendLine($"{indent}}}");
    }

    /// <summary>
    /// 生成单个方法的 TypeScript 签名。
    /// </summary>
    /// <param name="sb">字符串构建器。</param>
    /// <param name="method">方法模型。</param>
    /// <param name="indent">缩进字符串。</param>
    private static void GenerateMethodSignature(StringBuilder sb, BoundMethodModel method, string indent)
    {
        // 过滤掉 CancellationToken 参数（不暴露给前端）
        var visibleParams = method.Parameters.Where(p => !p.IsCancellationToken).ToList();

        var paramList = visibleParams.Select(p =>
        {
            var prefix = p.IsVariadic ? "..." : string.Empty;
            return $"{prefix}{p.Name}: {p.TypeName}";
        });

        var asyncPrefix = method.IsAsync ? "Promise<" : string.Empty;
        var asyncSuffix = method.IsAsync ? ">" : string.Empty;

        sb.AppendLine($"{indent}{method.MethodName}({string.Join(", ", paramList)}): {asyncPrefix}{method.ReturnTypeName}{asyncSuffix};");
    }

    /// <summary>
    /// 生成单个方法的调用封装。
    /// </summary>
    /// <param name="sb">字符串构建器。</param>
    /// <param name="method">方法模型。</param>
    /// <param name="indent">缩进字符串。</param>
    private static void GenerateMethodCaller(StringBuilder sb, BoundMethodModel method, string indent)
    {
        // 过滤掉 CancellationToken 参数
        var visibleParams = method.Parameters.Where(p => !p.IsCancellationToken).ToList();

        var paramList = visibleParams.Select(p =>
        {
            var prefix = p.IsVariadic ? "..." : string.Empty;
            return $"{prefix}{p.Name}: {p.TypeName}";
        });

        var argsList = visibleParams.Select(p => p.Name);

        var asyncPrefix = method.IsAsync ? "async " : string.Empty;
        var awaitKeyword = method.IsAsync ? "await " : string.Empty;
        var returnType = method.IsAsync ? $"Promise<{method.ReturnTypeName}>" : method.ReturnTypeName;

        sb.AppendLine($"{indent}static {asyncPrefix}{method.MethodName}({string.Join(", ", paramList)}): {returnType} {{");
        sb.AppendLine($"{indent}  return {awaitKeyword}wails.bindings.call({method.ID}, [{string.Join(", ", argsList)}]);");
        sb.AppendLine($"{indent}}}");
    }
}
