using Wails.Net.Plugins.Sql;

namespace Wails.Net.Plugins.Sql.Tests;

public class SqlPluginTests
{
    [Test]
    public async Task Plugin_Name_MatchesCommandPrefix()
    {
        var plugin = new SqlPlugin();
        await Assert.That(plugin.Name).IsEqualTo("sqlite");
    }
}