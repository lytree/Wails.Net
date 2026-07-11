using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;

namespace Wails.Net.Application.Plugins.BuiltIn;

/// <summary>
/// HTTP 代理插件，允许前端通过后端发起 HTTP 请求绕过 CORS。
/// 对应 Tauri v2 的 <c>@tauri-apps/api/http</c>。
/// 提供 <c>http.fetch</c>、<c>http.get</c>、<c>http.post</c> 等命令。
/// </summary>
public class HttpPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "http";

    private static readonly HttpClient s_client = new();

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        // 无需注册额外服务，HttpClient 为静态共享实例。
    }

    /// <summary>
    /// 配置插件，注册 HTTP 相关命令。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        context.Commands.MapCommand("http.fetch", (Func<HttpRequestOptions, Task<HttpResponseResult>>)(async options =>
            await FetchAsync(options)));

        context.Commands.MapCommand("http.get", (Func<string, Task<HttpResponseResult>>)(async url =>
            await GetAsync(url)));

        context.Commands.MapCommand("http.post", (Func<string, string?, Task<HttpResponseResult>>)(async (url, body) =>
            await PostAsync(url, body)));

        context.Commands.MapCommand("http.put", (Func<string, string?, Task<HttpResponseResult>>)(async (url, body) =>
            await PutAsync(url, body)));

        context.Commands.MapCommand("http.delete", (Func<string, Task<HttpResponseResult>>)(async url =>
            await DeleteAsync(url)));
    }

    /// <summary>
    /// 执行自定义 HTTP 请求。
    /// </summary>
    /// <param name="options">请求选项。</param>
    /// <returns>响应结果。</returns>
    private static async Task<HttpResponseResult> FetchAsync(HttpRequestOptions options)
    {
        using var request = new HttpRequestMessage(
            new HttpMethod(options.Method ?? "GET"),
            options.Url);

        if (!string.IsNullOrEmpty(options.Body))
        {
            request.Content = new StringContent(
                options.Body,
                Encoding.UTF8,
                options.ContentType ?? "application/json");
        }

        if (options.Headers is not null)
        {
            foreach (var header in options.Headers)
            {
                if (!string.IsNullOrEmpty(header.Key) && !string.IsNullOrEmpty(header.Value))
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
        }

        using var response = await s_client.SendAsync(request);
        return await ToResultAsync(response);
    }

    /// <summary>
    /// 发起 GET 请求。
    /// </summary>
    private static async Task<HttpResponseResult> GetAsync(string url)
    {
        using var response = await s_client.GetAsync(url);
        return await ToResultAsync(response);
    }

    /// <summary>
    /// 发起 POST 请求。
    /// </summary>
    private static async Task<HttpResponseResult> PostAsync(string url, string? body)
    {
        using var content = body is not null
            ? new StringContent(body, Encoding.UTF8, "application/json")
            : null;
        using var response = await s_client.PostAsync(url, content);
        return await ToResultAsync(response);
    }

    /// <summary>
    /// 发起 PUT 请求。
    /// </summary>
    private static async Task<HttpResponseResult> PutAsync(string url, string? body)
    {
        using var content = body is not null
            ? new StringContent(body, Encoding.UTF8, "application/json")
            : null;
        using var response = await s_client.PutAsync(url, content);
        return await ToResultAsync(response);
    }

    /// <summary>
    /// 发起 DELETE 请求。
    /// </summary>
    private static async Task<HttpResponseResult> DeleteAsync(string url)
    {
        using var response = await s_client.DeleteAsync(url);
        return await ToResultAsync(response);
    }

    /// <summary>
    /// 将 HttpResponseMessage 转换为可序列化的 HttpResponseResult。
    /// </summary>
    private static async Task<HttpResponseResult> ToResultAsync(HttpResponseMessage response)
    {
        var result = new HttpResponseResult
        {
            StatusCode = (int)response.StatusCode,
            Ok = response.IsSuccessStatusCode
        };

        foreach (var header in response.Headers)
        {
            result.Headers[header.Key] = string.Join(", ", header.Value);
        }

        if (response.Content.Headers.ContentType is not null &&
            response.Content.Headers.ContentType.MediaType is not null)
        {
            result.ContentType = response.Content.Headers.ContentType.MediaType;
        }

        result.Body = await response.Content.ReadAsStringAsync();

        return result;
    }
}

/// <summary>
/// HTTP 请求选项。
/// 对应 Tauri v2 的 <c>RequestOptions</c>。
/// </summary>
public sealed class HttpRequestOptions
{
    /// <summary>请求 URL</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>HTTP 方法（GET/POST/PUT/DELETE 等），默认 GET</summary>
    public string? Method { get; set; }

    /// <summary>请求体（字符串）</summary>
    public string? Body { get; set; }

    /// <summary>请求体内容类型，默认 application/json</summary>
    public string? ContentType { get; set; }

    /// <summary>自定义请求头</summary>
    public Dictionary<string, string>? Headers { get; set; }
}

/// <summary>
/// HTTP 响应结果。
/// 对应 Tauri v2 的 <c>Response</c>。
/// </summary>
public sealed class HttpResponseResult
{
    /// <summary>HTTP 状态码</summary>
    public int StatusCode { get; set; }

    /// <summary>是否成功（2xx）</summary>
    public bool Ok { get; set; }

    /// <summary>响应体（字符串）</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>响应内容类型</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>响应头</summary>
    public Dictionary<string, string> Headers { get; set; } = new();
}
