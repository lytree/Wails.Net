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
}
