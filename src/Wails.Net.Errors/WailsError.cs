namespace Wails.Net.Errors;

/// <summary>
/// Wails.Net 框架所有异常的基类。
/// 提供结构化的错误代码和可选的原因异常信息。
/// 对应 Wails v3 Go 版本 pkg/errs 包中的错误类型。
/// </summary>
public class WailsError : Exception
{
    /// <summary>
    /// 获取与此错误关联的错误代码。
    /// </summary>
    public ErrorCodes ErrorCode { get; }

    /// <summary>
    /// 获取导致此错误的可选原始异常。
    /// </summary>
    public Exception? Cause { get; }

    /// <summary>
    /// 使用指定的错误消息初始化 <see cref="WailsError"/> 类的新实例。
    /// 错误代码默认为 <see cref="ErrorCodes.Unknown"/>。
    /// </summary>
    /// <param name="message">描述错误的消息。</param>
    public WailsError(string message) : this(ErrorCodes.Unknown, message, null)
    {
    }

    /// <summary>
    /// 使用错误消息和内部异常初始化 <see cref="WailsError"/> 类的新实例。
    /// 错误代码默认为 <see cref="ErrorCodes.Unknown"/>。
    /// </summary>
    /// <param name="message">描述错误的消息。</param>
    /// <param name="innerException">导致当前异常的异常。</param>
    public WailsError(string message, Exception? innerException) : this(ErrorCodes.Unknown, message, innerException)
    {
    }

    /// <summary>
    /// 使用错误代码和错误消息初始化 <see cref="WailsError"/> 类的新实例。
    /// </summary>
    /// <param name="errorCode">与此错误关联的错误代码。</param>
    /// <param name="message">描述错误的消息。</param>
    public WailsError(ErrorCodes errorCode, string message) : this(errorCode, message, null)
    {
    }

    /// <summary>
    /// 使用错误代码、错误消息和内部异常初始化 <see cref="WailsError"/> 类的新实例。
    /// </summary>
    /// <param name="errorCode">与此错误关联的错误代码。</param>
    /// <param name="message">描述错误的消息。</param>
    /// <param name="innerException">导致当前异常的异常。</param>
    public WailsError(ErrorCodes errorCode, string message, Exception? innerException) : base(message, innerException)
    {
        ErrorCode = errorCode;
        Cause = innerException;
    }
}
