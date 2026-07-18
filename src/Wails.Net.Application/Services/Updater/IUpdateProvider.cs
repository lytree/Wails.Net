using System.Text.Json.Serialization;

namespace Wails.Net.Application.Services.Updater;

/// <summary>
/// 更新源提供者接口，抽象"检查更新"的来源。
/// <para>
/// 对应 Wails v3 中 updater 包的隐式 provider 概念：
/// 默认从单个 HTTP URL 获取更新清单（<see cref="HttpUpdateProvider"/>），
/// 用户可实现此接口以接入 GitHub Releases、GitLab Releases、自建 API 等多种来源。
/// </para>
/// <para>
/// 多 Provider 模式：可在 <see cref="UpdaterService"/> 中注册多个提供者，
/// 服务会按注册顺序依次尝试，首个返回非 null 清单的提供者胜出。
/// </para>
/// </summary>
public interface IUpdateProvider
{
    /// <summary>
    /// 获取提供者名称，用于日志、错误事件 payload（对应 Wails v3 ErrorInfo.Provider 字段）。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 异步检查更新。返回 null 表示此提供者无可用更新或检查失败（应尝试下一个提供者）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>更新清单；若此提供者不适用或无更新返回 null。</returns>
    Task<UpdateManifest?> CheckAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 更新检查结果，包含来源提供者信息和清单。
/// </summary>
public sealed class ProviderResult
{
    /// <summary>
    /// 获取或设置来源提供者名称。
    /// </summary>
    public string ProviderName { get; init; } = string.Empty;

    /// <summary>
    /// 获取或设置更新清单。
    /// </summary>
    public UpdateManifest? Manifest { get; init; }

    /// <summary>
    /// 获取或设置检查过程中的异常（若有）。
    /// </summary>
    public Exception? Error { get; init; }

    /// <summary>
    /// 获取此结果是否成功（Manifest 非空且无异常）。
    /// </summary>
    [JsonIgnore]
    public bool IsSuccess => Manifest is not null && Error is null;
}
