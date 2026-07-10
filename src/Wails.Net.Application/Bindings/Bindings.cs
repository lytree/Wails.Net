using System.Reflection;
using System.Text.Json;
using Wails.Net.Errors;

namespace Wails.Net.Application.Bindings;

/// <summary>
/// 绑定管理器，负责注册和调用绑定方法。
/// 对应 Wails v3 Go 版本 bindings.go 中的 Bindings 结构。
/// </summary>
public class BindingManager
{
    /// <summary>
    /// 按全限定名索引的绑定方法字典。
    /// </summary>
    private readonly Dictionary<string, BoundMethod> _boundMethods = new();

    /// <summary>
    /// 按 FNV-1a 哈希 ID 索引的绑定方法字典。
    /// </summary>
    private readonly Dictionary<uint, BoundMethod> _boundByID = new();

    /// <summary>
    /// 需要排除的服务内部方法名称集合。
    /// 这些方法由服务生命周期管理，不应暴露给前端。
    /// </summary>
    private static readonly HashSet<string> ExcludedMethodNames = new(StringComparer.Ordinal)
    {
        "ServiceName",
        "ServiceStartup",
        "ServiceShutdown"
    };

    /// <summary>
    /// 获取已注册的所有绑定方法（按全限定名索引）。
    /// </summary>
    public IReadOnlyDictionary<string, BoundMethod> BoundMethods => _boundMethods;

    /// <summary>
    /// 获取已注册的所有绑定方法（按 ID 索引）。
    /// </summary>
    public IReadOnlyDictionary<uint, BoundMethod> BoundByID => _boundByID;

    /// <summary>
    /// 注册指定实例的所有公共方法。
    /// 排除服务内部方法（ServiceName、ServiceStartup、ServiceShutdown）。
    /// </summary>
    /// <param name="instance">要注册的实例。</param>
    public void Add(object instance)
    {
        var type = instance.GetType();
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        foreach (var method in methods)
        {
            // 跳过排除的方法
            if (ExcludedMethodNames.Contains(method.Name))
            {
                continue;
            }

            // 跳过特殊方法（属性 getter/setter、运算符等）
            if (method.IsSpecialName)
            {
                continue;
            }

            // 跳过 Object 继承的方法
            if (method.DeclaringType == typeof(object))
            {
                continue;
            }

            AddMethod(instance, method);
        }
    }

    /// <summary>
    /// 注册单个方法。
    /// </summary>
    /// <param name="instance">方法所属实例。</param>
    /// <param name="methodInfo">方法反射信息。</param>
    /// <returns>已注册的 BoundMethod 实例。</returns>
    private BoundMethod AddMethod(object instance, MethodInfo methodInfo)
    {
        var fullName = GetFullMethodName(methodInfo);
        var id = FNV1aHash(fullName);

        var parameters = methodInfo.GetParameters();
        var parameterTypes = new Type[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            parameterTypes[i] = parameters[i].ParameterType;
        }

        var isVariadic = parameters.Length > 0 && parameters[^1].GetCustomAttribute<ParamArrayAttribute>() is not null;
        var hasCancellationToken = parameters.Length > 0 && parameters[0].ParameterType == typeof(CancellationToken);

        var boundMethod = new BoundMethod(fullName, id, instance, methodInfo, parameterTypes, isVariadic, hasCancellationToken);

        _boundMethods[fullName] = boundMethod;
        _boundByID[id] = boundMethod;

        return boundMethod;
    }

    /// <summary>
    /// 按 ID 调用绑定方法。
    /// </summary>
    /// <param name="id">绑定方法的 FNV-1a 哈希 ID。</param>
    /// <param name="args">JSON 参数数组。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>包含调用结果或错误的字典。</returns>
    public async Task<Dictionary<string, object?>> Call(uint id, JsonElement[] args, CancellationToken cancellationToken = default)
    {
        if (!_boundByID.TryGetValue(id, out var method))
        {
            var error = new CallError($"未找到 ID 为 {id} 的绑定方法", null, CallErrorKind.ReferenceError);
            return new Dictionary<string, object?>
            {
                ["result"] = null,
                ["error"] = error.ToJson()
            };
        }

        return await method.Call(args, cancellationToken);
    }

    /// <summary>
    /// 按全限定名调用绑定方法。
    /// </summary>
    /// <param name="fullName">方法全限定名。</param>
    /// <param name="args">JSON 参数数组。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>包含调用结果或错误的字典。</returns>
    public async Task<Dictionary<string, object?>> Call(string fullName, JsonElement[] args, CancellationToken cancellationToken = default)
    {
        if (!_boundMethods.TryGetValue(fullName, out var method))
        {
            var error = new CallError($"未找到名为 '{fullName}' 的绑定方法", null, CallErrorKind.ReferenceError);
            return new Dictionary<string, object?>
            {
                ["result"] = null,
                ["error"] = error.ToJson()
            };
        }

        return await method.Call(args, cancellationToken);
    }

    /// <summary>
    /// 获取绑定方法的全限定名（Namespace.ClassName.MethodName）。
    /// </summary>
    /// <param name="methodInfo">方法反射信息。</param>
    /// <returns>全限定名。</returns>
    private static string GetFullMethodName(MethodInfo methodInfo)
    {
        var declaringType = methodInfo.DeclaringType;
        var namespaceName = declaringType?.Namespace ?? "";
        var className = declaringType?.Name ?? "";
        return $"{namespaceName}.{className}.{methodInfo.Name}";
    }

    /// <summary>
    /// 计算 FNV-1a 32 位哈希。
    /// 与 Go 版本 fnv.New32a() 一致，确保跨语言兼容。
    /// </summary>
    /// <param name="text">要哈希的文本。</param>
    /// <returns>32 位无符号哈希值。</returns>
    public static uint FNV1aHash(string text)
    {
        const uint offsetBasis = 2166136261u;
        const uint prime = 16777619u;

        var hash = offsetBasis;
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(text))
        {
            hash ^= b;
            hash *= prime;
        }

        return hash;
    }
}
