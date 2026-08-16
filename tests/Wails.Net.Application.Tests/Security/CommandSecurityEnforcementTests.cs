using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Assertions;
using TUnit.Core;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Security;

namespace Wails.Net.Application.Tests.Security;

/// <summary>
/// SEC-H-001 阶段一：<see cref="CommandSecurity"/> API 接线测试（TUnit）。
/// <para>
/// 覆盖三项验收标准（docs/architecture/refactor-and-hardening-plan.md §2.1.4）：
/// </para>
/// <list type="number">
/// <item><c>CommandSecurity.Require(...)</c> 通过 <see cref="CommandDispatcher"/> 的 capability 校验链；</item>
/// <item><c>CommandSecurity.WithScope(...)</c> 显式 ScopeMapping 优先于源生成器 extractor；</item>
/// <item><c>CommandSecurity.Public()</c> 被 <see cref="Application.Manifests.ManifestGenerator"/> 识别为公开命令。</item>
/// </list>
/// <para>遵循 AGENTS.md §3.4：零反射，委托通过源生成器或编译期闭包构建。</para>
/// </summary>
[NotInParallel]
public sealed class CommandSecurityEnforcementTests
{
    private static PermissionManager CreateManager(bool enabled, bool denyByDefault, params string[] permissions)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new PermissionOptions
        {
            Enabled = enabled,
            DenyByDefault = denyByDefault,
            Permissions = permissions.ToList(),
        });
        return new PermissionManager(options, NullLogger<PermissionManager>.Instance);
    }

    private static IServiceProvider EmptyServices() => new ServiceCollection().BuildServiceProvider();

    /// <summary>
    /// 构建一个测试用 registry：注册 "fs.read" 命令（参数来自 JSON 对象 "path" 字段），
    /// 返回 "read:{path}"。使用新 <see cref="Register(string, CompiledCommandInvoker, IReadOnlyList{Type}, CommandSecurity?)"/> 重载，
    /// 让 <see cref="CommandSecurity"/> 真正落地到 <see cref="CommandRegistry.CommandEntry.Security"/>。
    /// <para>
    /// 手动构建 <see cref="CompiledCommandInvoker"/>：从 JSON 对象中按属性名取 path 字符串，
    /// 模拟 SourceGenerator 为 FileSystemPlugin.ReadText 生成的 invoker 形态（与 ScopeValidationTests 风格一致）。
    /// 这样既能让 <see cref="CommandDispatcher.ExtractScopeValues"/> 按 WithScope("path", ...) 取值，
    /// 又能让命令体执行后返回可断言的结果。
    /// </para>
    /// </summary>
    private static CommandRegistry BuildFsReadRegistry(CommandSecurity? security)
    {
        var registry = new CommandRegistry();
        CompiledCommandInvoker invoker = (instance, parameters, ctx) =>
        {
            var path = parameters.TryGetProperty("path", out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? string.Empty
                : string.Empty;
            return Task.FromResult<object?>($"read:{path}");
        };
        registry.Register("fs.read", invoker, new[] { typeof(string) }, security);
        return registry;
    }

    /// <summary>
    /// CommandSecurity.Require 的 capability 在 dispatcher 链路生效：未授权被拒。
    /// </summary>
    [Test]
    public async Task Require_Capability_WithoutPermission_IsDenied()
    {
        var registry = BuildFsReadRegistry(CommandSecurity.Require("fs:allow-read"));
        var manager = CreateManager(enabled: true, denyByDefault: true /* 不授予 fs:allow-read */);
        var dispatcher = new CommandDispatcher(registry, EmptyServices(),
            NullLogger<CommandDispatcher>.Instance, manager);

        var request = new InvokeRequest(Guid.NewGuid(), "fs.read",
            JsonDocument.Parse(@"{""path"":""/tmp/x""}").RootElement);
        var response = await dispatcher.DispatchAsync(request);

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Error).Contains("Permission denied");
    }

    /// <summary>
    /// CommandSecurity.Require 的 capability 在 dispatcher 链路生效：授权后放行。
    /// </summary>
    [Test]
    public async Task Require_Capability_WithPermission_IsAllowed()
    {
        var registry = BuildFsReadRegistry(CommandSecurity.Require("fs:allow-read"));
        var manager = CreateManager(enabled: true, denyByDefault: true, "fs:allow-read");
        var dispatcher = new CommandDispatcher(registry, EmptyServices(),
            NullLogger<CommandDispatcher>.Instance, manager);

        var request = new InvokeRequest(Guid.NewGuid(), "fs.read",
            JsonDocument.Parse(@"{""path"":""/tmp/x""}").RootElement);
        var response = await dispatcher.DispatchAsync(request);

        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Result).IsEqualTo("read:/tmp/x");
    }

    /// <summary>
    /// CommandSecurity.WithScope 的 ScopeMapping 在 dispatcher 链路生效：scope 内放行、scope 外拒绝。
    /// 显式声明优先：即使源生成器为同名命令注册了 extractor（模拟），本测试也只验证显式路径。
    /// </summary>
    [Test]
    public async Task WithScope_ExplicitMapping_DrivesScopeValidation()
    {
        var registry = BuildFsReadRegistry(
            CommandSecurity.Require("fs:allow-read").WithScope("path", "fs:allow-read"));
        var manager = CreateManager(enabled: true, denyByDefault: true, "fs:allow-read");
        var fsScope = new FileSystemScope();
        fsScope.AddPath("/tmp");
        manager.SetScope("fs:allow-read", fsScope);
        var dispatcher = new CommandDispatcher(registry, EmptyServices(),
            NullLogger<CommandDispatcher>.Instance, manager);

        // 范围外：拒绝
        var outReq = new InvokeRequest(Guid.NewGuid(), "fs.read",
            JsonDocument.Parse(@"{""path"":""/etc/passwd""}").RootElement);
        var outResp = await dispatcher.DispatchAsync(outReq);
        await Assert.That(outResp.Success).IsFalse();
        await Assert.That(outResp.Error).Contains("Scope denied");

        // 范围内：放行
        var inReq = new InvokeRequest(Guid.NewGuid(), "fs.read",
            JsonDocument.Parse(@"{""path"":""/tmp/file.txt""}").RootElement);
        var inResp = await dispatcher.DispatchAsync(inReq);
        await Assert.That(inResp.Success).IsTrue();
    }

    /// <summary>
    /// 显式 ScopeMapping 优先于源生成器注册的 extractor（双来源合并：显式在前，生成器兜底）。
    /// 验证：显式 WithScope("path", "fs:allow-read") 在调用，而源生成器为同名命令注册了不同 permission 的 extractor。
    /// 期望 dispatcher 仅以显式声明为准。
    /// </summary>
    [Test]
    public async Task WithScope_ExplicitMapping_OverridesGeneratedExtractor()
    {
        var registry = BuildFsReadRegistry(
            CommandSecurity.Require("fs:allow-read").WithScope("path", "fs:allow-read"));

        // 模拟源生成器为同名命令注册了一个指向 *不同* 权限的 extractor
        // （即"如果走源生成器路径就会用错的权限去校验"。本测试断言显式声明不会被它覆盖。）
        var generatedExtractor = (Wails.Net.Application.Bindings.ScopeExtractor)(p =>
        {
            if (p.TryGetProperty("path", out var v) && v.ValueKind == JsonValueKind.String)
            {
                var s = v.GetString();
                if (!string.IsNullOrEmpty(s)) return ("fs:WRONG-permission", s);
            }
            return null;
        });
        Wails.Net.Application.Bindings.GeneratedBindingRegistry.Register(
            "fs.read",
            (_, _, _) => Task.FromResult<object?>(null),
            typeof(CommandSecurityEnforcementTests).FullName!,
            requiredCapabilities: null,
            scopeExtractors: new[] { generatedExtractor });

        try
        {
            var manager = CreateManager(enabled: true, denyByDefault: true, "fs:allow-read");
            var fsScope = new FileSystemScope();
            fsScope.AddPath("/tmp");
            manager.SetScope("fs:allow-read", fsScope);
            // 不注册 fs:WRONG-permission → 如果 dispatcher 用了生成的 extractor，会因为权限未授予而失败；
            // 但因为显式 WithScope 优先，dispatcher 会用 fs:allow-read（在 scope 内）放行。
            var dispatcher = new CommandDispatcher(registry, EmptyServices(),
                NullLogger<CommandDispatcher>.Instance, manager);

            var request = new InvokeRequest(Guid.NewGuid(), "fs.read",
                JsonDocument.Parse(@"{""path"":""/tmp/file.txt""}").RootElement);
            var response = await dispatcher.DispatchAsync(request);

            await Assert.That(response.Success).IsTrue();
            await Assert.That(response.Result).IsEqualTo("read:/tmp/file.txt");
        }
        finally
        {
            // 定向移除本测试注册的 "fs.read"，避免污染随后执行的
            // Require_Capability_WithPermission_IsAllowed 等依赖无 scope extractor 的测试。
            // 不能使用 Clear()（会清空源生成器在进程启动时注册的其他调用器）。
            Wails.Net.Application.Bindings.GeneratedBindingRegistry.Remove("fs.read");
        }
    }

    /// <summary>
    /// backward compat：未声明 Security 的匿名命令（缺省重载）仍按旧路径通过 capability 校验。
    /// 验证新增 Security 字段为 null 时不破坏已有行为（与设计 §2.1.2 兼容性条款一致）。
    /// </summary>
    [Test]
    public async Task AnonymousCommand_NoSecurity_StaysAllowed()
    {
        var registry = new CommandRegistry();
        // 旧重载，无 capability、无 Security
        registry.MapCommand("app.ping", (Func<string, string>)(s => $"pong:{s}"));
        var manager = CreateManager(enabled: true, denyByDefault: true /* 不授予任何权限 */);
        var dispatcher = new CommandDispatcher(registry, EmptyServices(),
            NullLogger<CommandDispatcher>.Instance, manager);

        var request = new InvokeRequest(Guid.NewGuid(), "app.ping",
            JsonDocument.Parse(@"""hi""").RootElement);
        var response = await dispatcher.DispatchAsync(request);

        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Result).IsEqualTo("pong:hi");
    }

    /// <summary>
    /// CommandSecurity.Public() 的 IsPublic 字段被 ManifestGenerator.FindUncoveredCapabilities 识别为公开命令。
    /// 在 capability 列表空集合下，公开命令不应被报告为未覆盖。
    /// </summary>
    [Test]
    public async Task Public_IsPublicFlag_IsSkippedByManifestCoverageCheck()
    {
        var registry = new CommandRegistry();
        registry.MapCommand("app.info", (Func<string, string>)(s => s),
            CommandSecurity.Public());

        // 空 capability 文件：未授予任何权限
        var caps = new List<Wails.Net.Application.Security.Capability>();
        var uncovered = Wails.Net.Application.Manifests.ManifestGenerator.FindUncoveredCapabilities(
            registry, caps);
        await Assert.That(uncovered).DoesNotContain("app.info");
    }

    /// <summary>
    /// CommandSecurity 已声明但 Require/Public 均未设置时，ManifestGenerator 报告为未覆盖（fail-closed for new paths）。
    /// 这是 SEC-H-001 阶段一对"声明与执行两张皮"的守门：声明了 Security 但既不要求权限也不标 IsPublic → 视为未完成声明。
    /// </summary>
    [Test]
    public async Task EmptySecurity_IsReportedAsUncoveredByManifest()
    {
        var registry = new CommandRegistry();
        // 通过 Register 直接设置"声明了 Security 但 Require 为空且 IsPublic=false"
        registry.Register(
            "app.awkward",
            (_, _, _) => Task.FromResult<object?>(null),
            System.Array.Empty<Type>(),
            new CommandSecurity());

        var caps = new List<Wails.Net.Application.Security.Capability>();
        var uncovered = Wails.Net.Application.Manifests.ManifestGenerator.FindUncoveredCapabilities(
            registry, caps);
        await Assert.That(uncovered).Contains(u => u.Contains("app.awkward"));
    }
}