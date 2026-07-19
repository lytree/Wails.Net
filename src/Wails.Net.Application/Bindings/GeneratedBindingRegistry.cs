using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace Wails.Net.Application.Bindings;

/// <summary>
/// 强类型方法调用器委托。
/// 由源代码生成器生成，替代运行时 <see cref="System.Reflection.MethodInfo.Invoke(object?, object?[])"/>。
/// </summary>
/// <param name="instance">方法所属实例。</param>
/// <param name="args">JSON 参数数组。</param>
/// <param name="cancellationToken">取消令牌。</param>
/// <returns>
/// 异步返回的方法结果：
/// <list type="bullet">
/// <item>同步方法：用 <see cref="Task.FromResult{TResult}(TResult)"/> 包装；</item>
/// <item>异步方法：在生成的调用器内部 <c>await</c>，将 <c>Task&lt;T&gt;</c> 的 Result 装箱为 <see cref="object"/>；</item>
/// <item>无返回值方法：返回 <c>null</c>。</item>
/// </list>
/// 调用方仅需 <c>await</c> 此委托返回值，无需运行时反射提取 <c>Task.Result</c>，遵循 AGENTS.md §3.4 禁令。
/// </returns>
public delegate Task<object?> GeneratedInvoker(object instance, JsonElement[] args, CancellationToken cancellationToken);

/// <summary>
/// 编译时生成的绑定注册表。
/// 源代码生成器生成的代码通过 <see cref="Register(string, GeneratedInvoker, string, IReadOnlyList{string}?, IReadOnlyList{ScopeExtractor}?)"/>
/// 注册调用器，<see cref="BindingManager"/> 在注册服务时优先查询此注册表。
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
    /// 按 "方法全名" 索引的所需能力列表字典。
    /// 由源生成器在编译期从 [RequireCapability] 特性提取，运行时零反射权限校验。
    /// </summary>
    private static readonly Dictionary<string, IReadOnlyList<string>> _methodCapabilities = new(StringComparer.Ordinal);

    /// <summary>
    /// 按 "方法全名" 索引的 Scope 提取器列表字典。
    /// 由源生成器在编译期为标记 [ScopeParameter] 的参数生成，运行时零反射 Scope 校验。
    /// </summary>
    private static readonly Dictionary<string, IReadOnlyList<ScopeExtractor>> _methodScopeExtractors = new(StringComparer.Ordinal);

    /// <summary>
    /// 注册一个由源生成器生成的调用器。
    /// 通常由生成的代码在 <c>[ModuleInitializer]</c> 中调用。
    /// </summary>
    /// <param name="fullName">方法全名（[Binding] 为 Namespace.ClassName.MethodName，[Command] 为自定义命令名）。</param>
    /// <param name="invoker">强类型调用器委托。</param>
    /// <param name="typeFullName">所属类型的全名（Namespace.ClassName），用于查找实例。</param>
    public static void Register(string fullName, GeneratedInvoker invoker, string typeFullName)
        => Register(fullName, invoker, typeFullName, requiredCapabilities: null, scopeExtractors: null);

    /// <summary>
    /// 注册一个由源生成器生成的调用器，附带能力元数据和 Scope 提取器。
    /// 通常由生成的代码在 <c>[ModuleInitializer]</c> 中调用。
    /// </summary>
    /// <param name="fullName">方法全名（[Binding] 为 Namespace.ClassName.MethodName，[Command] 为自定义命令名）。</param>
    /// <param name="invoker">强类型调用器委托。</param>
    /// <param name="typeFullName">所属类型的全名（Namespace.ClassName），用于查找实例。</param>
    /// <param name="requiredCapabilities">
    /// 方法所需的能力标识列表（来自 <see cref="Security.RequireCapabilityAttribute"/>），可为 null。
    /// 非空时由 <see cref="Commands.CommandDispatcher"/> 在调度时校验是否已授权。
    /// </param>
    /// <param name="scopeExtractors">
    /// 方法的 Scope 提取器列表（来自 <see cref="Security.ScopeParameterAttribute"/>），可为 null。
    /// 非空时由 <see cref="Commands.CommandDispatcher"/> 在调度时提取 Scope 值并校验。
    /// </param>
    public static void Register(
        string fullName,
        GeneratedInvoker invoker,
        string typeFullName,
        IReadOnlyList<string>? requiredCapabilities,
        IReadOnlyList<ScopeExtractor>? scopeExtractors)
    {
        _invokers[fullName] = invoker;
        _methodToTypeName[fullName] = typeFullName;

        if (requiredCapabilities is { Count: > 0 })
        {
            _methodCapabilities[fullName] = requiredCapabilities;
        }

        if (scopeExtractors is { Count: > 0 })
        {
            _methodScopeExtractors[fullName] = scopeExtractors;
        }

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
    /// 尝试获取指定方法的能力列表。
    /// 由源生成器在编译期从 [RequireCapability] 特性提取，运行时零反射权限校验。
    /// </summary>
    /// <param name="fullName">方法全名或命令名。</param>
    /// <param name="requiredCapabilities">输出参数，若找到则为能力列表，否则为 null。</param>
    /// <returns>是否找到能力列表。</returns>
    public static bool TryGetCapabilities(string fullName, out IReadOnlyList<string>? requiredCapabilities)
    {
        return _methodCapabilities.TryGetValue(fullName, out requiredCapabilities);
    }

    /// <summary>
    /// 尝试获取指定方法的 Scope 提取器列表。
    /// 由源生成器在编译期为标记 [ScopeParameter] 的参数生成，运行时零反射 Scope 校验。
    /// </summary>
    /// <param name="fullName">方法全名或命令名。</param>
    /// <param name="scopeExtractors">输出参数，若找到则为 Scope 提取器列表，否则为 null。</param>
    /// <returns>是否找到 Scope 提取器列表。</returns>
    public static bool TryGetScopeExtractors(string fullName, out IReadOnlyList<ScopeExtractor>? scopeExtractors)
    {
        return _methodScopeExtractors.TryGetValue(fullName, out scopeExtractors);
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
        _methodCapabilities.Clear();
        _methodScopeExtractors.Clear();
    }
}

/// <summary>
/// Scope 提取器委托，从 JSON 参数中提取 (权限标识, 值) 对。
/// 由源生成器在编译期为标记 <see cref="Security.ScopeParameterAttribute"/> 的参数生成，
/// 替代运行时反射 <see cref="Commands.CommandDispatcher.ExtractScopeValues"/>。
/// </summary>
/// <param name="parameters">前端传入的 JSON 参数。</param>
/// <returns>提取到的 (权限标识, 值) 对，无匹配时返回 null。</returns>
public delegate (string PermissionId, string Value)? ScopeExtractor(System.Text.Json.JsonElement parameters);
