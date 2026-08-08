using Wails.Net.Plugins.Mobile;

namespace Wails.Net.Plugins.Mobile.Tests;

public class MobilePluginTests
{
    [Test]
    public async Task Plugin_Name_MatchesCommandPrefix()
    {
        var plugin = new MobilePlugin();
        await Assert.That(plugin.Name).IsEqualTo("mobile");
    }
}