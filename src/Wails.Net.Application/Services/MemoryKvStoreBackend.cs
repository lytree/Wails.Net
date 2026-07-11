using System.Collections.Concurrent;

namespace Wails.Net.Application.Services;

/// <summary>
/// 基于内存的键值存储后端实现。
/// 使用 <see cref="ConcurrentDictionary{TKey, TValue}"/> 存储数据，无持久化能力。
/// 适用于临时数据、测试场景或无需持久化的运行时状态。
/// </summary>
public class MemoryKvStoreBackend : IKvStoreBackend
{
    /// <summary>
    /// 内存键值存储字典。
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _store = new();

    /// <summary>
    /// 使用默认空存储构造内存后端实例。
    /// </summary>
    public MemoryKvStoreBackend()
    {
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
}
