using System.Text.Json;
using System.Threading.Tasks;

namespace Wails.Net.Application.Commands;

/// <summary>
/// MapCommand 扩展方法，提供 Minimal API 风格的命令注册。
/// <para>
/// 所有重载通过强类型泛型签名在编译期构建 <see cref="CompiledCommandInvoker"/> 闭包，
/// 闭包统一返回 <see cref="Task{TResult}"/>（Result 类型为 <see cref="object"/>）。
/// 同步方法在闭包内用 <see cref="Task.FromResult{TResult}(TResult)"/> 包装结果，
/// 异步方法在闭包内 <c>await</c> 并提取 <c>Task&lt;T&gt;</c> 的 Result 装箱为 <see cref="object"/>。
/// 调用方仅需 <c>await</c> 委托返回值，无需运行时反射提取 <c>Task.Result</c>，遵循 AGENTS.md §3.4 禁令。
/// </para>
/// <para>
/// 参数解析约定：
/// <list type="bullet">
/// <item>无业务参数：忽略 <paramref name="parameters"/>；</item>
/// <item>单个业务参数：将 <paramref name="parameters"/> 整体反序列化为 T；</item>
/// <item>多个业务参数：将 <paramref name="parameters"/> 视为 JSON 数组，按位置反序列化各元素。</item>
/// </list>
/// 与 <see cref="Transport.MessageProcessor.TryDispatchCommandAsync"/> 的参数包装规则一致：
/// 单参数透传、多参数包装为数组、无参数为 default。
/// </para>
/// </summary>
public static class MapCommandExtensions
{
    /// <summary>
    /// 默认 JSON 反序列化选项（Web 风格：驼峰命名、不区分大小写）。
    /// 与 <see cref="CommandDispatcher"/> 的 _scopeJsonOptions 一致。
    /// </summary>
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    // ============================================================
    // 同步重载：Action 系列
    // ============================================================

    /// <summary>
    /// 注册无参数无返回值的命令。
    /// </summary>
    /// <param name="registry">命令注册表。</param>
    /// <param name="name">命令名称。</param>
    /// <param name="handler">命令处理委托。</param>
    /// <returns>命令注册表，用于链式调用。</returns>
    public static CommandRegistry MapCommand(this CommandRegistry registry, string name, Action handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return registry.Register(name, (instance, parameters, ctx) =>
        {
            handler();
            return Task.FromResult<object?>(null);
        });
    }

    /// <summary>
    /// 注册注入 <see cref="ICommandContext"/> 的无返回值命令。
    /// </summary>
    /// <param name="registry">命令注册表。</param>
    /// <param name="name">命令名称。</param>
    /// <param name="handler">命令处理委托。</param>
    /// <returns>命令注册表，用于链式调用。</returns>
    public static CommandRegistry MapCommand(this CommandRegistry registry, string name, Action<ICommandContext> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return registry.Register(name, (instance, parameters, ctx) =>
        {
            handler(ctx!);
            return Task.FromResult<object?>(null);
        });
    }

    /// <summary>
    /// 注册单参数无返回值命令。
    /// </summary>
    /// <typeparam name="T">参数类型。</typeparam>
    /// <param name="registry">命令注册表。</param>
    /// <param name="name">命令名称。</param>
    /// <param name="handler">命令处理委托。</param>
    /// <returns>命令注册表，用于链式调用。</returns>
    public static CommandRegistry MapCommand<T>(this CommandRegistry registry, string name, Action<T> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var options = _jsonOptions;
        return registry.Register(name, (instance, parameters, ctx) =>
        {
            var arg = Deserialize<T>(parameters, options);
            handler(arg);
            return Task.FromResult<object?>(null);
        });
    }

    /// <summary>
    /// 注册注入 <see cref="ICommandContext"/> 与单参数的无返回值命令。
    /// </summary>
    /// <typeparam name="T">业务参数类型。</typeparam>
    /// <param name="registry">命令注册表。</param>
    /// <param name="name">命令名称。</param>
    /// <param name="handler">命令处理委托。</param>
    /// <returns>命令注册表，用于链式调用。</returns>
    public static CommandRegistry MapCommand<T>(this CommandRegistry registry, string name, Action<ICommandContext, T> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var options = _jsonOptions;
        return registry.Register(name, (instance, parameters, ctx) =>
        {
            var arg = Deserialize<T>(parameters, options);
            handler(ctx!, arg);
            return Task.FromResult<object?>(null);
        });
    }

    /// <summary>
    /// 注册注入 <see cref="ICommandContext"/> 与两个参数的无返回值命令。
    /// </summary>
    /// <typeparam name="T1">第一个参数类型。</typeparam>
    /// <typeparam name="T2">第二个参数类型。</typeparam>
    /// <param name="registry">命令注册表。</param>
    /// <param name="name">命令名称。</param>
    /// <param name="handler">命令处理委托。</param>
    /// <returns>命令注册表，用于链式调用。</returns>
    public static CommandRegistry MapCommand<T1, T2>(
        this CommandRegistry registry,
        string name,
        Action<ICommandContext, T1, T2> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var options = _jsonOptions;
        return registry.Register(name, (instance, parameters, ctx) =>
        {
            var (a1, a2) = ParseArgs2<T1, T2>(parameters, options);
            handler(ctx!, a1, a2);
            return Task.FromResult<object?>(null);
        });
    }

    /// <summary>
    /// 注册双参数无返回值命令。
    /// </summary>
    /// <typeparam name="T1">第一个参数类型。</typeparam>
    /// <typeparam name="T2">第二个参数类型。</typeparam>
    /// <param name="registry">命令注册表。</param>
    /// <param name="name">命令名称。</param>
    /// <param name="handler">命令处理委托。</param>
    /// <returns>命令注册表，用于链式调用。</returns>
    public static CommandRegistry MapCommand<T1, T2>(
        this CommandRegistry registry,
        string name,
        Action<T1, T2> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var options = _jsonOptions;
        return registry.Register(name, (instance, parameters, ctx) =>
        {
            var (a1, a2) = ParseArgs2<T1, T2>(parameters, options);
            handler(a1, a2);
            return Task.FromResult<object?>(null);
        });
    }

    /// <summary>
    /// 注册三参数无返回值命令（参数 2 为 bool）。
    /// 适配 <c>fs.rmdir</c> 等场景。
    /// </summary>
    /// <typeparam name="T1">第一个参数类型。</typeparam>
    /// <typeparam name="T2">第二个参数类型。</typeparam>
    /// <param name="registry">命令注册表。</param>
    /// <param name="name">命令名称。</param>
    /// <param name="handler">命令处理委托。</param>
    /// <returns>命令注册表，用于链式调用。</returns>
    public static CommandRegistry MapCommand<T1>(
        this CommandRegistry registry,
        string name,
        Action<T1, bool> handler)
        where T1 : notnull
    {
        ArgumentNullException.ThrowIfNull(handler);
        var options = _jsonOptions;
        return registry.Register(name, (instance, parameters, ctx) =>
        {
            var (a1, b2) = ParseArgs2<T1, bool>(parameters, options);
            handler(a1, b2);
            return Task.FromResult<object?>(null);
        });
    }

    // ============================================================
    // 同步重载：Func 系列
    // ============================================================

    /// <summary>
    /// 注册无参数有返回值命令。
    /// </summary>
    /// <typeparam name="TResult">返回值类型。</typeparam>
    /// <param name="registry">命令注册表。</param>
    /// <param name="name">命令名称。</param>
    /// <param name="handler">命令处理委托。</param>
    /// <returns>命令注册表，用于链式调用。</returns>
    public static CommandRegistry MapCommand<TResult>(this CommandRegistry registry, string name, Func<TResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return registry.Register(name, (instance, parameters, ctx) =>
        {
            var result = handler();
            return Task.FromResult<object?>(result);
        });
    }

    /// <summary>
    /// 注册注入 <see cref="ICommandContext"/> 的有返回值命令。
    /// </summary>
    /// <typeparam name="TResult">返回值类型。</typeparam>
    /// <param name="registry">命令注册表。</param>
    /// <param name="name">命令名称。</param>
    /// <param name="handler">命令处理委托。</param>
    /// <returns>命令注册表，用于链式调用。</returns>
    public static CommandRegistry MapCommand<TResult>(
        this CommandRegistry registry,
        string name,
        Func<ICommandContext, TResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return registry.Register(name, (instance, parameters, ctx) =>
        {
            var result = handler(ctx!);
            return Task.FromResult<object?>(result);
        });
    }

    /// <summary>
    /// 注册单参数有返回值命令。
    /// </summary>
    /// <typeparam name="T">参数类型。</typeparam>
    /// <typeparam name="TResult">返回值类型。</typeparam>
    /// <param name="registry">命令注册表。</param>
    /// <param name="name">命令名称。</param>
    /// <param name="handler">命令处理委托。</param>
    /// <returns>命令注册表，用于链式调用。</returns>
    public static CommandRegistry MapCommand<T, TResult>(
        this CommandRegistry registry,
        string name,
        Func<T, TResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var options = _jsonOptions;
        return registry.Register(name, (instance, parameters, ctx) =>
        {
            var arg = Deserialize<T>(parameters, options);
            var result = handler(arg);
            return Task.FromResult<object?>(result);
        });
    }

    /// <summary>
    /// 注册注入 <see cref="ICommandContext"/> 与单参数的有返回值命令。
    /// </summary>
    /// <typeparam name="T">业务参数类型。</typeparam>
    /// <typeparam name="TResult">返回值类型。</typeparam>
    /// <param name="registry">命令注册表。</param>
    /// <param name="name">命令名称。</param>
    /// <param name="handler">命令处理委托。</param>
    /// <returns>命令注册表，用于链式调用。</returns>
    public static CommandRegistry MapCommand<T, TResult>(
        this CommandRegistry registry,
        string name,
        Func<ICommandContext, T, TResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var options = _jsonOptions;
        return registry.Register(name, (instance, parameters, ctx) =>
        {
            var arg = Deserialize<T>(parameters, options);
            var result = handler(ctx!, arg);
            return Task.FromResult<object?>(result);
        });
    }

    // ============================================================
    // 异步重载：Func<Task> 系列
    // 闭包内部 await Task<T> 并装箱 T 为 object?，调用方无需再处理 Task.Result
    // ============================================================

    /// <summary>
    /// 注册无参数无返回值的异步命令。
    /// </summary>
    /// <param name="registry">命令注册表。</param>
    /// <param name="name">命令名称。</param>
    /// <param name="handler">异步命令处理委托。</param>
    /// <returns>命令注册表，用于链式调用。</returns>
    public static CommandRegistry MapCommandAsync(this CommandRegistry registry, string name, Func<Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return registry.Register(name, async (instance, parameters, ctx) =>
        {
            await handler().ConfigureAwait(false);
            return null;
        });
    }

    /// <summary>
    /// 注册带参数无返回值的异步命令。
    /// </summary>
    /// <typeparam name="T">参数类型。</typeparam>
    /// <param name="registry">命令注册表。</param>
    /// <param name="name">命令名称。</param>
    /// <param name="handler">异步命令处理委托。</param>
    /// <returns>命令注册表，用于链式调用。</returns>
    public static CommandRegistry MapCommandAsync<T>(this CommandRegistry registry, string name, Func<T, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var options = _jsonOptions;
        return registry.Register(name, async (instance, parameters, ctx) =>
        {
            var arg = Deserialize<T>(parameters, options);
            await handler(arg).ConfigureAwait(false);
            return null;
        });
    }

    /// <summary>
    /// 注册注入 <see cref="ICommandContext"/> 与单参数的无返回值异步命令。
    /// 适配 <c>updater.install</c> 等场景。
    /// </summary>
    /// <typeparam name="T">业务参数类型。</typeparam>
    /// <param name="registry">命令注册表。</param>
    /// <param name="name">命令名称。</param>
    /// <param name="handler">异步命令处理委托。</param>
    /// <returns>命令注册表，用于链式调用。</returns>
    public static CommandRegistry MapCommandAsync<T>(
        this CommandRegistry registry,
        string name,
        Func<ICommandContext, T, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var options = _jsonOptions;
        return registry.Register(name, async (instance, parameters, ctx) =>
        {
            var arg = Deserialize<T>(parameters, options);
            await handler(ctx!, arg).ConfigureAwait(false);
            return null;
        });
    }

    /// <summary>
    /// 注册双参数无返回值的异步命令。
    /// 适配 <c>fs.writeAsync</c> 等场景。
    /// </summary>
    /// <typeparam name="T1">第一个参数类型。</typeparam>
    /// <typeparam name="T2">第二个参数类型。</typeparam>
    /// <param name="registry">命令注册表。</param>
    /// <param name="name">命令名称。</param>
    /// <param name="handler">异步命令处理委托。</param>
    /// <returns>命令注册表，用于链式调用。</returns>
    public static CommandRegistry MapCommandAsync<T1, T2>(
        this CommandRegistry registry,
        string name,
        Func<T1, T2, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var options = _jsonOptions;
        return registry.Register(name, async (instance, parameters, ctx) =>
        {
            var (a1, a2) = ParseArgs2<T1, T2>(parameters, options);
            await handler(a1, a2).ConfigureAwait(false);
            return null;
        });
    }

    /// <summary>
    /// 注册注入 <see cref="ICommandContext"/> 双参数无返回值的异步命令。
    /// </summary>
    /// <typeparam name="T1">第一个参数类型。</typeparam>
    /// <typeparam name="T2">第二个参数类型。</typeparam>
    /// <param name="registry">命令注册表。</param>
    /// <param name="name">命令名称。</param>
    /// <param name="handler">异步命令处理委托。</param>
    /// <returns>命令注册表，用于链式调用。</returns>
    public static CommandRegistry MapCommandAsync<T1, T2>(
        this CommandRegistry registry,
        string name,
        Func<ICommandContext, T1, T2, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var options = _jsonOptions;
        return registry.Register(name, async (instance, parameters, ctx) =>
        {
            var (a1, a2) = ParseArgs2<T1, T2>(parameters, options);
            await handler(ctx!, a1, a2).ConfigureAwait(false);
            return null;
        });
    }

    /// <summary>
    /// 注册无参数有返回值的异步命令。
    /// </summary>
    /// <typeparam name="TResult">返回值类型。</typeparam>
    /// <param name="registry">命令注册表。</param>
    /// <param name="name">命令名称。</param>
    /// <param name="handler">异步命令处理委托。</param>
    /// <returns>命令注册表，用于链式调用。</returns>
    public static CommandRegistry MapCommandAsync<TResult>(
        this CommandRegistry registry,
        string name,
        Func<Task<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return registry.Register(name, async (instance, parameters, ctx) =>
        {
            var result = await handler().ConfigureAwait(false);
            return result;
        });
    }

    /// <summary>
    /// 注册带参数有返回值的异步命令。
    /// </summary>
    /// <typeparam name="T">参数类型。</typeparam>
    /// <typeparam name="TResult">返回值类型。</typeparam>
    /// <param name="registry">命令注册表。</param>
    /// <param name="name">命令名称。</param>
    /// <param name="handler">异步命令处理委托。</param>
    /// <returns>命令注册表，用于链式调用。</returns>
    public static CommandRegistry MapCommandAsync<T, TResult>(
        this CommandRegistry registry,
        string name,
        Func<T, Task<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var options = _jsonOptions;
        return registry.Register(name, async (instance, parameters, ctx) =>
        {
            var arg = Deserialize<T>(parameters, options);
            var result = await handler(arg).ConfigureAwait(false);
            return result;
        });
    }

    /// <summary>
    /// 注册注入 <see cref="ICommandContext"/> 无返回值的异步命令。
    /// </summary>
    /// <param name="registry">命令注册表。</param>
    /// <param name="name">命令名称。</param>
    /// <param name="handler">异步命令处理委托。</param>
    /// <returns>命令注册表，用于链式调用。</returns>
    public static CommandRegistry MapCommandAsync(
        this CommandRegistry registry,
        string name,
        Func<ICommandContext, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return registry.Register(name, async (instance, parameters, ctx) =>
        {
            await handler(ctx!).ConfigureAwait(false);
            return null;
        });
    }

    /// <summary>
    /// 注册注入 <see cref="ICommandContext"/> 带返回值的异步命令。
    /// </summary>
    /// <typeparam name="TResult">返回值类型。</typeparam>
    /// <param name="registry">命令注册表。</param>
    /// <param name="name">命令名称。</param>
    /// <param name="handler">异步命令处理委托。</param>
    /// <returns>命令注册表，用于链式调用。</returns>
    public static CommandRegistry MapCommandAsync<TResult>(
        this CommandRegistry registry,
        string name,
        Func<ICommandContext, Task<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return registry.Register(name, async (instance, parameters, ctx) =>
        {
            var result = await handler(ctx!).ConfigureAwait(false);
            return result;
        });
    }

    /// <summary>
    /// 注册注入 <see cref="ICommandContext"/> 与单参数的有返回值异步命令。
    /// </summary>
    /// <typeparam name="T">业务参数类型。</typeparam>
    /// <typeparam name="TResult">返回值类型。</typeparam>
    /// <param name="registry">命令注册表。</param>
    /// <param name="name">命令名称。</param>
    /// <param name="handler">异步命令处理委托。</param>
    /// <returns>命令注册表，用于链式调用。</returns>
    public static CommandRegistry MapCommandAsync<T, TResult>(
        this CommandRegistry registry,
        string name,
        Func<ICommandContext, T, Task<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var options = _jsonOptions;
        return registry.Register(name, async (instance, parameters, ctx) =>
        {
            var arg = Deserialize<T>(parameters, options);
            var result = await handler(ctx!, arg).ConfigureAwait(false);
            return result;
        });
    }

    /// <summary>
    /// 注册注入 <see cref="ICommandContext"/> 与两个参数的有返回值异步命令。
    /// </summary>
    /// <typeparam name="T1">第一个参数类型。</typeparam>
    /// <typeparam name="T2">第二个参数类型。</typeparam>
    /// <typeparam name="TResult">返回值类型。</typeparam>
    /// <param name="registry">命令注册表。</param>
    /// <param name="name">命令名称。</param>
    /// <param name="handler">异步命令处理委托。</param>
    /// <returns>命令注册表，用于链式调用。</returns>
    public static CommandRegistry MapCommandAsync<T1, T2, TResult>(
        this CommandRegistry registry,
        string name,
        Func<ICommandContext, T1, T2, Task<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var options = _jsonOptions;
        return registry.Register(name, async (instance, parameters, ctx) =>
        {
            var (a1, a2) = ParseArgs2<T1, T2>(parameters, options);
            var result = await handler(ctx!, a1, a2).ConfigureAwait(false);
            return result;
        });
    }

    /// <summary>
    /// 注册两个业务参数无 <see cref="ICommandContext"/> 的有返回值异步命令。
    /// </summary>
    public static CommandRegistry MapCommandAsync<T1, T2, TResult>(
        this CommandRegistry registry,
        string name,
        Func<T1, T2, Task<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var options = _jsonOptions;
        return registry.Register(name, async (instance, parameters, ctx) =>
        {
            var (a1, a2) = ParseArgs2<T1, T2>(parameters, options);
            var result = await handler(a1, a2).ConfigureAwait(false);
            return result;
        });
    }

    /// <summary>
    /// 注册三个业务参数 + <see cref="ICommandContext"/> 的有返回值异步命令。
    /// </summary>
    public static CommandRegistry MapCommandAsync<T1, T2, T3, TResult>(
        this CommandRegistry registry,
        string name,
        Func<ICommandContext, T1, T2, T3, Task<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var options = _jsonOptions;
        return registry.Register(name, async (instance, parameters, ctx) =>
        {
            var (a1, a2, a3) = ParseArgs3<T1, T2, T3>(parameters, options);
            var result = await handler(ctx!, a1, a2, a3).ConfigureAwait(false);
            return result;
        });
    }

    /// <summary>
    /// 注册四个业务参数 + <see cref="ICommandContext"/> 的有返回值异步命令。
    /// </summary>
    public static CommandRegistry MapCommandAsync<T1, T2, T3, T4, TResult>(
        this CommandRegistry registry,
        string name,
        Func<ICommandContext, T1, T2, T3, T4, Task<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var options = _jsonOptions;
        return registry.Register(name, async (instance, parameters, ctx) =>
        {
            var (a1, a2, a3, a4) = ParseArgs4<T1, T2, T3, T4>(parameters, options);
            var result = await handler(ctx!, a1, a2, a3, a4).ConfigureAwait(false);
            return result;
        });
    }

    /// <summary>
    /// 注册七个业务参数 + <see cref="ICommandContext"/> 的有返回值异步命令。
    /// 适配 <c>sqlite.select</c> 等多参数查询场景。
    /// </summary>
    public static CommandRegistry MapCommandAsync<T1, T2, T3, T4, T5, T6, T7, TResult>(
        this CommandRegistry registry,
        string name,
        Func<ICommandContext, T1, T2, T3, T4, T5, T6, T7, Task<TResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var options = _jsonOptions;
        return registry.Register(name, async (instance, parameters, ctx) =>
        {
            var (a1, a2, a3, a4, a5, a6, a7) = ParseArgs7<T1, T2, T3, T4, T5, T6, T7>(parameters, options);
            var result = await handler(ctx!, a1, a2, a3, a4, a5, a6, a7).ConfigureAwait(false);
            return result;
        });
    }

    // ============================================================
    // 同步重载：扩展签名（覆盖插件场景）
    // ============================================================

    /// <summary>
    /// 注册三个业务参数 + <see cref="ICommandContext"/> 的无返回值命令。
    /// </summary>
    public static CommandRegistry MapCommand<T1, T2, T3>(
        this CommandRegistry registry,
        string name,
        Action<ICommandContext, T1, T2, T3> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var options = _jsonOptions;
        return registry.Register(name, (instance, parameters, ctx) =>
        {
            var (a1, a2, a3) = ParseArgs3<T1, T2, T3>(parameters, options);
            handler(ctx!, a1, a2, a3);
            return Task.FromResult<object?>(null);
        });
    }

    /// <summary>
    /// 注册三个业务参数无 <see cref="ICommandContext"/> 的无返回值命令。
    /// </summary>
    public static CommandRegistry MapCommand<T1, T2, T3>(
        this CommandRegistry registry,
        string name,
        Action<T1, T2, T3> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var options = _jsonOptions;
        return registry.Register(name, (instance, parameters, ctx) =>
        {
            var (a1, a2, a3) = ParseArgs3<T1, T2, T3>(parameters, options);
            handler(a1, a2, a3);
            return Task.FromResult<object?>(null);
        });
    }

    /// <summary>
    /// 注册两个业务参数 + <see cref="ICommandContext"/> 的有返回值同步命令。
    /// </summary>
    public static CommandRegistry MapCommand<T1, T2, TResult>(
        this CommandRegistry registry,
        string name,
        Func<ICommandContext, T1, T2, TResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var options = _jsonOptions;
        return registry.Register(name, (instance, parameters, ctx) =>
        {
            var (a1, a2) = ParseArgs2<T1, T2>(parameters, options);
            var result = handler(ctx!, a1, a2);
            return Task.FromResult<object?>(result);
        });
    }

    /// <summary>
    /// 注册三个业务参数 + <see cref="ICommandContext"/> 的有返回值同步命令。
    /// </summary>
    public static CommandRegistry MapCommand<T1, T2, T3, TResult>(
        this CommandRegistry registry,
        string name,
        Func<ICommandContext, T1, T2, T3, TResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var options = _jsonOptions;
        return registry.Register(name, (instance, parameters, ctx) =>
        {
            var (a1, a2, a3) = ParseArgs3<T1, T2, T3>(parameters, options);
            var result = handler(ctx!, a1, a2, a3);
            return Task.FromResult<object?>(result);
        });
    }

    /// <summary>
    /// 注册三个业务参数无 <see cref="ICommandContext"/> 的有返回值同步命令。
    /// </summary>
    public static CommandRegistry MapCommand<T1, T2, T3, TResult>(
        this CommandRegistry registry,
        string name,
        Func<T1, T2, T3, TResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var options = _jsonOptions;
        return registry.Register(name, (instance, parameters, ctx) =>
        {
            var (a1, a2, a3) = ParseArgs3<T1, T2, T3>(parameters, options);
            var result = handler(a1, a2, a3);
            return Task.FromResult<object?>(result);
        });
    }

    /// <summary>
    /// 注册两个业务参数无 <see cref="ICommandContext"/> 的有返回值同步命令。
    /// </summary>
    public static CommandRegistry MapCommand<T1, T2, TResult>(
        this CommandRegistry registry,
        string name,
        Func<T1, T2, TResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var options = _jsonOptions;
        return registry.Register(name, (instance, parameters, ctx) =>
        {
            var (a1, a2) = ParseArgs2<T1, T2>(parameters, options);
            var result = handler(a1, a2);
            return Task.FromResult<object?>(result);
        });
    }

    // ============================================================
    // 带能力声明的重载
    // ============================================================

    /// <summary>
    /// 注册无参数无返回值命令并指定所需能力列表。
    /// </summary>
    /// <param name="registry">命令注册表。</param>
    /// <param name="name">命令名称。</param>
    /// <param name="handler">命令处理委托。</param>
    /// <param name="requiredCapabilities">所需能力标识列表。</param>
    /// <returns>命令注册表，用于链式调用。</returns>
    public static CommandRegistry MapCommand(
        this CommandRegistry registry,
        string name,
        Action handler,
        params string[] requiredCapabilities)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return registry.Register(name, (instance, parameters, ctx) =>
        {
            handler();
            return Task.FromResult<object?>(null);
        }, requiredCapabilities);
    }

    /// <summary>
    /// 注册注入 <see cref="ICommandContext"/> 的无返回值命令并指定所需能力列表。
    /// </summary>
    /// <param name="registry">命令注册表。</param>
    /// <param name="name">命令名称。</param>
    /// <param name="handler">命令处理委托。</param>
    /// <param name="requiredCapabilities">所需能力标识列表。</param>
    /// <returns>命令注册表，用于链式调用。</returns>
    public static CommandRegistry MapCommand(
        this CommandRegistry registry,
        string name,
        Action<ICommandContext> handler,
        params string[] requiredCapabilities)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return registry.Register(name, (instance, parameters, ctx) =>
        {
            handler(ctx!);
            return Task.FromResult<object?>(null);
        }, requiredCapabilities);
    }

    /// <summary>
    /// 注册单参数无返回值命令并指定所需能力列表。
    /// </summary>
    /// <typeparam name="T">参数类型。</typeparam>
    /// <param name="registry">命令注册表。</param>
    /// <param name="name">命令名称。</param>
    /// <param name="handler">命令处理委托。</param>
    /// <param name="requiredCapabilities">所需能力标识列表。</param>
    /// <returns>命令注册表，用于链式调用。</returns>
    public static CommandRegistry MapCommand<T>(
        this CommandRegistry registry,
        string name,
        Action<T> handler,
        params string[] requiredCapabilities)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var options = _jsonOptions;
        return registry.Register(name, (instance, parameters, ctx) =>
        {
            var arg = Deserialize<T>(parameters, options);
            handler(arg);
            return Task.FromResult<object?>(null);
        }, requiredCapabilities);
    }

    /// <summary>
    /// 注册注入 <see cref="ICommandContext"/> 与单参数的无返回值命令并指定所需能力列表。
    /// </summary>
    /// <typeparam name="T">业务参数类型。</typeparam>
    /// <param name="registry">命令注册表。</param>
    /// <param name="name">命令名称。</param>
    /// <param name="handler">命令处理委托。</param>
    /// <param name="requiredCapabilities">所需能力标识列表。</param>
    /// <returns>命令注册表，用于链式调用。</returns>
    public static CommandRegistry MapCommand<T>(
        this CommandRegistry registry,
        string name,
        Action<ICommandContext, T> handler,
        params string[] requiredCapabilities)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var options = _jsonOptions;
        return registry.Register(name, (instance, parameters, ctx) =>
        {
            var arg = Deserialize<T>(parameters, options);
            handler(ctx!, arg);
            return Task.FromResult<object?>(null);
        }, requiredCapabilities);
    }

    /// <summary>
    /// 注册无参数有返回值命令并指定所需能力列表。
    /// </summary>
    /// <typeparam name="TResult">返回值类型。</typeparam>
    /// <param name="registry">命令注册表。</param>
    /// <param name="name">命令名称。</param>
    /// <param name="handler">命令处理委托。</param>
    /// <param name="requiredCapabilities">所需能力标识列表。</param>
    /// <returns>命令注册表，用于链式调用。</returns>
    public static CommandRegistry MapCommand<TResult>(
        this CommandRegistry registry,
        string name,
        Func<TResult> handler,
        params string[] requiredCapabilities)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return registry.Register(name, (instance, parameters, ctx) =>
        {
            var result = handler();
            return Task.FromResult<object?>(result);
        }, requiredCapabilities);
    }

    /// <summary>
    /// 注册注入 <see cref="ICommandContext"/> 的有返回值命令并指定所需能力列表。
    /// </summary>
    /// <typeparam name="TResult">返回值类型。</typeparam>
    /// <param name="registry">命令注册表。</param>
    /// <param name="name">命令名称。</param>
    /// <param name="handler">命令处理委托。</param>
    /// <param name="requiredCapabilities">所需能力标识列表。</param>
    /// <returns>命令注册表，用于链式调用。</returns>
    public static CommandRegistry MapCommand<TResult>(
        this CommandRegistry registry,
        string name,
        Func<ICommandContext, TResult> handler,
        params string[] requiredCapabilities)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return registry.Register(name, (instance, parameters, ctx) =>
        {
            var result = handler(ctx!);
            return Task.FromResult<object?>(result);
        }, requiredCapabilities);
    }

    // ============================================================
    // 内部参数解析辅助方法
    // ============================================================

    /// <summary>
    /// 反序列化单个参数。对 default JsonElement 返回 default(T)。
    /// </summary>
    /// <typeparam name="T">参数类型。</typeparam>
    /// <param name="parameters">前端传入的 JSON 元素。</param>
    /// <param name="options">JSON 序列化选项。</param>
    /// <returns>反序列化后的参数值；输入为 default 时返回 default(T)。</returns>
    private static T Deserialize<T>(JsonElement parameters, JsonSerializerOptions options)
    {
        if (parameters.ValueKind == JsonValueKind.Undefined || parameters.ValueKind == JsonValueKind.Null)
        {
            return default!;
        }

        return parameters.Deserialize<T>(options) ?? default!;
    }

    /// <summary>
    /// 解析两个参数。支持数组形式（多参数场景）或单元素 + 默认值。
    /// </summary>
    /// <typeparam name="T1">第一个参数类型。</typeparam>
    /// <typeparam name="T2">第二个参数类型。</typeparam>
    /// <param name="parameters">前端传入的 JSON 元素。</param>
    /// <param name="options">JSON 序列化选项。</param>
    /// <returns>解析后的二元组。</returns>
    private static (T1, T2) ParseArgs2<T1, T2>(JsonElement parameters, JsonSerializerOptions options)
    {
        if (parameters.ValueKind == JsonValueKind.Array && parameters.GetArrayLength() >= 2)
        {
            var a1 = parameters[0].Deserialize<T1>(options) ?? default!;
            var a2 = parameters[1].Deserialize<T2>(options) ?? default!;
            return (a1, a2);
        }

        // 降级：单参数场景下尝试整体反序列化为 T1，T2 用默认值
        if (parameters.ValueKind != JsonValueKind.Undefined && parameters.ValueKind != JsonValueKind.Null)
        {
            var a1 = parameters.Deserialize<T1>(options) ?? default!;
            return (a1, default!);
        }

        return default!;
    }

    /// <summary>
    /// 解析三个参数。要求参数以 JSON 数组形式提供。
    /// </summary>
    private static (T1, T2, T3) ParseArgs3<T1, T2, T3>(JsonElement parameters, JsonSerializerOptions options)
    {
        if (parameters.ValueKind == JsonValueKind.Array)
        {
            var length = parameters.GetArrayLength();
            var a1 = length > 0 ? parameters[0].Deserialize<T1>(options) ?? default! : default!;
            var a2 = length > 1 ? parameters[1].Deserialize<T2>(options) ?? default! : default!;
            var a3 = length > 2 ? parameters[2].Deserialize<T3>(options) ?? default! : default!;
            return (a1, a2, a3);
        }

        return default!;
    }

    /// <summary>
    /// 解析四个参数。要求参数以 JSON 数组形式提供。
    /// </summary>
    private static (T1, T2, T3, T4) ParseArgs4<T1, T2, T3, T4>(JsonElement parameters, JsonSerializerOptions options)
    {
        if (parameters.ValueKind == JsonValueKind.Array)
        {
            var length = parameters.GetArrayLength();
            var a1 = length > 0 ? parameters[0].Deserialize<T1>(options) ?? default! : default!;
            var a2 = length > 1 ? parameters[1].Deserialize<T2>(options) ?? default! : default!;
            var a3 = length > 2 ? parameters[2].Deserialize<T3>(options) ?? default! : default!;
            var a4 = length > 3 ? parameters[3].Deserialize<T4>(options) ?? default! : default!;
            return (a1, a2, a3, a4);
        }

        return default!;
    }

    /// <summary>
    /// 解析七个参数。要求参数以 JSON 数组形式提供。
    /// 适配 <c>sqlite.select</c> 等多参数查询场景。
    /// </summary>
    private static (T1, T2, T3, T4, T5, T6, T7) ParseArgs7<T1, T2, T3, T4, T5, T6, T7>(
        JsonElement parameters,
        JsonSerializerOptions options)
    {
        if (parameters.ValueKind == JsonValueKind.Array)
        {
            var length = parameters.GetArrayLength();
            var a1 = length > 0 ? parameters[0].Deserialize<T1>(options) ?? default! : default!;
            var a2 = length > 1 ? parameters[1].Deserialize<T2>(options) ?? default! : default!;
            var a3 = length > 2 ? parameters[2].Deserialize<T3>(options) ?? default! : default!;
            var a4 = length > 3 ? parameters[3].Deserialize<T4>(options) ?? default! : default!;
            var a5 = length > 4 ? parameters[4].Deserialize<T5>(options) ?? default! : default!;
            var a6 = length > 5 ? parameters[5].Deserialize<T6>(options) ?? default! : default!;
            var a7 = length > 6 ? parameters[6].Deserialize<T7>(options) ?? default! : default!;
            return (a1, a2, a3, a4, a5, a6, a7);
        }

        return default!;
    }
}
