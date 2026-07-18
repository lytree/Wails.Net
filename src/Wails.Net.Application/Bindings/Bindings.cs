using System.Reflection;
using System.Text.Json;
using Wails.Net.Errors;

namespace Wails.Net.Application.Bindings;

/// <summary>
/// 绑定管理器，负责注册和调用绑定方法。
/// 对应 Wails v3 Go 版本 bindings.go 中的 Bindings 结构。
/// 优先使用由源代码生成器生成的强类型调用器（<see cref="GeneratedBindingRegistry"/>），
/// 仅当目标方法未被生成器处理时回退到反射调用。
/// </summary>
public class BindingManager
{
    /// <summary>
    /// 按全限定名索引的绑定方法字典（仅含反射路径使用的方法）。
    /// </summary>
    private readonly Dictionary<string, BoundMethod> _boundMethods = new();

    /// <summary>
    /// 按 FNV-1a 哈希 ID 索引的绑定方法字典。
    /// </summary>
    private readonly Dictionary<uint, BoundMethod> _boundByID = new();

    /// <summary>
    /// 按类型全名（Namespace.ClassName）索引的实例字典。
    /// 用于在调用由源生成器生成的调用器时查找正确的实例。
    /// </summary>
    private readonly Dictionary<string, object> _instancesByTypeName = new(StringComparer.Ordinal);

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
    /// 同时将实例登记到 <see cref="_instancesByTypeName"/>，供源生成器调用器使用。
    /// </summary>
    /// <param name="instance">要注册的实例。</param>
    public void Add(object instance)
    {
        var type = instance.GetType();
        var typeFullName = GetFullTypeName(type);

        // 登记实例，供源生成器调用器查找
        _instancesByTypeName[typeFullName] = instance;

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
    /// 同时注册全限定名（Namespace.ClassName.MethodName）和短名称（ClassName.MethodName），
    /// 前端可使用任一格式调用。对应 Wails v3 Go 版本中包名作为前缀的做法。
    /// </summary>
    /// <param name="instance">方法所属实例。</param>
    /// <param name="methodInfo">方法反射信息。</param>
    /// <returns>已注册的 BoundMethod 实例。</returns>
    private BoundMethod AddMethod(object instance, MethodInfo methodInfo)
    {
        var fullName = GetFullMethodName(methodInfo);
        var shortName = GetShortMethodName(methodInfo);
        var id = FNV1aHash(fullName);
        var shortId = FNV1aHash(shortName);

        var parameters = methodInfo.GetParameters();
        var parameterTypes = new Type[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            parameterTypes[i] = parameters[i].ParameterType;
        }

        var isVariadic = parameters.Length > 0 && parameters[^1].GetCustomAttribute<ParamArrayAttribute>() is not null;
        var hasCancellationToken = parameters.Length > 0 && parameters[0].ParameterType == typeof(CancellationToken);

        var boundMethod = new BoundMethod(fullName, id, instance, methodInfo, parameterTypes, isVariadic, hasCancellationToken);

        // 注册全限定名（Namespace.ClassName.MethodName）
        _boundMethods[fullName] = boundMethod;
        _boundByID[id] = boundMethod;

        // 注册短名称（ClassName.MethodName）作为别名，便于前端调用
        _boundMethods[shortName] = boundMethod;
        _boundByID[shortId] = boundMethod;

        return boundMethod;
    }

    /// <summary>
    /// 按 ID 调用绑定方法。
    /// 优先使用反射注册的绑定方法（按 ID 路径仅支持反射，因为 ID 是 FNV-1a 哈希）。
    /// </summary>
    /// <param name="id">绑定方法的 FNV-1a 哈希 ID。</param>
    /// <param name="args">JSON 参数数组。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>包含调用结果或错误的字典。</returns>
    public async Task<Dictionary<string, object?>> Call(uint id, JsonElement[] args, CancellationToken cancellationToken = default)
    {
        if (_boundByID.TryGetValue(id, out var method))
        {
            return await method.Call(args, cancellationToken);
        }

        var error = new CallError($"未找到 ID 为 {id} 的绑定方法", null, CallErrorKind.ReferenceError);
        return new Dictionary<string, object?>
        {
            ["result"] = null,
            ["error"] = error.ToJson()
        };
    }

    /// <summary>
    /// 按全限定名调用绑定方法。
    /// 优先使用源生成器生成的强类型调用器（<see cref="GeneratedBindingRegistry"/>），
    /// 若未找到则回退到反射注册的 <see cref="BoundMethod"/>。
    /// </summary>
    /// <param name="fullName">方法全限定名或命令名。</param>
    /// <param name="args">JSON 参数数组。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>包含调用结果或错误的字典。</returns>
    public async Task<Dictionary<string, object?>> Call(string fullName, JsonElement[] args, CancellationToken cancellationToken = default)
    {
        // 1. 优先使用源生成器生成的强类型调用器
        if (GeneratedBindingRegistry.TryGetInvoker(fullName, out var invoker) && invoker is not null)
        {
            return await InvokeGeneratedAsync(fullName, invoker, args, cancellationToken);
        }

        // 2. 回退到反射注册的绑定方法
        if (_boundMethods.TryGetValue(fullName, out var method))
        {
            return await method.Call(args, cancellationToken);
        }

        // 诊断日志：输出注册表状态，便于排查 ModuleInitializer 未运行等问题
        System.Console.Error.WriteLine(
            $"[BindingManager] 未找到方法 '{fullName}'。GeneratedBindingRegistry.Count={GeneratedBindingRegistry.Count}, " +
            $"TryGetInvoker={GeneratedBindingRegistry.TryGetInvoker(fullName, out _)}, " +
            $"已注册实例数={_instancesByTypeName.Count}, " +
            $"反射方法数={_boundMethods.Count}");

        var error = new CallError($"未找到名为 '{fullName}' 的绑定方法", null, CallErrorKind.ReferenceError);
        return new Dictionary<string, object?>
        {
            ["result"] = null,
            ["error"] = error.ToJson()
        };
    }

    /// <summary>
    /// 调用源生成器生成的调用器并处理返回值（包括 Task / Task&lt;T&gt;）。
    /// </summary>
    /// <param name="fullName">方法全名（用于错误信息）。</param>
    /// <param name="invoker">生成的调用器委托。</param>
    /// <param name="args">JSON 参数数组。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>包含调用结果或错误的字典。</returns>
    private async Task<Dictionary<string, object?>> InvokeGeneratedAsync(
        string fullName,
        GeneratedInvoker invoker,
        JsonElement[] args,
        CancellationToken cancellationToken)
    {
        try
        {
            // 查找实例
            if (!GeneratedBindingRegistry.TryGetTypeFullName(fullName, out var typeFullName) || typeFullName is null)
            {
                return ErrorResult($"未找到方法 '{fullName}' 的类型信息", Errors.CallErrorKind.ReferenceError);
            }

            if (!_instancesByTypeName.TryGetValue(typeFullName, out var instance))
            {
                return ErrorResult($"未找到类型 '{typeFullName}' 的注册实例", Errors.CallErrorKind.ReferenceError);
            }

            var result = invoker(instance, args ?? Array.Empty<JsonElement>(), cancellationToken);

            // 若方法返回 Task，则等待其完成并提取结果
            if (result is Task task)
            {
                await task.ConfigureAwait(false);

                // 若是 Task<T>，提取 Result
                var taskType = task.GetType();
                if (taskType.IsGenericType)
                {
                    var resultProperty = taskType.GetProperty("Result");
                    var taskResult = resultProperty?.GetValue(task);
                    return SuccessResult(taskResult);
                }

                return SuccessResult(null);
            }

            return SuccessResult(result);
        }
        catch (InvalidOperationException ex)
        {
            return ErrorResult(ex.Message, Errors.CallErrorKind.RuntimeError);
        }
        catch (ArgumentException ex)
        {
            return ErrorResult(ex.Message, Errors.CallErrorKind.TypeError);
        }
        catch (JsonException ex)
        {
            return ErrorResult($"参数反序列化失败: {ex.Message}", Errors.CallErrorKind.TypeError);
        }
        catch (OperationCanceledException)
        {
            // P0-B1：取消异常直接重抛，让 MessageProcessor.ProcessCallAsync 统一处理为 "调用已被取消"。
            // 不在此包装为 ErrorResult，否则 ProcessCallAsync 无法识别取消语义。
            throw;
        }
        catch (Exception ex)
        {
            return ErrorResult(ex.Message, Errors.CallErrorKind.RuntimeError);
        }
    }

    /// <summary>
    /// 构建成功结果的字典。
    /// </summary>
    /// <param name="result">调用结果。</param>
    /// <returns>包含结果的字典。</returns>
    private static Dictionary<string, object?> SuccessResult(object? result)
    {
        return new Dictionary<string, object?>
        {
            ["result"] = result,
            ["error"] = null
        };
    }

    /// <summary>
    /// 构建错误结果的字典。
    /// </summary>
    /// <param name="message">错误消息。</param>
    /// <param name="kind">错误类型。</param>
    /// <returns>包含错误的字典。</returns>
    private static Dictionary<string, object?> ErrorResult(string message, Errors.CallErrorKind kind)
    {
        var error = new Errors.CallError(message, null, kind);
        return new Dictionary<string, object?>
        {
            ["result"] = null,
            ["error"] = error.ToJson()
        };
    }

    /// <summary>
    /// 为已绑定的方法注册别名。
    /// 对应 Wails v3 Go 版本 bindings.go 中的 BindAliases 方法。
    /// </summary>
    /// <param name="instance">已绑定的实例。</param>
    /// <param name="methodName">原方法名。</param>
    /// <param name="aliases">别名列表。</param>
    public void BindAliases(object instance, string methodName, params string[] aliases)
    {
        var type = instance.GetType();
        var namespaceName = type.Namespace ?? "";
        var className = type.Name ?? "";
        var fullName = $"{namespaceName}.{className}.{methodName}";

        if (!_boundMethods.TryGetValue(fullName, out var boundMethod))
        {
            throw new InvalidOperationException($"未找到名为 '{fullName}' 的绑定方法，无法注册别名");
        }

        foreach (var alias in aliases)
        {
            var aliasFullName = $"{namespaceName}.{className}.{alias}";
            var aliasId = FNV1aHash(aliasFullName);

            _boundMethods[aliasFullName] = boundMethod;
            _boundByID[aliasId] = boundMethod;
        }
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
    /// 获取类型的全名（Namespace.ClassName）。
    /// 与源生成器生成的 typeFullName 字符串保持一致，用于在 <see cref="_instancesByTypeName"/> 中查找实例。
    /// </summary>
    /// <param name="type">类型。</param>
    /// <returns>类型全名。</returns>
    private static string GetFullTypeName(Type type)
    {
        var namespaceName = type.Namespace ?? "";
        var className = type.Name ?? "";
        return namespaceName.Length > 0 ? $"{namespaceName}.{className}" : className;
    }

    /// <summary>
    /// 获取绑定方法的短名称（ClassName.MethodName）。
    /// 对应 Wails v3 Go 版本中省略包路径、仅用结构名与方法名的做法。
    /// </summary>
    /// <param name="methodInfo">方法反射信息。</param>
    /// <returns>短名称。</returns>
    private static string GetShortMethodName(MethodInfo methodInfo)
    {
        var declaringType = methodInfo.DeclaringType;
        var className = declaringType?.Name ?? "";
        return $"{className}.{methodInfo.Name}";
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
