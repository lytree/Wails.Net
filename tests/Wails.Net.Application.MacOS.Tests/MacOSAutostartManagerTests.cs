using System.Xml.Linq;
using TUnit.Core;
using Wails.Net.Application.Managers;

namespace Wails.Net.Application.MacOS.Tests;

/// <summary>
/// <see cref="MacOSAutostartManager"/> 的单元测试（TUnit）。
/// <para>
/// 覆盖 LaunchAgent 方案中与系统调用无关的纯逻辑：plist 标签归一化（<c>wails.autostart.&lt;slug&gt;</c>）、
/// plist 路径拼接、plist XML 结构与转义。真实的 <c>launchctl</c> 调用与文件写入需要 macOS 主机，
/// 非 macOS 环境下 <c>Enable/Disable/IsEnabled</c> 必须保持 no-op，因此也顺带守住"不在测试机上误写用户目录"。
/// </para>
/// </summary>
public sealed class MacOSAutostartManagerTests
{
    /// <summary>
    /// 标签应保留字母与数字、丢弃空格与标点，并带 <c>wails.autostart.</c> 前缀。
    /// </summary>
    [Test]
    [Arguments("MyApp", "wails.autostart.MyApp")]
    [Arguments("My-App 2!", "wails.autostart.MyApp2")]
    [Arguments("my_app.v2", "wails.autostart.myappv2")]
    [Arguments("  spaced name  ", "wails.autostart.spacedname")]
    public async Task GetLabel_NormalizesNameToSlug(string appName, string expected)
    {
        var manager = new MacOSAutostartManager(appName);

        await Assert.That(manager.GetLabel()).IsEqualTo(expected);
    }

    /// <summary>
    /// 应用名不含任何字母数字（空字符串 / 纯符号）时回退到固定标签 <c>wails.autostart.app</c>。
    /// </summary>
    [Test]
    [Arguments("")]
    [Arguments("!@#$%^&*()")]
    public async Task GetLabel_WithoutAlphanumerics_FallsBackToDefault(string appName)
    {
        var manager = new MacOSAutostartManager(appName);

        await Assert.That(manager.GetLabel()).IsEqualTo("wails.autostart.app");
    }

    /// <summary>
    /// LaunchAgents 目录固定为用户主目录下的 <c>Library/LaunchAgents</c>。
    /// </summary>
    [Test]
    public async Task GetLaunchAgentsDirectory_IsUnderUserLibrary()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "LaunchAgents");

        await Assert.That(MacOSAutostartManager.GetLaunchAgentsDirectory()).IsEqualTo(expected);
    }

    /// <summary>
    /// plist 路径应为 <c>~/Library/LaunchAgents/{label}.plist</c>。
    /// </summary>
    [Test]
    public async Task GetPlistPath_CombinesLaunchAgentsDirWithLabel()
    {
        var manager = new MacOSAutostartManager("My App!");

        var expected = Path.Combine(
            MacOSAutostartManager.GetLaunchAgentsDirectory(),
            "wails.autostart.MyApp.plist");

        await Assert.That(manager.GetPlistPath()).IsEqualTo(expected);
    }

    /// <summary>
    /// 生成的 plist 必须是合法 XML，并包含 LaunchAgent 必需的 Label / ProgramArguments / RunAtLoad / KeepAlive。
    /// </summary>
    [Test]
    public async Task BuildPlist_ProducesValidLaunchAgentXml()
    {
        const string label = "wails.autostart.MyApp";
        const string exe = "/Applications/MyApp.app/Contents/MacOS/MyApp";

        var plist = MacOSAutostartManager.BuildPlist(label, exe);
        var dict = ParsePlistDict(plist);

        await Assert.That(GetValueElement(dict, "Label").Value).IsEqualTo(label);

        var arguments = GetValueElement(dict, "ProgramArguments");
        await Assert.That(arguments.Name.LocalName).IsEqualTo("array");
        await Assert.That(arguments.Elements("string").Count()).IsEqualTo(1);
        await Assert.That(arguments.Elements("string").Single().Value).IsEqualTo(exe);

        // plist 布尔值以空元素 <true/> / <false/> 表示。
        await Assert.That(GetValueElement(dict, "RunAtLoad").Name.LocalName).IsEqualTo("true");
        await Assert.That(GetValueElement(dict, "KeepAlive").Name.LocalName).IsEqualTo("false");
    }

    /// <summary>
    /// 应用名与路径中的 XML 特殊字符必须转义，保证 plist 可被 launchd 解析。
    /// </summary>
    [Test]
    public async Task BuildPlist_EscapesXmlSpecialCharacters()
    {
        const string label = "wails.autostart.A&B";
        const string exe = "/Volumes/A & B <test>/MyApp";

        var dict = ParsePlistDict(MacOSAutostartManager.BuildPlist(label, exe));

        // XDocument 解析后应还原为原始字符串，证明转义正确且未破坏结构。
        await Assert.That(GetValueElement(dict, "Label").Value).IsEqualTo(label);
        await Assert.That(GetValueElement(dict, "ProgramArguments").Elements("string").Single().Value)
            .IsEqualTo(exe);
    }

    /// <summary>
    /// <see cref="MacOSAutostartManager.EscapeXml"/> 转义 &amp;、&lt;、&gt;、&quot;，单引号无需转义。
    /// </summary>
    [Test]
    public async Task EscapeXml_EscapesReservedCharacters()
    {
        await Assert.That(MacOSAutostartManager.EscapeXml("a&b<c>d\"e'f"))
            .IsEqualTo("a&amp;b&lt;c&gt;d&quot;e'f");
        await Assert.That(MacOSAutostartManager.EscapeXml("plain"))
            .IsEqualTo("plain");
    }

    /// <summary>
    /// 非 macOS 主机上 <see cref="MacOSAutostartManager.IsEnabled"/> 必须返回 false（LaunchAgent 机制不存在）。
    /// </summary>
    [Test]
    public async Task IsEnabled_OnNonMacOS_ReturnsFalse()
    {
        if (OperatingSystem.IsMacOS())
        {
            Skip.Test("macOS 主机上 IsEnabled 依赖真实 LaunchAgent，需在 macOS 端到端验证");
            return;
        }

        var manager = new MacOSAutostartManager("wails-test-autostart-" + Guid.NewGuid());

        await Assert.That(manager.IsEnabled()).IsFalse();
    }

    /// <summary>
    /// 非 macOS 主机上 <c>Enable/Disable</c> 为 no-op：不得抛出异常，也不得在用户主目录写入临时 plist。
    /// </summary>
    [Test]
    public async Task EnableAndDisable_OnNonMacOS_DoNotTouchFileSystem()
    {
        if (OperatingSystem.IsMacOS())
        {
            Skip.Test("macOS 主机上 Enable 会真实写入 LaunchAgents，需在 macOS 端到端验证");
            return;
        }

        var manager = new MacOSAutostartManager("wails-test-autostart-" + Guid.NewGuid());
        var plistPath = manager.GetPlistPath();

        await Assert.That(() => manager.Enable()).ThrowsNothing();
        await Assert.That(File.Exists(plistPath)).IsFalse();

        await Assert.That(() => manager.Disable()).ThrowsNothing();
        await Assert.That(File.Exists(plistPath)).IsFalse();
    }

    /// <summary>
    /// 解析 plist 并返回根 <c>&lt;dict&gt;</c> 元素。
    /// </summary>
    /// <param name="plist">plist XML 字符串。</param>
    /// <returns>根 dict 元素。</returns>
    private static XElement ParsePlistDict(string plist)
    {
        var document = XDocument.Parse(plist);
        var dict = document.Root?.Element("dict");
        if (dict is null)
        {
            throw new InvalidOperationException("plist 缺少根 <dict> 元素");
        }

        return dict;
    }

    /// <summary>
    /// 按 key 名取 dict 中紧随其后的值元素（plist 的 key/value 交替结构）。
    /// </summary>
    /// <param name="dict">根 dict 元素。</param>
    /// <param name="key">键名。</param>
    /// <returns>值元素。</returns>
    private static XElement GetValueElement(XElement dict, string key)
    {
        var children = dict.Elements().ToList();
        var index = children.FindIndex(element => element.Name == "key" && element.Value == key);
        if (index < 0 || index + 1 >= children.Count)
        {
            throw new InvalidOperationException($"plist 缺少键 {key} 或其值");
        }

        return children[index + 1];
    }
}
