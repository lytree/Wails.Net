using System.Reflection;
using Wails.Net.Generator.Models;

namespace Wails.Net.Generator;

/// <summary>
/// 绑定分析器，分析 C# 程序集并提取可暴露给前端的绑定方法。
/// 对应 Wails v3 Go 版本 internal/generator/analyse.go。
/// 采用基于反射的分析策略，与运行时 BindingManager 保持一致的方法筛选规则。
/// </summary>
public class BindingAnalyzer
{
    /// <summary>
    /// 需要排除的服务内部方法名称集合（与 BindingManager 一致）。
    /// </summary>
    private static readonly HashSet<string> ExcludedMethodNames = new(StringComparer.Ordinal)
    {
        "ServiceName",
        "ServiceStartup",
        "ServiceShutdown"
    };

    /// <summary>
    /// 分析指定程序集，提取所有可暴露给前端的绑定方法。
    /// </summary>
    /// <param name="assembly">要分析的程序集。</param>
    /// <returns>绑定方法模型列表。</returns>
    public List<BoundMethodModel> AnalyzeAssembly(Assembly assembly)
    {
        var models = new List<BoundMethodModel>();

        foreach (var type in assembly.GetExportedTypes())
        {
            // 跳过接口、委托等非类/结构体类型
            if (!type.IsClass && !type.IsValueType)
            {
                continue;
            }

            // 跳过抽象类和泛型类型定义（无法实例化）
            if (type.IsAbstract || type.IsGenericTypeDefinition)
            {
                continue;
            }

            // 分析所有公共非抽象类的实例方法
            var methods = AnalyzeType(type);
            models.AddRange(methods);
        }

        return models;
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
    /// 分析指定类型，提取其所有可暴露给前端的公共方法。
    /// </summary>
    /// <param name="type">要分析的类型。</param>
    /// <returns>绑定方法模型列表。</returns>
    public List<BoundMethodModel> AnalyzeType(Type type)
    {
        var models = new List<BoundMethodModel>();

        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        foreach (var method in methods)
        {
            if (!ShouldIncludeMethod(method))
            {
                continue;
            }

            var model = CreateModel(method);
            if (model is not null)
            {
                models.Add(model);
            }
        }

        return models;
    }

    /// <summary>
    /// 判断方法是否应包含在绑定中。
    /// 与运行时 BindingManager 的筛选规则保持一致。
    /// </summary>
    /// <param name="method">方法反射信息。</param>
    /// <returns>是否包含。</returns>
    private static bool ShouldIncludeMethod(MethodInfo method)
    {
        // 排除服务内部方法
        if (ExcludedMethodNames.Contains(method.Name))
        {
            return false;
        }

        // 排除特殊方法（属性 getter/setter、运算符等）
        if (method.IsSpecialName)
        {
            return false;
        }

        // 排除 Object 继承的方法
        if (method.DeclaringType == typeof(object))
        {
            return false;
        }

        // 排除泛型方法定义（无法在运行时无类型参数地反射调用）
        if (method.IsGenericMethodDefinition)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 根据方法反射信息创建绑定方法模型。
    /// </summary>
    /// <param name="method">方法反射信息。</param>
    /// <returns>绑定方法模型，若无法创建则返回 null。</returns>
    private static BoundMethodModel? CreateModel(MethodInfo method)
    {
        var declaringType = method.DeclaringType;
        if (declaringType is null)
        {
            return null;
        }

        var @namespace = declaringType.Namespace ?? string.Empty;
        var className = declaringType.Name;
        var methodName = method.Name;
        var fullName = BindingIdGenerator.GetFullName(@namespace, className, methodName);
        var id = BindingIdGenerator.Generate(fullName);

        var parameters = CreateParameterModels(method);
        var (returnTypeName, isAsync) = GetReturnTypeInfo(method.ReturnType);

        return new BoundMethodModel(
            fullName,
            id,
            @namespace,
            className,
            methodName,
            parameters,
            returnTypeName,
            isAsync);
    }

    /// <summary>
    /// 根据方法参数创建参数模型列表。
    /// </summary>
    /// <param name="method">方法反射信息。</param>
    /// <returns>参数模型列表。</returns>
    private static List<ParameterModel> CreateParameterModels(MethodInfo method)
    {
        var models = new List<ParameterModel>();
        var parameters = method.GetParameters();

        foreach (var param in parameters)
        {
            // CancellationToken 不暴露给前端
            var isCancellationToken = param.ParameterType == typeof(CancellationToken);

            // 检查是否为可变参数（params 关键字）
            var isVariadic = param.GetCustomAttribute<ParamArrayAttribute>() is not null;

            var typeName = TypeScriptTypeMapper.MapType(param.ParameterType);

            models.Add(new ParameterModel(
                param.Name ?? "arg",
                typeName,
                isVariadic,
                isCancellationToken));
        }

        return models;
    }

    /// <summary>
    /// 获取方法返回类型的 TypeScript 类型字符串和异步标记。
    /// </summary>
    /// <param name="returnType">C# 返回类型。</param>
    /// <returns>元组：(TypeScript 类型, 是否异步)。</returns>
    private static (string typeName, bool isAsync) GetReturnTypeInfo(Type returnType)
    {
        if (returnType == typeof(void))
        {
            return ("void", false);
        }

        if (TypeScriptTypeMapper.IsTaskType(returnType))
        {
            var tsType = TypeScriptTypeMapper.MapType(returnType);
            return (tsType, true);
        }

        return (TypeScriptTypeMapper.MapType(returnType), false);
    }
}
