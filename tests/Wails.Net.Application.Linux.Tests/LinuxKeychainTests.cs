using TUnit.Assertions;
using TUnit.Core;
using Wails.Net.Application.Platform;

namespace Wails.Net.Application.Linux.Tests;

/// <summary>
/// LinuxKeychain 的单元测试（P1：Linux Keychain / libsecret 补齐）。
/// <para>
/// 通过注入 <see cref="ILinuxSecretToolRunner"/> 假实现验证命令构造、标准输入与退出码语义，
/// 不依赖本机是否安装 <c>secret-tool</c>。
/// </para>
/// </summary>
[NotInParallel]
public sealed class LinuxKeychainTests
{
    [Test]
    public async Task SetPassword_UsesStoreWithServiceAndAccount()
    {
        var runner = new FakeRunner(new LinuxSecretToolResult(0, string.Empty, string.Empty, ToolMissing: false));
        var keychain = new LinuxKeychain(runner);

        var result = keychain.SetPassword("my-service", "user@example.com", "s3cret");

        await Assert.That(result).IsTrue();
        await Assert.That(runner.Calls.Count).IsEqualTo(1);
        var call = runner.Calls[0];
        await Assert.That(call.StandardInput).IsEqualTo("s3cret");
        await Assert.That(call.Arguments)
            .IsEquivalentTo(new[]
            {
                "store", "--label", "Wails.Net:my-service", "service", "my-service", "account", "user@example.com",
            });
    }

    [Test]
    public async Task SetPassword_ToolMissing_ReturnsFalse()
    {
        var runner = new FakeRunner(new LinuxSecretToolResult(-1, string.Empty, "missing", ToolMissing: true));
        var keychain = new LinuxKeychain(runner);

        var result = keychain.SetPassword("service", "account", "password");

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task SetPassword_NonZeroExit_ReturnsFalse()
    {
        var runner = new FakeRunner(new LinuxSecretToolResult(1, string.Empty, "failed", ToolMissing: false));
        var keychain = new LinuxKeychain(runner);

        var result = keychain.SetPassword("service", "account", "password");

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task GetPassword_TrimsTrailingNewline()
    {
        var runner = new FakeRunner(new LinuxSecretToolResult(0, "s3cret\n", string.Empty, ToolMissing: false));
        var keychain = new LinuxKeychain(runner);

        var password = keychain.GetPassword("service", "account");

        await Assert.That(password).IsEqualTo("s3cret");
        await Assert.That(runner.Calls[0].StandardInput).IsNull();
        await Assert.That(runner.Calls[0].Arguments)
            .IsEquivalentTo(new[] { "lookup", "service", "service", "account", "account" });
    }

    [Test]
    public async Task GetPassword_NotFound_ReturnsNull()
    {
        var runner = new FakeRunner(new LinuxSecretToolResult(1, string.Empty, string.Empty, ToolMissing: false));
        var keychain = new LinuxKeychain(runner);

        var password = keychain.GetPassword("service", "account");

        await Assert.That(password).IsNull();
    }

    [Test]
    public async Task DeletePassword_NotFound_ReturnsTrue()
    {
        var runner = new FakeRunner(new LinuxSecretToolResult(1, string.Empty, string.Empty, ToolMissing: false));
        var keychain = new LinuxKeychain(runner);

        var result = keychain.DeletePassword("service", "account");

        await Assert.That(result).IsTrue();
        await Assert.That(runner.Calls[0].Arguments)
            .IsEquivalentTo(new[] { "clear", "service", "service", "account", "account" });
    }

    [Test]
    public async Task DeletePassword_Error_ReturnsFalse()
    {
        var runner = new FakeRunner(new LinuxSecretToolResult(2, string.Empty, "error", ToolMissing: false));
        var keychain = new LinuxKeychain(runner);

        var result = keychain.DeletePassword("service", "account");

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task InvalidArguments_DoNotInvokeSecretTool()
    {
        var runner = new FakeRunner(new LinuxSecretToolResult(0, string.Empty, string.Empty, ToolMissing: false));
        var keychain = new LinuxKeychain(runner);

        await Assert.That(keychain.SetPassword(string.Empty, "account", "password")).IsFalse();
        await Assert.That(keychain.SetPassword("service", string.Empty, "password")).IsFalse();
        await Assert.That(keychain.GetPassword(null!, "account")).IsNull();
        await Assert.That(keychain.DeletePassword("service", null!)).IsFalse();
        await Assert.That(runner.Calls.Count).IsEqualTo(0);
    }

    private sealed class FakeRunner : ILinuxSecretToolRunner
    {
        private readonly LinuxSecretToolResult _result;

        public FakeRunner(LinuxSecretToolResult result)
        {
            _result = result;
        }

        public List<FakeCall> Calls { get; } = [];

        public LinuxSecretToolResult Run(IReadOnlyList<string> args, string? standardInput)
        {
            Calls.Add(new FakeCall(args.ToArray(), standardInput));
            return _result;
        }
    }

    private sealed record FakeCall(string[] Arguments, string? StandardInput);
}
