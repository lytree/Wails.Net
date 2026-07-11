using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TUnit.Core;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Plugins.BuiltIn;

namespace Wails.Net.Application.Tests;

/// <summary>
/// 新增内置插件（Localization、FileAssociation、Upload）的单元测试（TUnit）。
/// 对应 Wails v3 / Tauri v2 功能对齐阶段新增的插件。
/// </summary>
[NotInParallel]
public sealed class NewPlugins2Tests
{
    /// <summary>
    /// 创建模拟的 <see cref="IPluginContext"/>，提供 CommandRegistry、配置和日志工厂。
    /// </summary>
    private static IPluginContext CreatePluginContext()
    {
        var services = new ServiceCollection();
        var commands = new CommandRegistry();
        var config = new ConfigurationBuilder().Build();
        var loggerFactory = LoggerFactory.Create(_ => { });

        var context = Substitute.For<IPluginContext>();
        context.Services.Returns(services);
        context.Commands.Returns(commands);
        context.Configuration.Returns(config);
        context.LoggerFactory.Returns(loggerFactory);
        return context;
    }

    // ---------------------------------------------------------------------
    // LocalizationPlugin
    // ---------------------------------------------------------------------

    [Test]
    public async Task LocalizationPlugin_Name_ReturnsLocalization()
    {
        // 安排
        var plugin = new LocalizationPlugin();

        // 操作与断言
        await Assert.That(plugin.Name).IsEqualTo("localization");
    }

    [Test]
    public async Task LocalizationPlugin_ConfigureServices_DoesNotThrow()
    {
        // 安排
        var plugin = new LocalizationPlugin();
        var services = new ServiceCollection();

        // 操作与断言
        await Assert.That(() => plugin.ConfigureServices(services)).ThrowsNothing();
    }

    [Test]
    public async Task LocalizationPlugin_Configure_RegistersCommands()
    {
        // 安排
        var plugin = new LocalizationPlugin();
        var context = CreatePluginContext();

        // 操作
        await Assert.That(() => plugin.Configure(context)).ThrowsNothing();

        // 断言：应注册 5 个 localization.* 命令
        await Assert.That(context.Commands.Count).IsEqualTo(5);
        var names = context.Commands.GetCommandNames().ToList();
        await Assert.That(names.Contains("localization.setLocale")).IsTrue();
        await Assert.That(names.Contains("localization.getLocale")).IsTrue();
        await Assert.That(names.Contains("localization.t")).IsTrue();
        await Assert.That(names.Contains("localization.registerTranslations")).IsTrue();
        await Assert.That(names.Contains("localization.getAvailableLocales")).IsTrue();
    }

    [Test]
    public async Task LocalizationPlugin_SetLocale_UpdatesCurrentLocale()
    {
        // 安排
        LocalizationPlugin.SetLocale("zh-CN");

        // 操作与断言
        await Assert.That(LocalizationPlugin.Translate("test.key")).IsEqualTo("test.key");
    }

    [Test]
    public async Task LocalizationPlugin_RegisterTranslations_AndTranslate_ReturnsValue()
    {
        // 安排
        LocalizationPlugin.SetLocale("zh-CN");
        var translations = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["greeting"] = "你好"
        });

        // 操作
        LocalizationPlugin.RegisterTranslations("zh-CN", translations);

        // 断言
        await Assert.That(LocalizationPlugin.Translate("greeting")).IsEqualTo("你好");
    }

    [Test]
    public async Task LocalizationPlugin_Translate_FallbacksToDefaultLocale()
    {
        // 安排
        LocalizationPlugin.SetLocale("ja-JP");
        var translations = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["farewell"] = "Goodbye"
        });
        LocalizationPlugin.RegisterTranslations("en-US", translations);

        // 操作与断言：ja-JP 未注册，回退到 en-US
        await Assert.That(LocalizationPlugin.Translate("farewell")).IsEqualTo("Goodbye");
    }

    [Test]
    public async Task LocalizationPlugin_Translate_UnknownKey_ReturnsKeyItself()
    {
        // 安排
        LocalizationPlugin.SetLocale("en-US");

        // 操作与断言
        await Assert.That(LocalizationPlugin.Translate("nonexistent.key")).IsEqualTo("nonexistent.key");
    }

    [Test]
    public async Task LocalizationPlugin_Translate_WithParams_InterpolatesValues()
    {
        // 安排
        LocalizationPlugin.SetLocale("en-US");
        var translations = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["welcome"] = "Hello, {name}!"
        });
        LocalizationPlugin.RegisterTranslations("en-US", translations);
        var parameters = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["name"] = "World"
        });

        // 操作与断言
        await Assert.That(LocalizationPlugin.Translate("welcome", parameters)).IsEqualTo("Hello, World!");
    }

    [Test]
    public async Task LocalizationPlugin_SetLocale_EmptyOrWhitespace_DoesNotChange()
    {
        // 安排
        LocalizationPlugin.SetLocale("fr-FR");

        // 操作：传入空字符串不应改变当前 locale
        LocalizationPlugin.SetLocale("   ");

        // 断言
        await Assert.That(LocalizationPlugin.Translate("test")).IsEqualTo("test");
    }

    [Test]
    public async Task LocalizationPlugin_RegisterTranslations_NullOrWhitespace_Ignores()
    {
        // 安排与操作：传入空 locale 或空 JSON 不应抛出异常
        await Assert.That(() => LocalizationPlugin.RegisterTranslations("", "{}")).ThrowsNothing();
        await Assert.That(() => LocalizationPlugin.RegisterTranslations("en-US", "")).ThrowsNothing();
    }

    // ---------------------------------------------------------------------
    // FileAssociationPlugin
    // ---------------------------------------------------------------------

    [Test]
    public async Task FileAssociationPlugin_Name_ReturnsFileAssociation()
    {
        // 安排
        var plugin = new FileAssociationPlugin([".testext"], "TestApp");

        // 操作与断言
        await Assert.That(plugin.Name).IsEqualTo("file-association");
    }

    [Test]
    public async Task FileAssociationPlugin_ConfigureServices_DoesNotThrow()
    {
        // 安排
        var plugin = new FileAssociationPlugin([".testext"], "TestApp");
        var services = new ServiceCollection();

        // 操作与断言
        await Assert.That(() => plugin.ConfigureServices(services)).ThrowsNothing();
    }

    [Test]
    public async Task FileAssociationPlugin_Configure_RegistersCommands()
    {
        // 安排：使用空扩展名数组避免在测试中污染系统注册表
        var plugin = new FileAssociationPlugin([], "TestApp");
        var context = CreatePluginContext();

        // 操作
        await Assert.That(() => plugin.Configure(context)).ThrowsNothing();

        // 断言：应注册 3 个 fileassociation.* 命令
        await Assert.That(context.Commands.Count).IsEqualTo(3);
        var names = context.Commands.GetCommandNames().ToList();
        await Assert.That(names.Contains("fileassociation.register")).IsTrue();
        await Assert.That(names.Contains("fileassociation.unregister")).IsTrue();
        await Assert.That(names.Contains("fileassociation.getRegistered")).IsTrue();
    }

    [Test]
    public async Task FileAssociationPlugin_RegisterExtension_AddsToExtensionList()
    {
        // 安排
        var plugin = new FileAssociationPlugin([], "TestApp");

        // 操作
        plugin.RegisterExtension(".newext");

        // 断言
        await Assert.That(plugin.GetRegisteredExtensions()).Contains(".newext");
    }

    [Test]
    public async Task FileAssociationPlugin_UnregisterExtension_RemovesFromList()
    {
        // 安排
        var plugin = new FileAssociationPlugin([".removeext"], "TestApp");

        // 操作
        plugin.UnregisterExtension(".removeext");

        // 断言
        await Assert.That(plugin.GetRegisteredExtensions().Contains(".removeext")).IsFalse();
    }

    [Test]
    public async Task FileAssociationPlugin_RegisterExtension_NormalizesExtension()
    {
        // 安排
        var plugin = new FileAssociationPlugin([], "TestApp");

        // 操作：传入不带点的扩展名，应规范化为小写带点形式
        plugin.RegisterExtension("MYEXT");

        // 断言
        await Assert.That(plugin.GetRegisteredExtensions().Contains(".myext")).IsTrue();
    }

    [Test]
    public async Task FileAssociationPlugin_RegisterExtension_DuplicateDoesNotAddTwice()
    {
        // 安排
        var plugin = new FileAssociationPlugin([".dup"], "TestApp");

        // 操作：再次注册相同扩展名不应重复添加
        plugin.RegisterExtension(".dup");

        // 断言
        var count = plugin.GetRegisteredExtensions().Count(e => e == ".dup");
        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async Task FileAssociationPlugin_RegisterExtension_NullOrWhitespace_Ignores()
    {
        // 安排
        var plugin = new FileAssociationPlugin([], "TestApp");

        // 操作与断言：传入空值不应抛出异常
        await Assert.That(() => plugin.RegisterExtension("")).ThrowsNothing();
        await Assert.That(() => plugin.RegisterExtension("   ")).ThrowsNothing();
        await Assert.That(plugin.GetRegisteredExtensions()).IsEmpty();
    }

    [Test]
    public async Task FileAssociationPlugin_UnregisterExtension_UnknownExtension_DoesNotThrow()
    {
        // 安排
        var plugin = new FileAssociationPlugin([".exists"], "TestApp");

        // 操作与断言：注销不存在的扩展名不应抛出异常
        await Assert.That(() => plugin.UnregisterExtension(".notexists")).ThrowsNothing();
    }

    // ---------------------------------------------------------------------
    // UploadPlugin
    // ---------------------------------------------------------------------

    [Test]
    public async Task UploadPlugin_Name_ReturnsUpload()
    {
        // 安排
        var plugin = new UploadPlugin();

        // 操作与断言
        await Assert.That(plugin.Name).IsEqualTo("upload");
    }

    [Test]
    public async Task UploadPlugin_ConfigureServices_DoesNotThrow()
    {
        // 安排
        var plugin = new UploadPlugin();
        var services = new ServiceCollection();

        // 操作与断言
        await Assert.That(() => plugin.ConfigureServices(services)).ThrowsNothing();
    }

    [Test]
    public async Task UploadPlugin_Configure_RegistersCommands()
    {
        // 安排
        var plugin = new UploadPlugin();
        var context = CreatePluginContext();

        // 操作
        await Assert.That(() => plugin.Configure(context)).ThrowsNothing();

        // 断言：应注册 4 个 upload.* 命令
        await Assert.That(context.Commands.Count).IsEqualTo(4);
        var names = context.Commands.GetCommandNames().ToList();
        await Assert.That(names.Contains("upload.download")).IsTrue();
        await Assert.That(names.Contains("upload.upload")).IsTrue();
        await Assert.That(names.Contains("upload.downloadWithProgress")).IsTrue();
        await Assert.That(names.Contains("upload.uploadWithProgress")).IsTrue();
    }

    [Test]
    public async Task UploadPlugin_DownloadFileAsync_InvalidUrl_ReturnsFalse()
    {
        // 安排与操作：使用无效 URL 下载
        var result = await UploadPlugin.DownloadFileAsync(
            "http://invalid.localhost.invalid/file.txt",
            Path.GetTempFileName());

        // 断言：应返回 false，不抛出异常
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task UploadPlugin_UploadFileAsync_NonExistentFile_ReturnsFalse()
    {
        // 安排与操作：上传不存在的文件
        var result = await UploadPlugin.UploadFileAsync(
            "http://localhost/nonexistent",
            Path.Combine(Path.GetTempPath(), "nonexistent_upload_test_file.txt"));

        // 断言：应返回 false
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task UploadPlugin_DownloadWithProgressAsync_InvalidUrl_ReturnsFalse()
    {
        // 安排与操作：使用无效 URL 下载带进度
        var result = await UploadPlugin.DownloadWithProgressAsync(
            "http://invalid.localhost.invalid/file.txt",
            Path.GetTempFileName());

        // 断言：应返回 false
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task UploadPlugin_UploadWithProgressAsync_NonExistentFile_ReturnsFalse()
    {
        // 安排与操作：上传不存在的文件
        var result = await UploadPlugin.UploadWithProgressAsync(
            "http://localhost/nonexistent",
            Path.Combine(Path.GetTempPath(), "nonexistent_upload_progress_test_file.txt"));

        // 断言：应返回 false
        await Assert.That(result).IsFalse();
    }
}

/// <summary>
/// <see cref="NewPlugins2Tests"/> 的扩展方法，提供对 FileAssociationPlugin 私有字段的访问。
/// 通过重新调用 Configure 并读取 fileassociation.getRegistered 命令来获取已注册扩展名列表。
/// </summary>
internal static class FileAssociationPluginTestExtensions
{
    /// <summary>
    /// 获取已注册的文件扩展名列表。
    /// </summary>
    /// <param name="plugin">插件实例。</param>
    /// <returns>已注册扩展名数组。</returns>
    public static string[] GetRegisteredExtensions(this FileAssociationPlugin plugin)
    {
        var services = new ServiceCollection();
        var commands = new CommandRegistry();
        var config = new ConfigurationBuilder().Build();
        var loggerFactory = LoggerFactory.Create(_ => { });

        var context = NSubstitute.Substitute.For<IPluginContext>();
        context.Services.Returns(services);
        context.Commands.Returns(commands);
        context.Configuration.Returns(config);
        context.LoggerFactory.Returns(loggerFactory);
        plugin.Configure(context);

        // 调用 fileassociation.getRegistered 命令
        var entry = commands.Find("fileassociation.getRegistered");
        if (entry is null)
        {
            return [];
        }

        var result = entry.Method.Invoke(entry.Instance, null);
        return result is string[] arr ? arr : Array.Empty<string>();
    }
}
