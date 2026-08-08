using Wails.Net.Plugins.Websocket;

namespace Wails.Net.Plugins.Websocket.Tests;

public class WebsocketPluginTests
{
    [Test]
    public async Task Plugin_Name_MatchesCommandPrefix()
    {
        var plugin = new WebsocketPlugin();
        await Assert.That(plugin.Name).IsEqualTo("websocket");
    }
}