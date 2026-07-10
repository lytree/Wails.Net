using System.CommandLine;
using System.CommandLine.Invocation;

namespace Wails.Net.Cli.Commands;

/// <summary>
/// 异步命令行动作辅助类，将委托包装为 <see cref="AsynchronousCommandLineAction"/>。
/// 用于 System.CommandLine 2.0.0-beta5+ 的新 API（使用 Action 属性而非 SetHandler）。
/// </summary>
internal sealed class AsyncAction : AsynchronousCommandLineAction
{
    /// <summary>包装的异步委托。</summary>
    private readonly Func<ParseResult, CancellationToken, Task<int>> _handler;

    /// <summary>
    /// 使用指定委托构造实例。
    /// </summary>
    /// <param name="handler">异步处理委托，返回退出码。</param>
    private AsyncAction(Func<ParseResult, CancellationToken, Task<int>> handler)
    {
        _handler = handler;
    }

    /// <summary>
    /// 从委托创建 <see cref="AsyncAction"/> 实例。
    /// </summary>
    /// <param name="handler">异步处理委托。</param>
    /// <returns>命令行动作实例。</returns>
    public static AsyncAction Create(Func<ParseResult, CancellationToken, Task<int>> handler) =>
        new(handler);

    /// <summary>
    /// 从简单委托创建 <see cref="AsyncAction"/> 实例（忽略 ParseResult 和 CancellationToken）。
    /// </summary>
    /// <param name="handler">异步处理委托。</param>
    /// <returns>命令行动作实例。</returns>
    public static AsyncAction Create(Func<Task<int>> handler) =>
        new((_, _) => handler());

    /// <inheritdoc/>
    public override Task<int> InvokeAsync(ParseResult parseResult, CancellationToken cancellationToken = default)
        => _handler(parseResult, cancellationToken);
}
