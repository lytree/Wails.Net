using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Wails.Net.Application.Commands;

/// <summary>
/// 命令注册表，存储命令名到方法信息的映射。
/// 作为 <see cref="Bindings.BindingManager"/> 的现代替代方案，
/// 提供类似 ASP.NET Core Minimal API 的命令注册方式。
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
    /// 在注册时通过 <see cref="CommandInvokerCompiler.Compile"/> 编译表达式树，
    /// 生成强类型调用器，运行时零反射调用。
    /// </summary>
    /// <param name="name">命令名称。</param>
    /// <param name="instance">命令所属实例。</param>
    /// <param name="method">命令方法反射信息。</param>
    public void Register(string name, object instance, MethodInfo method)
    {
        var invoker = CommandInvokerCompiler.Compile(method);
        _commands[name] = new CommandEntry(name, instance, method, invoker, []);
    }

    /// <summary>
    /// 注册命令并指定所需能力列表。
    /// 在注册时通过 <see cref="CommandInvokerCompiler.Compile"/> 编译表达式树，
    /// 生成强类型调用器，运行时零反射调用。
    /// 调度时 <see cref="CommandDispatcher"/> 会校验所有所需能力是否已授权。
    /// </summary>
    /// <param name="name">命令名称。</param>
    /// <param name="instance">命令所属实例。</param>
    /// <param name="method">命令方法反射信息。</param>
    /// <param name="requiredCapabilities">所需能力标识列表（如 <c>fs:allow-read</c>）。</param>
    public void Register(string name, object instance, MethodInfo method, params string[] requiredCapabilities)
    {
        var invoker = CommandInvokerCompiler.Compile(method);
        _commands[name] = new CommandEntry(name, instance, method, invoker, requiredCapabilities);
    }

    /// <summary>
    /// 注册命令容器（标记了 <see cref="DesktopCommandAttribute"/> 的类）。
    /// 标记了 <see cref="CommandAttribute"/> 的方法按指定名称注册；
    /// 未标记的公共非 void 方法按 "类名.方法名" 的小写形式注册。
    /// </summary>
    /// <param name="instance">命令容器实例。</param>
    public void RegisterCommands(object instance)
    {
        var type = instance.GetType();
        var hasAttr = type.GetCustomAttribute<DesktopCommandAttribute>() != null;

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            var cmdAttr = method.GetCustomAttribute<CommandAttribute>();
            if (cmdAttr != null)
            {
                Register(cmdAttr.Name, instance, method);
            }
            else if (hasAttr && !method.IsSpecialName && method.ReturnType != typeof(void))
            {
                // 如果类标记了 [DesktopCommand]，未标记 [Command] 的公共方法也自动注册
                // 命令名为 "类名.方法名" 的小写形式
                var name = $"{type.Name}.{method.Name}".ToLowerInvariant();
                Register(name, instance, method);
            }
        }
    }

    /// <summary>
    /// 从程序集扫描并注册所有标记 <see cref="DesktopCommandAttribute"/> 的类。
    /// 实例通过 <see cref="ActivatorUtilities"/> 创建，支持构造函数依赖注入。
    /// </summary>
    /// <param name="assembly">要扫描的程序集。</param>
    /// <param name="services">DI 服务容器，用于创建实例。</param>
    public void RegisterFromAssembly(Assembly assembly, IServiceProvider services)
    {
        var commandTypes = assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<DesktopCommandAttribute>() != null && !t.IsAbstract && !t.IsInterface);

        foreach (var type in commandTypes)
        {
            var instance = ActivatorUtilities.CreateInstance(services, type);
            RegisterCommands(instance);
        }
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
    /// 命令条目，记录命令名、实例、方法信息和编译后的调用器。
    /// <see cref="Invoker"/> 为表达式树编译的强类型委托，运行时零反射调用；
    /// <see cref="Method"/> 保留用于权限校验等元数据查询。
    /// <see cref="RequiredCapabilities"/> 记录命令所需的能力标识列表，
    /// 调度时由 <see cref="CommandDispatcher"/> 校验是否已授权。
    /// </summary>
    /// <param name="Name">命令名称。</param>
    /// <param name="Instance">命令所属实例。</param>
    /// <param name="Method">命令方法反射信息（仅用于权限校验等，不用于调用）。</param>
    /// <param name="Invoker">编译后的强类型调用器，可为 null（编译失败时回退到反射）。</param>
    /// <param name="RequiredCapabilities">所需能力标识列表（如 <c>fs:allow-read</c>），为空表示无特殊要求。</param>
    public sealed record CommandEntry(
        string Name,
        object Instance,
        MethodInfo Method,
        CompiledCommandInvoker? Invoker,
        IReadOnlyList<string> RequiredCapabilities);
}
