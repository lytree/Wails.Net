using Wails.Net.Plugins.Store;

namespace Wails.Net.Plugins.Store.Tests;

public class StorePluginTests
{
    [Test]
    public async Task Plugin_Name_MatchesCommandPrefix()
    {
        var plugin = new StorePlugin();
        await Assert.That(plugin.Name).IsEqualTo("store");
    }
}