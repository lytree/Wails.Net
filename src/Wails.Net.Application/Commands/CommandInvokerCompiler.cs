using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Wails.Net.Application.Bindings;

namespace Wails.Net.Application.Commands;

/// <summary>
/// 统一的命令调用器委托，替代运行时 <see cref="MethodInfo.Invoke(object?, object?[])"/>。
/// 通过表达式树编译，运行时零反射调用。
/// </summary>
/// <param name="instance">命令所属实例。</param>
/// <param name="parameters">前端传入的 JSON 参数（整个 payload）。</param>
/// <param name="ctx">命令上下文，提供 CancellationToken、Services 等。可为 null。</param>
/// <returns>方法的返回值。若方法返回 Task，则为 Task 实例（由调用方 await）。</returns>
public delegate object? CompiledCommandInvoker(object instance, JsonElement parameters, ICommandContext? ctx);

/// <summary>
/// 命令调用器编译器，将 <see cref="MethodInfo"/> 编译为 <see cref="CompiledCommandInvoker"/> 委托。
/// 借鉴 ASP.NET Core 的表达式树编译模式，在注册时一次性编译，运行时零反射调用。
/// <para>
/// 参数绑定策略（与 <see cref="CommandDispatcher.ExecuteCommandAsync"/> 原有逻辑一致）：
/// <list type="bullet">
/// <item><see cref="ICommandContext"/> → 注入 ctx</item>
/// <item><see cref="CancellationToken"/> → 注入 ctx.CancellationToken</item>
/// <item><see cref="IServiceProvider"/> → 注入 ctx.Services</item>
/// <item>其他类型 → <see cref="JsonSerializer.Deserialize(string, Type, JsonSerializerOptions?)"/> 反序列化</item>
/// </list>
/// </para>
/// </summary>
public static class CommandInvokerCompiler
{
    /// <summary>
    /// 编译缓存，避免重复编译同一个 MethodInfo。
    /// </summary>
    private static readonly ConcurrentDictionary<MethodInfo, CompiledCommandInvoker?> _cache = new();

    /// <summary>
    /// 默认 JSON 序列化选项。
    /// </summary>
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// 为指定 <see cref="MethodInfo"/> 编译 <see cref="CompiledCommandInvoker"/>。
    /// 若方法参数无法绑定则返回 null。
    /// </summary>
    /// <param name="method">要编译的方法反射信息。</param>
    /// <returns>编译后的调用器委托，若无法编译则返回 null。</returns>
    public static CompiledCommandInvoker? Compile(MethodInfo method)
    {
        return _cache.GetOrAdd(method, static (m, options) => CompileCore(m, options), _jsonOptions);
    }

    /// <summary>
    /// 核心编译逻辑。构建表达式树并编译为委托。
    /// </summary>
    private static CompiledCommandInvoker? CompileCore(MethodInfo method, JsonSerializerOptions jsonOptions)
    {
        var declaringType = method.DeclaringType;
        if (declaringType is null)
        {
            return null;
        }

        // 表达式参数
        var instanceParam = Expression.Parameter(typeof(object), "instance");
        var parametersParam = Expression.Parameter(typeof(JsonElement), "parameters");
        var ctxParam = Expression.Parameter(typeof(ICommandContext), "ctx");

        var methodParams = method.GetParameters();
        var args = new Expression[methodParams.Length];

        // JsonElement.GetRawText() 方法信息
        var getRawTextMethod = typeof(JsonElement).GetMethod(nameof(JsonElement.GetRawText))!;

        // JsonSerializer.Deserialize(string, Type, JsonSerializerOptions) 方法信息
        var deserializeMethod = typeof(JsonSerializer).GetMethod(
            nameof(JsonSerializer.Deserialize),
            new[] { typeof(string), typeof(Type), typeof(JsonSerializerOptions) })!;

        // CancellationToken 属性
        var ctxCancellationTokenProp = typeof(ICommandContext).GetProperty(nameof(ICommandContext.CancellationToken))!;
        var ctxServicesProp = typeof(ICommandContext).GetProperty(nameof(ICommandContext.Services))!;

        for (var i = 0; i < methodParams.Length; i++)
        {
            var paramType = methodParams[i].ParameterType;

            if (paramType == typeof(ICommandContext))
            {
                args[i] = ctxParam;
            }
            else if (paramType == typeof(CancellationToken))
            {
                args[i] = Expression.Property(ctxParam, ctxCancellationTokenProp);
            }
            else if (paramType == typeof(IServiceProvider))
            {
                args[i] = Expression.Property(ctxParam, ctxServicesProp);
            }
            else
            {
                // JsonSerializer.Deserialize(parameters.GetRawText(), paramType, options)
                var getRawTextCall = Expression.Call(parametersParam, getRawTextMethod);
                var paramTypeConstant = Expression.Constant(paramType, typeof(Type));
                var optionsConstant = Expression.Constant(jsonOptions, typeof(JsonSerializerOptions));
                args[i] = Expression.Call(
                    deserializeMethod,
                    getRawTextCall,
                    paramTypeConstant,
                    optionsConstant);

                // 处理可空类型（Deserialize 返回 object?，需要转换）
                args[i] = Expression.Convert(args[i], paramType);
            }
        }

        // 转换 instance 为目标类型
        var instanceCast = Expression.Convert(instanceParam, declaringType);

        // 调用方法
        var call = Expression.Call(instanceCast, method, args);

        // 处理返回值
        Expression body;
        if (method.ReturnType == typeof(void))
        {
            // void 方法：调用后返回 null
            body = Expression.Block(call, Expression.Constant(null, typeof(object)));
        }
        else
        {
            // 非 void：转换为 object
            body = Expression.Convert(call, typeof(object));
        }

        var lambda = Expression.Lambda<CompiledCommandInvoker>(
            body,
            instanceParam,
            parametersParam,
            ctxParam);

        return lambda.Compile();
    }

    /// <summary>
    /// 尝试从 <see cref="GeneratedBindingRegistry"/> 获取源生成器调用器，
    /// 并包装为 <see cref="CompiledCommandInvoker"/>。
    /// 仅适用于 <c>[Command]</c> 或 <c>[Binding]</c> 标记的方法。
    /// </summary>
    /// <param name="commandName">命令名（如 "counter.increment"）。</param>
    /// <returns>包装后的调用器，若源生成器未注册则返回 null。</returns>
    public static CompiledCommandInvoker? TryGetGeneratedInvoker(string commandName)
    {
        if (!GeneratedBindingRegistry.TryGetInvoker(commandName, out var generated) || generated is null)
        {
            return null;
        }

        // 将 GeneratedInvoker (instance, JsonElement[], CancellationToken) 包装为 CompiledCommandInvoker
        return (instance, parameters, ctx) =>
        {
            // GeneratedInvoker 接受 JsonElement[] 数组
            // 将单个 JsonElement 包装为数组（保持与 BindingManager 调用约定一致）
            var argsArray = new[] { parameters };
            return generated(instance, argsArray, ctx?.CancellationToken ?? CancellationToken.None);
        };
    }
}
