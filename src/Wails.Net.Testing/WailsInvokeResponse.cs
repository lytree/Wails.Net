using System.Text.Json;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Transport;
using Wails.Net.Errors;

namespace Wails.Net.Testing;

/// <summary>
/// 结构化描述一次绑定调用失败的错误信息（对应前端运行时的 CallError）。
/// </summary>
public sealed class CallErrorInfo
{
    /// <summary>
    /// 构造调用错误信息。
    /// </summary>
    /// <param name="message">错误消息。</param>
    /// <param name="cause">导致此错误的原始原因，可为 null。</param>
    /// <param name="kind">错误类型字符串（<see cref="CallErrorKind"/> 的枚举名）。</param>
    public CallErrorInfo(string message, string? cause, string kind)
    {
        Message = message;
        Cause = cause;
        Kind = kind;
    }

    /// <summary>错误消息。</summary>
    public string Message { get; }

    /// <summary>导致此错误的原始原因，可为 null。</summary>
    public string? Cause { get; }

    /// <summary>错误类型（<see cref="CallErrorKind"/> 的枚举名，如 RuntimeError）。</summary>
    public string Kind { get; }

    /// <inheritdoc />
    public override string ToString() => $"{Kind}: {Message}";
}

/// <summary>
/// 调用 <see cref="WailsTestHost.InvokeResponseAsync"/> 返回的结构化响应。
/// 无论成功或失败都不抛异常，便于测试同时断言成功路径与错误路径。
/// </summary>
public sealed class WailsInvokeResponse
{
    /// <summary>
    /// 从 <see cref="ResponseMessage"/> 构建结构化响应。
    /// </summary>
    /// <param name="response">IPC 管线返回的响应消息。</param>
    /// <param name="callId">本次调用的消息 ID。</param>
    /// <returns>结构化响应。</returns>
    internal static WailsInvokeResponse From(ResponseMessage response, string callId)
    {
        object? rawResult = null;
        CallErrorInfo? error = null;

        if (response.Result.TryGetValue("error", out var errObj) && errObj is not null)
        {
            error = ParseError(errObj);
        }

        if (response.Result.TryGetValue("result", out var resObj))
        {
            rawResult = resObj;
        }

        return new WailsInvokeResponse(callId, error is null, error, rawResult);
    }

    /// <summary>
    /// 将 IPC 响应中的 error 字段（<see cref="CallError.ToJson"/> 产生的字典）解析为 <see cref="CallErrorInfo"/>。
    /// </summary>
    private static CallErrorInfo? ParseError(object? errObj)
    {
        if (errObj is Dictionary<string, object?> dict)
        {
            dict.TryGetValue("message", out var message);
            dict.TryGetValue("cause", out var cause);
            dict.TryGetValue("kind", out var kind);
            return new CallErrorInfo(
                message as string ?? "(未知错误)",
                cause as string,
                kind as string ?? CallErrorKind.RuntimeError.ToString());
        }

        return new CallErrorInfo(errObj?.ToString() ?? "(未知错误)", null, CallErrorKind.RuntimeError.ToString());
    }

    private WailsInvokeResponse(string callId, bool isSuccess, CallErrorInfo? error, object? rawResult)
    {
        CallId = callId;
        IsSuccess = isSuccess;
        Error = error;
        RawResult = rawResult;
    }

    /// <summary>本次调用的消息 ID。</summary>
    public string CallId { get; }

    /// <summary>调用是否成功（error 字段为空）。</summary>
    public bool IsSuccess { get; }

    /// <summary>调用失败时非空，描述错误信息。</summary>
    public CallErrorInfo? Error { get; }

    /// <summary>响应中的 <c>result</c> 原始对象（可能为 <see cref="JsonElement"/> 或 CLR 对象）。</summary>
    public object? RawResult { get; }

    /// <summary>
    /// 将 <see cref="RawResult"/> 反序列化为指定类型。
    /// 调用失败（<see cref="IsSuccess"/> 为 false）时直接抛出 <see cref="WailsInvocationException"/>。
    /// </summary>
    /// <typeparam name="T">目标类型。</typeparam>
    /// <returns>反序列化后的结果；<see cref="RawResult"/> 为 null 时返回 <c>default</c>。</returns>
    public T GetResult<T>()
    {
        if (!IsSuccess)
        {
            throw new WailsInvocationException(
                Error ?? new CallErrorInfo("(未知错误)", null, CallErrorKind.RuntimeError.ToString()),
                CallId);
        }

        if (RawResult is null)
        {
            return default!;
        }

        var json = JsonSerializer.Serialize(RawResult, JsonOptions.DefaultSerializerOptions);
        return JsonSerializer.Deserialize<T>(json, JsonOptions.DefaultSerializerOptions)!;
    }
}

/// <summary>
/// 绑定调用失败时抛出的异常，携带结构化错误信息 <see cref="CallErrorInfo"/>。
/// </summary>
public sealed class WailsInvocationException : Exception
{
    /// <summary>
    /// 构造调用异常。
    /// </summary>
    /// <param name="error">结构化错误信息。</param>
    /// <param name="callId">本次调用的消息 ID。</param>
    public WailsInvocationException(CallErrorInfo error, string callId)
        : base($"绑定调用失败 [{callId}]: {error}")
    {
        Error = error;
        CallId = callId;
    }

    /// <summary>结构化错误信息。</summary>
    public CallErrorInfo Error { get; }

    /// <summary>本次调用的消息 ID。</summary>
    public string CallId { get; }
}
