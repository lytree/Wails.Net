using System.Collections.Concurrent;
using System.Text.Json;
using Wails.Net.Application.Options;

namespace Wails.Net.Application.Services;

/// <summary>
/// 键值存储服务，提供简单的持久化键值存储。
/// 对应 Wails v3 Go 版本 pkg/services/kvstore。
/// 数据以 JSON 格式持久化到磁盘文件，使用 ConcurrentDictionary 保证线程安全。
/// </summary>
public class KvStoreService : IServiceStartup, IServiceShutdown
{
    /// <summary>
    /// 键值存储字典，值为 JSON 字符串。
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _store = new();

    /// <summary>
    /// 持久化文件路径，为 null 时不进行持久化。
    /// </summary>
    private string? _filePath;

    /// <summary>
    /// 持久化操作的锁。
    /// </summary>
    private readonly object _persistenceLock = new();

    /// <summary>
    /// JSON 序列化选项。
    /// </summary>
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// 获取或设置持久化文件路径。
    /// 在服务启动前设置以自定义持久化位置。
    /// </summary>
    public string? FilePath
    {
        get => _filePath;
        set => _filePath = value;
    }

    /// <summary>
    /// 使用默认配置构造键值存储服务实例。
    /// </summary>
    public KvStoreService()
    {
    }

    /// <summary>
    /// 使用指定持久化文件路径构造键值存储服务实例。
    /// </summary>
    /// <param name="filePath">持久化文件路径。</param>
    public KvStoreService(string filePath)
    {
        _filePath = filePath;
    }

    /// <summary>
    /// 服务启动，从磁盘加载持久化数据。
    /// </summary>
    /// <param name="options">应用选项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示启动操作的异步任务。</returns>
    public Task ServiceStartup(ApplicationOptions options, CancellationToken cancellationToken)
    {
        LoadFromDisk();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 服务关闭，将数据保存到磁盘。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示关闭操作的异步任务。</returns>
    public Task ServiceShutdown(CancellationToken cancellationToken)
    {
        SaveToDisk();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 获取指定键的值（JSON 字符串）。
    /// </summary>
    /// <param name="key">键名。</param>
    /// <returns>对应的 JSON 字符串值，若键不存在则返回 null。</returns>
    public string? Get(string key)
    {
        return _store.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>
    /// 设置指定键的值，将值序列化为 JSON 字符串存储。
    /// </summary>
    /// <param name="key">键名。</param>
    /// <param name="value">要存储的值，可为任意可序列化对象。</param>
    public void Set(string key, object value)
    {
        var json = JsonSerializer.Serialize(value, JsonOpts);
        _store[key] = json;
    }

    /// <summary>
    /// 删除指定键。
    /// </summary>
    /// <param name="key">键名。</param>
    /// <returns>键存在并删除成功返回 true，键不存在返回 false。</returns>
    public bool Delete(string key)
    {
        return _store.TryRemove(key, out _);
    }

    /// <summary>
    /// 获取所有键的数组。
    /// </summary>
    /// <returns>所有键的字符串数组。</returns>
    public string[] Keys()
    {
        return _store.Keys.ToArray();
    }

    /// <summary>
    /// 清空所有键值对。
    /// </summary>
    public void Clear()
    {
        _store.Clear();
    }

    /// <summary>
    /// 从磁盘加载持久化数据。
    /// 若文件不存在或读取失败则不执行任何操作。
    /// </summary>
    private void LoadFromDisk()
    {
        if (string.IsNullOrEmpty(_filePath))
        {
            return;
        }

        lock (_persistenceLock)
        {
            if (!File.Exists(_filePath))
            {
                return;
            }

            try
            {
                var json = File.ReadAllText(_filePath);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOpts);
                if (dict is not null)
                {
                    foreach (var pair in dict)
                    {
                        _store[pair.Key] = pair.Value;
                    }
                }
            }
            catch (JsonException)
            {
                // 持久化文件损坏时忽略，使用空存储
            }
        }
    }

    /// <summary>
    /// 将数据保存到磁盘。
    /// 若未设置文件路径则不执行任何操作。
    /// </summary>
    private void SaveToDisk()
    {
        if (string.IsNullOrEmpty(_filePath))
        {
            return;
        }

        lock (_persistenceLock)
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var dict = new Dictionary<string, string>(_store);
            var json = JsonSerializer.Serialize(dict, JsonOpts);
            File.WriteAllText(_filePath, json);
        }
    }
}
