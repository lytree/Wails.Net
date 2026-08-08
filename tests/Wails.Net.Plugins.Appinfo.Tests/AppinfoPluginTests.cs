using Wails.Net.Plugins.Appinfo;

namespace Wails.Net.Plugins.Appinfo.Tests;

public class AppinfoPluginTests
{
    [Test]
    public async Task Plugin_Name_MatchesCommandPrefix()
    {
        var plugin = new AppinfoPlugin();
        await Assert.That(plugin.Name).IsEqualTo("app");
    }
}