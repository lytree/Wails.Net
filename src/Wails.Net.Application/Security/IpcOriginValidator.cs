namespace Wails.Net.Application.Security;

/// <summary>
/// IPC 源校验器，验证 WebView 消息来源是否可信。
/// 对应 Tauri v2 的 IPC 安全校验。
/// </summary>
public sealed class IpcOriginValidator
{
    private readonly UrlWhitelist _whitelist;

    /// <summary>构造 IPC 源校验器</summary>
    public IpcOriginValidator(UrlWhitelist whitelist)
    {
        ArgumentNullException.ThrowIfNull(whitelist);
        _whitelist = whitelist;
    }

    /// <summary>验证 IPC 消息来源是否允许</summary>
    public bool Validate(string? origin)
    {
        // 本地源（wails://, http://localhost, http://127.0.0.1）总是允许
        if (string.IsNullOrEmpty(origin)) return true;
        if (IsLocalOrigin(origin)) return true;
        return _whitelist.IsAllowed(origin);
    }

    /// <summary>
    /// 验证 IPC 消息来源信息是否允许（OriginInfo 三字段版本）。
    /// 对应 Wails v3 Go 版本中 <c>originInfo</c> 校验逻辑：
    /// <list type="bullet">
    /// <item>当 <see cref="OriginInfo.IsMainFrame"/> 为 false 时（iframe 消息），
    /// 同时校验 <see cref="OriginInfo.Origin"/> 与 <see cref="OriginInfo.TopOrigin"/>；</item>
    /// <item>当为 true（主框架消息）时，与单参数版本行为一致。</item>
    /// </list>
    /// 任一字段不在白名单且非本地源时拒绝。
    /// </summary>
    /// <param name="originInfo">消息来源信息。</param>
    /// <returns>若允许返回 true；否则返回 false。</returns>
    public bool Validate(OriginInfo originInfo)
    {
        // 主框架消息：与单字段校验等价
        if (originInfo.IsMainFrame)
        {
            return Validate(originInfo.Origin);
        }

        // iframe 消息：Origin 与 TopOrigin 都需校验
        if (!Validate(originInfo.Origin))
        {
            return false;
        }

        // TopOrigin 与 Origin 不同时再校验，相同则跳过
        if (!string.IsNullOrEmpty(originInfo.TopOrigin) &&
            !string.Equals(originInfo.Origin, originInfo.TopOrigin, StringComparison.OrdinalIgnoreCase))
        {
            return Validate(originInfo.TopOrigin);
        }

        return true;
    }

    /// <summary>检查是否为本地源</summary>
    private static bool IsLocalOrigin(string origin)
    {
        return origin.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase)
            || origin.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || origin.StartsWith("wails://", StringComparison.OrdinalIgnoreCase)
            || origin.StartsWith("https://wails.localhost", StringComparison.OrdinalIgnoreCase);
    }
}
