using System.Linq;
using TUnit.Core;
using TUnit.Assertions;
using Wails.Net.Errors;

namespace Wails.Net.Errors.Tests;

/// <summary>
/// <see cref="ErrorCodes"/> 枚举的契约测试：这些值被序列化并经 IPC / 跨进程传递，数值稳定是向后兼容的前提。
/// </summary>
public class ErrorCodesTests
{
    [Test]
    public async Task NumericValues_AreStableContract()
    {
        // 这些值被序列化/跨进程传递，禁止随意改动
        // 注意：TUnitAssertions0005 禁止对编译期常量直接断言，故先转局部变量
        var none = (int)ErrorCodes.None;
        var unknown = (int)ErrorCodes.Unknown;
        var invalidArgument = (int)ErrorCodes.InvalidArgument;
        var bindingNotFound = (int)ErrorCodes.BindingNotFound;
        var transportNotStarted = (int)ErrorCodes.TransportNotStarted;
        var assetNotFound = (int)ErrorCodes.AssetNotFound;
        var windowNotFound = (int)ErrorCodes.WindowNotFound;
        var platformNotSupported = (int)ErrorCodes.PlatformNotSupported;
        var serviceStartupFailed = (int)ErrorCodes.ServiceStartupFailed;
        var updaterError = (int)ErrorCodes.UpdaterError;
        await Assert.That(none).IsEqualTo(0);
        await Assert.That(unknown).IsEqualTo(1);
        await Assert.That(invalidArgument).IsEqualTo(2);
        await Assert.That(bindingNotFound).IsEqualTo(101);
        await Assert.That(transportNotStarted).IsEqualTo(201);
        await Assert.That(assetNotFound).IsEqualTo(300);
        await Assert.That(windowNotFound).IsEqualTo(401);
        await Assert.That(platformNotSupported).IsEqualTo(500);
        await Assert.That(serviceStartupFailed).IsEqualTo(601);
        await Assert.That(updaterError).IsEqualTo(700);
    }

    [Test]
    public async Task AllValues_AreUnique()
    {
        var values = Enum.GetValues<ErrorCodes>().Select(v => (int)v).ToArray();

        await Assert.That(values.Distinct().Count()).IsEqualTo(values.Length);
    }
}
