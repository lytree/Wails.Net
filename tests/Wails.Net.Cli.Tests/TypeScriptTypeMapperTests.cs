using System.Collections;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using Wails.Net.Generator;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// C# 到 TypeScript 类型映射器单元测试。
/// 验证基元类型、可空类型、集合类型、Task 类型、枚举的映射规则。
/// </summary>
[NotInParallel]
public sealed class TypeScriptTypeMapperTests
{
    [Test]
    public async Task MapType_NullType_ReturnsUnknown()
    {
        var result = TypeScriptTypeMapper.MapType(null!);
        await Assert.That(result).IsEqualTo("unknown");
    }

    [Test]
    [Arguments(typeof(bool), "boolean")]
    [Arguments(typeof(byte), "number")]
    [Arguments(typeof(sbyte), "number")]
    [Arguments(typeof(short), "number")]
    [Arguments(typeof(ushort), "number")]
    [Arguments(typeof(int), "number")]
    [Arguments(typeof(uint), "number")]
    [Arguments(typeof(long), "number")]
    [Arguments(typeof(ulong), "number")]
    [Arguments(typeof(float), "number")]
    [Arguments(typeof(double), "number")]
    [Arguments(typeof(decimal), "number")]
    [Arguments(typeof(char), "string")]
    [Arguments(typeof(string), "string")]
    [Arguments(typeof(object), "unknown")]
    [Arguments(typeof(DateTime), "string")]
    [Arguments(typeof(DateTimeOffset), "string")]
    [Arguments(typeof(TimeSpan), "string")]
    [Arguments(typeof(Guid), "string")]
    [Arguments(typeof(Uri), "string")]
    [Arguments(typeof(void), "void")]
    public async Task MapType_PrimitiveTypes_ReturnExpectedStrings(Type type, string expected)
    {
        var result = TypeScriptTypeMapper.MapType(type);
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task MapType_ByteArray_ReturnsNumberArray()
    {
        var result = TypeScriptTypeMapper.MapType(typeof(byte[]));
        await Assert.That(result).IsEqualTo("number[]");
    }

    [Test]
    public async Task MapType_NullableInt_ReturnsNumberOrNull()
    {
        var result = TypeScriptTypeMapper.MapType(typeof(int?));
        await Assert.That(result).IsEqualTo("number | null");
    }

    [Test]
    public async Task MapType_NullableBool_ReturnsBooleanOrNull()
    {
        var result = TypeScriptTypeMapper.MapType(typeof(bool?));
        await Assert.That(result).IsEqualTo("boolean | null");
    }

    [Test]
    public async Task MapType_IntArray_ReturnsNumberArray()
    {
        var result = TypeScriptTypeMapper.MapType(typeof(int[]));
        await Assert.That(result).IsEqualTo("number[]");
    }

    [Test]
    public async Task MapType_StringArray_ReturnsStringArray()
    {
        var result = TypeScriptTypeMapper.MapType(typeof(string[]));
        await Assert.That(result).IsEqualTo("string[]");
    }

    [Test]
    public async Task MapType_ListOfInt_ReturnsNumberArray()
    {
        var result = TypeScriptTypeMapper.MapType(typeof(List<int>));
        await Assert.That(result).IsEqualTo("number[]");
    }

    [Test]
    public async Task MapType_IListString_ReturnsStringArray()
    {
        var result = TypeScriptTypeMapper.MapType(typeof(IList<string>));
        await Assert.That(result).IsEqualTo("string[]");
    }

    [Test]
    public async Task MapType_IEnumerableDouble_ReturnsNumberArray()
    {
        var result = TypeScriptTypeMapper.MapType(typeof(IEnumerable<double>));
        await Assert.That(result).IsEqualTo("number[]");
    }

    [Test]
    public async Task MapType_IReadOnlyListBool_ReturnsBooleanArray()
    {
        var result = TypeScriptTypeMapper.MapType(typeof(IReadOnlyList<bool>));
        await Assert.That(result).IsEqualTo("boolean[]");
    }

    [Test]
    public async Task MapType_DictionaryStringInt_ReturnsRecord()
    {
        var result = TypeScriptTypeMapper.MapType(typeof(Dictionary<string, int>));
        await Assert.That(result).IsEqualTo("Record<string, number>");
    }

    [Test]
    public async Task MapType_IDictionaryStringBool_ReturnsRecord()
    {
        var result = TypeScriptTypeMapper.MapType(typeof(IDictionary<string, bool>));
        await Assert.That(result).IsEqualTo("Record<string, boolean>");
    }

    [Test]
    public async Task MapType_IReadOnlyDictionaryIntString_ReturnsRecord()
    {
        var result = TypeScriptTypeMapper.MapType(typeof(IReadOnlyDictionary<int, string>));
        await Assert.That(result).IsEqualTo("Record<number, string>");
    }

    [Test]
    public async Task MapType_TaskOfInt_ReturnsNumber()
    {
        var result = TypeScriptTypeMapper.MapType(typeof(Task<int>));
        await Assert.That(result).IsEqualTo("number");
    }

    [Test]
    public async Task MapType_TaskOfString_ReturnsString()
    {
        var result = TypeScriptTypeMapper.MapType(typeof(Task<string>));
        await Assert.That(result).IsEqualTo("string");
    }

    [Test]
    public async Task MapType_ValueTaskOfBool_ReturnsBoolean()
    {
        var result = TypeScriptTypeMapper.MapType(typeof(ValueTask<bool>));
        await Assert.That(result).IsEqualTo("boolean");
    }

    [Test]
    public async Task MapType_PlainTask_ReturnsVoid()
    {
        var result = TypeScriptTypeMapper.MapType(typeof(Task));
        await Assert.That(result).IsEqualTo("void");
    }

    [Test]
    public async Task MapType_PlainValueTask_ReturnsVoid()
    {
        var result = TypeScriptTypeMapper.MapType(typeof(ValueTask));
        await Assert.That(result).IsEqualTo("void");
    }

    [Test]
    public async Task MapType_CancellationToken_ReturnsVoid()
    {
        var result = TypeScriptTypeMapper.MapType(typeof(CancellationToken));
        await Assert.That(result).IsEqualTo("void");
    }

    [Test]
    public async Task MapType_Enum_ReturnsEnumName()
    {
        var result = TypeScriptTypeMapper.MapType(typeof(TestEnum));
        await Assert.That(result).IsEqualTo(nameof(TestEnum));
    }

    [Test]
    public async Task MapType_CustomClass_ReturnsTypeName()
    {
        var result = TypeScriptTypeMapper.MapType(typeof(CustomTestClass));
        await Assert.That(result).IsEqualTo(nameof(CustomTestClass));
    }

    [Test]
    public async Task MapType_GenericClass_StripsBacktickSuffix()
    {
        var result = TypeScriptTypeMapper.MapType(typeof(List<string>));
        // List<string> 已被识别为集合，这里用一个未被特殊处理的泛型类测试
        var genericResult = TypeScriptTypeMapper.MapType(typeof(CustomGenericClass<int>));
        await Assert.That(genericResult).IsEqualTo("CustomGenericClass");
    }

    [Test]
    public async Task MapType_TupleTwo_ReturnsTupleType()
    {
        var result = TypeScriptTypeMapper.MapType(typeof(Tuple<int, string>));
        await Assert.That(result).IsEqualTo("[number, string]");
    }

    [Test]
    public async Task MapType_TupleThree_ReturnsTupleType()
    {
        var result = TypeScriptTypeMapper.MapType(typeof(Tuple<bool, int, string>));
        await Assert.That(result).IsEqualTo("[boolean, number, string]");
    }

    [Test]
    public async Task IsTaskType_NullType_ReturnsFalse()
    {
        var result = TypeScriptTypeMapper.IsTaskType(null!);
        await Assert.That(result).IsFalse();
    }

    [Test]
    [Arguments(typeof(Task), true)]
    [Arguments(typeof(ValueTask), true)]
    [Arguments(typeof(Task<int>), true)]
    [Arguments(typeof(Task<string>), true)]
    [Arguments(typeof(ValueTask<bool>), true)]
    [Arguments(typeof(int), false)]
    [Arguments(typeof(string), false)]
    [Arguments(typeof(void), false)]
    [Arguments(typeof(List<int>), false)]
    public async Task IsTaskType_VariousTypes_ReturnsExpected(Type type, bool expected)
    {
        var result = TypeScriptTypeMapper.IsTaskType(type);
        await Assert.That(result).IsEqualTo(expected);
    }

    private enum TestEnum
    {
        Alpha,
        Beta,
        Gamma,
    }

    private sealed class CustomTestClass;

    private sealed class CustomGenericClass<T>;
}
