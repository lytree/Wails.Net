using Wails.Net.Plugins.PersistedScope;

namespace Wails.Net.Plugins.PersistedScope.Tests;

public class PersistedScopePluginTests
{
    [Test]
    public async Task Plugin_Name_MatchesCommandPrefix()
    {
        var plugin = new PersistedScopePlugin();
        await Assert.That(plugin.Name).IsEqualTo("persisted-scope");
    }
}