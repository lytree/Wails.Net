using TUnit.Core;
using TUnit.Assertions;
using Wails.Net.Errors;

namespace Wails.Net.Errors.Tests;

/// <summary>
/// <see cref="WailsError"/> 异常基类的单元测试：验证四个构造重载、错误代码默认行为与原因异常传播。
/// </summary>
public class WailsErrorTests
{
    [Test]
    public async Task Ctor_MessageOnly_DefaultsToUnknown_AndNullCause()
    {
        var ex = new WailsError("oops");

        await Assert.That(ex.ErrorCode).IsEqualTo(ErrorCodes.Unknown);
        await Assert.That(ex.Message).IsEqualTo("oops");
        await Assert.That(ex.Cause).IsNull();
        await Assert.That(ex.InnerException).IsNull();
    }

    [Test]
    public async Task Ctor_MessageAndInner_SetsCauseAndInner()
    {
        var inner = new InvalidOperationException("bad");
        var ex = new WailsError("oops", inner);

        await Assert.That(ex.ErrorCode).IsEqualTo(ErrorCodes.Unknown);
        // Cause 必须指向内部异常（引用相等，Exception 未重写 Equals）
        await Assert.That(ex.Cause).IsEqualTo(inner);
        await Assert.That(ex.InnerException).IsEqualTo(inner);
    }

    [Test]
    public async Task Ctor_ErrorCodeAndMessage_SetsCode()
    {
        var ex = new WailsError(ErrorCodes.NotFound, "missing");

        await Assert.That(ex.ErrorCode).IsEqualTo(ErrorCodes.NotFound);
        await Assert.That(ex.Message).IsEqualTo("missing");
        await Assert.That(ex.Cause).IsNull();
    }

    [Test]
    public async Task Ctor_Full_SetsAll()
    {
        var inner = new Exception("root");
        var ex = new WailsError(ErrorCodes.ServiceStartupFailed, "fail", inner);

        await Assert.That(ex.ErrorCode).IsEqualTo(ErrorCodes.ServiceStartupFailed);
        await Assert.That(ex.Message).IsEqualTo("fail");
        await Assert.That(ex.Cause).IsEqualTo(inner);
    }

    [Test]
    public async Task IsException_TypePreserved()
    {
        var ex = new WailsError("x");

        await Assert.That(ex is Exception).IsTrue();
    }
}
