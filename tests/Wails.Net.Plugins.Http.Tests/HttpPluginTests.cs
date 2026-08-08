using Wails.Net.Plugins.Http;

namespace Wails.Net.Plugins.Http.Tests;

public class HttpPluginTests
{
    [Test]
    public async Task Plugin_Name_MatchesCommandPrefix()
    {
        var plugin = new HttpPlugin();
        await Assert.That(plugin.Name).IsEqualTo("http");
    }
}