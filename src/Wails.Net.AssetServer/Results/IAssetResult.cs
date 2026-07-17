using System.Net;

namespace Wails.Net.AssetServer.Results;

/// <summary>
/// 资源响应结果接口，抽象 HTTP 响应的生成逻辑。
/// <para>
/// M8 新增。AssetServer 的 <c>ServeAssetCoreAsync</c> 生成 <see cref="IAssetResult"/> 实例，
/// 再由 <c>WriteResultAsync</c> 统一写入 <see cref="HttpListenerResponse"/>。
/// 此抽象使响应逻辑可测试、可扩展，同时保持 <c>ServeHttpAsync</c> 公共签名不变。
/// </para>
/// </summary>
public interface IAssetResult
{
    /// <summary>
    /// 获取 HTTP 状态码（如 200、206、304、404、416、500）。
    /// </summary>
    int StatusCode { get; }

    /// <summary>
    /// 获取内容类型（MIME），可为 null（如 304/404 无需 Content-Type）。
    /// </summary>
    string? ContentType { get; }

    /// <summary>
    /// 异步将结果写入 HTTP 响应。
    /// 实现者负责设置状态码、头部并写入响应体。
    /// </summary>
    /// <param name="response">HTTP 响应对象。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    Task WriteAsync(HttpListenerResponse response, CancellationToken cancellationToken = default);
}
