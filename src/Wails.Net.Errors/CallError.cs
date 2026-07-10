namespace Wails.Net.Errors;

/// <summary>
/// 表示绑定调用失败的结构化错误。
/// 对应 Wails v3 Go 版本 bindings.go 中的 CallError 类型，
/// 用于将调用错误信息序列化为 JSON 并返回给前端。
/// </summary>
public class CallError
{
    /// <summary>
    /// 获取错误消息。
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// 获取导致此错误的原始错误消息（可为空）。
    /// </summary>
    public string? Cause { get; }

    /// <summary>
    /// 获取错误类型。
    /// </summary>
    public CallErrorKind Kind { get; }

    /// <summary>
    /// 使用消息、原因和错误类型初始化 <see cref="CallError"/> 类的新实例。
    /// </summary>
    /// <param name="message">错误消息。</param>
    /// <param name="cause">导致此错误的原始错误消息。</param>
    /// <param name="kind">错误类型。</param>
    public CallError(string message, string? cause, CallErrorKind kind)
    {
        Message = message;
        Cause = cause;
        Kind = kind;
    }

    /// <summary>
    /// 返回包含所有字段的字符串表示形式。
    /// </summary>
    /// <returns>包含消息、原因和错误类型的字符串。</returns>
    public override string ToString()
    {
        var cause = Cause ?? "(none)";
        return $"Message: {Message}, Cause: {cause}, Kind: {Kind}";
    }

    /// <summary>
    /// 将此错误转换为可用于 JSON 序列化的字典。
    /// 字段名使用 camelCase 以符合前端 JSON 约定。
    /// </summary>
    /// <returns>包含 message、cause 和 kind 字段的字典。</returns>
    public Dictionary<string, object?> ToJson()
    {
        return new Dictionary<string, object?>
        {
            ["message"] = Message,
            ["cause"] = Cause,
            ["kind"] = Kind.ToString()
        };
    }
}

/// <summary>
/// 定义绑定调用错误的类型。
/// 对应 JavaScript 运行时的错误类型。
/// </summary>
public enum CallErrorKind
{
    /// <summary>
    /// 引用错误：访问未定义的变量或方法。
    /// </summary>
    ReferenceError = 0,

    /// <summary>
    /// 类型错误：参数类型不匹配或方法签名错误。
    /// </summary>
    TypeError = 1,

    /// <summary>
    /// 运行时错误：执行过程中发生的其他错误。
    /// </summary>
    RuntimeError = 2
}
