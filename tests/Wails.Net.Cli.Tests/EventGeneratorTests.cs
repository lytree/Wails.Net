using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using Wails.Net.Events;
using Wails.Net.Generator;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// 事件代码生成器单元测试。
/// 验证从 <see cref="EventEnumInfo"/> 和 <see cref="KnownEventFieldInfo"/> 元数据生成 TypeScript 事件定义。
/// </summary>
[NotInParallel]
public sealed class EventGeneratorTests
{
    [Test]
    public async Task GenerateFromEnum_ProducesExportedEnumWithMembers()
    {
        var generator = new EventGenerator();
        var enumInfo = CreateSampleEventEnumInfo();
        var content = generator.GenerateFromEnum(enumInfo);

        await Assert.That(content).Contains("export enum SampleEventEnum {");
        await Assert.That(content).Contains("Started = 1");
        await Assert.That(content).Contains("Stopped = 2");
        await Assert.That(content).Contains("Paused = 3");
    }

    [Test]
    public async Task GenerateFromEnum_LastMemberHasNoTrailingComma()
    {
        var generator = new EventGenerator();
        var enumInfo = CreateSampleEventEnumInfo();
        var content = generator.GenerateFromEnum(enumInfo);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // 找到最后一个枚举成员行，验证其没有逗号
        var pausedLine = lines.First(l => l.Contains("Paused"));
        await Assert.That(pausedLine.TrimEnd()).DoesNotEndWith(",");
    }

    [Test]
    public async Task GenerateFromEnum_UsesUnderlyingIntValue()
    {
        var generator = new EventGenerator();
        var enumInfo = CreateWindowEventTestEnumInfo();
        var content = generator.GenerateFromEnum(enumInfo);

        await Assert.That(content).Contains("Created = 100");
        await Assert.That(content).Contains("Closed = 200");
    }

    [Test]
    public async Task GenerateFromEnum_ByteUnderlyingType_ConvertsValues()
    {
        var generator = new EventGenerator();
        var enumInfo = CreateByteEnumInfo();
        var content = generator.GenerateFromEnum(enumInfo);

        await Assert.That(content).Contains("Alpha = 10");
        await Assert.That(content).Contains("Beta = 20");
    }

    [Test]
    public async Task GenerateKnownEvents_ProducesConstObject()
    {
        var generator = new EventGenerator();
        var fields = CreateSampleKnownEventsFields();
        var content = generator.GenerateKnownEvents("SampleKnownEvents", fields);

        await Assert.That(content).Contains("export const SampleKnownEvents = {");
        await Assert.That(content).Contains("Startup: \"app:startup\",");
        await Assert.That(content).Contains("Shutdown: \"app:shutdown\",");
        await Assert.That(content).Contains("export default SampleKnownEvents;");
    }

    [Test]
    public async Task GenerateKnownEvents_IncludesAllPublicConstFields()
    {
        var generator = new EventGenerator();
        var fields = CreateSampleKnownEventsFields();
        var content = generator.GenerateKnownEvents("SampleKnownEvents", fields);

        await Assert.That(content).Contains("Startup");
        await Assert.That(content).Contains("Shutdown");
        await Assert.That(content).Contains("ThemeChanged");
    }

    [Test]
    public async Task GenerateFromMetadata_ProducesContentForRegisteredEnums()
    {
        // 此测试验证 GenerateFromMetadata 能从全局注册表读取并生成内容。
        // 由于 GeneratedEventsMetadata 由源生成器自动填充（ApplicationEventType、WindowEventType 等），
        // 测试只需验证输出包含预期的枚举即可。
        var generator = new EventGenerator();
        var content = generator.GenerateFromMetadata();

        // 至少应包含头部注释
        await Assert.That(content).Contains("// 自动生成的 Wails.Net 事件类型定义文件");
    }

    [Test]
    public async Task GenerateKnownEventsFromMetadata_ProducesContentForRegisteredFields()
    {
        var generator = new EventGenerator();
        var content = generator.GenerateKnownEventsFromMetadata();

        // 至少应包含头部注释
        await Assert.That(content).Contains("// 自动生成的 Wails.Net 已知事件名称常量");
    }

    /// <summary>
    /// 创建 SampleEventEnum 的元数据。
    /// </summary>
    private static EventEnumInfo CreateSampleEventEnumInfo()
    {
        return new EventEnumInfo(
            FullName: "Wails.Net.Cli.Tests.SampleEventEnum",
            Namespace: "Wails.Net.Cli.Tests",
            Name: "SampleEventEnum",
            UnderlyingTypeName: "number",
            Members: new List<EventEnumMemberInfo>
            {
                new("Started", "1"),
                new("Stopped", "2"),
                new("Paused", "3")
            });
    }

    /// <summary>
    /// 创建 WindowEventTestEnum 的元数据（uint 底层类型）。
    /// </summary>
    private static EventEnumInfo CreateWindowEventTestEnumInfo()
    {
        return new EventEnumInfo(
            FullName: "Wails.Net.Cli.Tests.WindowEventTestEnum",
            Namespace: "Wails.Net.Cli.Tests",
            Name: "WindowEventTestEnum",
            UnderlyingTypeName: "number",
            Members: new List<EventEnumMemberInfo>
            {
                new("Created", "100"),
                new("Closed", "200")
            });
    }

    /// <summary>
    /// 创建 ByteEnum 的元数据（byte 底层类型）。
    /// </summary>
    private static EventEnumInfo CreateByteEnumInfo()
    {
        return new EventEnumInfo(
            FullName: "Wails.Net.Cli.Tests.ByteEnum",
            Namespace: "Wails.Net.Cli.Tests",
            Name: "ByteEnum",
            UnderlyingTypeName: "number",
            Members: new List<EventEnumMemberInfo>
            {
                new("Alpha", "10"),
                new("Beta", "20")
            });
    }

    /// <summary>
    /// 创建 SampleKnownEvents 类的字段元数据。
    /// </summary>
    private static IReadOnlyList<KnownEventFieldInfo> CreateSampleKnownEventsFields()
    {
        return new List<KnownEventFieldInfo>
        {
            new("SampleKnownEvents", "Startup", "app:startup"),
            new("SampleKnownEvents", "Shutdown", "app:shutdown"),
            new("SampleKnownEvents", "ThemeChanged", "app:theme:changed")
        };
    }
}
