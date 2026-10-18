using TUnit.Core;
using Wails.Net.Application.Platform;

namespace Wails.Net.Application.Linux.Tests;

/// <summary>
/// <see cref="LinuxSingleInstanceNames"/> 的单元测试（TUnit），镜像 Go 版
/// <c>pkg/application/single_instance_linux_test.go</c>（beta.23 #5973）。
/// 全部为纯逻辑断言，不触发任何 D-Bus 原生调用，可在任意平台执行。
/// </summary>
[NotInParallel]
public sealed class LinuxSingleInstanceNamesTests
{
    [Test]
    public async Task TryDerive_KeepsUniqueIdVerbatimInBusName()
    {
        var success = LinuxSingleInstanceNames.TryDerive(
            "com.myapp.myapplication", out var busName, out var interfaceName, out var objectPath, out var error);

        await Assert.That(success).IsTrue();
        await Assert.That(error).IsNull();
        await Assert.That(busName).IsEqualTo("com.myapp.myapplication.SingleInstance");
        await Assert.That(interfaceName).IsEqualTo("com.myapp.myapplication.SingleInstance");
        await Assert.That(objectPath).IsEqualTo("/com/myapp/myapplication/SingleInstance");
    }

    [Test]
    public async Task TryDerive_HyphensSurviveOnlyInBusName()
    {
        // 连字符在 bus name 中合法，但在 interface name 与 object path 中不合法，故仅 bus name 保留它们。
        var success = LinuxSingleInstanceNames.TryDerive(
            "net.my-company.my-app", out var busName, out var interfaceName, out var objectPath, out var error);

        await Assert.That(success).IsTrue();
        await Assert.That(error).IsNull();
        await Assert.That(busName).IsEqualTo("net.my-company.my-app.SingleInstance");
        await Assert.That(interfaceName).IsEqualTo("net.my_company.my_app.SingleInstance");
        await Assert.That(objectPath).IsEqualTo("/net/my_company/my_app/SingleInstance");
    }

    [Test]
    public async Task TryDerive_SingleElementStillYieldsTwoNameElements()
    {
        var success = LinuxSingleInstanceNames.TryDerive(
            "myapp", out var busName, out var interfaceName, out var objectPath, out var error);

        await Assert.That(success).IsTrue();
        await Assert.That(error).IsNull();
        await Assert.That(busName).IsEqualTo("myapp.SingleInstance");
        await Assert.That(interfaceName).IsEqualTo("myapp.SingleInstance");
        await Assert.That(objectPath).IsEqualTo("/myapp/SingleInstance");
    }

    [Test]
    public async Task TryDerive_BusNameStaysUnderTheAppId()
    {
        // bus name 必须保持以应用自身 ID 为前缀，沙箱构建才能在不被授予整个 session bus 的前提下占用它。
        const string uniqueId = "net.koofr.stage";

        var success = LinuxSingleInstanceNames.TryDerive(
            uniqueId, out var busName, out _, out _, out var error);

        await Assert.That(success).IsTrue();
        await Assert.That(error).IsNull();
        await Assert.That(busName.StartsWith(uniqueId, StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    [Arguments("")]
    [Arguments("com..myapp")]
    [Arguments("com.myapp.")]
    [Arguments("com.1myapp")]
    [Arguments("com.my app")]
    [Arguments("com/myapp")]
    public async Task TryDerive_RejectsInvalidIds(string uniqueId)
    {
        var success = LinuxSingleInstanceNames.TryDerive(uniqueId, out _, out _, out _, out var error);

        await Assert.That(success).IsFalse();
        await Assert.That(error).IsNotNull();
    }
}
