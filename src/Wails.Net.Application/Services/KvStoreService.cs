using System.Text.Json;
using Wails.Net.Application.Options;

namespace Wails.Net.Application.Services;

/// <summary>
/// 键值存储服务，提供基于后端的键值存储与命名空间隔离。
/// 对应 Wails v3 Go 版本 pkg/services/kvstore。
/// 支持注入不同 <see cref="IKvStoreBackend"/> 后端（默认 JSON 文件后端），
/// 支持命名空间前缀隔离与键变更监听。
/// </summary>
public class KvStoreService : IServiceStartup, IServiceShutdown
{
    /// <summary>
    /// 键值存储后端实例。
    /// </summary>
    private readonly IKvStoreBackend _backend;

    /// <summary>
    /// 当前命名空间前缀，为空字符串时表示无命名空间。
    /// </summary>
    private string _namespace = string.Empty;

    /// <summary>
    /// JSON 序列化选项。
    /// </summary>
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// 键变更事件，在键被设置或删除时触发。
    /// 回调参数为原始键名（不含命名空间前缀）和新值（删除时为 null）。
    /// </summary>
    public event Action<string, string?>? OnKeyChanged;

    /// <summary>
    /// 获取持久化文件路径。
    /// 当后端为 <see cref="JsonFileKvStoreBackend"/> 时返回其文件路径，否则返回 null。
    /// </summary>
    public string? FilePath => (_backend as JsonFileKvStoreBackend)?.FilePath;

    /// <summary>
    /// 使用默认 JSON 文件后端（内存模式）构造键值存储服务实例。
    /// </summary>
    public KvStoreService()
        : this(new JsonFileKvStoreBackend())
    {
    }

    /// <summary>
    /// 使用指定持久化文件路径构造键值存储服务实例。
    /// </summary>
    /// <param name="filePath">持久化文件路径。</param>
    public KvStoreService(string filePath)
        : this(new JsonFileKvStoreBackend(filePath))
    {
    }

    /// <summary>
    /// 使用指定后端构造键值存储服务实例。
    /// </summary>
    /// <param name="backend">键值存储后端实例。</param>
    public KvStoreService(IKvStoreBackend backend)
    {
        _backend = backend;
    }

    /// <summary>
    /// 设置命名空间，设置后所有键操作自动添加前缀 <c>ns:</c> 以实现键空间隔离。
    /// 传入 null 或空字符串表示清除命名空间。
    /// </summary>
    /// <param name="ns">命名空间名称。</param>
    public void SetNamespace(string ns)
    {
        _namespace = ns ?? string.Empty;
    }

    /// <summary>
    /// 服务启动，初始化后端（持久化后端从磁盘加载数据）。
    /// </summary>
    /// <param name="options">应用选项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示启动操作的异步任务。</returns>
    public async Task ServiceStartup(ApplicationOptions options, CancellationToken cancellationToken)
    {
        if (_backend is JsonFileKvStoreBackend jsonBackend)
        {
            await jsonBackend.LoadAsync();
        }
    }

    /// <summary>
    /// 服务关闭，保存数据到持久化后端（若后端支持持久化）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示关闭操作的异步任务。</returns>
    public async Task ServiceShutdown(CancellationToken cancellationToken)
    {
        if (_backend is JsonFileKvStoreBackend jsonBackend)
        {
            await jsonBackend.SaveAsync();
        }
    }

    /// <summary>
    /// 获取指定键的值（JSON 字符串）。
    /// </summary>
    /// <param name="key">键名。</param>
    /// <returns>对应的 JSON 字符串值，若键不存在则返回 null。</returns>
    public string? Get(string key)
    {
        return _backend.GetAsync(ApplyNamespace(key)).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 设置指定键的值，将值序列化为 JSON 字符串存储。
    /// </summary>
    /// <param name="key">键名。</param>
    /// <param name="value">要存储的值，可为任意可序列化对象。</param>
    public void Set(string key, object value)
    {
        var json = JsonSerializer.Serialize(value, JsonOpts);
        _backend.SetAsync(ApplyNamespace(key), json).GetAwaiter().GetResult();
        OnKeyChanged?.Invoke(key, json);
    }

    /// <summary>
    /// 删除指定键。
    /// </summary>
    /// <param name="key">键名。</param>
    /// <returns>键存在并删除成功返回 true，键不存在返回 false。</returns>
    public bool Delete(string key)
    {
        var deleted = _backend.DeleteAsync(ApplyNamespace(key)).GetAwaiter().GetResult();
        if (deleted)
        {
            OnKeyChanged?.Invoke(key, null);
        }

        return deleted;
    }

    /// <summary>
    /// 获取当前命名空间下的所有键的数组（不含命名空间前缀）。
    /// </summary>
    /// <returns>所有键的字符串数组。</returns>
    public string[] Keys()
    {
        var nsPrefix = string.IsNullOrEmpty(_namespace) ? null : _namespace + ":";
        var keys = _backend.GetKeysAsync(nsPrefix).GetAwaiter().GetResult();
        if (nsPrefix is null)
        {
            return keys;
        }

        return keys
            .Select(k => k.StartsWith(nsPrefix, StringComparison.Ordinal) ? k[nsPrefix.Length..] : k)
            .ToArray();
    }

    /// <summary>
    /// 清空当前命名空间下的所有键值对。
    /// 未设置命名空间时清空整个存储。
    /// </summary>
    public void Clear()
    {
        if (string.IsNullOrEmpty(_namespace))
        {
            _backend.ClearAsync().GetAwaiter().GetResult();
            return;
        }

        var nsPrefix = _namespace + ":";
        var keys = _backend.GetKeysAsync(nsPrefix).GetAwaiter().GetResult();
        foreach (var k in keys)
        {
            _backend.DeleteAsync(k).GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// 为键名应用命名空间前缀。
    /// 未设置命名空间时返回原始键名。
    /// </summary>
    /// <param name="key">原始键名。</param>
    /// <returns>应用命名空间后的键名。</returns>
    private string ApplyNamespace(string key)
    {
        return string.IsNullOrEmpty(_namespace) ? key : $"{_namespace}:{key}";
    }
}
