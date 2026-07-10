namespace Wails.Net.Generator.Models;

/// <summary>
/// 绑定方法模型，表示一个可暴露给前端的方法的元数据。
/// 对应 Wails v3 Go 版本 internal/generator/analyse.go 中的方法分析结果。
/// </summary>
public sealed class BoundMethodModel
{
    /// <summary>
    /// 方法的全限定名（Namespace.ClassName.MethodName）。
    /// </summary>
    public string FullName { get; }

    /// <summary>
    /// 方法的 FNV-1a 32 位哈希 ID（与运行时一致）。
    /// </summary>
    public uint ID { get; }

    /// <summary>
    /// 方法所属类型的命名空间。
    /// </summary>
    public string Namespace { get; }

    /// <summary>
    /// 方法所属类型的名称。
    /// </summary>
    public string ClassName { get; }

    /// <summary>
    /// 方法名称。
    /// </summary>
    public string MethodName { get; }

    /// <summary>
    /// 方法参数列表。
    /// </summary>
    public IReadOnlyList<ParameterModel> Parameters { get; }

    /// <summary>
    /// 方法返回类型的 TypeScript 类型字符串。
    /// </summary>
    public string ReturnTypeName { get; }

    /// <summary>
    /// 方法是否为异步（返回 Task 或 Task&lt;T&gt;）。
    /// </summary>
    public bool IsAsync { get; }

    /// <summary>
    /// 使用指定参数构造 BoundMethodModel 实例。
    /// </summary>
    /// <param name="fullName">方法全限定名。</param>
    /// <param name="id">FNV-1a 哈希 ID。</param>
    /// <param name="namespace">命名空间。</param>
    /// <param name="className">类名。</param>
    /// <param name="methodName">方法名。</param>
    /// <param name="parameters">参数列表。</param>
    /// <param name="returnTypeName">返回类型的 TypeScript 类型。</param>
    /// <param name="isAsync">是否为异步方法。</param>
    public BoundMethodModel(
        string fullName,
        uint id,
        string @namespace,
        string className,
        string methodName,
        IReadOnlyList<ParameterModel> parameters,
        string returnTypeName,
        bool isAsync)
    {
        FullName = fullName;
        ID = id;
        Namespace = @namespace;
        ClassName = className;
        MethodName = methodName;
        Parameters = parameters;
        ReturnTypeName = returnTypeName;
        IsAsync = isAsync;
    }
}

/// <summary>
/// 方法参数模型。
/// </summary>
public sealed class ParameterModel
{
    /// <summary>
    /// 参数名称。
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 参数的 TypeScript 类型字符串。
    /// </summary>
    public string TypeName { get; }

    /// <summary>
    /// 参数是否为可变参数（params 关键字）的一部分。
    /// </summary>
    public bool IsVariadic { get; }

    /// <summary>
    /// 参数是否为 CancellationToken（自动注入，不暴露给前端）。
    /// </summary>
    public bool IsCancellationToken { get; }

    /// <summary>
    /// 使用指定参数构造 ParameterModel 实例。
    /// </summary>
    /// <param name="name">参数名称。</param>
    /// <param name="typeName">TypeScript 类型。</param>
    /// <param name="isVariadic">是否为可变参数。</param>
    /// <param name="isCancellationToken">是否为 CancellationToken。</param>
    public ParameterModel(string name, string typeName, bool isVariadic, bool isCancellationToken)
    {
        Name = name;
        TypeName = typeName;
        IsVariadic = isVariadic;
        IsCancellationToken = isCancellationToken;
    }
}
