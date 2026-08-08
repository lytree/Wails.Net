using Wails.Net.Plugins.Keychain;

namespace Wails.Net.Plugins.Keychain.Tests;

public class KeychainPluginTests
{
    [Test]
    public async Task Plugin_Name_MatchesCommandPrefix()
    {
        var plugin = new KeychainPlugin();
        await Assert.That(plugin.Name).IsEqualTo("keychain");
    }
}