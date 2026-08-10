using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TUnit.Core;
using Wails.Net.Application.Security;

namespace Wails.Net.Application.Tests.Security;

/// <summary>
/// Capability.platforms 平台过滤单元测试（OPT-SEC-002）。
/// 覆盖：
/// <list type="bullet">
/// <item>Platforms 为空 → 所有平台生效。</item>
/// <item>Platforms 非空 → 仅当前平台匹配（大小写不敏感）时授权。</item>
/// <item>RegisterToManager 在当前平台不匹配时跳过权限授权。</item>
/// <item>未知平台名在加载阶段输出 warning。</item>
/// </list>
/// </summary>
[NotInParallel]
public sealed class CapabilityPlatformTests
{
    private static PermissionManager CreateManager(string currentPlatform)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new PermissionOptions
        {
            Enabled = true,
            DenyByDefault = true,
        });
        return new PermissionManager(options, NullLogger<PermissionManager>.Instance, currentPlatform);
    }

    [Test]
    public async Task Platforms_Default_IsEmpty()
    {
        var capability = new Capability("test-capability");

        await Assert.That(capability.Platforms).IsNotNull();
        await Assert.That(capability.Platforms.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Platforms_Empty_AppliesToAllPlatforms()
    {
        var manager = CreateManager(WailsPlatform.Windows);
        var capability = new Capability("test-capability"); // Platforms 空

        await Assert.That(manager.IsCapabilityApplicableToPlatform(capability)).IsTrue();
    }

    [Test]
    public async Task Platforms_MatchingCurrentPlatform_Applies()
    {
        var manager = CreateManager(WailsPlatform.Linux);
        var capability = new Capability("test-capability") { Platforms = { WailsPlatform.Linux } };

        await Assert.That(manager.IsCapabilityApplicableToPlatform(capability)).IsTrue();
    }

    [Test]
    public async Task Platforms_NotMatchingCurrentPlatform_DoesNotApply()
    {
        var manager = CreateManager(WailsPlatform.Linux);
        var capability = new Capability("test-capability") { Platforms = { WailsPlatform.Windows } };

        await Assert.That(manager.IsCapabilityApplicableToPlatform(capability)).IsFalse();
    }

    [Test]
    public async Task Platforms_CaseInsensitive_Matching()
    {
        var manager = CreateManager(WailsPlatform.Android);
        var capability = new Capability("test-capability") { Platforms = { "ANDROID" } };

        await Assert.That(manager.IsCapabilityApplicableToPlatform(capability)).IsTrue();
    }

    [Test]
    public async Task RegisterToManager_PlatformMismatch_DoesNotGrantPermissions()
    {
        var manager = CreateManager(WailsPlatform.Linux); // 当前平台 Linux
        var capabilities = new List<Capability>
        {
            new("windows-only")
            {
                Platforms = { WailsPlatform.Windows },
                Permissions = { "fs:allow-read" },
            },
        };

        CapabilityFileLoader.RegisterToManager(manager, capabilities);

        // 能力已声明，但权限未授权（平台不匹配）
        await Assert.That(manager.IsGranted("fs:allow-read")).IsFalse();
    }

    [Test]
    public async Task RegisterToManager_PlatformMatch_GrantsPermissions()
    {
        var manager = CreateManager(WailsPlatform.Windows); // 当前平台 Windows
        var capabilities = new List<Capability>
        {
            new("windows-only")
            {
                Platforms = { WailsPlatform.Windows },
                Permissions = { "fs:allow-read" },
            },
        };

        CapabilityFileLoader.RegisterToManager(manager, capabilities);

        await Assert.That(manager.IsGranted("fs:allow-read")).IsTrue();
    }

    [Test]
    public async Task RegisterToManager_ServerPlatform_GrantsServerCapability()
    {
        var manager = CreateManager(WailsPlatform.Server); // Server 模式
        var capabilities = new List<Capability>
        {
            new("server-only")
            {
                Platforms = { WailsPlatform.Server },
                Permissions = { "http:allow-fetch" },
            },
            new("desktop-only")
            {
                Platforms = { WailsPlatform.Windows, WailsPlatform.Linux },
                Permissions = { "window:allow-close" },
            },
        };

        CapabilityFileLoader.RegisterToManager(manager, capabilities);

        await Assert.That(manager.IsGranted("http:allow-fetch")).IsTrue();
        await Assert.That(manager.IsGranted("window:allow-close")).IsFalse();
    }

    [Test]
    public async Task LoadFromDirectory_UnknownPlatform_LogsWarning()
    {
        // 构造临时能力文件目录
        var dir = Path.Combine(Path.GetTempPath(), "wails-capabilities-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(dir, "bad-platform.json"),
                """{ "identifier": "bad", "permissions": ["core:default"], "platforms": ["win32"] }""");

            var logger = new ListLogger();
            var capabilities = CapabilityFileLoader.LoadFromDirectory(dir, logger);

            await Assert.That(capabilities.Count).IsEqualTo(1);
            await Assert.That(logger.WarningMessages.Count).IsGreaterThan(0);
            await Assert.That(logger.WarningMessages[0]).Contains("win32");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// 简单内存 ILogger，收集 warning 消息用于断言。
    /// </summary>
    private sealed class ListLogger : ILogger
    {
        public List<string> WarningMessages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                WarningMessages.Add(formatter(state, exception));
            }
        }
    }
}
