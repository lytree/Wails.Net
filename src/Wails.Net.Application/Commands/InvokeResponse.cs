namespace Wails.Net.Application.Commands;

/// <summary>
/// 调用响应。
/// 对应 Wails v3 后端处理完前端调用后返回的消息。
/// </summary>
/// <param name="Id">对应的请求唯一标识。</param>
/// <param name="Success">是否调用成功。</param>
/// <param name="Result">调用结果（成功时）。</param>
/// <param name="Error">错误信息（失败时）。</param>
public sealed record InvokeResponse(Guid Id, bool Success, object? Result, string? Error);
