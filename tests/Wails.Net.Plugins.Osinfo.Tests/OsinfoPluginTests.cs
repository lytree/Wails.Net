using Wails.Net.Plugins.Osinfo;

namespace Wails.Net.Plugins.Osinfo.Tests;

public class OsinfoPluginTests
{
    [Test]
    public async Task Plugin_Name_MatchesCommandPrefix()
    {
        var plugin = new OsinfoPlugin();
        await Assert.That(plugin.Name).IsEqualTo("os");
    }
}