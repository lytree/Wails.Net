using System.Net;

namespace Wails.Net.Application.Transport;

/// <summary>
/// 资源服务器传输适配器，将 AssetServer 绑定到传输层。
/// 对应 Wails v3 Go 版本中的 AssetServerTransport 接口实现。
/// 当传输层收到资源请求时，通过此适配器将请求转发给 AssetServer 处理。
/// </summary>
public class AssetServerTransport
{
    /// <summary>
    /// 绑定的资源服务器实例。
    /// </summary>
    private Wails.Net.AssetServer.AssetServer? _assetServer;

    /// <summary>
    /// 获取是否已绑定资源服务器。
    /// </summary>
    public bool HasAssetServer => _assetServer is not null;

    /// <summary>
    /// 将资源服务器绑定到当前传输适配器。
    /// 传输层在收到资源请求时，通过 <see cref="ServeAsync" /> 将请求转发给 AssetServer 处理。
    /// </summary>
    /// <param name="assetServer">Wails 内部资源服务器实例。</param>
    public void ServeAssets(Wails.Net.AssetServer.AssetServer assetServer)
    {
        ArgumentNullException.ThrowIfNull(assetServer);
        _assetServer = assetServer;
    }

    /// <summary>
    /// 将 HTTP 请求转发给绑定的资源服务器处理。
    /// 若未绑定资源服务器则返回 404 响应。
    /// </summary>
    /// <param name="context">HTTP 请求上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示处理操作的异步任务。</returns>
    public async Task ServeAsync(HttpListenerContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_assetServer is null)
        {
            // 未绑定资源服务器，返回 404
            context.Response.StatusCode = 404;
            context.Response.Close();
            return;
        }

        await _assetServer.ServeHttpAsync(context, cancellationToken);
    }

    /// <summary>
    /// 将资源服务器绑定到实现了 <see cref="IAssetServerTransport" /> 接口的传输层。
    /// 这是一个便捷方法，用于将 AssetServer 通过 IAssetServerTransport 接口注入传输层。
    /// </summary>
    /// <param name="transport">实现了 IAssetServerTransport 接口的传输层实例。</param>
    /// <param name="assetServer">要绑定的资源服务器实例。</param>
    public static void BindToTransport(IAssetServerTransport transport, Wails.Net.AssetServer.AssetServer assetServer)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(assetServer);
        transport.ServeAssets(assetServer);
    }
}
