using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace Wails.Net.Application.Commands;

/// <summary>
/// 命令注册表，存储命令名到调用器的映射。
/// 作为 <see cref="Bindings.BindingManager"/> 的现代替代方案，
/// 提供类似 ASP.NET Core Minimal API 的命令注册方式。
/// <para>
/// 注册时直接接收 <see cref="CompiledCommandInvoker"/> 委托（由 <see cref="MapCommandExtensions"/>
/// 在编译期通过强类型泛型重载构建），运行时零反射调用，遵循 AGENTS.md §3.4 禁令。
/// </para>
/// </summary>
public sealed class CommandRegistry
{
    /// <summary>
    /// 命令名到条目的并发字典。
    /// </summary>
    private readonly ConcurrentDictionary<string, CommandEntry> _commands = new();

    /// <summary>
    /// 已注册的命令数量。
    /// </summary>
    public int Count => _commands.Count;

    /// <summary>
    /// 注册命令。
    /// </summary>
    /// <param name="name">命令名称。</param>
    /// <param name="invoker">编译期构建的强类型调用器委托（由 <see cref="MapCommandExtensions"/> 创建）。</param>
    /// <returns>当前注册表实例，用于链式调用。</returns>
    public CommandRegistry Register(string name, CompiledCommandInvoker invoker)
    {
        ArgumentNullException.ThrowIfNull(invoker);
        _commands[name] = new CommandEntry(name, Instance: null, invoker, RequiredCapabilities: []);
        return this;
    }

    /// <summary>
    /// 注册命令并指定所需能力列表。
    /// 调度时 <see cref="CommandDispatcher"/> 会校验所有所需能力是否已授权。
    /// </summary>
    /// <param name="name">命令名称。</param>
    /// <param name="invoker">编译期构建的强类型调用器委托（由 <see cref="MapCommandExtensions"/> 创建）。</param>
    /// <param name="requiredCapabilities">所需能力标识列表（如 <c>fs:allow-read</c>）。</param>
    /// <returns>当前注册表实例，用于链式调用。</returns>
    public CommandRegistry Register(string name, CompiledCommandInvoker invoker, params string[] requiredCapabilities)
    {
        ArgumentNullException.ThrowIfNull(invoker);
        _commands[name] = new CommandEntry(name, Instance: null, invoker, requiredCapabilities);
        return this;
    }

    /// <summary>
    /// 注册命令容器（标记了 <see cref="DesktopCommandAttribute"/> 的类）。
    /// </summary>
    /// <param name="instance">命令容器实例。</param>
    /// <remarks>
    /// 此方法已弃用并禁止使用。原实现依赖运行时反射枚举类型方法（<c>Type.GetMethods</c>），
    /// 违反 AGENTS.md §3.4 "禁止使用反射获取对应方法" 的禁令。
    /// 请改用 <see cref="CommandAttribute"/> 配合 <c>Wails.Net.SourceGenerators.BindingSourceGenerator</c>，
    /// 在编译期通过 <c>[ModuleInitializer]</c> 调用
    /// <see cref="Bindings.GeneratedBindingRegistry.Register"/> 注册强类型调用器。
    /// </remarks>
    /// <exception cref="NotSupportedException">始终抛出。原反射实现已移除，遵循 AGENTS.md §3.4 禁令。</exception>
    [Obsolete("此方法已弃用并禁止使用。原反射实现已移除（遵循 AGENTS.md §3.4）。请使用 [Command] 特性配合源生成器在编译期注册。")]
    public void RegisterCommands(object instance)
    {
        throw new NotSupportedException(
            "RegisterCommands 已弃用并禁止使用。原反射实现已移除（遵循 AGENTS.md §3.4 禁令）。" +
            "请使用 [Command] 特性配合 Wails.Net.SourceGenerators.BindingSourceGenerator 在编译期注册调用器。");
    }

    /// <summary>
    /// 从程序集扫描并注册所有标记 <see cref="DesktopCommandAttribute"/> 的类。
    /// 实例通过 <see cref="ActivatorUtilities"/> 创建，支持构造函数依赖注入。
    /// </summary>
    /// <param name="assembly">已弃用参数，仅用于向后兼容签名。</param>
    /// <param name="services">DI 服务容器，用于创建实例。</param>
    /// <remarks>
    /// 此方法已弃用并禁止使用。原实现依赖运行时反射扫描程序集类型（<c>Assembly.GetTypes</c>），
    /// 违反 AGENTS.md §3.4 "禁止使用反射获取对应方法" 的禁令。
    /// 请改用 <see cref="CommandAttribute"/> 配合源生成器在编译期注册。
    /// </remarks>
    /// <exception cref="NotSupportedException">始终抛出。原反射实现已移除，遵循 AGENTS.md §3.4 禁令。</exception>
    [Obsolete("此方法已弃用并禁止使用。原反射实现已移除（遵循 AGENTS.md §3.4）。请使用 [Command] 特性配合源生成器在编译期注册。")]
    public void RegisterFromAssembly(object? assembly, IServiceProvider services)
    {
        throw new NotSupportedException(
            "RegisterFromAssembly 已弃用并禁止使用。原反射实现已移除（遵循 AGENTS.md §3.4 禁令）。" +
            "请使用 [Command] 特性配合 Wails.Net.SourceGenerators.BindingSourceGenerator 在编译期注册调用器。");
    }

    /// <summary>
    /// 查找命令。
    /// </summary>
    /// <param name="name">命令名称。</param>
    /// <returns>命令条目，若未找到则返回 null。</returns>
    public CommandEntry? Find(string name)
    {
        return _commands.TryGetValue(name, out var entry) ? entry : null;
    }

    /// <summary>
    /// 获取所有已注册的命令名。
    /// </summary>
    /// <returns>命令名集合。</returns>
    public IEnumerable<string> GetCommandNames() => _commands.Keys;

    /// <summary>
    /// 命令条目，记录命令名、调用器委托和所需能力列表。
    /// <see cref="Invoker"/> 为编译期构建的强类型委托（由 <see cref="MapCommandExtensions"/> 闭包捕获目标实例），
    /// 运行时零反射调用。
    /// <see cref="RequiredCapabilities"/> 记录命令所需的能力标识列表，
    /// 调度时由 <see cref="CommandDispatcher"/> 校验是否已授权。
    /// </summary>
    /// <param name="Name">命令名称。</param>
    /// <param name="Instance">命令所属实例。为 null 表示委托已闭包捕获目标实例（默认场景）。</param>
    /// <param name="Invoker">编译期构建的强类型调用器委托。</param>
    /// <param name="RequiredCapabilities">所需能力标识列表（如 <c>fs:allow-read</c>），为空表示无特殊要求。</param>
    public sealed record CommandEntry(
        string Name,
        object? Instance,
        CompiledCommandInvoker? Invoker,
        IReadOnlyList<string> RequiredCapabilities);
}
