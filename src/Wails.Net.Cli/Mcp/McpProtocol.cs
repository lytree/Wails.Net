using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Wails.Net.Cli.Mcp;

/// <summary>
/// MCP（Model Context Protocol）JSON-RPC 2.0 协议常量与消息构造辅助。
/// 对应 Wails v3 Go 版本 <c>internal/commands/mcp.go</c> 中基于 <c>modelcontextprotocol/go-sdk</c> 的协议层。
/// </summary>
internal static class McpProtocol
{
    /// <summary>JSON 序列化选项：保留中文等非 ASCII 字符，避免转义为 \uXXXX。</summary>
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// 序列化协议节点。
    /// </summary>
    /// <param name="node">节点。</param>
    /// <returns>JSON 文本。</returns>
    public static string Serialize(JsonNode node) => node.ToJsonString(SerializerOptions);

    /// <summary>JSON-RPC 版本号。</summary>
    public const string JsonRpcVersion = "2.0";

    /// <summary>服务端支持的最新 MCP 协议版本。</summary>
    public const string LatestVersion = "2025-06-18";

    /// <summary>MCP 服务端名称。</summary>
    public const string ServerName = "wails-net-project";

    /// <summary>支持的协议版本集合（初始化时按客户端请求回显）。</summary>
    private static readonly string[] SupportedVersions =
    [
        "2024-11-05",
        "2025-03-26",
        "2025-06-18",
    ];

    /// <summary>JSON-RPC 错误码：解析失败。</summary>
    public const int ParseErrorCode = -32700;

    /// <summary>JSON-RPC 错误码：非法请求。</summary>
    public const int InvalidRequestCode = -32600;

    /// <summary>JSON-RPC 错误码：方法不存在。</summary>
    public const int MethodNotFoundCode = -32601;

    /// <summary>JSON-RPC 错误码：参数非法。</summary>
    public const int InvalidParamsCode = -32602;

    /// <summary>JSON-RPC 错误码：内部错误。</summary>
    public const int InternalErrorCode = -32603;

    /// <summary>
    /// 协商 MCP 协议版本：客户端请求的版本受支持时回显，否则返回服务端最新版本。
    /// </summary>
    /// <param name="requested">客户端请求的协议版本。</param>
    /// <returns>协商后的协议版本。</returns>
    public static string NegotiateVersion(string? requested) =>
        !string.IsNullOrEmpty(requested) && Array.IndexOf(SupportedVersions, requested) >= 0
            ? requested
            : LatestVersion;

    /// <summary>
    /// 构造 JSON-RPC 成功响应。
    /// </summary>
    /// <param name="id">请求 ID（可为 null）。</param>
    /// <param name="result">结果节点。</param>
    /// <returns>响应对象。</returns>
    public static JsonObject Success(JsonNode? id, JsonNode result) => new()
    {
        ["jsonrpc"] = JsonRpcVersion,
        ["id"] = id?.DeepClone(),
        ["result"] = result,
    };

    /// <summary>
    /// 构造 JSON-RPC 错误响应。
    /// </summary>
    /// <param name="id">请求 ID（可为 null）。</param>
    /// <param name="code">错误码。</param>
    /// <param name="message">错误信息。</param>
    /// <param name="data">可选附加数据。</param>
    /// <returns>响应对象。</returns>
    public static JsonObject Error(JsonNode? id, int code, string message, JsonNode? data = null)
    {
        var error = new JsonObject
        {
            ["code"] = code,
            ["message"] = message,
        };
        if (data is not null)
        {
            error["data"] = data.DeepClone();
        }

        return new JsonObject
        {
            ["jsonrpc"] = JsonRpcVersion,
            ["id"] = id?.DeepClone(),
            ["error"] = error,
        };
    }
}
