using System.Text.Json;

namespace Wails.Net.Application.Commands;

/// <summary>
/// 前端调用请求。
/// 对应 Wails v3 前端通过传输层发起的方法调用消息。
/// </summary>
/// <param name="Id">请求唯一标识，用于关联响应。</param>
/// <param name="Method">要调用的命令名称。</param>
/// <param name="Parameters">调用参数，以 JSON 元素形式提供。</param>
public sealed record InvokeRequest(Guid Id, string Method, JsonElement Parameters);
