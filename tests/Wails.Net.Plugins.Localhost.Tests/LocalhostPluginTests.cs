using Wails.Net.Plugins.Localhost;

namespace Wails.Net.Plugins.Localhost.Tests;

public class LocalhostPluginTests
{
    [Test]
    public async Task Plugin_Name_MatchesCommandPrefix()
    {
        var plugin = new LocalhostPlugin();
        await Assert.That(plugin.Name).IsEqualTo("localhost");
    }
}