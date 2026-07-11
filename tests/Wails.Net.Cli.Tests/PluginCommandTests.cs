using TUnit.Assertions;
using TUnit.Core;
using Wails.Net.Cli.Commands;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// PluginCommand 单元测试。
/// 验证插件名到 NuGet 包标识的解析逻辑。
/// </summary>
[NotInParallel]
public sealed class PluginCommandTests
{
    [Test]
    [Arguments("filesystem", "Wails.Net.Plugins.FileSystem")]
    [Arguments("fs", "Wails.Net.Plugins.FileSystem")]
    [Arguments("clipboard", "Wails.Net.Plugins.Clipboard")]
    [Arguments("notification", "Wails.Net.Plugins.Notification")]
    [Arguments("dialog", "Wails.Net.Plugins.Dialog")]
    [Arguments("tray", "Wails.Net.Plugins.Tray")]
    [Arguments("sqlite", "Wails.Net.Plugins.Sqlite")]
    [Arguments("shell", "Wails.Net.Plugins.Shell")]
    [Arguments("updater", "Wails.Net.Plugins.Updater")]
    [Arguments("autostart", "Wails.Net.Plugins.Autostart")]
    public async Task ResolvePackageId_KnownShortName_ReturnsFullPackageId(string shortName, string expected)
    {
        var result = PluginCommand.ResolvePackageId(shortName);
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    [Arguments("FileSystem")] // 大小写不敏感
    [Arguments("FILESYSTEM")]
    [Arguments("Fs")]
    public async Task ResolvePackageId_CaseInsensitive_ReturnsPackageId(string mixedCase)
    {
        var result = PluginCommand.ResolvePackageId(mixedCase);
        await Assert.That(result).IsEqualTo("Wails.Net.Plugins.FileSystem");
    }

    [Test]
    [Arguments("Wails.Net.Plugins.FileSystem")] // 已是完整包名
    [Arguments("SomeCompany.MyPlugin")]
    [Arguments("My.Org.Plugin.v2")]
    public async Task ResolvePackageId_FullPackageName_ReturnsAsIs(string fullName)
    {
        var result = PluginCommand.ResolvePackageId(fullName);
        await Assert.That(result).IsEqualTo(fullName);
    }

    [Test]
    [Arguments("unknownplugin")]
    [Arguments("foo")]
    [Arguments("xyz123")]
    public async Task ResolvePackageId_UnknownShortName_ReturnsNull(string unknown)
    {
        var result = PluginCommand.ResolvePackageId(unknown);
        await Assert.That(result).IsNull();
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments(null!)]
    public async Task ResolvePackageId_NullOrWhitespace_ReturnsNull(string? invalid)
    {
        var result = PluginCommand.ResolvePackageId(invalid!);
        await Assert.That(result).IsNull();
    }
}
