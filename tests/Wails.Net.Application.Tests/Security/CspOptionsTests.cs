using TUnit.Core;
using Wails.Net.Application.Security;

namespace Wails.Net.Application.Tests.Security;

/// <summary>
/// 内容安全策略（CSP）选项单元测试（TUnit）。
/// 对应主题 C：CspOptions BuildHeader 默认值、自定义值、Enabled=false。
/// </summary>
public sealed class CspOptionsTests
{
    [Test]
    public async Task BuildHeader_DefaultValues_ContainsSelfSources()
    {
        var csp = new CspOptions();

        var header = csp.BuildHeader();

        await Assert.That(header).Contains("default-src 'self'");
        await Assert.That(header).Contains("script-src 'self'");
        await Assert.That(header).Contains("object-src 'none'");
    }

    [Test]
    public async Task BuildHeader_Disabled_ReturnsEmpty()
    {
        var csp = new CspOptions { Enabled = false };

        await Assert.That(csp.BuildHeader()).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task BuildHeader_CustomValues_Reflected()
    {
        var csp = new CspOptions
        {
            DefaultSrc = "'none'",
            ScriptSrc = "'self' 'unsafe-eval'"
        };

        var header = csp.BuildHeader();

        await Assert.That(header).Contains("default-src 'none'");
        await Assert.That(header).Contains("script-src 'self' 'unsafe-eval'");
    }
}
