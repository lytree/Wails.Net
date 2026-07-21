using System.Text.Json;
using Wails.Net.Errors;

namespace Wails.Net.Application.Bindings;

/// <summary>
/// 绑定管理器，负责注册和调用绑定方法。
/// 对应 Wails v3 Go 版本 bindings.go 中的 Bindings 结构。
/// <para>
/// 调用路径：使用由源代码生成器生成的强类型调用器（<see cref="GeneratedBindingRegistry"/>）。
/// <see cref="GeneratedInvoker"/> 委托统一返回 <see cref="System.Threading.Tasks.Task{TResult}"/>（Result 类型为 <see cref="object"/>），
/// 在调用器内部 <c>await</c> 异步方法并装箱结果，调用方仅需 <c>await</c> 即可，
/// 无需运行时反射提取 <c>Task.Result</c>，遵循 AGENTS.md §3.4 禁令。
/// </para>
/// </summary>
public class BindingManager
{
    /// <summary>
    /// 按类型全名（Namespace.ClassName）索引的实例字典。
    /// 用于在调用由源生成器生成的调用器时查找正确的实例。
    /// </summary>
    private readonly Dictionary<string, object> _instancesByTypeName = new(StringComparer.Ordinal);

    /// <summary>
    /// 注册服务实例到 <see cref="_instancesByTypeName"/>，供源生成器生成的调用器查找。
    /// 不进行反射方法枚举，遵循 AGENTS.md §3.4 禁令。
    /// 类型全名由运行时 <see cref="object.GetType"/> 获取（NativeAOT 友好，非反射枚举）。
    /// </summary>
    /// <param name="instance">要注册的服务实例。</param>
    public void RegisterInstance(object instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var typeFullName = GetFullTypeName(instance.GetType());
        _instancesByTypeName[typeFullName] = instance;
    }

    /// <summary>
    /// 注册服务实例的兼容性别名，等价于 <see cref="RegisterInstance"/>。
    /// 保留以减少调用点改动；语义已变更为仅注册实例，不再通过反射枚举方法（遵循 AGENTS.md §3.4）。
    /// </summary>
    /// <param name="instance">要注册的服务实例。</param>
    public void Add(object instance) => RegisterInstance(instance);

    /// <summary>
    /// 按 FNV-1a 哈希 ID 调用绑定方法。
    /// 使用源生成器生成的强类型调用器（<see cref="GeneratedBindingRegistry"/>）执行调用，
    /// 运行时零反射。
    /// </summary>
    /// <param name="id">方法 FNV-1a 哈希 ID。</param>
    /// <param name="args">JSON 参数数组。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>包含调用结果或错误的字典。</returns>
    public Task<Dictionary<string, object?>> Call(uint id, JsonElement[] args, CancellationToken cancellationToken = default)
    {
        // 与 Wails v3 Go 版本一致：通过 ID 反查方法全名后委托给字符串重载。
        // ID → 全名映射来自编译期生成的 GeneratedBindingsMetadata.Methods，运行时零反射。
        foreach (var m in GeneratedBindingsMetadata.Methods)
        {
            if (m.Id == id)
            {
                return Call(m.FullName, args, cancellationToken);
            }
        }

        var error = new CallError($"未找到 ID 为 '{id}' 的绑定方法", null, CallErrorKind.ReferenceError);
        return Task.FromResult(new Dictionary<string, object?>
        {
            ["result"] = null,
            ["error"] = error.ToJson()
        });
    }

    /// <summary>
    /// 按全限定名调用绑定方法。
    /// 使用源生成器生成的强类型调用器（<see cref="GeneratedBindingRegistry"/>）执行调用，
    /// 运行时零反射。
    /// </summary>
    /// <param name="fullName">方法全限定名或命令名。</param>
    /// <param name="args">JSON 参数数组。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>包含调用结果或错误的字典。</returns>
    public async Task<Dictionary<string, object?>> Call(string fullName, JsonElement[] args, CancellationToken cancellationToken = default)
    {
        // 使用源生成器生成的强类型调用器（零反射路径，遵循 AGENTS.md §3.4）
        if (GeneratedBindingRegistry.TryGetInvoker(fullName, out var invoker) && invoker is not null)
        {
            return await InvokeGeneratedAsync(fullName, invoker, args, cancellationToken);
        }

        // 未找到绑定方法时返回 ReferenceError。
        // 注意：许多合法命令（dialog.* / window.* / application.* / notification.* 等）
        // 通过 CommandDispatcher 插件路径调用，会先在此返回"未找到"再由 MessageProcessor 回退。
        // 因此不应在此输出诊断日志——会污染 stderr 并误导用户。
        // 若需排查 ModuleInitializer 未运行等问题，请使用 dotnet trace 或显式日志。
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

            // GeneratedInvoker 委托统一返回 Task<object?>：调用器内部已 await 异步方法并装箱结果。
            // 调用方仅需 await，无需运行时反射提取 Task.Result（遵循 AGENTS.md §3.4）。
            var result = await invoker(instance, args ?? Array.Empty<JsonElement>(), cancellationToken).ConfigureAwait(false);
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
    /// 获取已注册的所有绑定方法全名集合。
    /// 用于响应前端 "bindings" 查询。
    /// 数据来源为 <see cref="GeneratedBindingsMetadata.Methods"/>，由源生成器在编译期填充，
    /// 不依赖运行时反射。
    /// </summary>
    /// <returns>已注册绑定方法的全名集合。</returns>
    public IReadOnlyCollection<string> GetRegisteredMethodNames()
        => GeneratedBindingsMetadata.Methods.Select(m => m.FullName).ToList();

    /// <summary>
    /// 兼容性外观：返回以方法全名为键、占位 <c>object</c> 为值的只读字典。
    /// 数据来源为 <see cref="GeneratedBindingsMetadata.Methods"/>，由源生成器在编译期填充，
    /// 不依赖运行时反射。值仅为占位（非 MethodInfo），仅用于测试断言键存在性。
    /// </summary>
    /// <remarks>
    /// 旧反射路径下的 BoundMethods 返回 MethodInfo，反射删除后此属性仅为兼容旧测试 API 而保留。
    /// 业务代码应使用 <see cref="GetRegisteredMethodNames"/> 或 <see cref="GeneratedBindingRegistry"/>。
    /// </remarks>
    public IReadOnlyDictionary<string, object> BoundMethods
        => GeneratedBindingsMetadata.Methods.ToDictionary(m => m.FullName, _ => (object)new object());

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

/// <summary>
/// JSON 序列化选项的默认配置。
/// 源代码生成器生成的代码也使用此选项。
/// </summary>
public static class JsonOptions
{
    /// <summary>
    /// 默认的 JSON 序列化选项（驼峰命名、不区分大小写反序列化）。
    /// </summary>
    public static readonly JsonSerializerOptions DefaultSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
}
