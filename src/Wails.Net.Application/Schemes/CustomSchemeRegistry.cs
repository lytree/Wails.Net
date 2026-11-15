namespace Wails.Net.Application.Schemes;

/// <summary>
/// <see cref="ICustomSchemeRegistry"/> 的默认实现：不区分大小写的字典 + 单锁保护。
/// <para>
/// 桌面端 scheme 注册只发生在启动装配期（低频），读路径（窗口创建时查询）也不在热路径上，
/// 因此采用单锁而非并发字典，保持与抽取前一致的可见性与内存模型。
/// </para>
/// </summary>
public sealed class CustomSchemeRegistry : ICustomSchemeRegistry
{
    private readonly Dictionary<string, Wails.Net.AssetServer.AssetServer> _schemes =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly object _lock = new();

    /// <inheritdoc />
    public void Register(string scheme, Wails.Net.AssetServer.AssetServer assetServer)
    {
        ArgumentException.ThrowIfNullOrEmpty(scheme);
        ArgumentNullException.ThrowIfNull(assetServer);

        lock (_lock)
        {
            _schemes[scheme] = assetServer;
        }
    }

    /// <inheritdoc />
    public bool Unregister(string scheme)
    {
        if (string.IsNullOrEmpty(scheme))
        {
            return false;
        }

        lock (_lock)
        {
            return _schemes.Remove(scheme);
        }
    }

    /// <inheritdoc />
    public bool IsRegistered(string scheme)
    {
        if (string.IsNullOrEmpty(scheme))
        {
            return false;
        }

        lock (_lock)
        {
            return _schemes.ContainsKey(scheme);
        }
    }

    /// <inheritdoc />
    public bool TryGetAssetServer(string scheme, out Wails.Net.AssetServer.AssetServer? assetServer)
    {
        if (string.IsNullOrEmpty(scheme))
        {
            assetServer = null;
            return false;
        }

        lock (_lock)
        {
            return _schemes.TryGetValue(scheme, out assetServer);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> RegisteredSchemes
    {
        get
        {
            lock (_lock)
            {
                return _schemes.Keys.ToList().AsReadOnly();
            }
        }
    }
}
