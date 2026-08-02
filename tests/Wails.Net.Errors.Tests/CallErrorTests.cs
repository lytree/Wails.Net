using TUnit.Core;
using TUnit.Assertions;
using Wails.Net.Errors;

namespace Wails.Net.Errors.Tests;

/// <summary>
/// <see cref="CallError"/> 与 <see cref="CallErrorKind"/> 的结构化错误单元测试。
/// 这些是前后端 IPC 错误契约的基础，camelCase JSON 字段名与枚举字符串必须与前端严格一致。
/// </summary>
public class CallErrorTests
{
    [Test]
    public async Task Constructor_SetsAllFields()
    {
        var err = new CallError("boom", "root cause", CallErrorKind.RuntimeError);

        await Assert.That(err.Message).IsEqualTo("boom");
        await Assert.That(err.Cause).IsEqualTo("root cause");
        await Assert.That(err.Kind).IsEqualTo(CallErrorKind.RuntimeError);
    }

    [Test]
    public async Task ToString_IncludesCause_WhenPresent()
    {
        var err = new CallError("boom", "root cause", CallErrorKind.RuntimeError);

        await Assert.That(err.ToString()).IsEqualTo("Message: boom, Cause: root cause, Kind: RuntimeError");
    }

    [Test]
    public async Task ToString_ShowsNone_WhenCauseNull()
    {
        var err = new CallError("boom", null, CallErrorKind.ReferenceError);

        await Assert.That(err.ToString()).IsEqualTo("Message: boom, Cause: (none), Kind: ReferenceError");
    }

    [Test]
    public async Task ToJson_ReturnsCamelCaseDictionary_WithKindAsString()
    {
        var err = new CallError("boom", "cause", CallErrorKind.TypeError);

        var json = err.ToJson();

        await Assert.That(json["message"]).IsEqualTo("boom");
        await Assert.That(json["cause"]).IsEqualTo("cause");
        // 枚举必须序列化为字符串（前端按字符串匹配），而非数值
        await Assert.That(json["kind"]).IsEqualTo("TypeError");
    }

    [Test]
    public async Task ToJson_NullCause_SerializesAsNull()
    {
        var err = new CallError("boom", null, CallErrorKind.RuntimeError);

        var json = err.ToJson();

        await Assert.That(json.ContainsKey("cause")).IsTrue();
        await Assert.That(json["cause"]).IsNull();
    }

    [Test]
    public async Task CallErrorKind_EnumValues_AreStableContract()
    {
        // 这些数值会被序列化/前端匹配，禁止随意改动
        // 注意：TUnitAssertions0005 禁止对编译期常量直接断言，故先转局部变量
        var referenceError = (int)CallErrorKind.ReferenceError;
        var typeError = (int)CallErrorKind.TypeError;
        var runtimeError = (int)CallErrorKind.RuntimeError;
        await Assert.That(referenceError).IsEqualTo(0);
        await Assert.That(typeError).IsEqualTo(1);
        await Assert.That(runtimeError).IsEqualTo(2);
    }
}
