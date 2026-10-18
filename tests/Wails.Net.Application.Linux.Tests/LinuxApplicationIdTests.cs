using TUnit.Core;
using Wails.Net.Application.Platform;

namespace Wails.Net.Application.Linux.Tests;

/// <summary>
/// <see cref="LinuxApplicationId"/> 的单元测试（TUnit），镜像 Go 版
/// <c>pkg/application/application_linux_appid_test.go</c>（beta.23 #5972）。
/// 全部为纯逻辑断言，不触发任何原生 GTK/GLib 调用，可在任意平台执行。
/// </summary>
[NotInParallel]
public sealed class LinuxApplicationIdTests
{
    [Test]
    public async Task ResolveApplicationId_DerivesFromName_WhenUnset()
    {
        var id = LinuxApplicationId.ResolveApplicationId("My App", null, out var error);

        await Assert.That(error).IsNull();
        await Assert.That(id).IsEqualTo("org.wails.my_app");
    }

    [Test]
    public async Task ResolveApplicationId_KeepsHyphens_WhenDerived()
    {
        var id = LinuxApplicationId.ResolveApplicationId("koofr-stage", null, out var error);

        await Assert.That(error).IsNull();
        await Assert.That(id).IsEqualTo("org.wails.koofr-stage");
    }

    [Test]
    public async Task ResolveApplicationId_FallsBackToWailsapp_WhenNameEmpty()
    {
        var id = LinuxApplicationId.ResolveApplicationId(null, null, out var error);

        await Assert.That(error).IsNull();
        await Assert.That(id).IsEqualTo("org.wails.wailsapp");
    }

    [Test]
    public async Task ResolveApplicationId_UsesOverrideVerbatim()
    {
        var id = LinuxApplicationId.ResolveApplicationId("My App", "com.myapp.myapplication", out var error);

        await Assert.That(error).IsNull();
        await Assert.That(id).IsEqualTo("com.myapp.myapplication");
    }

    [Test]
    public async Task ResolveApplicationId_OverrideWinsOverNameEntirely()
    {
        var id = LinuxApplicationId.ResolveApplicationId("something else", "com.example.WailsFlatpakAppId", out var error);

        await Assert.That(error).IsNull();
        await Assert.That(id).IsEqualTo("com.example.WailsFlatpakAppId");
    }

    [Test]
    public async Task ResolveApplicationId_FallsBackToDerived_WhenOverrideInvalid()
    {
        var id = LinuxApplicationId.ResolveApplicationId("My App", "yeehaw", out var error);

        await Assert.That(error).IsNotNull();
        await Assert.That(id).IsEqualTo("org.wails.my_app");
    }

    [Test]
    public async Task ResolveApplicationId_TruncatesDerived_WhenLongerThan255()
    {
        var id = LinuxApplicationId.ResolveApplicationId(new string('l', 800), null, out var error);

        await Assert.That(error).IsNull();
        await Assert.That(id.Length).IsEqualTo(255);
        await Assert.That(LinuxApplicationId.ValidateApplicationId(id)).IsNull();
    }

    [Test]
    public async Task ResolveApplicationId_DerivedIdIsAlwaysValid()
    {
        var names = new[]
        {
            "My App",
            "koofr-stage",
            string.Empty,
            "1Password",
            "...",
            "app.with.dots",
            "ünïcodé",
            "__leading__and__trailing__",
            "9",
            "-",
        };

        foreach (var name in names)
        {
            var id = LinuxApplicationId.ResolveApplicationId(name, null, out var error);

            await Assert.That(error).IsNull();
            await Assert.That(LinuxApplicationId.ValidateApplicationId(id)).IsNull();
        }
    }

    [Test]
    [Arguments("com.example.MyApp", true)]
    [Arguments("com.example", true)]
    [Arguments("com.example.my_app", true)]
    [Arguments("org.wails.koofr-stage", true)]
    [Arguments("com.example.App2", true)]
    [Arguments("", false)]
    [Arguments("yeehaw", false)]
    [Arguments(".com.example", false)]
    [Arguments("com.example.", false)]
    [Arguments("com..example", false)]
    [Arguments("com.example.2ndApp", false)]
    [Arguments("2com.example", false)]
    [Arguments("com.example/MyApp", false)]
    [Arguments("com.example.My App", false)]
    [Arguments(":1.42", false)]
    [Arguments("com.example.Mü", false)]
    public async Task ValidateApplicationId_MatchesGtkContract(string id, bool valid)
    {
        var error = LinuxApplicationId.ValidateApplicationId(id);

        await Assert.That(error is null).IsEqualTo(valid);
    }

    [Test]
    public async Task ValidateApplicationId_AcceptsUpTo255Characters()
    {
        var maxLength = "com." + new string('a', 251);
        var tooLong = "com." + new string('a', 252);

        await Assert.That(LinuxApplicationId.ValidateApplicationId(maxLength)).IsNull();
        await Assert.That(LinuxApplicationId.ValidateApplicationId(tooLong)).IsNotNull();
    }

    [Test]
    public async Task ResolveProgramName_ReturnsNull_WhenNeitherOptionSet()
    {
        var result = LinuxApplicationId.ResolveProgramName(null, null, "org.wails.my_app");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ResolveProgramName_ReturnsNull_WhenOverrideEmpty()
    {
        var result = LinuxApplicationId.ResolveProgramName(null, string.Empty, "org.wails.my_app");

        await Assert.That(result).IsNull();
    }

    [Test]
    [Arguments("", "com.example.MyApp", "com.example.MyApp", "com.example.MyApp")]
    [Arguments("myapp", "com.example.MyApp", "com.example.MyApp", "myapp")]
    [Arguments("myapp", "", "org.wails.my_app", "myapp")]
    public async Task ResolveProgramName_MatchesGoRules(
        string programName,
        string applicationIdOverride,
        string applicationId,
        string expected)
    {
        var result = LinuxApplicationId.ResolveProgramName(programName, applicationIdOverride, applicationId);

        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task ResolveProgramName_InheritsFallbackId_WhenOverrideInvalid()
    {
        var applicationId = LinuxApplicationId.ResolveApplicationId("My App", "yeehaw", out _);
        var result = LinuxApplicationId.ResolveProgramName(null, "yeehaw", applicationId);

        // Go 注释：继承的是交给 GTK 的那个 id，而非被它拒绝的那个。
        await Assert.That(applicationId).IsEqualTo("org.wails.my_app");
        await Assert.That(result).IsEqualTo("org.wails.my_app");
    }

    [Test]
    public async Task SanitizeAppName_PrefixesLeadingDigit_AfterTrimming()
    {
        // Go: "1Password" -> "_1password"（前导数字在 trim 之后加前缀，故不会被裁掉）。
        await Assert.That(LinuxApplicationId.SanitizeAppName("1Password")).IsEqualTo("_1password");
    }

    [Test]
    public async Task SanitizeAppName_FallsBackToWailsapp_WhenAllCharactersInvalid()
    {
        await Assert.That(LinuxApplicationId.SanitizeAppName("...")).IsEqualTo("wailsapp");
    }
}
