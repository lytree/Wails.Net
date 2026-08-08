using Wails.Net.Application.Plugins.Keychain;

namespace Wails.Net.Application.Platform;

/// <summary>
/// macOS Keychain Services 钥匙串实现。
/// 对应 Wails v3 Go 版本 <c>internal/keychain/keychain_darwin.go</c>，
/// 通过 <c>Security.framework</c>（SecItemAdd / SecItemCopyMatching / SecItemDelete）读写系统钥匙串。
/// <para>
/// 凭据以 GenericPassword 类型存储，<c>Service</c> + <c>Account</c> 组成唯一键，
/// 与 Windows Credential Manager 的 <c>{Service}:{Account}</c> 语义对齐。
/// 在非 macOS 目标（<c>#if !MACOS</c>）保留 no-op 骨架保证任意宿主编译。
/// </para>
/// </summary>
public sealed class MacOSKeychain : IPlatformKeychain
{
#if MACOS
    /// <summary>
    /// 构造通用密码查询/更新记录。
    /// </summary>
    /// <param name="service">服务标识。</param>
    /// <param name="account">账户标识。</param>
    /// <returns>SecRecord 查询记录。</returns>
    private static Security.SecRecord BuildRecord(string service, string account)
        => new(Security.SecKind.GenericPassword)
        {
            Service = service,
            Account = account,
        };
#endif

    /// <inheritdoc />
    public bool SetPassword(string service, string account, string password)
    {
#if MACOS
        ArgumentException.ThrowIfNullOrEmpty(service);
        ArgumentException.ThrowIfNullOrEmpty(account);
        ArgumentNullException.ThrowIfNull(password);

        var record = BuildRecord(service, account);
        record.ValueData = Foundation.NSData.FromString(password, Foundation.NSStringEncoding.UTF8);

        // 先删除旧条目再添加，保证写入即更新（Add 在条目存在时返回 DuplicateItem）。
        Security.SecKeyChain.Remove(record);
        return Security.SecKeyChain.Add(record) == Security.SecStatusCode.Success;
#else
        return false;
#endif
    }

    /// <inheritdoc />
    public string? GetPassword(string service, string account)
    {
#if MACOS
        ArgumentException.ThrowIfNullOrEmpty(service);
        ArgumentException.ThrowIfNullOrEmpty(account);

        var record = BuildRecord(service, account);
        var data = Security.SecKeyChain.QueryAsData(record, out var status);
        if (status != Security.SecStatusCode.Success || data is null)
        {
            return null;
        }

        return data.ToString(Foundation.NSStringEncoding.UTF8);
#else
        return null;
#endif
    }

    /// <inheritdoc />
    public bool DeletePassword(string service, string account)
    {
#if MACOS
        ArgumentException.ThrowIfNullOrEmpty(service);
        ArgumentException.ThrowIfNullOrEmpty(account);

        var record = BuildRecord(service, account);
        var status = Security.SecKeyChain.Remove(record);
        // 条目不存在（ItemNotFound）业务上视作成功。
        return status == Security.SecStatusCode.Success
            || status == Security.SecStatusCode.ItemNotFound;
#else
        return false;
#endif
    }
}
