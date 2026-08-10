using TUnit.Core;
using Wails.Net.Application.Security;

namespace Wails.Net.Application.Tests.Security;

/// <summary>
/// IPC handshake token 单元测试（OPT-IPC-002）。
/// 覆盖 token 生成格式、进程内稳定性与不可预测性。
/// </summary>
[NotInParallel]
public sealed class IpcHandshakeTokenTests
{
    [Test]
    public async Task Generate_ReturnsNonEmptyToken()
    {
        var token = IpcHandshakeToken.Generate();

        await Assert.That(token).IsNotNull();
        await Assert.That(token.Length).IsGreaterThan(20);
    }

    [Test]
    public async Task Generate_ProducesBase64UrlSafeChars()
    {
        var token = IpcHandshakeToken.Generate();

        // Base64Url：字母、数字、-、_（无 + / 与填充 =）
        await Assert.That(token.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_')).IsFalse();
        await Assert.That(token.Contains('+')).IsFalse();
        await Assert.That(token.Contains('/')).IsFalse();
        await Assert.That(token.EndsWith('=')).IsFalse();
    }

    [Test]
    public async Task Current_IsStableWithinProcess()
    {
        var first = IpcHandshakeToken.Current;
        var second = IpcHandshakeToken.Current;

        await Assert.That(first).IsEqualTo(second);
    }

    [Test]
    public async Task Generate_ProducesDifferentTokensAcrossCalls()
    {
        var t1 = IpcHandshakeToken.Generate();
        var t2 = IpcHandshakeToken.Generate();

        await Assert.That(t1).IsNotEqualTo(t2);
    }

    [Test]
    public async Task Current_IsHighEntropy()
    {
        // 32 字节 CSPRNG → Base64Url 无填充，长度约 43 字符
        var token = IpcHandshakeToken.Current;

        await Assert.That(token.Length).IsGreaterThanOrEqualTo(40);
    }
}
