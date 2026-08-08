using Wails.Net.Plugins.Upload;

namespace Wails.Net.Plugins.Upload.Tests;

public class UploadPluginTests
{
    [Test]
    public async Task Plugin_Name_MatchesCommandPrefix()
    {
        var plugin = new UploadPlugin();
        await Assert.That(plugin.Name).IsEqualTo("upload");
    }
}