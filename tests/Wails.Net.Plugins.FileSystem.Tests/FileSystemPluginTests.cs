using Wails.Net.Plugins.FileSystem;

namespace Wails.Net.Plugins.FileSystem.Tests;

public class FileSystemPluginTests
{
    /// <summary>
    /// 插件标识遵循仓库规范：kebab-case 插件 ID（`filesystem`），而非命令前缀（`fs.*`）。
    /// </summary>
    [Test]
    public async Task Plugin_Name_MatchesPluginId()
    {
        var plugin = new FileSystemPlugin();
        await Assert.That(plugin.Name).IsEqualTo("filesystem");
    }
}