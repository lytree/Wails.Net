using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TUnit.Core;
using Wails.Net.Application.Security;

namespace Wails.Net.Application.Tests.Security;

/// <summary>
/// CapabilityManifestValidator 单元测试（R3-A.3 落地补齐，R3-C.3 收口）。
/// <para>
/// 覆盖：
/// <list type="bullet">
/// <item>ValidateShape：CM001-CM005 各类错误码（identifier 缺失/重复/permissions 空/permissions 元素空/未知平台）。</item>
/// <item>FindUndeclaredPermissions：capability 引用未声明的 permission 时报错，deny 前缀（!）不要求先声明。</item>
/// <item>LoadSchemaJson：嵌入 schema 资源可读且为合法 JSON。</item>
/// </list>
/// </para>
/// </summary>
public sealed class CapabilityManifestValidatorTests
{
    private static Capability MakeCapability(string identifier, params string[] permissions)
    {
        return new Capability
        {
            Identifier = identifier,
            Description = identifier + " desc",
            Permissions = permissions.ToList(),
            Windows = new List<string>(),
            Platforms = new List<string>(),
            Remote = new List<string>(),
        };
    }

    [Test]
    public async Task ValidateShape_EmptyList_NoIssues()
    {
        var issues = CapabilityManifestValidator.ValidateShape(Array.Empty<Capability>());
        await Assert.That(issues).IsEmpty();
    }

    [Test]
    public async Task ValidateShape_ValidCapability_NoIssues()
    {
        var caps = new[] { MakeCapability("default", "core:default", "fs:allow-read") };
        var issues = CapabilityManifestValidator.ValidateShape(caps);
        await Assert.That(issues).IsEmpty();
    }

    [Test]
    public async Task ValidateShape_MissingIdentifier_ReportsCM001()
    {
        var caps = new[] { MakeCapability("", "core:default") };
        var issues = CapabilityManifestValidator.ValidateShape(caps);
        await Assert.That(issues).Count().IsEqualTo(1);
        await Assert.That(issues[0].Code).IsEqualTo("CM001");
        await Assert.That(issues[0].Severity).IsEqualTo(CapabilityValidationSeverity.Error);
    }

    [Test]
    public async Task ValidateShape_DuplicateIdentifier_ReportsCM002()
    {
        var caps = new[]
        {
            MakeCapability("default", "core:default"),
            MakeCapability("default", "fs:allow-read"),
        };
        var issues = CapabilityManifestValidator.ValidateShape(caps);
        await Assert.That(issues.Any(i => i.Code == "CM002")).IsTrue();
    }

    [Test]
    public async Task ValidateShape_EmptyPermissions_ReportsCM003()
    {
        var caps = new[] { MakeCapability("default") };
        var issues = CapabilityManifestValidator.ValidateShape(caps);
        await Assert.That(issues.Any(i => i.Code == "CM003")).IsTrue();
    }

    [Test]
    public async Task ValidateShape_EmptyPermissionElement_ReportsCM004()
    {
        var cap = MakeCapability("default", "core:default", "", "  ");
        var issues = CapabilityManifestValidator.ValidateShape(new[] { cap });
        await Assert.That(issues.Count(i => i.Code == "CM004")).IsEqualTo(2);
    }

    [Test]
    public async Task ValidateShape_UnknownPlatform_ReportsCM005()
    {
        var cap = MakeCapability("default", "core:default");
        cap.Platforms.Add("FreeBSD");
        var issues = CapabilityManifestValidator.ValidateShape(new[] { cap });
        await Assert.That(issues.Any(i => i.Code == "CM005")).IsTrue();
        await Assert.That(issues.First(i => i.Code == "CM005").Severity).IsEqualTo(CapabilityValidationSeverity.Warning);
    }

    [Test]
    public async Task ValidateShape_AllKnownPlatforms_NoIssues()
    {
        var cap = MakeCapability("default", "core:default");
        cap.Platforms.AddRange(new[] { "windows", "linux", "android", "macos", "server" });
        var issues = CapabilityManifestValidator.ValidateShape(new[] { cap });
        await Assert.That(issues).IsEmpty();
    }

    private static PermissionManager NewManager()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new PermissionOptions { Enabled = false, Mode = PermissionMode.Compatibility });
        return new PermissionManager(options, NullLogger<PermissionManager>.Instance, "windows");
    }

    [Test]
    public async Task FindUndeclaredPermissions_AllDeclared_NoIssues()
    {
        var manager = NewManager();
        manager.RegisterDeclaredPermission("fs:allow-read", "允许读取");
        manager.RegisterDeclaredPermission("fs:allow-write", "允许写入");
        var caps = new[] { MakeCapability("default", "fs:allow-read", "fs:allow-write") };
        var undeclared = CapabilityManifestValidator.FindUndeclaredPermissions(manager, caps);
        await Assert.That(undeclared).IsEmpty();
    }

    [Test]
    public async Task FindUndeclaredPermissions_UndeclaredRef_ReportsIssue()
    {
        var manager = NewManager();
        manager.RegisterDeclaredPermission("fs:allow-read", "允许读取");
        var caps = new[] { MakeCapability("default", "fs:allow-read", "fs:allow-nonexistent") };
        var undeclared = CapabilityManifestValidator.FindUndeclaredPermissions(manager, caps);
        await Assert.That(undeclared).Count().IsEqualTo(1);
        await Assert.That(undeclared[0]).Contains("fs:allow-nonexistent");
    }

    [Test]
    public async Task FindUndeclaredPermissions_DenyPrefix_BypassesDeclarationCheck()
    {
        var manager = NewManager();
        manager.RegisterDeclaredPermission("fs:allow-read", "允许读取");
        var caps = new[] { MakeCapability("default", "fs:allow-read", "!fs:allow-write") };
        var undeclared = CapabilityManifestValidator.FindUndeclaredPermissions(manager, caps);
        await Assert.That(undeclared).IsEmpty();
    }

    [Test]
    public async Task FindUndeclaredPermissions_EmptyPermission_Skipped()
    {
        var manager = NewManager();
        var caps = new[] { MakeCapability("default", "") };
        var undeclared = CapabilityManifestValidator.FindUndeclaredPermissions(manager, caps);
        await Assert.That(undeclared).IsEmpty();
    }

    [Test]
    public async Task FindUndeclaredPermissions_MultipleCapabilities_Aggregates()
    {
        var manager = NewManager();
        manager.RegisterDeclaredPermission("a:allow-x", "X");
        var caps = new[]
        {
            MakeCapability("cap1", "a:allow-x", "a:allow-missing"),
            MakeCapability("cap2", "a:allow-missing-2"),
        };
        var undeclared = CapabilityManifestValidator.FindUndeclaredPermissions(manager, caps);
        await Assert.That(undeclared).Count().IsEqualTo(2);
    }

    [Test]
    public async Task LoadSchemaJson_ReturnsNonEmptyValidJson()
    {
        var json = CapabilityManifestValidator.LoadSchemaJson();
        await Assert.That(json).IsNotNull();
        await Assert.That(json!.Length).IsGreaterThan(0);
        await Assert.That(json).Contains("capability");
        // 必须能解析为合法 JSON
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        await Assert.That(doc.RootElement.ValueKind).IsEqualTo(System.Text.Json.JsonValueKind.Object);
    }

    [Test]
    public async Task ValidateShape_CaseInsensitivePlatform_IsAccepted()
    {
        var cap = MakeCapability("default", "core:default");
        cap.Platforms.Add("WINDOWS");
        var issues = CapabilityManifestValidator.ValidateShape(new[] { cap });
        // 大小写不敏感：WINDOWS 等同 windows，不应触发 CM005
        await Assert.That(issues.Any(i => i.Code == "CM005")).IsFalse();
    }
}