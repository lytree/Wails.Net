using Wails.Net.Plugins.FileSystem;

namespace Wails.Net.Plugins.FileSystem.Tests;

public class FileSystemPluginTests
{
    [Test]
    public async Task Plugin_Name_MatchesCommandPrefix()
    {
        var plugin = new FileSystemPlugin();
        await Assert.That(plugin.Name).IsEqualTo("fs");
    }
}