namespace Wails.Net.Application.Services;

/// <summary>
/// 服务注册表，管理已注册的服务。
/// </summary>
public class ServiceRegistry
{
    private readonly List<object> _services = new();

    /// <summary>
    /// 获取已注册服务的只读列表。
    /// </summary>
    public IReadOnlyList<object> Services => _services;

    /// <summary>
    /// 注册服务。
    /// </summary>
    /// <param name="service">要注册的服务实例。</param>
    public void Register(object service)
    {
        _services.Add(service);
    }

    /// <summary>
    /// 注销服务。
    /// </summary>
    /// <param name="service">要注销的服务实例。</param>
    public void Unregister(object service)
    {
        _services.Remove(service);
    }

    /// <summary>
    /// 获取指定类型的服务。
    /// </summary>
    /// <typeparam name="T">服务类型。</typeparam>
    /// <returns>匹配的服务实例，若不存在则返回 null。</returns>
    public T? GetService<T>() where T : class
    {
        return _services.OfType<T>().FirstOrDefault();
    }

    /// <summary>
    /// 获取所有匹配类型的服务。
    /// </summary>
    /// <typeparam name="T">服务类型。</typeparam>
    /// <returns>匹配的服务实例集合。</returns>
    public IEnumerable<T> GetServices<T>() where T : class
    {
        return _services.OfType<T>();
    }

    /// <summary>
    /// 清空所有服务。
    /// </summary>
    public void Clear()
    {
        _services.Clear();
    }
}
