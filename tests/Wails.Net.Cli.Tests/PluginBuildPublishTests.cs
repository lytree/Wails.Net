using TUnit.Assertions;
using TUnit.Core;
using Wails.Net.Cli.Build;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// PluginBuilder / PluginPublisher 单元测试。
/// 验证插件命名转换、仓库布局发现、版本解析与一致性校验、NuGet 包定位等纯逻辑。
/// </summary>
[NotInParallel]
public sealed class PluginBuildPublishTests
{
    // ---- 命名转换 ----

    [Test]
    [Arguments("Updater", "updater")]
    [Arguments("FileSystem", "file-system")]
    [Arguments("WebSocket", "web-socket")]
    [Arguments("FsWatch", "fs-watch")]
    [Arguments("GlobalShortcut", "global-shortcut")]
    [Arguments("PersistedScope", "persisted-scope")]
    [Arguments("OsInfo", "os-info")]
    [Arguments("PowerManagement", "power-management")]
    [Arguments("DeepLink", "deep-link")]
    [Arguments("AppInfo", "app-info")]
    public async Task ToKebabCase_PascalCase_ReturnsKebabCase(string input, string expected)
    {
        var result = PluginBuilder.ToKebabCase(input);
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    [Arguments("updater", "Updater")]
    [Arguments("file-system", "FileSystem")]
    [Arguments("fs-watch", "FsWatch")]
    [Arguments("global_shortcut", "GlobalShortcut")]
    [Arguments("web socket", "WebSocket")]
    public async Task ToPascalCase_KebabOrSeparated_ReturnsPascalCase(string input, string expected)
    {
        var result = PluginBuilder.ToPascalCase(input);
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task ToKebabCase_NullOrEmpty_ReturnsEmpty()
    {
        await Assert.That(PluginBuilder.ToKebabCase(string.Empty)).IsEqualTo(string.Empty);
        await Assert.That(PluginBuilder.ToKebabCase("   ")).IsEqualTo("   ");
    }

    // ---- package.json 字段读取 ----

    [Test]
    public async Task ReadPackageJsonField_ExistingField_ReturnsValue()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "package.json"),
                """{ "name": "@wails-net/plugin-updater", "version": "0.1.0-alpha.1" }""");

            await Assert.That(PluginBuilder.ReadPackageJsonField(dir, "name"))
                .IsEqualTo("@wails-net/plugin-updater");
            await Assert.That(PluginBuilder.ReadPackageJsonField(dir, "version"))
                .IsEqualTo("0.1.0-alpha.1");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public async Task ReadPackageJsonField_MissingFileOrField_ReturnsNull()
    {
        var dir = CreateTempDir();
        try
        {
            await Assert.That(PluginBuilder.ReadPackageJsonField(dir, "name")).IsNull();
            File.WriteAllText(Path.Combine(dir, "package.json"), """{ "name": "x" }""");
            await Assert.That(PluginBuilder.ReadPackageJsonField(dir, "version")).IsNull();
            File.WriteAllText(Path.Combine(dir, "package.json"), "not-json{{{");
            await Assert.That(PluginBuilder.ReadPackageJsonField(dir, "name")).IsNull();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---- 仓库根发现与插件发现 ----

    [Test]
    public async Task FindRepoRoot_FromSubdir_ReturnsRepoRoot()
    {
        var repo = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(repo, "Directory.Build.props"), "<Project/>");
            Directory.CreateDirectory(Path.Combine(repo, "src", "Wails.Net.Plugins.Updater"));

            var subdir = Path.Combine(repo, "src", "Wails.Net.Plugins.Updater");
            var root = PluginBuilder.FindRepoRoot(subdir);
            await Assert.That(root).IsEqualTo(repo);
        }
        finally
        {
            Directory.Delete(repo, recursive: true);
        }
    }

    [Test]
    public async Task DiscoverPlugins_MonorepoLayout_ReturnsBothBackendAndFrontend()
    {
        var repo = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(repo, "Directory.Build.props"),
                """<Project><PropertyGroup><WailsNetVersion>0.1.0-alpha.1</WailsNetVersion></PropertyGroup></Project>""");

            var backendDir = Path.Combine(repo, "src", "Wails.Net.Plugins.Updater");
            Directory.CreateDirectory(backendDir);
            File.WriteAllText(Path.Combine(backendDir, "Wails.Net.Plugins.Updater.csproj"), "<Project/>");

            var frontendDir = Path.Combine(repo, "packages", "wails-net-plugin-updater");
            Directory.CreateDirectory(frontendDir);
            File.WriteAllText(Path.Combine(frontendDir, "package.json"),
                """{ "name": "@wails-net/plugin-updater", "version": "0.1.0-alpha.1" }""");

            var plugins = PluginBuilder.DiscoverPlugins(startDir: repo);

            await Assert.That(plugins).Count().IsEqualTo(1);
            var plugin = plugins[0];
            await Assert.That(plugin.Name).IsEqualTo("updater");
            await Assert.That(plugin.BackendPackageId).IsEqualTo("Wails.Net.Plugins.Updater");
            await Assert.That(plugin.BackendProjectPath).IsNotNull();
            await Assert.That(plugin.FrontendDir).IsNotNull();
            await Assert.That(plugin.FrontendPackageName).IsEqualTo("@wails-net/plugin-updater");
        }
        finally
        {
            Directory.Delete(repo, recursive: true);
        }
    }

    [Test]
    public async Task DiscoverPlugins_BackendOnly_ReturnsWithoutFrontend()
    {
        var repo = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(repo, "Directory.Build.props"), "<Project/>");
            var backendDir = Path.Combine(repo, "src", "Wails.Net.Plugins.Clipboard");
            Directory.CreateDirectory(backendDir);
            File.WriteAllText(Path.Combine(backendDir, "Wails.Net.Plugins.Clipboard.csproj"), "<Project/>");

            var plugins = PluginBuilder.DiscoverPlugins(startDir: repo);

            await Assert.That(plugins).Count().IsEqualTo(1);
            await Assert.That(plugins[0].Name).IsEqualTo("clipboard");
            await Assert.That(plugins[0].FrontendDir).IsNull();
        }
        finally
        {
            Directory.Delete(repo, recursive: true);
        }
    }

    [Test]
    public async Task DiscoverPlugins_FilterByName_MatchKebabOrPascal()
    {
        var repo = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(repo, "Directory.Build.props"), "<Project/>");
            foreach (var name in new[] { "Updater", "FileSystem" })
            {
                var dir = Path.Combine(repo, "src", $"Wails.Net.Plugins.{name}");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, $"Wails.Net.Plugins.{name}.csproj"), "<Project/>");
            }

            var byKebab = PluginBuilder.DiscoverPlugins("file-system", repo);
            var byPascal = PluginBuilder.DiscoverPlugins("Updater", repo);

            await Assert.That(byKebab).Count().IsEqualTo(1);
            await Assert.That(byKebab[0].Name).IsEqualTo("file-system");
            await Assert.That(byPascal).Count().IsEqualTo(1);
            await Assert.That(byPascal[0].Name).IsEqualTo("updater");
        }
        finally
        {
            Directory.Delete(repo, recursive: true);
        }
    }

    [Test]
    public async Task DiscoverPlugins_UnknownName_ReturnsEmpty()
    {
        var repo = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(repo, "Directory.Build.props"), "<Project/>");
            var plugins = PluginBuilder.DiscoverPlugins("does-not-exist", repo);
            await Assert.That(plugins).IsEmpty();
        }
        finally
        {
            Directory.Delete(repo, recursive: true);
        }
    }

    // ---- 版本一致性校验 ----

    [Test]
    public async Task ValidateVersionConsistency_MatchingVersions_Passes()
    {
        var repo = CreateTempDir();
        try
        {
            var plugin = CreateDualPlugin(repo, "0.1.0-alpha.1", "0.1.0-alpha.1");
            var error = PluginPublisher.ValidateVersionConsistency(plugin, out var backend, out var frontend);
            await Assert.That(error).IsNull();
            await Assert.That(backend).IsEqualTo("0.1.0-alpha.1");
            await Assert.That(frontend).IsEqualTo("0.1.0-alpha.1");
        }
        finally
        {
            Directory.Delete(repo, recursive: true);
        }
    }

    [Test]
    public async Task ValidateVersionConsistency_MismatchedVersions_ReturnsError()
    {
        var repo = CreateTempDir();
        try
        {
            var plugin = CreateDualPlugin(repo, "0.1.0-alpha.1", "0.2.0");
            var error = PluginPublisher.ValidateVersionConsistency(plugin, out _, out _);
            await Assert.That(error).IsNotNull();
            await Assert.That(error).Contains("0.1.0-alpha.1");
            await Assert.That(error).Contains("0.2.0");
        }
        finally
        {
            Directory.Delete(repo, recursive: true);
        }
    }

    [Test]
    public async Task ValidateVersionConsistency_BackendOnly_Passes()
    {
        var repo = CreateTempDir();
        try
        {
            var plugin = CreateBackendOnlyPlugin(repo, "1.0.0");
            var error = PluginPublisher.ValidateVersionConsistency(plugin, out var backend, out var frontend);
            await Assert.That(error).IsNull();
            await Assert.That(backend).IsEqualTo("1.0.0");
            await Assert.That(frontend).IsNull();
        }
        finally
        {
            Directory.Delete(repo, recursive: true);
        }
    }

    // ---- NuGet 包定位 ----

    [Test]
    public async Task FindNuGetPackage_MatchingPackage_ReturnsNupkg()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "Wails.Net.Plugins.Updater.0.1.0-alpha.1.nupkg"), "x");
            File.WriteAllText(Path.Combine(dir, "Wails.Net.Plugins.Updater.0.1.0-alpha.1.snupkg"), "x");
            File.WriteAllText(Path.Combine(dir, "Wails.Net.Plugins.Other.1.0.0.nupkg"), "x");

            var plugin = new PluginLayout
            {
                Name = "updater",
                BackendPackageId = "Wails.Net.Plugins.Updater",
            };

            var found = PluginPublisher.FindNuGetPackage(plugin, dir);
            await Assert.That(found).IsNotNull();
            await Assert.That(Path.GetFileName(found!)).IsEqualTo("Wails.Net.Plugins.Updater.0.1.0-alpha.1.nupkg");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public async Task FindNuGetPackage_NoMatch_ReturnsNull()
    {
        var dir = CreateTempDir();
        try
        {
            var plugin = new PluginLayout
            {
                Name = "updater",
                BackendPackageId = "Wails.Net.Plugins.Updater",
            };
            await Assert.That(PluginPublisher.FindNuGetPackage(plugin, dir)).IsNull();
            await Assert.That(PluginPublisher.FindNuGetPackage(plugin, Path.Combine(dir, "missing"))).IsNull();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---- 辅助构造 ----

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"wails-net-plugin-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static PluginLayout CreateDualPlugin(string repo, string backendVersion, string frontendVersion)
    {
        File.WriteAllText(Path.Combine(repo, "Directory.Build.props"),
            $"<Project><PropertyGroup><WailsNetVersion>{backendVersion}</WailsNetVersion></PropertyGroup></Project>");

        var backendDir = Path.Combine(repo, "src", "Wails.Net.Plugins.Updater");
        Directory.CreateDirectory(backendDir);
        File.WriteAllText(Path.Combine(backendDir, "Wails.Net.Plugins.Updater.csproj"), "<Project/>");

        var frontendDir = Path.Combine(repo, "packages", "wails-net-plugin-updater");
        Directory.CreateDirectory(frontendDir);
        File.WriteAllText(Path.Combine(frontendDir, "package.json"),
            $$"""{ "name": "@wails-net/plugin-updater", "version": "{{frontendVersion}}" }""");

        return PluginBuilder.DiscoverPlugins(startDir: repo).Single();
    }

    private static PluginLayout CreateBackendOnlyPlugin(string repo, string backendVersion)
    {
        File.WriteAllText(Path.Combine(repo, "Directory.Build.props"),
            $"<Project><PropertyGroup><WailsNetVersion>{backendVersion}</WailsNetVersion></PropertyGroup></Project>");

        var backendDir = Path.Combine(repo, "src", "Wails.Net.Plugins.Updater");
        Directory.CreateDirectory(backendDir);
        File.WriteAllText(Path.Combine(backendDir, "Wails.Net.Plugins.Updater.csproj"), "<Project/>");

        return PluginBuilder.DiscoverPlugins(startDir: repo).Single();
    }
}
