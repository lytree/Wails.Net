using TUnit.Core;
using Wails.Net.Application.Options;
using Wails.Net.Application.Services;

namespace Wails.Net.Application.Tests;

/// <summary>
/// NotificationService 的单元测试（TUnit）。
/// 测试通知发送、记录保存、生命周期方法。
/// </summary>
[NotInParallel]
public sealed class NotificationServiceTests
{
    [Test]
    public async Task SendNotification_DoesNotThrow()
    {
        // 安排
        var service = new NotificationService();
        await service.ServiceStartup(new ApplicationOptions(), CancellationToken.None);

        // 操作与断言
        await Assert.That(() => service.SendNotification("Title", "Body")).ThrowsNothing();
    }

    [Test]
    public async Task SendNotification_RecordsNotification()
    {
        // 安排
        var service = new NotificationService();
        await service.ServiceStartup(new ApplicationOptions(), CancellationToken.None);

        // 操作
        service.SendNotification("Test Title", "Test Body");

        // 断言
        var records = service.GetNotifications();
        await Assert.That(records.Count).IsEqualTo(1);
        await Assert.That(records[0].Title).IsEqualTo("Test Title");
        await Assert.That(records[0].Body).IsEqualTo("Test Body");
        await Assert.That(records[0].ActionText).IsNull();
    }

    [Test]
    public async Task SendNotificationWithAction_DoesNotThrow()
    {
        // 安排
        var service = new NotificationService();
        await service.ServiceStartup(new ApplicationOptions(), CancellationToken.None);

        // 操作与断言
        await Assert.That(() => service.SendNotificationWithAction("Title", "Body", "Click Me"))
            .ThrowsNothing();
    }

    [Test]
    public async Task SendNotificationWithAction_RecordsActionText()
    {
        // 安排
        var service = new NotificationService();
        await service.ServiceStartup(new ApplicationOptions(), CancellationToken.None);

        // 操作
        service.SendNotificationWithAction("Action Title", "Action Body", "Confirm");

        // 断言
        var records = service.GetNotifications();
        await Assert.That(records.Count).IsEqualTo(1);
        await Assert.That(records[0].Title).IsEqualTo("Action Title");
        await Assert.That(records[0].Body).IsEqualTo("Action Body");
        await Assert.That(records[0].ActionText).IsEqualTo("Confirm");
    }

    [Test]
    public async Task SendNotification_MultipleNotifications_AllRecorded()
    {
        // 安排
        var service = new NotificationService();
        await service.ServiceStartup(new ApplicationOptions(), CancellationToken.None);

        // 操作
        service.SendNotification("First", "Body 1");
        service.SendNotification("Second", "Body 2");
        service.SendNotificationWithAction("Third", "Body 3", "OK");

        // 断言
        var records = service.GetNotifications();
        await Assert.That(records.Count).IsEqualTo(3);
        await Assert.That(records[0].Title).IsEqualTo("First");
        await Assert.That(records[1].Title).IsEqualTo("Second");
        await Assert.That(records[2].Title).IsEqualTo("Third");
    }

    [Test]
    public async Task ServiceStartup_DoesNotThrow()
    {
        // 安排
        var service = new NotificationService();

        // 操作与断言
        await Assert.That(() => service.ServiceStartup(new ApplicationOptions(), CancellationToken.None))
            .ThrowsNothing();
    }

    [Test]
    public async Task ServiceShutdown_DoesNotThrow()
    {
        // 安排
        var service = new NotificationService();
        await service.ServiceStartup(new ApplicationOptions(), CancellationToken.None);

        // 操作与断言
        await Assert.That(() => service.ServiceShutdown(CancellationToken.None)).ThrowsNothing();
    }
}
