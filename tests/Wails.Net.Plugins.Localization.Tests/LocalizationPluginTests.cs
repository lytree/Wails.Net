using Wails.Net.Plugins.Localization;

namespace Wails.Net.Plugins.Localization.Tests;

public class LocalizationPluginTests
{
    [Test]
    public async Task Plugin_Name_MatchesCommandPrefix()
    {
        var plugin = new LocalizationPlugin();
        await Assert.That(plugin.Name).IsEqualTo("localization");
    }
}