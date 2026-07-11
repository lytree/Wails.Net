using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;

namespace Wails.Net.Application.Plugins.BuiltIn;

/// <summary>
/// 加密安全存储插件，用于安全保存密码、密钥等敏感数据。
/// 对应 Tauri v2 的 <c>@tauri-apps/plugin-stronghold</c>。
/// 使用 AES-GCM 认证加密和 PBKDF2 密钥派生，加密数据持久化到 JSON 文件。
/// </summary>
public class StrongholdPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "stronghold";

    /// <summary>
    /// 默认密钥派生迭代次数。
    /// </summary>
    private const int Pbkdf2Iterations = 100_000;

    /// <summary>
    /// 默认加密文件路径。相对于当前工作目录。
    /// </summary>
    private const string DefaultVaultPath = "stronghold.vault.json";

    /// <summary>
    /// AES-GCM 认证标签长度（字节）。
    /// </summary>
    private const int TagSize = 16;

    /// <summary>
    /// 当前持有的金库实例（线程安全）。
    /// </summary>
    private static readonly ConcurrentDictionary<string, StrongholdVault> _vaults = new();

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。此插件无需注册额外服务。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        // 无需注册额外服务，金库通过静态字典管理生命周期
    }

    /// <summary>
    /// 配置插件，注册加密存储相关命令。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        // 解锁或创建金库
        context.Commands.MapCommand("stronghold.unlock", (Func<string, string?, bool>)((password, vaultPath) =>
        {
            var path = string.IsNullOrEmpty(vaultPath) ? DefaultVaultPath : vaultPath;
            var vault = _vaults.GetOrAdd(path, _ => new StrongholdVault(path));
            return vault.Unlock(password, Pbkdf2Iterations);
        }));

        // 锁定金库（清除内存中的密钥）
        context.Commands.MapCommand("stronghold.lock", (Action<string?>)((vaultPath) =>
        {
            var path = string.IsNullOrEmpty(vaultPath) ? DefaultVaultPath : vaultPath;
            if (_vaults.TryGetValue(path, out var vault))
            {
                vault.Lock();
            }
        }));

        // 保存加密秘密
        context.Commands.MapCommand("stronghold.saveSecret", (Func<string, string, string?, bool>)((key, value, vaultPath) =>
        {
            var path = string.IsNullOrEmpty(vaultPath) ? DefaultVaultPath : vaultPath;
            if (!_vaults.TryGetValue(path, out var vault) || !vault.IsUnlocked)
            {
                return false;
            }
            return vault.SetSecret(key, value);
        }));

        // 获取解密秘密
        context.Commands.MapCommand("stronghold.getSecret", (Func<string, string?, string?>)((key, vaultPath) =>
        {
            var path = string.IsNullOrEmpty(vaultPath) ? DefaultVaultPath : vaultPath;
            if (!_vaults.TryGetValue(path, out var vault) || !vault.IsUnlocked)
            {
                return null;
            }
            return vault.GetSecret(key);
        }));

        // 删除秘密
        context.Commands.MapCommand("stronghold.deleteSecret", (Func<string, string?, bool>)((key, vaultPath) =>
        {
            var path = string.IsNullOrEmpty(vaultPath) ? DefaultVaultPath : vaultPath;
            if (!_vaults.TryGetValue(path, out var vault) || !vault.IsUnlocked)
            {
                return false;
            }
            return vault.DeleteSecret(key);
        }));

        // 列出所有秘密键
        context.Commands.MapCommand("stronghold.listKeys", (Func<string?, string[]?>)((vaultPath) =>
        {
            var path = string.IsNullOrEmpty(vaultPath) ? DefaultVaultPath : vaultPath;
            if (!_vaults.TryGetValue(path, out var vault) || !vault.IsUnlocked)
            {
                return null;
            }
            return vault.ListKeys();
        }));

        // 检查金库是否已解锁
        context.Commands.MapCommand("stronghold.isUnlocked", (Func<string?, bool>)((vaultPath) =>
        {
            var path = string.IsNullOrEmpty(vaultPath) ? DefaultVaultPath : vaultPath;
            return _vaults.TryGetValue(path, out var vault) && vault.IsUnlocked;
        }));

        // 修改密码
        context.Commands.MapCommand("stronghold.changePassword", (Func<string, string, string?, bool>)((oldPassword, newPassword, vaultPath) =>
        {
            var path = string.IsNullOrEmpty(vaultPath) ? DefaultVaultPath : vaultPath;
            if (!_vaults.TryGetValue(path, out var vault))
            {
                return false;
            }
            return vault.ChangePassword(oldPassword, newPassword, Pbkdf2Iterations);
        }));
    }

    /// <summary>
    /// 加密金库实现。
    /// 使用 PBKDF2 从密码派生密钥，AES-GCM 加密数据，持久化到 JSON 文件。
    /// 文件格式：{ "salt": "...", "nonce": "...", "ciphertext": "...", "tag": "..." }
    /// 其中 ciphertext 为加密后的 JSON 字典 { key: value, ... }。
    /// </summary>
    private sealed class StrongholdVault : IDisposable
    {
        /// <summary>金库文件路径。</summary>
        private readonly string _path;

        /// <summary>当前解密密钥（解锁后非 null，锁定后 null）。</summary>
        private byte[]? _key;

        /// <summary>解密后的秘密字典（解锁后填充）。</summary>
        private readonly ConcurrentDictionary<string, string> _secrets = new();

        /// <summary>文件中存储的盐值。</summary>
        private byte[]? _salt;

        /// <summary>文件中存储的 nonce。</summary>
        private byte[]? _nonce;

        /// <summary>对象锁。</summary>
        private readonly object _lock = new();

        /// <summary>
        /// 指示金库是否已解锁。
        /// </summary>
        public bool IsUnlocked => _key is not null;

        /// <summary>
        /// 构造金库。
        /// </summary>
        /// <param name="path">金库文件路径。</param>
        public StrongholdVault(string path)
        {
            _path = path;
        }

        /// <summary>
        /// 使用密码解锁或创建金库。
        /// 若文件不存在则创建新金库；若存在则验证密码并解密。
        /// </summary>
        /// <param name="password">金库密码。</param>
        /// <param name="iterations">PBKDF2 迭代次数。</param>
        /// <returns>成功返回 true，密码错误返回 false。</returns>
        public bool Unlock(string password, int iterations)
        {
            lock (_lock)
            {
                if (string.IsNullOrEmpty(password))
                {
                    return false;
                }

                if (File.Exists(_path))
                {
                    // 加载现有金库
                    try
                    {
                        var json = File.ReadAllText(_path);
                        var container = JsonSerializer.Deserialize<VaultContainer>(json);
                        if (container is null
                            || string.IsNullOrEmpty(container.Salt)
                            || string.IsNullOrEmpty(container.Nonce)
                            || string.IsNullOrEmpty(container.Ciphertext)
                            || string.IsNullOrEmpty(container.Tag))
                        {
                            return false;
                        }

                        _salt = Convert.FromBase64String(container.Salt);
                        _nonce = Convert.FromBase64String(container.Nonce);
                        var ciphertext = Convert.FromBase64String(container.Ciphertext);
                        var tag = Convert.FromBase64String(container.Tag);

                        _key = Rfc2898DeriveBytes.Pbkdf2(
                            Encoding.UTF8.GetBytes(password), _salt, iterations, HashAlgorithmName.SHA256, 32);

                        // 尝试解密
                        try
                        {
                            var plaintext = new byte[ciphertext.Length];
                            using var aes = new AesGcm(_key, TagSize);
                            aes.Decrypt(_nonce, ciphertext, tag, plaintext);

                            var jsonDict = JsonSerializer.Deserialize<Dictionary<string, string>>(Encoding.UTF8.GetString(plaintext));
                            CryptographicOperations.ZeroMemory(plaintext);

                            if (jsonDict is not null)
                            {
                                _secrets.Clear();
                                foreach (var kv in jsonDict)
                                {
                                    _secrets[kv.Key] = kv.Value;
                                }
                            }
                            return true;
                        }
                        catch (CryptographicException)
                        {
                            // 密码错误
                            CryptographicOperations.ZeroMemory(_key);
                            _key = null;
                            _salt = null;
                            _nonce = null;
                            return false;
                        }
                    }
                    catch
                    {
                        return false;
                    }
                }
                else
                {
                    // 创建新金库
                    _salt = RandomNumberGenerator.GetBytes(16);
                    _nonce = RandomNumberGenerator.GetBytes(12);
                    _key = Rfc2898DeriveBytes.Pbkdf2(
                        Encoding.UTF8.GetBytes(password), _salt, iterations, HashAlgorithmName.SHA256, 32);
                    _secrets.Clear();
                    SaveVault();
                    return true;
                }
            }
        }

        /// <summary>
        /// 锁定金库，清除内存中的密钥和秘密数据。
        /// </summary>
        public void Lock()
        {
            lock (_lock)
            {
                if (_key is not null)
                {
                    CryptographicOperations.ZeroMemory(_key);
                    _key = null;
                }
                _salt = null;
                _nonce = null;
                _secrets.Clear();
            }
        }

        /// <summary>
        /// 保存秘密。
        /// </summary>
        /// <param name="key">键。</param>
        /// <param name="value">值。</param>
        /// <returns>成功返回 true。</returns>
        public bool SetSecret(string key, string value)
        {
            lock (_lock)
            {
                if (_key is null) return false;
                _secrets[key] = value;
                SaveVault();
                return true;
            }
        }

        /// <summary>
        /// 获取秘密。
        /// </summary>
        /// <param name="key">键。</param>
        /// <returns>值，未找到返回 null。</returns>
        public string? GetSecret(string key)
        {
            lock (_lock)
            {
                if (_key is null) return null;
                return _secrets.TryGetValue(key, out var value) ? value : null;
            }
        }

        /// <summary>
        /// 删除秘密。
        /// </summary>
        /// <param name="key">键。</param>
        /// <returns>删除成功返回 true，键不存在返回 false。</returns>
        public bool DeleteSecret(string key)
        {
            lock (_lock)
            {
                if (_key is null) return false;
                var removed = _secrets.TryRemove(key, out _);
                if (removed) SaveVault();
                return removed;
            }
        }

        /// <summary>
        /// 列出所有秘密键。
        /// </summary>
        /// <returns>键数组。</returns>
        public string[] ListKeys()
        {
            lock (_lock)
            {
                if (_key is null) return Array.Empty<string>();
                return _secrets.Keys.ToArray();
            }
        }

        /// <summary>
        /// 修改金库密码。
        /// </summary>
        /// <param name="oldPassword">旧密码。</param>
        /// <param name="newPassword">新密码。</param>
        /// <param name="iterations">PBKDF2 迭代次数。</param>
        /// <returns>成功返回 true，旧密码错误返回 false。</returns>
        public bool ChangePassword(string oldPassword, string newPassword, int iterations)
        {
            lock (_lock)
            {
                if (_key is null || _salt is null) return false;

                // 验证旧密码：尝试用旧密码重新派生密钥，应与当前密钥一致
                var oldKey = Rfc2898DeriveBytes.Pbkdf2(
                    Encoding.UTF8.GetBytes(oldPassword), _salt, iterations, HashAlgorithmName.SHA256, 32);
                if (!oldKey.SequenceEqual(_key))
                {
                    CryptographicOperations.ZeroMemory(oldKey);
                    return false;
                }

                // 生成新盐和密钥
                _salt = RandomNumberGenerator.GetBytes(16);
                _nonce = RandomNumberGenerator.GetBytes(12);
                CryptographicOperations.ZeroMemory(_key);
                CryptographicOperations.ZeroMemory(oldKey);
                _key = Rfc2898DeriveBytes.Pbkdf2(
                    Encoding.UTF8.GetBytes(newPassword), _salt, iterations, HashAlgorithmName.SHA256, 32);
                SaveVault();
                return true;
            }
        }

        /// <summary>
        /// 将当前秘密字典加密保存到文件。
        /// </summary>
        private void SaveVault()
        {
            if (_key is null || _salt is null || _nonce is null) return;

            var json = JsonSerializer.Serialize(_secrets.ToDictionary(kv => kv.Key, kv => kv.Value));
            var plaintext = Encoding.UTF8.GetBytes(json);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[TagSize];

            using (var aes = new AesGcm(_key, TagSize))
            {
                aes.Encrypt(_nonce, plaintext, ciphertext, tag);
            }

            var container = new VaultContainer
            {
                Salt = Convert.ToBase64String(_salt),
                Nonce = Convert.ToBase64String(_nonce),
                Ciphertext = Convert.ToBase64String(ciphertext),
                Tag = Convert.ToBase64String(tag)
            };

            var fileJson = JsonSerializer.Serialize(container, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_path, fileJson);

            CryptographicOperations.ZeroMemory(plaintext);
        }

        public void Dispose()
        {
            Lock();
        }
    }

    /// <summary>
    /// 金库文件容器（JSON 序列化用）。
    /// </summary>
    private sealed class VaultContainer
    {
        /// <summary>PBKDF2 盐值（Base64）。</summary>
        public string Salt { get; set; } = string.Empty;

        /// <summary>AES-GCM nonce（Base64）。</summary>
        public string Nonce { get; set; } = string.Empty;

        /// <summary>加密数据（Base64）。</summary>
        public string Ciphertext { get; set; } = string.Empty;

        /// <summary>AES-GCM 认证标签（Base64）。</summary>
        public string Tag { get; set; } = string.Empty;
    }
}
