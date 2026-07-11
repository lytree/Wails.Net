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

    /// <summary>检查是否为本地源</summary>
    private static bool IsLocalOrigin(string origin)
    {
        return origin.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase)
            || origin.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || origin.StartsWith("wails://", StringComparison.OrdinalIgnoreCase)
            || origin.StartsWith("https://wails.localhost", StringComparison.OrdinalIgnoreCase);
    }
}
