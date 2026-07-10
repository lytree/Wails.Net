using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using Wails.Net.Generator;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// 事件代码生成器单元测试。
/// 验证从 C# 枚举和 KnownEvents 常量类生成 TypeScript 事件定义。
/// </summary>
[NotInParallel]
public sealed class EventGeneratorTests
{
    [Test]
    public async Task GenerateFromEnum_ProducesExportedEnumWithMembers()
    {
        var generator = new EventGenerator();
        var content = generator.GenerateFromEnum(typeof(SampleEventEnum));

        await Assert.That(content).Contains("export enum SampleEventEnum {");
        await Assert.That(content).Contains("Started = 1");
        await Assert.That(content).Contains("Stopped = 2");
        await Assert.That(content).Contains("Paused = 3");
    }

    [Test]
    public async Task GenerateFromEnum_LastMemberHasNoTrailingComma()
    {
        var generator = new EventGenerator();
        var content = generator.GenerateFromEnum(typeof(SampleEventEnum));
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // 找到最后一个枚举成员行，验证其没有逗号
        var pausedLine = lines.First(l => l.Contains("Paused"));
        await Assert.That(pausedLine.TrimEnd()).DoesNotEndWith(",");
    }

    [Test]
    public async Task GenerateFromEnum_UsesUnderlyingIntValue()
    {
        var generator = new EventGenerator();
        var content = generator.GenerateFromEnum(typeof(WindowEventTestEnum));

        await Assert.That(content).Contains("Created = 100");
        await Assert.That(content).Contains("Closed = 200");
    }

    [Test]
    public async Task GenerateFromAssembly_FindsAllExportedEnums()
    {
        var generator = new EventGenerator();
        var content = generator.GenerateFromAssembly(typeof(SampleEventEnum).Assembly);

        await Assert.That(content).Contains("export enum SampleEventEnum");
    }

    [Test]
    public async Task GenerateKnownEvents_ProducesConstObject()
    {
        var generator = new EventGenerator();
        var content = generator.GenerateKnownEvents(typeof(SampleKnownEvents));

        await Assert.That(content).Contains("export const SampleKnownEvents = {");
        await Assert.That(content).Contains("Startup: \"app:startup\",");
        await Assert.That(content).Contains("Shutdown: \"app:shutdown\",");
        await Assert.That(content).Contains("export default SampleKnownEvents;");
    }

    [Test]
    public async Task GenerateKnownEvents_IncludesAllPublicConstFields()
    {
        var generator = new EventGenerator();
        var content = generator.GenerateKnownEvents(typeof(SampleKnownEvents));

        await Assert.That(content).Contains("Startup");
        await Assert.That(content).Contains("Shutdown");
        await Assert.That(content).Contains("ThemeChanged");
    }

    [Test]
    public async Task GenerateFromEnum_ByteUnderlyingType_ConvertsValues()
    {
        var generator = new EventGenerator();
        var content = generator.GenerateFromEnum(typeof(ByteEnum));

        await Assert.That(content).Contains("Alpha = 10");
        await Assert.That(content).Contains("Beta = 20");
    }
}

public enum SampleEventEnum
{
    Started = 1,
    Stopped = 2,
    Paused = 3,
}

public enum WindowEventTestEnum : uint
{
    Created = 100,
    Closed = 200,
}

public enum ByteEnum : byte
{
    Alpha = 10,
    Beta = 20,
}

public static class SampleKnownEvents
{
    public const string Startup = "app:startup";
    public const string Shutdown = "app:shutdown";
    public const string ThemeChanged = "app:theme:changed";
}
