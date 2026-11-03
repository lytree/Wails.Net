using TUnit.Core;
using Wails.Net.Application.Dialogs;
using Wails.Net.Application.Managers;

namespace Wails.Net.Application.Tests;

/// <summary>
/// DialogManager 的单元测试（TUnit）。
/// 测试对话框管理器对 IPlatformApp 的委托行为及 Server 模式下的默认值。
/// </summary>
[NotInParallel]
public sealed class DialogManagerTests
{
    [Test]
    public async Task Constructor_WithNullPlatformApp_AllowsServerMode()
    {
        // 安排与操作
        var manager = new DialogManager(null);

        // 断言 - 不抛出异常即视为成功
        await Assert.That(manager).IsNotNull();
    }

    [Test]
    public async Task ShowMessageDialog_WithNullPlatformApp_ReturnsDefaultZero()
    {
        // 安排
        var manager = new DialogManager(null);

        // 操作
        var result = await manager.ShowMessageDialog("Title", "Message", DialogStyle.Info, new[] { "OK" });

        // 断言
        await Assert.That(result).IsEqualTo(0);
    }

    [Test]
    public async Task ShowMessageDialog_DelegatesToPlatformApp()
    {
        // 安排
        var fakePlatform = new FakePlatformApp { ShowMessageDialogResult = 2 };
        var manager = new DialogManager(fakePlatform);
        var buttons = new[] { "Yes", "No", "Cancel" };

        // 操作
        var result = await manager.ShowMessageDialog("Confirm", "Are you sure?", DialogStyle.Question, buttons);

        // 断言
        await Assert.That(result).IsEqualTo(2);
        await Assert.That(fakePlatform.ShowMessageDialogCalls).Count().IsEqualTo(1);
        var call = fakePlatform.ShowMessageDialogCalls.ToArray()[0];
        await Assert.That(call.title).IsEqualTo("Confirm");
        await Assert.That(call.message).IsEqualTo("Are you sure?");
        await Assert.That(call.style).IsEqualTo(DialogStyle.Question);
        await Assert.That(call.buttons).IsEqualTo(buttons);
    }

    [Test]
    public async Task OpenFileDialog_WithNullPlatformApp_ReturnsNull()
    {
        // 安排
        var manager = new DialogManager(null);

        // 操作
        var result = await manager.OpenFileDialog(new OpenFileDialogOptions());

        // 断言
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task OpenFileDialog_DelegatesToPlatformApp()
    {
        // 安排
        var fakePlatform = new FakePlatformApp { OpenFileDialogResult = "/path/to/file.txt" };
        var manager = new DialogManager(fakePlatform);
        var options = new OpenFileDialogOptions { Title = "Open", AllowFiles = true };

        // 操作
        var result = await manager.OpenFileDialog(options);

        // 断言
        await Assert.That(result).IsEqualTo("/path/to/file.txt");
        await Assert.That(fakePlatform.OpenFileDialogCalls).Count().IsEqualTo(1);
        await Assert.That(fakePlatform.OpenFileDialogCalls.ToArray()[0]).IsSameReferenceAs(options);
    }

    [Test]
    public async Task SaveFileDialog_WithNullPlatformApp_ReturnsNull()
    {
        // 安排
        var manager = new DialogManager(null);

        // 操作
        var result = await manager.SaveFileDialog(new SaveFileDialogOptions());

        // 断言
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task SaveFileDialog_DelegatesToPlatformApp()
    {
        // 安排
        var fakePlatform = new FakePlatformApp { SaveFileDialogResult = "/path/to/save.txt" };
        var manager = new DialogManager(fakePlatform);
        var options = new SaveFileDialogOptions { Title = "Save", Filename = "default.txt" };

        // 操作
        var result = await manager.SaveFileDialog(options);

        // 断言
        await Assert.That(result).IsEqualTo("/path/to/save.txt");
        await Assert.That(fakePlatform.SaveFileDialogCalls).Count().IsEqualTo(1);
        await Assert.That(fakePlatform.SaveFileDialogCalls.ToArray()[0]).IsSameReferenceAs(options);
    }

    [Test]
    public async Task OpenMultipleFilesDialog_WithNullPlatformApp_ReturnsNull()
    {
        // 安排
        var manager = new DialogManager(null);

        // 操作
        var result = await manager.OpenMultipleFilesDialog(new OpenFileDialogOptions());

        // 断言
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task OpenMultipleFilesDialog_DelegatesToPlatformApp()
    {
        // 安排
        var expected = new[] { "/path/file1.txt", "/path/file2.txt" };
        var fakePlatform = new FakePlatformApp { OpenMultipleFilesDialogResult = expected };
        var manager = new DialogManager(fakePlatform);
        var options = new OpenFileDialogOptions { CanChooseMultiple = true };

        // 操作
        var result = await manager.OpenMultipleFilesDialog(options);

        // 断言
        await Assert.That(result).IsEquivalentTo(expected);
        await Assert.That(fakePlatform.OpenMultipleFilesDialogCalls).Count().IsEqualTo(1);
        await Assert.That(fakePlatform.OpenMultipleFilesDialogCalls.ToArray()[0]).IsSameReferenceAs(options);
    }

    [Test]
    public async Task ShowMessageDialog_PreservesDialogStyle()
    {
        // 安排
        var fakePlatform = new FakePlatformApp();
        var manager = new DialogManager(fakePlatform);

        // 操作 - 测试所有 DialogStyle 枚举值
        await manager.ShowMessageDialog("T", "M", DialogStyle.Info, Array.Empty<string>());
        await manager.ShowMessageDialog("T", "M", DialogStyle.Warning, Array.Empty<string>());
        await manager.ShowMessageDialog("T", "M", DialogStyle.Error, Array.Empty<string>());
        await manager.ShowMessageDialog("T", "M", DialogStyle.Question, Array.Empty<string>());

        // 断言
        await Assert.That(fakePlatform.ShowMessageDialogCalls).Count().IsEqualTo(4);
        var calls = fakePlatform.ShowMessageDialogCalls.ToArray();
        await Assert.That(calls[0].style).IsEqualTo(DialogStyle.Info);
        await Assert.That(calls[1].style).IsEqualTo(DialogStyle.Warning);
        await Assert.That(calls[2].style).IsEqualTo(DialogStyle.Error);
        await Assert.That(calls[3].style).IsEqualTo(DialogStyle.Question);
    }

    [Test]
    public async Task ShowMessageDialog_WithEmptyButtons_DelegatesCorrectly()
    {
        // 安排
        var fakePlatform = new FakePlatformApp { ShowMessageDialogResult = 0 };
        var manager = new DialogManager(fakePlatform);

        // 操作
        var result = await manager.ShowMessageDialog("Title", "Message", DialogStyle.Info, Array.Empty<string>());

        // 断言
        await Assert.That(result).IsEqualTo(0);
        await Assert.That(fakePlatform.ShowMessageDialogCalls).Count().IsEqualTo(1);
        await Assert.That(fakePlatform.ShowMessageDialogCalls.ToArray()[0].buttons).IsEmpty();
    }

    [Test]
    public async Task ShowMessageDialog_Options_WithNullPlatformApp_ReturnsDefaultZero()
    {
        // 安排
        var manager = new DialogManager(null);
        var callbackInvoked = false;
        var options = new MessageDialogOptions
        {
            Title = "Title",
            Message = "Message",
            Style = DialogStyle.Question,
            Buttons =
            [
                new MessageDialogButton { Label = "是", IsDefault = true },
                new MessageDialogButton { Label = "否", IsCancel = true, Callback = () => callbackInvoked = true },
            ],
        };

        // 操作
        var result = await manager.ShowMessageDialog(options);

        // 断言：Server 模式返回默认值且不触发回调
        await Assert.That(result).IsEqualTo(0);
        await Assert.That(callbackInvoked).IsFalse();
    }

    [Test]
    public async Task ShowMessageDialog_Options_DelegatesLabelsAndInvokesCallback()
    {
        // 安排：平台未重写 Options 重载时走接口默认实现（回退到 string[] 版本并触发回调）
        var fakePlatform = new FakePlatformApp { ShowMessageDialogResult = 1 };
        var manager = new DialogManager(fakePlatform);
        var callbackInvoked = false;
        var options = new MessageDialogOptions
        {
            Title = "Confirm",
            Message = "Are you sure?",
            Style = DialogStyle.Question,
            Buttons =
            [
                new MessageDialogButton { Label = "Yes", IsDefault = true },
                new MessageDialogButton { Label = "No", IsCancel = true, Callback = () => callbackInvoked = true },
            ],
        };

        // 操作
        var result = await manager.ShowMessageDialog(options);

        // 断言
        await Assert.That(result).IsEqualTo(1);
        await Assert.That(callbackInvoked).IsTrue();
        await Assert.That(fakePlatform.ShowMessageDialogCalls).Count().IsEqualTo(1);
        var call = fakePlatform.ShowMessageDialogCalls.ToArray()[0];
        await Assert.That(call.title).IsEqualTo("Confirm");
        await Assert.That(call.message).IsEqualTo("Are you sure?");
        await Assert.That(call.style).IsEqualTo(DialogStyle.Question);
        await Assert.That(call.buttons).IsEquivalentTo(new[] { "Yes", "No" });
    }

    // ========== Run 之前调用（Wails v3.1.0 #6390） ==========

    [Test]
    public async Task ShowMessageDialog_BeforeRun_ReturnsMinusOneAndSkipsPlatform()
    {
        // 安排：运行状态判定为 false（Run 尚未开始）
        var fakePlatform = new FakePlatformApp { ShowMessageDialogResult = 1 };
        var manager = new DialogManager(fakePlatform, () => false);

        // 操作
        var result = await manager.ShowMessageDialog("T", "M", DialogStyle.Info, new[] { "OK" });

        // 断言：直接返回 -1，且不调用平台对话框（避免 GTK 主循环未启动时永久挂起）
        await Assert.That(result).IsEqualTo(-1);
        await Assert.That(fakePlatform.ShowMessageDialogCalls).Count().IsEqualTo(0);
    }

    [Test]
    public async Task ShowMessageDialog_Options_BeforeRun_ReturnsMinusOne()
    {
        // 安排
        var fakePlatform = new FakePlatformApp { ShowMessageDialogResult = 1 };
        var manager = new DialogManager(fakePlatform, () => false);
        var options = new MessageDialogOptions { Title = "T", Message = "M", Style = DialogStyle.Question };

        // 操作
        var result = await manager.ShowMessageDialog(options);

        // 断言
        await Assert.That(result).IsEqualTo(-1);
        await Assert.That(fakePlatform.ShowMessageDialogCalls).Count().IsEqualTo(0);
    }

    [Test]
    public async Task FileDialogs_BeforeRun_ReturnNull()
    {
        // 安排
        var fakePlatform = new FakePlatformApp { OpenFileDialogResult = "/tmp/a.txt", SaveFileDialogResult = "/tmp/b.txt" };
        var manager = new DialogManager(fakePlatform, () => false);

        // 操作
        var open = await manager.OpenFileDialog(new OpenFileDialogOptions());
        var save = await manager.SaveFileDialog(new SaveFileDialogOptions());
        var multiple = await manager.OpenMultipleFilesDialog(new OpenFileDialogOptions());

        // 断言
        await Assert.That(open).IsNull();
        await Assert.That(save).IsNull();
        await Assert.That(multiple).IsNull();
        await Assert.That(fakePlatform.OpenFileDialogCalls).Count().IsEqualTo(0);
        await Assert.That(fakePlatform.SaveFileDialogCalls).Count().IsEqualTo(0);
        await Assert.That(fakePlatform.OpenMultipleFilesDialogCalls).Count().IsEqualTo(0);
    }

    [Test]
    public async Task ShowMessageDialog_WhenRunning_Delegates()
    {
        // 安排：运行状态判定为 true
        var fakePlatform = new FakePlatformApp { ShowMessageDialogResult = 1 };
        var manager = new DialogManager(fakePlatform, () => true);

        // 操作
        var result = await manager.ShowMessageDialog("T", "M", DialogStyle.Info, new[] { "OK" });

        // 断言
        await Assert.That(result).IsEqualTo(1);
        await Assert.That(fakePlatform.ShowMessageDialogCalls).Count().IsEqualTo(1);
    }

    [Test]
    public async Task AttachRunningState_OverridesOnlyWhenNotSet()
    {
        // 安排：构造时已提供判定，Attach 不应覆盖
        var fakePlatform = new FakePlatformApp { ShowMessageDialogResult = 1 };
        var manager = new DialogManager(fakePlatform, () => false);

        // 操作
        manager.AttachRunningState(() => true);
        var result = await manager.ShowMessageDialog("T", "M", DialogStyle.Info, new[] { "OK" });

        // 断言：仍使用构造时的 false 判定
        await Assert.That(result).IsEqualTo(-1);
        await Assert.That(fakePlatform.ShowMessageDialogCalls).Count().IsEqualTo(0);
    }
}
