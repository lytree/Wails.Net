using System.Security.Cryptography;
using System.Text;
using Android.Content;
using Android.Security.Keystore;
using Android.Util;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using Wails.Net.Application.Plugins.Keychain;

namespace Wails.Net.Application.Platform;

/// <summary>
/// Android 平台钥匙串实现：Android Keystore（AES-256-GCM）加密 + 私有 SharedPreferences 存储。
/// 对应 Wails v3 Go 版本 <c>internal/keychain</c> 的 Android 缺失项（Tauri v2 使用等价方案）。
/// <para>
/// Android Keystore 只保存密钥、不保存任意字符串，因此：
/// <list type="bullet">
/// <item>为每个 <c>service + account</c> 在 <c>AndroidKeyStore</c> 中生成独立 AES 密钥；</item>
/// <item>用 <c>AES/GCM/NoPadding</c> 加密密码明文，IV 与密文一起以 Base64 写入应用私有 SharedPreferences；</item>
/// <item>删除时同时移除密文条目与密钥条目。</item>
/// </list>
/// 密钥永不离开 Keystore（硬件支持时由 TEE/StrongBox 保护），满足本地凭据存储要求。
/// </para>
/// </summary>
public sealed class AndroidKeychain : IPlatformKeychain
{
    /// <summary>私有 SharedPreferences 名称。</summary>
    private const string PreferencesName = "wails_net_keychain";

    /// <summary>Android Keystore 提供者名称。</summary>
    private const string KeyStoreProvider = "AndroidKeyStore";

    /// <summary>AES-GCM 变换。</summary>
    private const string Transformation = "AES/GCM/NoPadding";

    /// <summary>GCM 认证标签位数。</summary>
    private const int GcmTagBits = 128;

    /// <summary>IV 与密文之间的分隔符。</summary>
    private const char PayloadSeparator = ':';

    private readonly Context? _context;

    private readonly object _sync = new();

    /// <summary>
    /// 构造 Android 钥匙串实例（从全局 Context 获取 SharedPreferences）。
    /// </summary>
    public AndroidKeychain()
    {
        _context = global::Android.App.Application.Context;
    }

    /// <inheritdoc />
    public bool SetPassword(string service, string account, string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(service);
        ArgumentException.ThrowIfNullOrEmpty(account);
        ArgumentNullException.ThrowIfNull(password);

        if (_context is null)
        {
            return false;
        }

        lock (_sync)
        {
            try
            {
                var alias = BuildKeyAlias(service, account);
                var key = GetOrCreateKey(alias);
                if (key is null)
                {
                    return false;
                }

                using var cipher = Cipher.GetInstance(Transformation);
                if (cipher is null)
                {
                    return false;
                }

                cipher.Init(Javax.Crypto.CipherMode.EncryptMode, key);
                var iv = cipher.GetIV();
                var encrypted = cipher.DoFinal(Encoding.UTF8.GetBytes(password));
                if (iv is null || encrypted is null)
                {
                    return false;
                }

                var payload = Base64.EncodeToString(iv, Base64Flags.NoWrap)
                    + PayloadSeparator
                    + Base64.EncodeToString(encrypted, Base64Flags.NoWrap);

                using var preferences = GetPreferences();
                using var editor = preferences?.Edit();
                if (editor is null)
                {
                    return false;
                }

                editor.PutString(BuildPreferenceKey(service, account), payload);
                return editor.Commit();
            }
            catch
            {
                return false;
            }
        }
    }

    /// <inheritdoc />
    public string? GetPassword(string service, string account)
    {
        ArgumentException.ThrowIfNullOrEmpty(service);
        ArgumentException.ThrowIfNullOrEmpty(account);

        if (_context is null)
        {
            return null;
        }

        lock (_sync)
        {
            try
            {
                using var preferences = GetPreferences();
                var payload = preferences?.GetString(BuildPreferenceKey(service, account), null);
                if (string.IsNullOrEmpty(payload))
                {
                    return null;
                }

                var separator = payload.IndexOf(PayloadSeparator);
                if (separator <= 0 || separator == payload.Length - 1)
                {
                    return null;
                }

                var iv = Base64.Decode(payload[..separator], Base64Flags.NoWrap);
                var encrypted = Base64.Decode(payload[(separator + 1)..], Base64Flags.NoWrap);
                if (iv is null || encrypted is null)
                {
                    return null;
                }

                var key = GetExistingKey(BuildKeyAlias(service, account));
                if (key is null)
                {
                    return null;
                }

                using var cipher = Cipher.GetInstance(Transformation);
                if (cipher is null)
                {
                    return null;
                }

                using var spec = new GCMParameterSpec(GcmTagBits, iv);
                cipher.Init(Javax.Crypto.CipherMode.DecryptMode, key, spec);
                var plain = cipher.DoFinal(encrypted);
                return plain is null ? null : Encoding.UTF8.GetString(plain);
            }
            catch
            {
                return null;
            }
        }
    }

    /// <inheritdoc />
    public bool DeletePassword(string service, string account)
    {
        ArgumentException.ThrowIfNullOrEmpty(service);
        ArgumentException.ThrowIfNullOrEmpty(account);

        if (_context is null)
        {
            return false;
        }

        lock (_sync)
        {
            try
            {
                var alias = BuildKeyAlias(service, account);

                using var preferences = GetPreferences();
                using var editor = preferences?.Edit();
                editor?.Remove(BuildPreferenceKey(service, account));
                editor?.Commit();

                using var keyStore = OpenKeyStore();
                if (keyStore is not null && keyStore.ContainsAlias(alias))
                {
                    keyStore.DeleteEntry(alias);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 打开 Android Keystore。
    /// </summary>
    /// <returns>已加载的 KeyStore；失败返回 null。</returns>
    private static KeyStore? OpenKeyStore()
    {
        var keyStore = KeyStore.GetInstance(KeyStoreProvider);
        keyStore?.Load(null, null);
        return keyStore;
    }

    /// <summary>
    /// 获取已存在的 AES 密钥。
    /// </summary>
    /// <param name="alias">密钥别名。</param>
    /// <returns>密钥；不存在或读取失败返回 null。</returns>
    private static IKey? GetExistingKey(string alias)
    {
        try
        {
            using var keyStore = OpenKeyStore();
            if (keyStore is null || !keyStore.ContainsAlias(alias))
            {
                return null;
            }

            return keyStore.GetKey(alias, null);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 获取或创建 AES-256-GCM 密钥（密钥永久保存在 Android Keystore）。
    /// </summary>
    /// <param name="alias">密钥别名。</param>
    /// <returns>密钥；创建失败返回 null。</returns>
    private static IKey? GetOrCreateKey(string alias)
    {
        var existing = GetExistingKey(alias);
        if (existing is not null)
        {
            return existing;
        }

        var generator = Javax.Crypto.KeyGenerator.GetInstance(
            KeyProperties.KeyAlgorithmAes, KeyStoreProvider);
        if (generator is null)
        {
            return null;
        }

        var spec = new KeyGenParameterSpec.Builder(alias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm)
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
            .SetKeySize(256)
            .Build();

        generator.Init(spec);
        return generator.GenerateKey();
    }

    /// <summary>
    /// 获取应用私有 SharedPreferences。
    /// </summary>
    /// <returns>SharedPreferences 实例。</returns>
    private ISharedPreferences? GetPreferences()
        => _context?.GetSharedPreferences(PreferencesName, FileCreationMode.Private);

    /// <summary>
    /// 构造 Keystore 别名：对 <c>service + "\n" + account</c> 做 SHA-256 并转十六进制，
    /// 避免任意字符导致别名非法，同时不泄露明文标识。
    /// </summary>
    /// <param name="service">服务标识。</param>
    /// <param name="account">账户标识。</param>
    /// <returns>稳定的密钥别名。</returns>
    private static string BuildKeyAlias(string service, string account)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(service + "\n" + account));
        return "wails_key_" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// 构造 SharedPreferences 键名（明文标识，便于调试）。
    /// </summary>
    /// <param name="service">服务标识。</param>
    /// <param name="account">账户标识。</param>
    /// <returns>条目键名。</returns>
    private static string BuildPreferenceKey(string service, string account)
        => service + "\n" + account;
}
