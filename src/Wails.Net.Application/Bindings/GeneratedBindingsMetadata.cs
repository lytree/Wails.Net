namespace Wails.Net.Application.Bindings;

/// <summary>
/// 绑定方法的参数元数据，由源代码生成器在编译期填充。
/// 对应 <see cref="Wails.Net.Generator.Models.ParameterModel"/>，但仅承载运行时所需的最少信息。
/// </summary>
/// <param name="Name">参数名称。</param>
/// <param name="TypeName">参数的 TypeScript 类型字符串（由源生成器映射）。</param>
/// <param name="IsVariadic">是否为可变参数（params 关键字）。</param>
/// <param name="IsCancellationToken">是否为 CancellationToken（自动注入，不暴露给前端）。</param>
public sealed record BoundParameterInfo(
    string Name,
    string TypeName,
    bool IsVariadic,
    bool IsCancellationToken);

/// <summary>
/// 绑定方法的元数据，由源代码生成器在编译期填充。
/// 替代运行时反射 <see cref="System.Reflection.MethodInfo"/> 提供的信息，
/// 供 <c>Wails.Net.Generator.BindingAnalyzer</c> 和 <c>TypeScriptGenerator</c> 使用。
/// </summary>
/// <param name="FullName">方法全限定名（Namespace.ClassName.MethodName）。</param>
/// <param name="Id">FNV-1a 32 位哈希 ID，与运行时 <see cref="BindingManager.FNV1aHash"/> 一致。</param>
/// <param name="Namespace">方法所属类型的命名空间。</param>
/// <param name="ClassName">方法所属类型名称。</param>
/// <param name="MethodName">方法名。</param>
/// <param name="Parameters">参数列表。</param>
/// <param name="ReturnTypeName">方法返回类型的 TypeScript 类型字符串。</param>
/// <param name="IsAsync">是否为异步方法（返回 Task 或 Task&lt;T&gt;）。</param>
/// <param name="IsCommand">是否为命令方法（标记了 [Command] 特性，命令名与 <paramref name="FullName"/> 相同）。</param>
public sealed record BoundMethodInfo(
    string FullName,
    uint Id,
    string Namespace,
    string ClassName,
    string MethodName,
    IReadOnlyList<BoundParameterInfo> Parameters,
    string ReturnTypeName,
    bool IsAsync,
    bool IsCommand);

/// <summary>
/// 编译时生成的绑定元数据注册表。
/// 源代码生成器 <c>Wails.Net.SourceGenerators.BindingSourceGenerator</c> 在编译期生成代码，
/// 通过 <see cref="Register(BoundMethodInfo)"/> 在模块加载时填充 <see cref="Methods"/> 列表。
/// </summary>
/// <remarks>
/// 此注册表替代了原 <c>BindingAnalyzer</c> 中基于 <see cref="System.Reflection"/> 的运行时分析方法，
/// 消除了反射开销并支持 AOT 裁剪。
/// </remarks>
public static class GeneratedBindingsMetadata
{
    /// <summary>
    /// 已注册的绑定方法元数据列表。
    /// 由源生成器生成的 <c>[ModuleInitializer]</c> 方法在模块加载时填充。
    /// </summary>
    private static readonly List<BoundMethodInfo> _methods = new();

    /// <summary>
    /// 获取已注册的所有绑定方法元数据的只读视图。
    /// </summary>
    public static IReadOnlyList<BoundMethodInfo> Methods => _methods;

    /// <summary>
    /// 注册一个绑定方法元数据。
    /// 通常由源生成器生成的代码在 <c>[ModuleInitializer]</c> 中调用。
    /// </summary>
    /// <param name="method">要注册的方法元数据。</param>
    public static void Register(BoundMethodInfo method) => _methods.Add(method);

    /// <summary>
    /// 清除所有已注册的方法元数据（仅用于测试）。
    /// </summary>
    internal static void Clear() => _methods.Clear();
}
