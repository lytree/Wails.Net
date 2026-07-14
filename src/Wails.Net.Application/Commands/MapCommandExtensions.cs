namespace Wails.Net.Application.Commands;

/// <summary>
/// MapCommand 扩展方法，提供 Minimal API 风格的命令注册。
/// </summary>
public static class MapCommandExtensions
{
    /// <summary>
    /// 注册委托为命令。
    /// </summary>
    /// <param name="registry">命令注册表。</param>
    /// <param name="name">命令名称。</param>
    /// <param name="handler">命令处理委托。</param>
    /// <returns>命令注册表，用于链式调用。</returns>
    public static CommandRegistry MapCommand(this CommandRegistry registry, string name, Delegate handler)
    {
        var method = handler.Method;
        var target = handler.Target;
        registry.Register(name, target!, method);
        return registry;
    }

    /// <summary>
    /// 注册委托为命令，并指定所需能力列表。
    /// 调度时 <see cref="CommandDispatcher"/> 会校验所有所需能力是否已授权。
    /// 对应 Tauri v2 的命令权限标记：每个命令可声明所需权限（如 <c>fs:allow-read</c>）。
    /// </summary>
    /// <param name="registry">命令注册表。</param>
    /// <param name="name">命令名称。</param>
    /// <param name="handler">命令处理委托。</param>
    /// <param name="requiredCapabilities">所需能力标识列表（如 <c>fs:allow-read</c>）。</param>
    /// <returns>命令注册表，用于链式调用。</returns>
    public static CommandRegistry MapCommand(
        this CommandRegistry registry,
        string name,
        Delegate handler,
        params string[] requiredCapabilities)
    {
        var method = handler.Method;
        var target = handler.Target;
        registry.Register(name, target!, method, requiredCapabilities);
        return registry;
    }

    /// <summary>
    /// 注册 <see cref="Action"/> 为命令。
    /// </summary>
    /// <param name="registry">命令注册表。</param>
    /// <param name="name">命令名称。</param>
    /// <param name="handler">无参数无返回值的命令处理委托。</param>
    /// <returns>命令注册表，用于链式调用。</returns>
    public static CommandRegistry MapCommand(this CommandRegistry registry, string name, Action handler)
    {
        return registry.MapCommand(name, (Delegate)handler);
    }

    /// <summary>
    /// 注册 <see cref="Func{TResult}"/> 为命令。
    /// </summary>
    /// <typeparam name="TResult">返回值类型。</typeparam>
    /// <param name="registry">命令注册表。</param>
    /// <param name="name">命令名称。</param>
    /// <param name="handler">无参数有返回值的命令处理委托。</param>
    /// <returns>命令注册表，用于链式调用。</returns>
    public static CommandRegistry MapCommand<TResult>(this CommandRegistry registry, string name, Func<TResult> handler)
    {
        return registry.MapCommand(name, (Delegate)handler);
    }

    /// <summary>
    /// 注册 <see cref="Func{T, TResult}"/> 为命令。
    /// </summary>
    /// <typeparam name="T">参数类型。</typeparam>
    /// <typeparam name="TResult">返回值类型。</typeparam>
    /// <param name="registry">命令注册表。</param>
    /// <param name="name">命令名称。</param>
    /// <param name="handler">带一个参数和返回值的命令处理委托。</param>
    /// <returns>命令注册表，用于链式调用。</returns>
    public static CommandRegistry MapCommand<T, TResult>(this CommandRegistry registry, string name, Func<T, TResult> handler)
    {
        return registry.MapCommand(name, (Delegate)handler);
    }

    /// <summary>
    /// 注册异步命令（无参数无返回值）。
    /// </summary>
    /// <param name="registry">命令注册表。</param>
    /// <param name="name">命令名称。</param>
    /// <param name="handler">异步命令处理委托。</param>
    /// <returns>命令注册表，用于链式调用。</returns>
    public static CommandRegistry MapCommandAsync(this CommandRegistry registry, string name, Func<Task> handler)
    {
        return registry.MapCommand(name, (Delegate)handler);
    }

    /// <summary>
    /// 注册异步命令（带参数无返回值）。
    /// </summary>
    /// <typeparam name="T">参数类型。</typeparam>
    /// <param name="registry">命令注册表。</param>
    /// <param name="name">命令名称。</param>
    /// <param name="handler">带一个参数的异步命令处理委托。</param>
    /// <returns>命令注册表，用于链式调用。</returns>
    public static CommandRegistry MapCommandAsync<T>(this CommandRegistry registry, string name, Func<T, Task> handler)
    {
        return registry.MapCommand(name, (Delegate)handler);
    }

    /// <summary>
    /// 注册异步命令（无参数有返回值）。
    /// </summary>
    /// <typeparam name="TResult">返回值类型。</typeparam>
    /// <param name="registry">命令注册表。</param>
    /// <param name="name">命令名称。</param>
    /// <param name="handler">带返回值的异步命令处理委托。</param>
    /// <returns>命令注册表，用于链式调用。</returns>
    public static CommandRegistry MapCommandAsync<TResult>(this CommandRegistry registry, string name, Func<Task<TResult>> handler)
    {
        return registry.MapCommand(name, (Delegate)handler);
    }
}
