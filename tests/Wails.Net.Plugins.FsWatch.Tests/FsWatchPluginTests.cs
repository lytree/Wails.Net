using Wails.Net.Plugins.FsWatch;

namespace Wails.Net.Plugins.FsWatch.Tests;

public class FsWatchPluginTests
{
    /// <summary>
    /// 插件标识遵循仓库规范：kebab-case 插件 ID（`fs-watch`）；命令前缀另为 `fswatch.*`。
    /// </summary>
    [Test]
    public async Task Plugin_Name_MatchesPluginId()
    {
        var plugin = new FsWatchPlugin();
        await Assert.That(plugin.Name).IsEqualTo("fs-watch");
    }
}