using System.Text.Json;

namespace Wails.Net.Application.Bindings;

/// <summary>
/// 强类型方法调用器委托。
/// 由源代码生成器生成，替代运行时 <see cref="System.Reflection.MethodInfo.Invoke(object?, object?[])"/>。
/// </summary>
/// <param name="instance">方法所属实例。</param>
/// <param name="args">JSON 参数数组。</param>
/// <param name="cancellationToken">取消令牌。</param>
/// <returns>方法的返回值。若方法返回 Task，则为 Task 实例（由调用方 await）。</returns>
public delegate object? GeneratedInvoker(object instance, JsonElement[] args, CancellationToken cancellationToken);

/// <summary>
/// 编译时生成的绑定注册表。
/// 源代码生成器生成的代码通过 <see cref="Register(string, GeneratedInvoker, string)"/> 注册调用器，
/// <see cref="BindingManager"/> 在注册服务时优先查询此注册表。
/// </summary>
public static class GeneratedBindingRegistry
{
    /// <summary>
    /// 按 "方法全名" 索引的调用器字典。
    /// 键格式可能是 "Namespace.ClassName.MethodName"（[Binding]）或自定义命令名（[Command]）。
    /// </summary>
    private static readonly Dictionary<string, GeneratedInvoker> _invokers = new(StringComparer.Ordinal);

    /// <summary>
    /// 按 "方法全名" 索引的所属类型全名（Namespace.ClassName）字典。
    /// 用于在调用时查找正确的实例。
    /// </summary>
    private static readonly Dictionary<string, string> _methodToTypeName = new(StringComparer.Ordinal);

    /// <summary>
    /// 按 "类型全名" 索引的方法名列表。
    /// 用于 <see cref="BindingManager"/> 快速判断某类型是否有生成的调用器。
    /// </summary>
    private static readonly Dictionary<string, List<string>> _typeMethods = new(StringComparer.Ordinal);

    /// <summary>
    /// 注册一个由源生成器生成的调用器。
    /// 通常由生成的代码在 <c>[ModuleInitializer]</c> 中调用。
    /// </summary>
    /// <param name="fullName">方法全名（[Binding] 为 Namespace.ClassName.MethodName，[Command] 为自定义命令名）。</param>
    /// <param name="invoker">强类型调用器委托。</param>
    /// <param name="typeFullName">所属类型的全名（Namespace.ClassName），用于查找实例。</param>
    public static void Register(string fullName, GeneratedInvoker invoker, string typeFullName)
    {
        _invokers[fullName] = invoker;
        _methodToTypeName[fullName] = typeFullName;

        // 同时维护类型到方法名的映射（仅当 fullName 是 "Type.Method" 形式时才有意义）
        var lastDot = fullName.LastIndexOf('.');
        if (lastDot > 0)
        {
            var possibleTypeName = fullName[..lastDot];
            var methodName = fullName[(lastDot + 1)..];

            // 只有当 fullName 的前缀与 typeFullName 匹配时，才登记到 _typeMethods
            // 这样 [Command("counter.increment")] 这种短名不会污染 _typeMethods
            if (possibleTypeName == typeFullName)
            {
                if (!_typeMethods.TryGetValue(typeFullName, out var methods))
                {
                    methods = new List<string>();
                    _typeMethods[typeFullName] = methods;
                }
                if (!methods.Contains(methodName))
                {
                    methods.Add(methodName);
                }
            }
        }
    }

    /// <summary>
    /// 尝试获取指定方法名的调用器。
    /// </summary>
    /// <param name="fullName">方法全名或命令名。</param>
    /// <param name="invoker">输出参数，若找到则为调用器委托，否则为 null。</param>
    /// <returns>是否找到调用器。</returns>
    public static bool TryGetInvoker(string fullName, out GeneratedInvoker? invoker)
    {
        return _invokers.TryGetValue(fullName, out invoker);
    }

    /// <summary>
    /// 尝试获取指定方法所属类型的全名。
    /// </summary>
    /// <param name="fullName">方法全名或命令名。</param>
    /// <param name="typeFullName">输出参数，若找到则为类型全名，否则为 null。</param>
    /// <returns>是否找到类型信息。</returns>
    public static bool TryGetTypeFullName(string fullName, out string? typeFullName)
    {
        return _methodToTypeName.TryGetValue(fullName, out typeFullName);
    }

    /// <summary>
    /// 尝试获取指定类型的所有已生成方法名。
    /// </summary>
    /// <param name="typeFullName">类型全名。</param>
    /// <param name="methodNames">输出参数，若找到则为方法名列表，否则为 null。</param>
    /// <returns>是否找到方法名列表。</returns>
    public static bool TryGetMethodNames(string typeFullName, out List<string>? methodNames)
    {
        return _typeMethods.TryGetValue(typeFullName, out methodNames);
    }

    /// <summary>
    /// 获取已注册的所有调用器数量。
    /// </summary>
    public static int Count => _invokers.Count;

    /// <summary>
    /// 清除所有已注册的调用器（仅用于测试）。
    /// </summary>
    internal static void Clear()
    {
        _invokers.Clear();
        _methodToTypeName.Clear();
        _typeMethods.Clear();
    }
}
