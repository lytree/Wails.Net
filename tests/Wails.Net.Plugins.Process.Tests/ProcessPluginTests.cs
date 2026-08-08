using Wails.Net.Plugins.Process;

namespace Wails.Net.Plugins.Process.Tests;

public class ProcessPluginTests
{
    [Test]
    public async Task Plugin_Name_MatchesCommandPrefix()
    {
        var plugin = new ProcessPlugin();
        await Assert.That(plugin.Name).IsEqualTo("process");
    }
}