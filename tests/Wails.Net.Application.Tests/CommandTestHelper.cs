using System.Text.Json;
using Wails.Net.Application.Commands;

namespace Wails.Net.Application.Tests;

/// <summary>
/// 命令调用测试辅助类。
/// 将 object?[] 参数转换为 CompiledCommandInvoker 所需的 JsonElement，
/// 调用编译期构建的强类型调用器（遵循 AGENTS.md §3.4 禁令，零反射）。
/// </summary>
public static class CommandTestHelper
{
    /// <summary>
    /// 默认 JSON 序列化选项（与 MapCommandExtensions 一致）。
    /// </summary>
    private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// 调用命令并返回结果。
    /// 自动从 args 中提取 <see cref="ICommandContext"/>（若存在）并从参数列表中移除，
    /// 剩余参数包装为 JsonElement 后传给 Invoker。
    /// </summary>
    /// <param name="registry">命令注册表。</param>
    /// <param name="name">命令名。</param>
    /// <param name="args">参数列表（可能包含 ICommandContext，会被自动提取）。</param>
    /// <returns>命令返回值（若为 Task 则返回 Task 实例本身，调用方负责 await）。</returns>
    public static object? Invoke(CommandRegistry registry, string name, params object?[] args)
    {
        var entry = registry.Find(name);
        if (entry is null)
        {
            throw new InvalidOperationException($"命令未找到: {name}");
        }

        if (entry.Invoker is null)
        {
            throw new InvalidOperationException($"命令 '{name}' 未注册调用器");
        }

        // 自动从 args 中提取 ICommandContext（若存在），剩余参数作为业务参数
        ICommandContext? ctx = null;
        var remainingArgs = new List<object?>();
        foreach (var arg in args)
        {
            if (ctx is null && arg is ICommandContext c)
            {
                ctx = c;
            }
            else
            {
                remainingArgs.Add(arg);
            }
        }

        var parameters = ArgsToJsonElement(remainingArgs);
        return entry.Invoker(entry.Instance, parameters, ctx).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 将参数列表序列化为 JsonElement。
    /// 无参数时返回 default（与 MessageProcessor 单参数透传约定一致，由各重载降级处理）。
    /// </summary>
    private static JsonElement ArgsToJsonElement(IReadOnlyList<object?> args)
    {
        if (args is null || args.Count == 0)
        {
            return default;
        }

        // 单参数场景：直接整体序列化为该参数的 JSON
        if (args.Count == 1)
        {
            return JsonSerializer.SerializeToElement(args[0], _options);
        }

        // 多参数场景：序列化为 JSON 数组
        return JsonSerializer.SerializeToElement(args, _options);
    }
}
