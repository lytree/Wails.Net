using Wails.Net.Plugins.OsInfo;

namespace Wails.Net.Plugins.Osinfo.Tests;

public class OsinfoPluginTests
{
    [Test]
    public async Task Plugin_Name_MatchesCommandPrefix()
    {
        var plugin = new OsInfoPlugin();
        await Assert.That(plugin.Name).IsEqualTo("os");
    }
}