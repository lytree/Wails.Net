using Wails.Net.Plugins.Shell;

namespace Wails.Net.Plugins.Shell.Tests;

public class ShellPluginTests
{
    [Test]
    public async Task Plugin_Name_MatchesCommandPrefix()
    {
        var plugin = new ShellPlugin();
        await Assert.That(plugin.Name).IsEqualTo("shell");
    }
}