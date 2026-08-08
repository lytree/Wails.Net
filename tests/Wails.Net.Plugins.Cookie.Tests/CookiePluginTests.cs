using Wails.Net.Plugins.Cookie;

namespace Wails.Net.Plugins.Cookie.Tests;

public class CookiePluginTests
{
    [Test]
    public async Task Plugin_Name_MatchesCommandPrefix()
    {
        var plugin = new CookiePlugin();
        await Assert.That(plugin.Name).IsEqualTo("cookie");
    }
}