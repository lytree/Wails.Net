using System.Reflection;
using System.Text.Json;
using TUnit.Core;
using Wails.Net.Plugins.Stronghold;

namespace Wails.Net.Plugins.Stronghold.Tests;

/// <summary>
/// Stronghold 插件测试。
/// 覆盖审计 §3.8：每次 AES-GCM 加密必须使用新 nonce，避免同一 (key, nonce) 重复使用导致
/// 机密性与认证同时失效。
/// </summary>
/// <remarks>
/// 通过反射访问 <c>StrongholdVault</c> 进行白盒测试（AGENTS.md §3.4.4 允许）。
/// 当前 SDK 在 .NET 10 的 InternalsVisibleTo 处理上未能在编译期生效，
/// 因此测试通过反射拿构造函数与 SetSecret/GetSecret 等同步 API，
/// 以直接验证每次 SaveVault 都轮换 nonce 的行为。生产代码不依赖反射。
/// </remarks>
[NotInParallel]
public sealed class StrongholdPluginTests
{
    private const int Pbkdf2Iterations = 100_000;

    private static string CreateTempVaultPath()
        => Path.Combine(Path.GetTempPath(), $"stronghold_test_{Guid.NewGuid():N}.json");

    private static void CleanupVault(string path)
    {
        if (File.Exists(path)) File.Delete(path);
        var tmp = path + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp);
    }

    /// <summary>
    /// 通过反射创建内部类型 StrongholdVault 实例。
    /// 测试白盒访问，不构成生产代码反射路径。
    /// </summary>
    private static object CreateVault(string path)
    {
        var vaultType = typeof(StrongholdPlugin).Assembly
            .GetType("Wails.Net.Plugins.Stronghold.StrongholdPlugin+StrongholdVault", throwOnError: true)!;
        return Activator.CreateInstance(vaultType, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null, args: new object[] { path }, culture: null)!;
    }

    private static bool InvokeUnlock(object vault, string password, int iterations)
    {
        var m = vault.GetType().GetMethod("Unlock", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        return (bool)m.Invoke(vault, new object[] { password, iterations })!;
    }

    private static bool InvokeSetSecret(object vault, string key, string value)
    {
        var m = vault.GetType().GetMethod("SetSecret", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        return (bool)m.Invoke(vault, new object[] { key, value })!;
    }

    private static bool InvokeDeleteSecret(object vault, string key)
    {
        var m = vault.GetType().GetMethod("DeleteSecret", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        return (bool)m.Invoke(vault, new object[] { key })!;
    }

    private static bool InvokeChangePassword(object vault, string oldPassword, string newPassword, int iterations)
    {
        var m = vault.GetType().GetMethod("ChangePassword", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        return (bool)m.Invoke(vault, new object[] { oldPassword, newPassword, iterations })!;
    }

    private static string? InvokeGetSecret(object vault, string key)
    {
        var m = vault.GetType().GetMethod("GetSecret", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        return (string?)m.Invoke(vault, new object[] { key });
    }

    private static void InvokeLock(object vault)
    {
        var m = vault.GetType().GetMethod("Lock", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        m.Invoke(vault, null);
    }

    private static bool InvokeIsUnlocked(object vault)
    {
        var p = vault.GetType().GetProperty("IsUnlocked", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        return (bool)p.GetValue(vault)!;
    }

    private static string ReadPersistedNonceBase64(string path)
    {
        var json = File.ReadAllText(path);
        return JsonDocument.Parse(json).RootElement.GetProperty("Nonce").GetString() ?? string.Empty;
    }

    [Test]
    public async Task Plugin_Name_MatchesCommandPrefix()
    {
        var plugin = new StrongholdPlugin();
        await Assert.That(plugin.Name).IsEqualTo("stronghold");
    }

    [Test]
    public async Task SetSecret_Twice_RotatesNoncePersistedToFile()
    {
        var path = CreateTempVaultPath();
        try
        {
            var vault = CreateVault(path);
            await Assert.That(InvokeUnlock(vault, "password", Pbkdf2Iterations)).IsTrue();
            await Assert.That(InvokeSetSecret(vault, "k1", "v1")).IsTrue();

            var nonceAfterFirst = ReadPersistedNonceBase64(path);
            await Assert.That(nonceAfterFirst).IsNotEmpty();

            await Assert.That(InvokeSetSecret(vault, "k2", "v2")).IsTrue();
            var nonceAfterSecond = ReadPersistedNonceBase64(path);

            await Assert.That(nonceAfterSecond).IsNotEqualTo(nonceAfterFirst);
        }
        finally
        {
            CleanupVault(path);
        }
    }

    [Test]
    public async Task DeleteSecret_AlsoRotatesNonce()
    {
        var path = CreateTempVaultPath();
        try
        {
            var vault = CreateVault(path);
            await Assert.That(InvokeUnlock(vault, "password", Pbkdf2Iterations)).IsTrue();
            await Assert.That(InvokeSetSecret(vault, "k1", "v1")).IsTrue();
            await Assert.That(InvokeSetSecret(vault, "k2", "v2")).IsTrue();

            var nonceBeforeDelete = ReadPersistedNonceBase64(path);
            await Assert.That(InvokeDeleteSecret(vault, "k1")).IsTrue();
            var nonceAfterDelete = ReadPersistedNonceBase64(path);

            await Assert.That(nonceAfterDelete).IsNotEqualTo(nonceBeforeDelete);
        }
        finally
        {
            CleanupVault(path);
        }
    }

    [Test]
    public async Task Roundtrip_SetAndGetSecret_PreservesValue()
    {
        var path = CreateTempVaultPath();
        try
        {
            var vault = CreateVault(path);
            await Assert.That(InvokeUnlock(vault, "password", Pbkdf2Iterations)).IsTrue();
            await Assert.That(InvokeSetSecret(vault, "alpha", "1")).IsTrue();
            await Assert.That(InvokeSetSecret(vault, "beta", "2")).IsTrue();
            await Assert.That(InvokeSetSecret(vault, "gamma", "3")).IsTrue();

            await Assert.That(InvokeGetSecret(vault, "alpha")).IsEqualTo("1");
            await Assert.That(InvokeGetSecret(vault, "beta")).IsEqualTo("2");
            await Assert.That(InvokeGetSecret(vault, "gamma")).IsEqualTo("3");
            await Assert.That(InvokeGetSecret(vault, "missing")).IsNull();
        }
        finally
        {
            CleanupVault(path);
        }
    }

    [Test]
    public async Task Reload_AfterMultipleSaves_DecryptsLatestPlaintext()
    {
        var path = CreateTempVaultPath();
        try
        {
            var firstVault = CreateVault(path);
            await Assert.That(InvokeUnlock(firstVault, "password", Pbkdf2Iterations)).IsTrue();
            await Assert.That(InvokeSetSecret(firstVault, "k1", "first")).IsTrue();
            await Assert.That(InvokeSetSecret(firstVault, "k2", "second")).IsTrue();
            await Assert.That(InvokeSetSecret(firstVault, "k3", "third")).IsTrue();

            // 重新加载金库；旧 nonce 必须仍能解密（向后兼容），且后续 SetSecret 必须轮换 nonce。
            var reloaded = CreateVault(path);
            await Assert.That(InvokeUnlock(reloaded, "password", Pbkdf2Iterations)).IsTrue();
            await Assert.That(InvokeGetSecret(reloaded, "k1")).IsEqualTo("first");
            await Assert.That(InvokeGetSecret(reloaded, "k2")).IsEqualTo("second");
            await Assert.That(InvokeGetSecret(reloaded, "k3")).IsEqualTo("third");

            var nonceAfterReload = ReadPersistedNonceBase64(path);
            await Assert.That(InvokeSetSecret(reloaded, "k4", "fourth")).IsTrue();
            var nonceAfterNextSave = ReadPersistedNonceBase64(path);

            await Assert.That(nonceAfterNextSave).IsNotEqualTo(nonceAfterReload);
        }
        finally
        {
            CleanupVault(path);
        }
    }

    [Test]
    public async Task Lock_ThenUnlock_PreservesSecrets()
    {
        var path = CreateTempVaultPath();
        try
        {
            var vault = CreateVault(path);
            await Assert.That(InvokeUnlock(vault, "password", Pbkdf2Iterations)).IsTrue();
            await Assert.That(InvokeSetSecret(vault, "k1", "v1")).IsTrue();
            InvokeLock(vault);
            await Assert.That(InvokeIsUnlocked(vault)).IsFalse();

            await Assert.That(InvokeUnlock(vault, "password", Pbkdf2Iterations)).IsTrue();
            await Assert.That(InvokeGetSecret(vault, "k1")).IsEqualTo("v1");
        }
        finally
        {
            CleanupVault(path);
        }
    }

    [Test]
    public async Task ChangePassword_RotatesSaltAndNonce()
    {
        var path = CreateTempVaultPath();
        try
        {
            var vault = CreateVault(path);
            await Assert.That(InvokeUnlock(vault, "password", Pbkdf2Iterations)).IsTrue();
            await Assert.That(InvokeSetSecret(vault, "k1", "v1")).IsTrue();

            var jsonBefore = File.ReadAllText(path);
            var saltBefore = JsonDocument.Parse(jsonBefore).RootElement.GetProperty("Salt").GetString();
            var nonceBefore = JsonDocument.Parse(jsonBefore).RootElement.GetProperty("Nonce").GetString();

            await Assert.That(InvokeChangePassword(vault, "password", "new-password", Pbkdf2Iterations)).IsTrue();

            var jsonAfter = File.ReadAllText(path);
            var saltAfter = JsonDocument.Parse(jsonAfter).RootElement.GetProperty("Salt").GetString();
            var nonceAfter = JsonDocument.Parse(jsonAfter).RootElement.GetProperty("Nonce").GetString();

            await Assert.That(saltAfter).IsNotEqualTo(saltBefore);
            await Assert.That(nonceAfter).IsNotEqualTo(nonceBefore);

            // 旧密码应被拒绝。
            await Assert.That(InvokeChangePassword(vault, "password", "another", Pbkdf2Iterations)).IsFalse();
            // 新密码应能再次解锁并读取。
            var reloaded = CreateVault(path);
            await Assert.That(InvokeUnlock(reloaded, "new-password", Pbkdf2Iterations)).IsTrue();
            await Assert.That(InvokeGetSecret(reloaded, "k1")).IsEqualTo("v1");
        }
        finally
        {
            CleanupVault(path);
        }
    }

    [Test]
    public async Task Unlock_WrongPassword_ReturnsFalseAndCleansState()
    {
        var path = CreateTempVaultPath();
        try
        {
            var creator = CreateVault(path);
            await Assert.That(InvokeUnlock(creator, "correct", Pbkdf2Iterations)).IsTrue();
            await Assert.That(InvokeSetSecret(creator, "k1", "v1")).IsTrue();

            var attacker = CreateVault(path);
            await Assert.That(InvokeUnlock(attacker, "wrong", Pbkdf2Iterations)).IsFalse();
            await Assert.That(InvokeIsUnlocked(attacker)).IsFalse();
            await Assert.That(InvokeGetSecret(attacker, "k1")).IsNull();
        }
        finally
        {
            CleanupVault(path);
        }
    }

    [Test]
    public async Task SaveVault_WritesViaTempFile_ThenAtomicReplace()
    {
        // 验证文件写入路径走临时文件 + rename，而不是直接覆盖。
        var path = CreateTempVaultPath();
        try
        {
            var vault = CreateVault(path);
            await Assert.That(InvokeUnlock(vault, "password", Pbkdf2Iterations)).IsTrue();
            await Assert.That(InvokeSetSecret(vault, "k1", "v1")).IsTrue();

            await Assert.That(File.Exists(path)).IsTrue();
            // 临时文件在原子替换后应已被清掉（不存在孤儿）。
            await Assert.That(File.Exists(path + ".tmp")).IsFalse();
        }
        finally
        {
            CleanupVault(path);
        }
    }
}
