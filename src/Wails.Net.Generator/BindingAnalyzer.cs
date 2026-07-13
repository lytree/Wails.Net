using System.Reflection;
using Wails.Net.Application.Bindings;
using Wails.Net.Generator.Models;

namespace Wails.Net.Generator;

/// <summary>
/// 绑定分析器，从 <see cref="GeneratedBindingsMetadata"/> 读取编译期生成的绑定方法元数据。
/// 对应 Wails v3 Go 版本 internal/generator/analyse.go。
/// </summary>
/// <remarks>
/// 此实现完全基于源代码生成器在编译期填充的元数据，
/// 不再使用 <see cref="System.Reflection"/> 进行运行时分析。
/// 当目标程序集加载到进程时，其 <c>[ModuleInitializer]</c> 会自动注册元数据。
/// </remarks>
public class BindingAnalyzer
{
    /// <summary>
    /// 分析指定程序集中所有可暴露给前端的绑定方法。
    /// </summary>
    /// <param name="assembly">要分析的程序集。程序集加载时其 <c>[ModuleInitializer]</c> 已注册元数据。</param>
    /// <returns>绑定方法模型列表。</returns>
    /// <remarks>
    /// 此方法返回 <see cref="GeneratedBindingsMetadata.Methods"/> 中已注册的所有方法。
    /// 由于元数据由源生成器在编译期填充，运行时无法区分方法来自哪个程序集——
    /// 实践中调用方一次只分析一个目标程序集，所有可访问的绑定方法均来自该程序集及其引用。
    /// </remarks>
    public List<BoundMethodModel> AnalyzeAssembly(Assembly assembly)
    {
        return GeneratedBindingsMetadata.Methods.Select(ToModel).ToList();
    }

    /// <summary>
    /// 分析指定类型实例，提取其所有可暴露给前端的绑定方法。
    /// 此重载与运行时 BindingManager.Add(object instance) 行为一致。
    /// </summary>
    /// <param name="instance">要分析的实例。</param>
    /// <returns>绑定方法模型列表。</returns>
    public List<BoundMethodModel> AnalyzeInstance(object instance)
    {
        return AnalyzeType(instance.GetType());
    }

    /// <summary>
    /// 分析指定类型，从 <see cref="GeneratedBindingsMetadata"/> 中筛选其绑定方法。
    /// </summary>
    /// <param name="type">要分析的类型。</param>
    /// <returns>绑定方法模型列表。</returns>
    public List<BoundMethodModel> AnalyzeType(Type type)
    {
        // 构造期望的类型全名（Namespace.ClassName）
        var fullTypeName = string.IsNullOrEmpty(type.Namespace)
            ? type.Name
            : $"{type.Namespace}.{type.Name}";

        return GeneratedBindingsMetadata.Methods
            .Where(m => $"{m.Namespace}.{m.ClassName}" == fullTypeName)
            .Select(ToModel)
            .ToList();
    }

    /// <summary>
    /// 将 <see cref="BoundMethodInfo"/> 转换为 <see cref="BoundMethodModel"/>。
    /// </summary>
    private static BoundMethodModel ToModel(BoundMethodInfo info)
    {
        return new BoundMethodModel(
            fullName: info.FullName,
            id: info.Id,
            @namespace: info.Namespace,
            className: info.ClassName,
            methodName: info.MethodName,
            parameters: info.Parameters
                .Select(p => new ParameterModel(p.Name, p.TypeName, p.IsVariadic, p.IsCancellationToken))
                .ToList(),
            returnTypeName: info.ReturnTypeName,
            isAsync: info.IsAsync);
    }
}
