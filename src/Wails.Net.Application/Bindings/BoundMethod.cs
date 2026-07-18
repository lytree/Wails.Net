using System.Reflection;
using System.Text.Json;

namespace Wails.Net.Application.Bindings;

/// <summary>
/// 表示一个已绑定的方法，包含反射元数据。
/// 对应 Wails v3 Go 版本 bindings.go 中的 boundMethod 结构。
/// </summary>
public class BoundMethod
{
    /// <summary>
    /// 方法的全限定名（Namespace.ClassName.MethodName）。
    /// </summary>
    public string FullName { get; }

    /// <summary>
    /// 方法的 FNV-1a 32 位哈希 ID。
    /// </summary>
    public uint ID { get; }

    /// <summary>
    /// 方法所属的实例（对于实例方法）。
    /// </summary>
    public object? Instance { get; }

    /// <summary>
    /// 方法的反射信息。
    /// </summary>
    public MethodInfo Info { get; }

    /// <summary>
    /// 方法参数类型数组。
    /// </summary>
    public Type[] ParameterTypes { get; }

    /// <summary>
    /// 方法是否为可变参数（使用 params 关键字）。
    /// </summary>
    public bool IsVariadic { get; }

    /// <summary>
    /// 方法是否接受 CancellationToken 作为首个参数（自动注入，对应 Go 的 context.Context）。
    /// </summary>
    public bool HasCancellationToken { get; }

    /// <summary>
    /// 获取或设置方法调用的超时时间。为 null 表示不限制。
    /// 仅对返回 Task 的异步方法生效，使用 CancellationTokenSource 配合 Task.WhenAny 实现超时控制。
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// 使用指定参数构造 BoundMethod 实例。
    /// </summary>
    /// <param name="fullName">方法全限定名。</param>
    /// <param name="id">FNV-1a 哈希 ID。</param>
    /// <param name="instance">方法所属实例。</param>
    /// <param name="info">方法反射信息。</param>
    /// <param name="parameterTypes">参数类型数组。</param>
    /// <param name="isVariadic">是否为可变参数方法。</param>
    /// <param name="hasCancellationToken">是否接受 CancellationToken。</param>
    public BoundMethod(
        string fullName,
        uint id,
        object? instance,
        MethodInfo info,
        Type[] parameterTypes,
        bool isVariadic,
        bool hasCancellationToken)
    {
        FullName = fullName;
        ID = id;
        Instance = instance;
        Info = info;
        ParameterTypes = parameterTypes;
        IsVariadic = isVariadic;
        HasCancellationToken = hasCancellationToken;
    }

    /// <summary>
    /// 调用绑定方法。
    /// 将 JSON 参数反序列化为对应类型，通过反射调用方法，并返回序列化结果。
    /// </summary>
    /// <param name="args">JSON 参数数组。</param>
    /// <param name="cancellationToken">取消令牌（若方法需要则自动注入）。</param>
    /// <returns>包含调用结果或错误的字典，可序列化为 JSON。</returns>
    public async Task<Dictionary<string, object?>> Call(JsonElement[] args, CancellationToken cancellationToken = default)
    {
        try
        {
            var parameters = BuildParameters(args, cancellationToken);
            var result = Info.Invoke(Instance, parameters);

            // 若方法返回 Task，则等待其完成
            if (result is Task task)
            {
                // 若配置了超时，使用 CancellationTokenSource 配合 Task.WhenAny 实现超时控制
                if (Timeout.HasValue)
                {
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeoutCts.CancelAfter(Timeout.Value);
                    var completed = await Task.WhenAny(task, Task.Delay(Timeout.Value, timeoutCts.Token));
                    if (completed != task)
                    {
                        return ErrorResult(
                            $"方法执行超时（{Timeout.Value.TotalMilliseconds}ms）",
                            Errors.CallErrorKind.RuntimeError);
                    }
                }

                await task;
                // 尝试获取 Task<T>.Result
                var resultProperty = result.GetType().GetProperty("Result");
                var taskResult = resultProperty?.GetValue(result);
                return SuccessResult(taskResult);
            }

            // 若方法返回值类型为 void（Task 返回已在上面的分支处理）
            if (Info.ReturnType == typeof(void))
            {
                return SuccessResult(null);
            }

            return SuccessResult(result);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is OperationCanceledException)
        {
            // P0-B1：反射调用包装的取消异常需要解包并重抛，
            // 使 MessageProcessor.ProcessCallAsync 能识别为取消操作并返回 "调用已被取消" 错误响应，
            // 而不是被当作普通 RuntimeError 处理。
            throw new OperationCanceledException(ex.InnerException.Message, ex.InnerException);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            // 反射调用包装的异常需要解包
            return ErrorResult(ex.InnerException.Message, ex.InnerException is ArgumentException
                ? Errors.CallErrorKind.TypeError
                : Errors.CallErrorKind.RuntimeError);
        }
        catch (OperationCanceledException)
        {
            // P0-B1：直接重抛取消异常，让 MessageProcessor 统一处理为 "调用已被取消"。
            // 不在此包装为 ErrorResult，否则 ProcessCallAsync 无法识别取消语义。
            throw;
        }
        catch (JsonException ex)
        {
            return ErrorResult($"参数反序列化失败: {ex.Message}", Errors.CallErrorKind.TypeError);
        }
        catch (ArgumentException ex)
        {
            return ErrorResult(ex.Message, Errors.CallErrorKind.TypeError);
        }
        catch (Exception ex)
        {
            return ErrorResult(ex.Message, Errors.CallErrorKind.RuntimeError);
        }
    }

    /// <summary>
    /// 从 JSON 参数数组和 CancellationToken 构建反射调用所需的参数数组。
    /// </summary>
    /// <param name="args">JSON 参数数组。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>反射调用参数数组。</returns>
    private object?[] BuildParameters(JsonElement[] args, CancellationToken cancellationToken)
    {
        var offset = HasCancellationToken ? 1 : 0;
        var paramCount = ParameterTypes.Length - offset;

        // 可变参数方法：最后一个参数是数组，需要特殊处理
        if (IsVariadic)
        {
            paramCount--;
        }

        var parameters = new object?[ParameterTypes.Length];

        // 注入 CancellationToken（若需要）
        if (HasCancellationToken)
        {
            parameters[0] = cancellationToken;
        }

        // 反序列化固定参数
        for (var i = 0; i < paramCount && i < args.Length; i++)
        {
            var paramIndex = i + offset;
            parameters[paramIndex] = DeserializeParameter(args[i], ParameterTypes[paramIndex]);
        }

        // 反序列化可变参数
        if (IsVariadic)
        {
            var variadicType = ParameterTypes[^1].GetElementType()
                ?? throw new InvalidOperationException("可变参数方法的最后一个参数类型无效。");
            var variadicArgs = Array.CreateInstance(variadicType, Math.Max(0, args.Length - paramCount));
            for (var i = paramCount; i < args.Length; i++)
            {
                variadicArgs.SetValue(DeserializeParameter(args[i], variadicType), i - paramCount);
            }
            parameters[^1] = variadicArgs;
        }

        return parameters;
    }

    /// <summary>
    /// 将 JSON 元素反序列化为指定类型。
    /// </summary>
    /// <param name="element">JSON 元素。</param>
    /// <param name="targetType">目标类型。</param>
    /// <returns>反序列化后的对象。</returns>
    private static object? DeserializeParameter(JsonElement element, Type targetType)
    {
        if (targetType == typeof(object) || targetType == typeof(JsonElement))
        {
            return element;
        }

        return element.Deserialize(targetType, JsonOptions.DefaultSerializerOptions);
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
