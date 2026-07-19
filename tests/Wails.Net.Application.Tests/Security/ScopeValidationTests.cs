using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Assertions;
using TUnit.Core;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Security;

namespace Wails.Net.Application.Tests.Security;

/// <summary>
/// Scope 校验单元测试（TUnit）。
/// 覆盖 <see cref="PermissionManager.ValidateScopes"/>、
/// CommandDispatcher 的 [ScopeParameter] 特性和 IScopeParameter 接口提取、
/// <see cref="ScopeInitializer"/> 配置加载。
/// 对应 Tauri v2 的参数级 Scope 校验。
/// 遵循 AGENTS.md §3.4 禁令：零反射，全部通过源生成器编译期构建的 ScopeExtractor 提取。
/// </summary>
[NotInParallel]
public sealed class ScopeValidationTests
{
    /// <summary>
    /// 创建带指定选项的 PermissionManager。
    /// </summary>
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

    [Test]
    public async Task ValidateScopes_Disabled_PermissionManager_ReturnsTrue()
    {
        // 安排：权限检查未启用
        var manager = CreateManager(enabled: false, denyByDefault: true);
        var scopeValues = new[] { ("fs:allow-read", "/etc/passwd") };

        // 操作与断言：未启用时全部放行
        await Assert.That(manager.ValidateScopes(scopeValues)).IsTrue();
    }

    [Test]
    public async Task ValidateScopes_EmptyScopeValues_ReturnsTrue()
    {
        // 安排：启用权限，DenyByDefault=true，无 Scope 值
        var manager = CreateManager(enabled: true, denyByDefault: true);

        // 操作与断言：空值列表返回 true
        await Assert.That(manager.ValidateScopes([])).IsTrue();
    }

    [Test]
    public async Task ValidateScopes_GrantedPermissionWithMatchingScope_ReturnsTrue()
    {
        // 安排：授权 fs:allow-read 权限并绑定文件系统 Scope
        var manager = CreateManager(enabled: true, denyByDefault: true, "fs:allow-read");
        var fsScope = new FileSystemScope();
        fsScope.AddPath(Path.GetTempPath());
        manager.SetScope("fs:allow-read", fsScope);

        var allowedPath = Path.Combine(Path.GetTempPath(), "test.txt");
        var scopeValues = new[] { ("fs:allow-read", allowedPath) };

        // 操作与断言：权限已授权且路径在范围内
        await Assert.That(manager.ValidateScopes(scopeValues)).IsTrue();
    }

    [Test]
    public async Task ValidateScopes_GrantedPermissionWithMismatchedScope_ReturnsFalse()
    {
        // 安排：授权 fs:allow-read 但只允许 TempPath 范围
        var manager = CreateManager(enabled: true, denyByDefault: true, "fs:allow-read");
        var fsScope = new FileSystemScope();
        fsScope.AddPath(Path.GetTempPath());
        manager.SetScope("fs:allow-read", fsScope);

        // 尝试访问范围外的路径
        var outsidePath = Path.Combine(AppContext.BaseDirectory, "secret.txt");
        var scopeValues = new[] { ("fs:allow-read", outsidePath) };

        // 操作与断言：权限已授权但路径不在范围内
        await Assert.That(manager.ValidateScopes(scopeValues)).IsFalse();
    }

    [Test]
    public async Task ValidateScopes_UngrantedPermission_ReturnsFalse()
    {
        // 安排：启用权限，未授权 fs:allow-read
        var manager = CreateManager(enabled: true, denyByDefault: true);
        var scopeValues = new[] { ("fs:allow-read", "/any/path") };

        // 操作与断言：权限未授权直接拒绝
        await Assert.That(manager.ValidateScopes(scopeValues)).IsFalse();
    }

    /// <summary>
    /// 测试 IScopeParameter 接口实现可直接返回 Scope 值。
    /// 模拟源生成器生成的 ScopeExtractor 在编译期构造 IScopeParameter 实例并调用 GetScopeValues() 的行为，
    /// 但本测试直接调用接口方法以验证实现契约，零反射。
    /// </summary>
    [Test]
    public async Task IScopeParameter_Interface_ReturnsScopeValues()
    {
        // 安排：构造 TestScopeOptions 实例并设置 URL
        var options = new TestScopeOptions
        {
            Url = "https://example.com"
        };

        // 操作：直接调用 GetScopeValues，模拟源生成器生成的 ScopeExtractor 在编译期构造 IScopeParameter 后调用接口方法
        var scopeValues = options.GetScopeValues().ToList();

        // 断言：返回 ("http:allow-fetch", "https://example.com")
        await Assert.That(scopeValues.Count).IsEqualTo(1);
        await Assert.That(scopeValues[0].PermissionId).IsEqualTo("http:allow-fetch");
        await Assert.That(scopeValues[0].Value).IsEqualTo("https://example.com");
    }

    /// <summary>
    /// 端到端测试：CommandDispatcher 调度命令时，
    /// 若参数路径超出 Scope 范围，命令被拒绝。
    /// Scope 提取器通过 GeneratedBindingRegistry 在测试中手动注册（模拟源生成器输出）。
    /// 调用器通过 CompiledCommandInvoker 强类型委托提供，零反射。
    /// </summary>
    [Test]
    public async Task CommandDispatcher_ScopeValidation_RejectsOutOfScopePath()
    {
        // 安排：手动注册 ScopeExtractor 到 GeneratedBindingRegistry（模拟源生成器编译期生成）。
        // 注意：不调用 Clear()，因为该注册表是全局静态状态，包含源生成器为其他测试（如
        // CancellablePromiseEndToEndTests、NativeIpcTransportTests）通过 [ModuleInitializer]
        // 注册的调用器。Clear() 会破坏其他测试，导致 GeneratedBindingRegistry.Count=0。
        // 测试命令名 "test.scope.check" 唯一，重复注册会覆盖，无需 Clear。
        var scopeExtractor = (ScopeExtractor)(p =>
        {
            if (p.TryGetProperty("path", out var v) && v.ValueKind == JsonValueKind.String)
            {
                var path = v.GetString();
                if (!string.IsNullOrEmpty(path)) return ("fs:allow-read", path);
            }
            return null;
        });
        var dummyInvoker = (GeneratedInvoker)((_, _, _) => Task.FromResult<object?>(null));
        GeneratedBindingRegistry.Register("test.scope.check", dummyInvoker,
            typeof(TestCommandTarget).FullName ?? "TestCommandTarget",
            requiredCapabilities: null,
            scopeExtractors: new[] { scopeExtractor });

        // 注册测试命令到 CommandRegistry，使用 CompiledCommandInvoker 委托（替代原 MethodInfo 反射路径）
        var registry = new CommandRegistry();
        var testInstance = new TestCommandTarget();
        var compiledInvoker = (CompiledCommandInvoker)((instance, parameters, _) =>
        {
            var path = parameters.GetProperty("path").GetString() ?? string.Empty;
            var target = (TestCommandTarget)(instance ?? testInstance);
            return Task.FromResult<object?>(target.CheckPath(path));
        });
        registry.Register("test.scope.check", compiledInvoker);

        // 授权 fs:allow-read 权限但只允许 TempPath 范围
        var manager = CreateManager(enabled: true, denyByDefault: true, "fs:allow-read");
        var fsScope = new FileSystemScope();
        fsScope.AddPath(Path.GetTempPath());
        manager.SetScope("fs:allow-read", fsScope);

        var dispatcher = new CommandDispatcher(registry, NullServiceProvider.Instance,
            NullLogger<CommandDispatcher>.Instance, manager);

        // 操作：调度命令，传入范围外路径
        var outsidePath = Path.Combine(AppContext.BaseDirectory, "secret.txt");
        var request = new InvokeRequest(Guid.NewGuid(), "test.scope.check",
            JsonDocument.Parse($@"{{""path"":""{outsidePath.Replace("\\", "\\\\")}""}}").RootElement);

        var response = await dispatcher.DispatchAsync(request);

        // 断言：命令被 Scope 校验拒绝（在到达 Invoker 前被拦截）
        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Error).Contains("Scope denied");
    }

    /// <summary>
    /// 端到端测试：当参数路径在 Scope 范围内时，命令正常执行。
    /// 与 <see cref="CommandDispatcher_ScopeValidation_RejectsOutOfScopePath"/> 互补，
    /// 覆盖通过 Scope 校验的合法路径。
    /// </summary>
    [Test]
    public async Task CommandDispatcher_ScopeValidation_PassesInScopePath()
    {
        // 不调用 Clear()，原因同上：避免破坏其他测试的全局注册状态。
        var scopeExtractor = (ScopeExtractor)(p =>
        {
            if (p.TryGetProperty("path", out var v) && v.ValueKind == JsonValueKind.String)
            {
                var path = v.GetString();
                if (!string.IsNullOrEmpty(path)) return ("fs:allow-read", path);
            }
            return null;
        });
        var dummyInvoker = (GeneratedInvoker)((_, _, _) => Task.FromResult<object?>(null));
        GeneratedBindingRegistry.Register("test.scope.check", dummyInvoker,
            typeof(TestCommandTarget).FullName ?? "TestCommandTarget",
            requiredCapabilities: null,
            scopeExtractors: new[] { scopeExtractor });

        var registry = new CommandRegistry();
        var testInstance = new TestCommandTarget();
        var compiledInvoker = (CompiledCommandInvoker)((instance, parameters, _) =>
        {
            var path = parameters.GetProperty("path").GetString() ?? string.Empty;
            var target = (TestCommandTarget)(instance ?? testInstance);
            return Task.FromResult<object?>(target.CheckPath(path));
        });
        registry.Register("test.scope.check", compiledInvoker);

        var manager = CreateManager(enabled: true, denyByDefault: true, "fs:allow-read");
        var fsScope = new FileSystemScope();
        fsScope.AddPath(Path.GetTempPath());
        manager.SetScope("fs:allow-read", fsScope);

        var dispatcher = new CommandDispatcher(registry, NullServiceProvider.Instance,
            NullLogger<CommandDispatcher>.Instance, manager);

        var allowedPath = Path.Combine(Path.GetTempPath(), "allowed.txt");
        var request = new InvokeRequest(Guid.NewGuid(), "test.scope.check",
            JsonDocument.Parse($@"{{""path"":""{allowedPath.Replace("\\", "\\\\")}""}}").RootElement);

        var response = await dispatcher.DispatchAsync(request);

        await Assert.That(response.Success).IsTrue();
    }

    [Test]
    public async Task ScopeInitializer_ReadsConfig_BindsScopes()
    {
        // 安排：构造带 Scope 配置的 PermissionOptions
        var manager = CreateManager(enabled: true, denyByDefault: true, "fs:allow-read");
        var tempPath = Path.GetTempPath();
        var options = new PermissionOptions
        {
            Enabled = true,
            DenyByDefault = true,
            Permissions = new List<string> { "fs:allow-read" },
            Scopes = new Dictionary<string, ScopeConfig>
            {
                ["fs:allow-read"] = new ScopeConfig
                {
                    Paths = new List<string> { tempPath }
                }
            }
        };

        // 操作：初始化 Scope 绑定
        ScopeInitializer.Initialize(manager, options, NullLogger.Instance);

        // 断言：TempPath 下的路径允许，范围外路径拒绝
        var allowedPath = Path.Combine(tempPath, "test.txt");
        var outsidePath = Path.Combine(AppContext.BaseDirectory, "secret.txt");
        await Assert.That(manager.ValidateScopes([("fs:allow-read", allowedPath)])).IsTrue();
        await Assert.That(manager.ValidateScopes([("fs:allow-read", outsidePath)])).IsFalse();
    }

    [Test]
    public async Task ScopeInitializer_EmptyConfig_NoOp()
    {
        // 安排：构造空 Scope 配置
        var manager = CreateManager(enabled: true, denyByDefault: true, "fs:allow-read");
        var options = new PermissionOptions
        {
            Enabled = true,
            DenyByDefault = true,
            Permissions = new List<string> { "fs:allow-read" },
            Scopes = new Dictionary<string, ScopeConfig>()
        };

        // 操作：初始化（应无任何操作）
        ScopeInitializer.Initialize(manager, options, NullLogger.Instance);

        // 断言：无 Scope 绑定时，任意路径都通过（因为 GetScope 返回 null）
        // 注：ValidateScopes 中 scope is null 时不校验，仅检查权限是否授权
        await Assert.That(manager.ValidateScopes([("fs:allow-read", "/any/path")])).IsTrue();
    }

    // ===== 测试辅助类型 =====

    /// <summary>
    /// 测试用 IScopeParameter 实现。
    /// 模拟源生成器编译期生成的 ScopeExtractor 对 Options 类型的处理：
    /// 反序列化参数 → 调用 IScopeParameter.GetScopeValues()。
    /// </summary>
    private sealed class TestScopeOptions : IScopeParameter
    {
        public string Url { get; set; } = string.Empty;

        public IEnumerable<(string PermissionId, string Value)> GetScopeValues()
        {
            if (!string.IsNullOrEmpty(Url))
            {
                yield return ("http:allow-fetch", Url);
            }
        }
    }

    /// <summary>
    /// 测试用命令目标，用于端到端测试。
    /// </summary>
    private sealed class TestCommandTarget
    {
        public string CheckPath([ScopeParameter("fs:allow-read")] string path) => path;
    }

    /// <summary>
    /// Null 服务提供者，用于测试。
    /// </summary>
    private sealed class NullServiceProvider : IServiceProvider
    {
        public static readonly NullServiceProvider Instance = new();
        public object? GetService(Type serviceType) => null;
    }
}
