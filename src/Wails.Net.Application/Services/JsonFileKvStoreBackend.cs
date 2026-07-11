using System.Collections.Concurrent;
using System.Text.Json;

namespace Wails.Net.Application.Services;

/// <summary>
/// 基于 JSON 文件的键值存储后端实现。
/// 数据以 JSON 格式持久化到磁盘文件，使用 <see cref="ConcurrentDictionary{TKey, TValue}"/> 保证线程安全。
/// 当未设置文件路径时，仅工作在内存模式，不进行持久化。
/// </summary>
public class JsonFileKvStoreBackend : IKvStoreBackend
{
    /// <summary>
    /// 键值存储字典，值为 JSON 字符串。
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _store = new();

    /// <summary>
    /// 持久化文件路径，为 null 时仅工作在内存模式。
    /// </summary>
    private readonly string? _filePath;

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
    /// 获取持久化文件路径，为 null 表示内存模式。
    /// </summary>
    public string? FilePath => _filePath;

    /// <summary>
    /// 使用内存模式构造 JSON 文件后端实例（不持久化）。
    /// </summary>
    public JsonFileKvStoreBackend() : this(null)
    {
    }

    /// <summary>
    /// 使用指定持久化文件路径构造 JSON 文件后端实例。
    /// </summary>
    /// <param name="filePath">持久化文件路径，为 null 时仅工作在内存模式。</param>
    public JsonFileKvStoreBackend(string? filePath)
    {
        _filePath = filePath;
    }

    /// <summary>
    /// 异步获取指定键的值。
    /// </summary>
    /// <param name="key">键名。</param>
    /// <returns>对应的值字符串，若键不存在则返回 null。</returns>
    public Task<string?> GetAsync(string key)
    {
        string? value = _store.TryGetValue(key, out var v) ? v : null;
        return Task.FromResult(value);
    }

    /// <summary>
    /// 异步设置指定键的值。
    /// </summary>
    /// <param name="key">键名。</param>
    /// <param name="value">要存储的值字符串。</param>
    /// <returns>表示设置操作的异步任务。</returns>
    public Task SetAsync(string key, string value)
    {
        _store[key] = value;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 异步删除指定键。
    /// </summary>
    /// <param name="key">键名。</param>
    /// <returns>键存在并删除成功返回 true，键不存在返回 false。</returns>
    public Task<bool> DeleteAsync(string key)
    {
        return Task.FromResult(_store.TryRemove(key, out _));
    }

    /// <summary>
    /// 异步获取匹配前缀的所有键。
    /// </summary>
    /// <param name="prefix">键前缀过滤条件，为 null 时返回所有键。</param>
    /// <returns>匹配键的字符串数组。</returns>
    public Task<string[]> GetKeysAsync(string? prefix = null)
    {
        var keys = prefix is null
            ? _store.Keys.ToArray()
            : _store.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        return Task.FromResult(keys);
    }

    /// <summary>
    /// 异步清空所有键值对。
    /// </summary>
    /// <returns>表示清空操作的异步任务。</returns>
    public Task ClearAsync()
    {
        _store.Clear();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 从磁盘加载持久化数据。
    /// 若文件不存在或读取失败则不执行任何操作。
    /// </summary>
    /// <returns>表示加载操作的异步任务。</returns>
    public Task LoadAsync()
    {
        if (string.IsNullOrEmpty(_filePath))
        {
            return Task.CompletedTask;
        }

        lock (_persistenceLock)
        {
            if (!File.Exists(_filePath))
            {
                return Task.CompletedTask;
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

        return Task.CompletedTask;
    }

    /// <summary>
    /// 将数据保存到磁盘。
    /// 若未设置文件路径则不执行任何操作。
    /// </summary>
    /// <returns>表示保存操作的异步任务。</returns>
    public Task SaveAsync()
    {
        if (string.IsNullOrEmpty(_filePath))
        {
            return Task.CompletedTask;
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

        return Task.CompletedTask;
    }
}
