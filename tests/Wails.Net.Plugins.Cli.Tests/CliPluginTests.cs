using Wails.Net.Plugins.Cli;

namespace Wails.Net.Plugins.Cli.Tests;

public class CliPluginTests
{
    [Test]
    public async Task Plugin_Name_MatchesCommandPrefix()
    {
        var plugin = new CliPlugin();
        await Assert.That(plugin.Name).IsEqualTo("cli");
    }
}