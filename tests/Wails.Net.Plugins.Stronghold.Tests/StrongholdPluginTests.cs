using Wails.Net.Plugins.Stronghold;

namespace Wails.Net.Plugins.Stronghold.Tests;

public class StrongholdPluginTests
{
    [Test]
    public async Task Plugin_Name_MatchesCommandPrefix()
    {
        var plugin = new StrongholdPlugin();
        await Assert.That(plugin.Name).IsEqualTo("stronghold");
    }
}