
using Wails.Net.Plugins.WebSocket;

namespace Wails.Net.Plugins.Websocket.Tests;

public class WebsocketPluginTests
{
    [Test]
    public async Task Plugin_Name_MatchesCommandPrefix()
    {
        var plugin = new WebSocketPlugin();
        await Assert.That(plugin.Name).IsEqualTo("websocket");
    }
}