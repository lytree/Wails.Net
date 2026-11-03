using TUnit.Core;
using Wails.Net.Application.Dialogs;
using Wails.Net.Application.Platform;

namespace Wails.Net.Application.Windows.Tests;

/// <summary>
/// Win32TaskDialog 的纯逻辑单元测试（TUnit），覆盖按钮 ID 规划与
/// TaskDialog / MessageBox 结果到按钮索引的映射规则（对应 Wails v3 Go 版本
/// dialogs_taskdialog.go 中的 planTaskDialog / buttonFor / messageBoxButtonFor）。
/// 不创建真实对话框，因此无需 GUI 环境。
/// </summary>
[NotInParallel]
public sealed class Win32TaskDialogTests
{
    [Test]
    public async Task CreatePlan_AssignsSequentialIdsFromThousand()
    {
        // 安排
        var buttons = CreateButtons("是", "否");

        // 操作
        var plan = Win32TaskDialog.CreatePlan(buttons);

        // 断言
        await Assert.That(plan.Count).IsEqualTo(2);
        await Assert.That(plan.ButtonIds).IsEquivalentTo(new[] { 1000, 1001 });
        await Assert.That(plan.Labels).IsEquivalentTo(new[] { "是", "否" });
        await Assert.That(plan.DefaultButtonId).IsEqualTo(0);
        await Assert.That(plan.CancelButtonIndex).IsEqualTo(-1);
    }

    [Test]
    public async Task CreatePlan_UsesFirstDefaultAndFirstCancelButton()
    {
        // 安排：两个按钮都标记默认/取消，验证“首个生效”
        var buttons = new[]
        {
            new MessageDialogButton { Label = "A", IsDefault = true, IsCancel = true },
            new MessageDialogButton { Label = "B", IsDefault = true, IsCancel = true },
            new MessageDialogButton { Label = "C" },
        };

        // 操作
        var plan = Win32TaskDialog.CreatePlan(buttons);

        // 断言
        await Assert.That(plan.DefaultButtonId).IsEqualTo(1000);
        await Assert.That(plan.CancelButtonIndex).IsEqualTo(0);
    }

    [Test]
    public async Task CreatePlan_WithEmptyButtons_ReturnsEmptyPlan()
    {
        // 安排与操作
        var plan = Win32TaskDialog.CreatePlan(Array.Empty<MessageDialogButton>());

        // 断言
        await Assert.That(plan.Count).IsEqualTo(0);
        await Assert.That(plan.ButtonIds).IsEmpty();
        await Assert.That(plan.CancelButtonIndex).IsEqualTo(-1);
    }

    [Test]
    public async Task ResolveTaskDialogButtonIndex_MapsCustomIdToIndex()
    {
        // 安排
        var plan = Win32TaskDialog.CreatePlan(CreateButtons("是", "否", "取消"));

        // 操作
        var index = Win32TaskDialog.ResolveTaskDialogButtonIndex(plan, 1001);

        // 断言
        await Assert.That(index).IsEqualTo(1);
    }

    [Test]
    public async Task ResolveTaskDialogButtonIndex_MapsCancelIdToCancelButton()
    {
        // 安排：最后一个按钮标记为取消
        var buttons = new[]
        {
            new MessageDialogButton { Label = "是" },
            new MessageDialogButton { Label = "否", IsCancel = true },
        };
        var plan = Win32TaskDialog.CreatePlan(buttons);

        // 操作
        var index = Win32TaskDialog.ResolveTaskDialogButtonIndex(plan, Win32TaskDialog.CancelButtonId);

        // 断言
        await Assert.That(index).IsEqualTo(1);
    }

    [Test]
    public async Task ResolveTaskDialogButtonIndex_CancelIdWithoutCancelButton_ReturnsMinusOne()
    {
        // 安排
        var plan = Win32TaskDialog.CreatePlan(CreateButtons("是", "否"));

        // 操作
        var index = Win32TaskDialog.ResolveTaskDialogButtonIndex(plan, Win32TaskDialog.CancelButtonId);

        // 断言
        await Assert.That(index).IsEqualTo(-1);
    }

    [Test]
    public async Task ResolveTaskDialogButtonIndex_UnknownId_ReturnsMinusOne()
    {
        // 安排
        var plan = Win32TaskDialog.CreatePlan(CreateButtons("是", "否"));

        // 操作与断言
        await Assert.That(Win32TaskDialog.ResolveTaskDialogButtonIndex(plan, 1)).IsEqualTo(-1);
        await Assert.That(Win32TaskDialog.ResolveTaskDialogButtonIndex(plan, 1005)).IsEqualTo(-1);
    }

    [Test]
    public async Task ResolveMessageBoxButtonIndex_MatchesLabelIgnoringCaseAndWhitespace()
    {
        // 安排
        var buttons = CreateButtons(" ok ", "取消");

        // 操作：IDOK（1）对应标签 "Ok"
        var index = Win32TaskDialog.ResolveMessageBoxButtonIndex(buttons, 1);

        // 断言
        await Assert.That(index).IsEqualTo(0);
    }

    [Test]
    public async Task ResolveMessageBoxButtonIndex_YesMapsToFirstNonCancelButton()
    {
        // 安排：取消按钮排在第一位，应跳过
        var buttons = new[]
        {
            new MessageDialogButton { Label = "取消", IsCancel = true },
            new MessageDialogButton { Label = "继续" },
        };

        // 操作：IDYES（6）按位置回退到首个非取消按钮
        var index = Win32TaskDialog.ResolveMessageBoxButtonIndex(buttons, 6);

        // 断言
        await Assert.That(index).IsEqualTo(1);
    }

    [Test]
    public async Task ResolveMessageBoxButtonIndex_NoPrefersCancelButton()
    {
        // 安排
        var buttons = new[]
        {
            new MessageDialogButton { Label = "是" },
            new MessageDialogButton { Label = "否", IsCancel = true },
        };

        // 操作：IDNO（7）优先取消按钮
        var index = Win32TaskDialog.ResolveMessageBoxButtonIndex(buttons, 7);

        // 断言
        await Assert.That(index).IsEqualTo(1);
    }

    [Test]
    public async Task ResolveMessageBoxButtonIndex_NoWithoutCancelFallsBackToSecondNonCancel()
    {
        // 安排
        var buttons = CreateButtons("是", "否");

        // 操作
        var index = Win32TaskDialog.ResolveMessageBoxButtonIndex(buttons, 7);

        // 断言
        await Assert.That(index).IsEqualTo(1);
    }

    [Test]
    public async Task ResolveMessageBoxButtonIndex_CancelMapsToCancelButton()
    {
        // 安排
        var buttons = new[]
        {
            new MessageDialogButton { Label = "是" },
            new MessageDialogButton { Label = "取消", IsCancel = true },
        };

        // 操作：IDCANCEL（2）映射取消按钮，标签 "取消" 不匹配英文 "Cancel" 时仍按语义回退
        var index = Win32TaskDialog.ResolveMessageBoxButtonIndex(buttons, 2);

        // 断言
        await Assert.That(index).IsEqualTo(1);
    }

    [Test]
    public async Task ResolveMessageBoxButtonIndex_UnknownOrOutOfRangeId_ReturnsMinusOne()
    {
        // 安排
        var buttons = CreateButtons("是", "否");

        // 操作与断言
        await Assert.That(Win32TaskDialog.ResolveMessageBoxButtonIndex(buttons, 0)).IsEqualTo(-1);
        await Assert.That(Win32TaskDialog.ResolveMessageBoxButtonIndex(buttons, 12)).IsEqualTo(-1);
    }

    [Test]
    public async Task TryShow_WithEmptyButtons_ReturnsFalse()
    {
        // 安排
        var options = new MessageDialogOptions
        {
            Title = "标题",
            Message = "消息",
            Style = DialogStyle.Question,
        };

        // 操作（无按钮时直接返回 false，不弹出任何对话框）
        var shown = Win32TaskDialog.TryShow(options, out var index);

        // 断言
        await Assert.That(shown).IsFalse();
        await Assert.That(index).IsEqualTo(-1);
    }

    /// <summary>
    /// 创建仅包含标签的按钮数组。
    /// </summary>
    /// <param name="labels">按钮标签。</param>
    /// <returns>按钮数组。</returns>
    private static MessageDialogButton[] CreateButtons(params string[] labels)
    {
        var buttons = new MessageDialogButton[labels.Length];
        for (var i = 0; i < labels.Length; i++)
        {
            buttons[i] = new MessageDialogButton { Label = labels[i] };
        }

        return buttons;
    }
}
