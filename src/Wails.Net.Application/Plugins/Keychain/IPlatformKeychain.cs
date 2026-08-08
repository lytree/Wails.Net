namespace Wails.Net.Application.Plugins.Keychain;

/// <summary>
/// 平台钥匙串接口，提供原生安全存储能力。
/// 对应 Wails v3 Go 版本 <c>internal/keychain</c> 与 Tauri v2 <c>@tauri-apps/plugin-stronghold</c> 之外的系统级钥匙串集成。
/// </summary>
/// <remarks>
/// 各平台实现：
/// <list type="bullet">
/// <item>Windows：通过 <c>CredReadW/CredWriteW</c> 调用 Credential Manager（advapi32.dll）。</item>
/// <item>Linux：通过 <c>libsecret</c> 调用 Secret Service（D-Bus）。</item>
/// <item>macOS：通过 Keychain Services（Security.framework，<see cref="Wails.Net.Application.Platform.MacOSKeychain"/>）。</item>
/// <item>Server：no-op 回退。</item>
/// </list>
/// </remarks>
public interface IPlatformKeychain
{
    /// <summary>
    /// 将指定凭据安全存储到系统钥匙串。
    /// </summary>
    /// <param name="service">服务标识（用于隔离不同应用的命名空间）。</param>
    /// <param name="account">账户标识（同一 service 下区分不同账号）。</param>
    /// <param name="password">要存储的密码明文。</param>
    /// <returns>若存储成功返回 true；否则返回 false。</returns>
    bool SetPassword(string service, string account, string password);

    /// <summary>
    /// 从系统钥匙串读取指定凭据。
    /// </summary>
    /// <param name="service">服务标识。</param>
    /// <param name="account">账户标识。</param>
    /// <returns>密码明文；若不存在或读取失败返回 null。</returns>
    string? GetPassword(string service, string account);

    /// <summary>
    /// 从系统钥匙串删除指定凭据。
    /// </summary>
    /// <param name="service">服务标识。</param>
    /// <param name="account">账户标识。</param>
    /// <returns>若删除成功或条目不存在返回 true；否则返回 false。</returns>
    bool DeletePassword(string service, string account);
}
