using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Assertions;
using TUnit.Core;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Security;

namespace Wails.Net.Application.Tests.Security;

/// <summary>
/// Scope 校验单元测试（TUnit）。
/// 覆盖 <see cref="PermissionManager.ValidateScopes"/>、
/// CommandDispatcher 的 [ScopeParameter] 特性和 IScopeParameter 接口提取、
/// <see cref="ScopeInitializer"/> 配置加载。
/// 对应 Tauri v2 的参数级 Scope 校验。
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
    /// 测试 [ScopeParameter] 特性标记的 string 参数被 CommandDispatcher 正确提取。
    /// 通过反射调用 private 方法 ExtractScopeValues 进行验证。
    /// </summary>
    [Test]
    public async Task ScopeParameter_Attribute_ExtractsStringParameter()
    {
        // 安排：定义带 [ScopeParameter] 特性的测试方法
        var method = typeof(ScopeValidationTests).GetMethod(
            nameof(TestCommandWithScopeAttribute), BindingFlags.NonPublic | BindingFlags.Static)!;
        var json = JsonDocument.Parse(@"{""path"":""/tmp/test.txt""}").RootElement;

        // 操作：通过反射调用 CommandDispatcher.ExtractScopeValues
        var result = InvokeExtractScopeValues(method, json);

        // 断言：提取到 ("fs:allow-read", "/tmp/test.txt")
        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result[0].PermissionId).IsEqualTo("fs:allow-read");
        await Assert.That(result[0].Value).IsEqualTo("/tmp/test.txt");
    }

    /// <summary>
    /// 测试实现 IScopeParameter 的 Options 类被 CommandDispatcher 正确提取。
    /// </summary>
    [Test]
    public async Task IScopeParameter_Interface_ExtractsValues()
    {
        // 安排：定义接收 TestScopeOptions 参数的测试方法
        var method = typeof(ScopeValidationTests).GetMethod(
            nameof(TestCommandWithScopeOptions), BindingFlags.NonPublic | BindingFlags.Static)!;
        var json = JsonDocument.Parse(@"{""url"":""https://example.com""}").RootElement;

        // 操作：通过反射调用 ExtractScopeValues
        var result = InvokeExtractScopeValues(method, json);

        // 断言：提取到 ("http:allow-fetch", "https://example.com")
        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result[0].PermissionId).IsEqualTo("http:allow-fetch");
        await Assert.That(result[0].Value).IsEqualTo("https://example.com");
    }

    /// <summary>
    /// 端到端测试：CommandDispatcher 调度 fs.read 命令时，
    /// 若路径超出 Scope 范围，命令被拒绝。
    /// </summary>
    [Test]
    public async Task CommandDispatcher_ScopeValidation_RejectsOutOfScopePath()
    {
        // 安排：注册测试命令到 CommandRegistry
        var registry = new CommandRegistry();
        var testInstance = new TestCommandTarget();
        registry.Register("test.scope.check",
            testInstance,
            typeof(TestCommandTarget).GetMethod(nameof(TestCommandTarget.CheckPath))!);

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

        // 断言：命令被 Scope 校验拒绝
        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Error).Contains("Scope denied");
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

    // ===== 测试辅助方法 =====

#pragma warning disable CA1822, IDE0060 // 测试方法签名不需要 static/参数
    private static void TestCommandWithScopeAttribute([ScopeParameter("fs:allow-read")] string path) { }
    private static void TestCommandWithScopeOptions(TestScopeOptions options) { }
#pragma warning restore CA1822, IDE0060

    /// <summary>
    /// 通过反射调用 CommandDispatcher.ExtractScopeValues 私有方法。
    /// </summary>
    private static List<(string PermissionId, string Value)> InvokeExtractScopeValues(MethodInfo method, JsonElement parameters)
    {
        var dispatcherType = typeof(CommandDispatcher);
        var methodInfo = dispatcherType.GetMethod("ExtractScopeValues",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (List<(string PermissionId, string Value)>)methodInfo.Invoke(null, [method, parameters])!;
    }

    /// <summary>
    /// 测试用 IScopeParameter 实现。
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
