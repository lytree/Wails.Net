using Wails.Net.Plugins.AppInfo;

namespace Wails.Net.Plugins.Appinfo.Tests;

public class AppinfoPluginTests
{
    [Test]
    public async Task Plugin_Name_MatchesCommandPrefix()
    {
        var plugin = new AppInfoPlugin();
        await Assert.That(plugin.Name).IsEqualTo("app");
    }
}