using Wails.Net.Plugins.Opener;

namespace Wails.Net.Plugins.Opener.Tests;

public class OpenerPluginTests
{
    [Test]
    public async Task Plugin_Name_MatchesCommandPrefix()
    {
        var plugin = new OpenerPlugin();
        await Assert.That(plugin.Name).IsEqualTo("opener");
    }
}