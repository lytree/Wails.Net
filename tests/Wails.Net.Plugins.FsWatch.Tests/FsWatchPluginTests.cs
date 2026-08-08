using Wails.Net.Plugins.FsWatch;

namespace Wails.Net.Plugins.FsWatch.Tests;

public class FsWatchPluginTests
{
    [Test]
    public async Task Plugin_Name_MatchesCommandPrefix()
    {
        var plugin = new FsWatchPlugin();
        await Assert.That(plugin.Name).IsEqualTo("fswatch");
    }
}