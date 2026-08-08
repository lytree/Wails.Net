using Wails.Net.Plugins.Path;

namespace Wails.Net.Plugins.Path.Tests;

public class PathPluginTests
{
    [Test]
    public async Task Plugin_Name_MatchesCommandPrefix()
    {
        var plugin = new PathPlugin();
        await Assert.That(plugin.Name).IsEqualTo("path");
    }
}