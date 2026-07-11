namespace Wails.Net.Application.Services;

/// <summary>
/// 键值存储后端接口，定义后端的异步数据访问契约。
/// 对应 Wails v3 Go 版本 pkg/services/kvstore 中的后端抽象。
/// 不同实现可提供内存、文件、数据库等不同存储介质。
/// </summary>
public interface IKvStoreBackend
{
    /// <summary>
    /// 异步获取指定键的值。
    /// </summary>
    /// <param name="key">键名。</param>
    /// <returns>对应的值字符串，若键不存在则返回 null。</returns>
    Task<string?> GetAsync(string key);

    /// <summary>
    /// 异步设置指定键的值。
    /// </summary>
    /// <param name="key">键名。</param>
    /// <param name="value">要存储的值字符串。</param>
    /// <returns>表示设置操作的异步任务。</returns>
    Task SetAsync(string key, string value);

    /// <summary>
    /// 异步删除指定键。
    /// </summary>
    /// <param name="key">键名。</param>
    /// <returns>键存在并删除成功返回 true，键不存在返回 false。</returns>
    Task<bool> DeleteAsync(string key);

    /// <summary>
    /// 异步获取匹配前缀的所有键。前缀为 null 时返回所有键。
    /// </summary>
    /// <param name="prefix">键前缀过滤条件，为 null 时返回所有键。</param>
    /// <returns>匹配键的字符串数组。</returns>
    Task<string[]> GetKeysAsync(string? prefix = null);

    /// <summary>
    /// 异步清空所有键值对。
    /// </summary>
    /// <returns>表示清空操作的异步任务。</returns>
    Task ClearAsync();
}
