using TUnit.Core;
using Wails.Net.Application.Dialogs;

namespace Wails.Net.Application.Tests;

/// <summary>
/// <see cref="MessageDialogOptions"/> / <see cref="MessageDialogButton"/> 的单元测试（TUnit）。
/// 覆盖按钮标签转换与按钮回调触发规则（对应 Wails v3 Go 版本 dialogs.go 的 Button / MessageDialogOptions）。
/// </summary>
[NotInParallel]
public sealed class MessageDialogOptionsTests
{
    [Test]
    public async Task Defaults_UseInfoStyleAndEmptyButtons()
    {
        // 安排与操作
        var options = new MessageDialogOptions();

        // 断言
        await Assert.That(options.Title).IsEqualTo(string.Empty);
        await Assert.That(options.Message).IsEqualTo(string.Empty);
        await Assert.That(options.Style).IsEqualTo(DialogStyle.Info);
        await Assert.That(options.Buttons).IsEmpty();
    }

    [Test]
    public async Task FromLabels_CreatesButtonsWithLabels()
    {
        // 操作
        var options = MessageDialogOptions.FromLabels("标题", "消息", DialogStyle.Question, ["是", "否"]);

        // 断言
        await Assert.That(options.Title).IsEqualTo("标题");
        await Assert.That(options.Message).IsEqualTo("消息");
        await Assert.That(options.Style).IsEqualTo(DialogStyle.Question);
        await Assert.That(options.Buttons).Count().IsEqualTo(2);
        await Assert.That(options.Buttons[0].Label).IsEqualTo("是");
        await Assert.That(options.Buttons[1].Label).IsEqualTo("否");
        await Assert.That(options.Buttons[0].IsDefault).IsFalse();
        await Assert.That(options.Buttons[1].IsCancel).IsFalse();
    }

    [Test]
    public async Task FromLabels_WithNullButtons_ThrowsArgumentNullException()
    {
        // 操作与断言
        await Assert.That(() => MessageDialogOptions.FromLabels("标题", "消息", DialogStyle.Info, null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task GetButtonLabels_ReturnsAllLabels()
    {
        // 安排
        var options = new MessageDialogOptions
        {
            Buttons =
            [
                new MessageDialogButton { Label = "A" },
                new MessageDialogButton { Label = "B" },
            ],
        };

        // 操作
        var labels = options.GetButtonLabels();

        // 断言
        await Assert.That(labels).IsEquivalentTo(new[] { "A", "B" });
    }

    [Test]
    public async Task GetButtonLabels_WithEmptyButtons_ReturnsEmptyArray()
    {
        // 安排与操作
        var labels = new MessageDialogOptions().GetButtonLabels();

        // 断言
        await Assert.That(labels).IsEmpty();
    }

    [Test]
    public async Task InvokeButtonCallback_InvokesMatchingCallback()
    {
        // 安排
        var invoked = false;
        var options = new MessageDialogOptions
        {
            Buttons =
            [
                new MessageDialogButton { Label = "A" },
                new MessageDialogButton { Label = "B", Callback = () => invoked = true },
            ],
        };

        // 操作
        options.InvokeButtonCallback(1);

        // 断言
        await Assert.That(invoked).IsTrue();
    }

    [Test]
    public async Task InvokeButtonCallback_WithNegativeOrOutOfRangeIndex_IsIgnored()
    {
        // 安排
        var invoked = false;
        var options = new MessageDialogOptions
        {
            Buttons = [new MessageDialogButton { Label = "A", Callback = () => invoked = true }],
        };

        // 操作
        options.InvokeButtonCallback(-1);
        options.InvokeButtonCallback(5);

        // 断言
        await Assert.That(invoked).IsFalse();
    }

    [Test]
    public async Task InvokeButtonCallback_WithNullCallback_DoesNotThrow()
    {
        // 安排
        var options = new MessageDialogOptions
        {
            Buttons = [new MessageDialogButton { Label = "A" }],
        };

        // 操作与断言
        await Assert.That(() => options.InvokeButtonCallback(0)).ThrowsNothing();
    }
}
