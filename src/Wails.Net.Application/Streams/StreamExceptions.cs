namespace Wails.Net.Application.Streams;

/// <summary>
/// 连接已关闭时由 <see cref="StreamConn.SendAsync"/> / <see cref="StreamConn.ReceiveAsync"/>
/// 抛出的异常。对应 Wails v3 Go 版本 <c>v3/pkg/application/stream.go</c> 中的 <c>ErrStreamClosed</c>：
/// 页面导航/重载、窗口关闭、前端调用 <c>close()</c> 或应用关闭后即触发。
/// </summary>
public sealed class StreamClosedException : Exception
{
    /// <summary>构造连接已关闭异常。</summary>
    public StreamClosedException()
        : base("wails: stream connection closed")
    {
    }

    /// <summary>以指定消息构造连接已关闭异常。</summary>
    /// <param name="message">异常消息。</param>
    public StreamClosedException(string message)
        : base(message)
    {
    }

    /// <summary>以指定消息与内部异常构造连接已关闭异常。</summary>
    /// <param name="message">异常消息。</param>
    /// <param name="innerException">内部异常。</param>
    public StreamClosedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// 单帧超过传输层最大线宽时由 <see cref="StreamConn.SendAsync"/> / <see cref="StreamConn.TrySend"/>
/// 抛出的异常。对应 Wails v3 Go 版本 <c>v3/pkg/application/stream.go</c> 中的 <c>ErrStreamTooLarge</c>：
/// 上限 <see cref="StreamConstants.MaxFrameBytes"/>（64 MiB）在两个方向、两种传输上一致。
/// </summary>
public sealed class StreamTooLargeException : Exception
{
    /// <summary>构造帧过大异常。</summary>
    public StreamTooLargeException()
        : base("wails: stream frame too large")
    {
    }

    /// <summary>以指定消息构造帧过大异常。</summary>
    /// <param name="message">异常消息。</param>
    public StreamTooLargeException(string message)
        : base(message)
    {
    }

    /// <summary>以指定消息与内部异常构造帧过大异常。</summary>
    /// <param name="message">异常消息。</param>
    /// <param name="innerException">内部异常。</param>
    public StreamTooLargeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
