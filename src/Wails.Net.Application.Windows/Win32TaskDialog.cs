using System.Runtime.InteropServices;
using Wails.Net.Application.Dialogs;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Wails.Net.Application.Platform;

/// <summary>
/// Windows TaskDialogIndirect 封装，用于显示带自定义按钮标签的问题对话框。
/// 对应 Wails v3 Go 版本 dialogs_taskdialog.go（按钮规划与 ID 映射）与
/// dialogs_windows.go 中的 <c>showTaskDialog</c>（原生调用与 TDN_CREATED 置顶）。
/// comctl32 v6 不可用或调用失败时返回 false，由调用方回退到 MessageBox。
/// </summary>
internal static class Win32TaskDialog
{
    /// <summary>
    /// 自定义按钮的第一个 ID。避开 IDOK..IDCONTINUE（1-11）等标准返回值。
    /// </summary>
    internal const int ButtonIdBase = 1000;

    /// <summary>
    /// TaskDialogIndirect 在 Esc/关闭时返回的标准取消 ID（IDCANCEL）。
    /// </summary>
    internal const int CancelButtonId = 2;

    /// <summary>
    /// MessageBox 标准返回值到按钮文本的映射（索引即返回值 ID）。
    /// 对应 Go 版本 <c>messageBoxButtonFor</c> 中的 responses 表。
    /// </summary>
    private static readonly string[] MessageBoxResponses =
    [
        string.Empty,
        "Ok",
        "Cancel",
        "Abort",
        "Retry",
        "Ignore",
        "Yes",
        "No",
        string.Empty,
        string.Empty,
        "Try Again",
        "Continue",
    ];

    /// <summary>
    /// 保持 TaskDialog 回调委托存活，避免被 GC 回收后原生侧持有悬空函数指针。
    /// </summary>
    private static readonly PFTASKDIALOGCALLBACK s_callback = OnTaskDialogCallback;

    /// <summary>
    /// TaskDialog 按钮规划结果。
    /// 对应 Go 版本 <c>taskDialogPlan</c>。
    /// </summary>
    internal sealed class ButtonPlan
    {
        /// <summary>
        /// 每个按钮对应的 TaskDialog 返回 ID。
        /// </summary>
        internal required int[] ButtonIds { get; init; }

        /// <summary>
        /// 每个按钮的标签。
        /// </summary>
        internal required string[] Labels { get; init; }

        /// <summary>
        /// 默认按钮 ID；未指定默认按钮时为 0（由 TaskDialog 使用首个按钮）。
        /// </summary>
        internal int DefaultButtonId { get; init; }

        /// <summary>
        /// 取消按钮索引；未指定取消按钮时为 -1。
        /// </summary>
        internal int CancelButtonIndex { get; init; } = -1;

        /// <summary>
        /// 按钮数量。
        /// </summary>
        internal int Count => ButtonIds.Length;
    }

    /// <summary>
    /// 根据按钮集合规划 TaskDialog 按钮 ID、默认按钮与取消按钮。
    /// 对应 Go 版本 <c>planTaskDialog</c>。
    /// </summary>
    /// <param name="buttons">按钮集合。</param>
    /// <returns>按钮规划结果。</returns>
    internal static ButtonPlan CreatePlan(IReadOnlyList<MessageDialogButton> buttons)
    {
        var ids = new int[buttons.Count];
        var labels = new string[buttons.Count];
        var defaultButtonId = 0;
        var cancelButtonIndex = -1;

        for (var i = 0; i < buttons.Count; i++)
        {
            ids[i] = ButtonIdBase + i;
            labels[i] = buttons[i].Label;

            if (defaultButtonId == 0 && buttons[i].IsDefault)
            {
                defaultButtonId = ids[i];
            }

            if (cancelButtonIndex < 0 && buttons[i].IsCancel)
            {
                cancelButtonIndex = i;
            }
        }

        return new ButtonPlan
        {
            ButtonIds = ids,
            Labels = labels,
            DefaultButtonId = defaultButtonId,
            CancelButtonIndex = cancelButtonIndex,
        };
    }

    /// <summary>
    /// 将 TaskDialogIndirect 返回的按钮 ID 映射为按钮索引。
    /// 对应 Go 版本 <c>taskDialogPlan.buttonFor</c>：取消 ID 映射到取消按钮，未知 ID 返回 -1。
    /// </summary>
    /// <param name="plan">按钮规划结果。</param>
    /// <param name="resultId">TaskDialogIndirect 返回的按钮 ID。</param>
    /// <returns>按钮索引；无匹配时返回 -1。</returns>
    internal static int ResolveTaskDialogButtonIndex(ButtonPlan plan, int resultId)
    {
        if (resultId >= ButtonIdBase && resultId - ButtonIdBase < plan.Count)
        {
            return resultId - ButtonIdBase;
        }

        if (resultId == CancelButtonId)
        {
            return plan.CancelButtonIndex;
        }

        return -1;
    }

    /// <summary>
    /// 将 MessageBox 返回值映射为按钮索引。
    /// 对应 Go 版本 <c>messageBoxButtonFor</c>：优先按标签匹配（大小写不敏感、忽略首尾空白），
    /// 再按 Yes/No/Cancel 语义回退到首个非取消按钮、取消按钮或第二个非取消按钮。
    /// </summary>
    /// <param name="buttons">按钮集合。</param>
    /// <param name="resultId">MessageBox 返回的标准 ID（IDOK=1、IDCANCEL=2、IDYES=6、IDNO=7 等）。</param>
    /// <returns>按钮索引；无匹配时返回 -1。</returns>
    internal static int ResolveMessageBoxButtonIndex(IReadOnlyList<MessageDialogButton> buttons, int resultId)
    {
        if (resultId > 0 && resultId < MessageBoxResponses.Length)
        {
            var response = MessageBoxResponses[resultId];
            if (response.Length > 0)
            {
                for (var i = 0; i < buttons.Count; i++)
                {
                    if (string.Equals(buttons[i].Label.Trim(), response, StringComparison.OrdinalIgnoreCase))
                    {
                        return i;
                    }
                }
            }
        }

        var firstPresent = -1;
        var secondPresent = -1;
        var cancel = -1;
        for (var i = 0; i < buttons.Count; i++)
        {
            if (buttons[i].IsCancel)
            {
                if (cancel < 0)
                {
                    cancel = i;
                }
            }
            else if (firstPresent < 0)
            {
                firstPresent = i;
            }
            else if (secondPresent < 0)
            {
                secondPresent = i;
            }
        }

        return resultId switch
        {
            6 => firstPresent,                      // IDYES → 首个非取消按钮
            7 => cancel >= 0 ? cancel : secondPresent, // IDNO → 取消按钮优先，否则第二个非取消按钮
            2 => cancel,                            // IDCANCEL → 取消按钮
            _ => -1,
        };
    }

    /// <summary>
    /// 尝试使用 TaskDialogIndirect 显示带自定义按钮的对话框。
    /// </summary>
    /// <param name="options">消息对话框选项（按钮集合不能为空）。</param>
    /// <param name="buttonIndex">被点击按钮的索引；取消且未定义取消按钮时为 -1。</param>
    /// <returns>成功显示并返回结果时为 true；comctl32 v6 不可用或调用失败时返回 false（调用方应回退 MessageBox）。</returns>
    internal static bool TryShow(MessageDialogOptions options, out int buttonIndex)
    {
        buttonIndex = -1;
        if (options.Buttons.Count == 0)
        {
            return false;
        }

        var plan = CreatePlan(options.Buttons);
        var labelHandles = new nint[plan.Count];
        nint titleHandle = 0;
        nint contentHandle = 0;
        nint buttonsHandle = 0;

        try
        {
            titleHandle = Marshal.StringToHGlobalUni(options.Title);
            contentHandle = Marshal.StringToHGlobalUni(options.Message);
            for (var i = 0; i < plan.Count; i++)
            {
                labelHandles[i] = Marshal.StringToHGlobalUni(plan.Labels[i]);
            }

            var buttonSize = Marshal.SizeOf<TASKDIALOG_BUTTON>();
            buttonsHandle = Marshal.AllocHGlobal(buttonSize * plan.Count);

            unsafe
            {
                var buttons = (TASKDIALOG_BUTTON*)buttonsHandle;
                for (var i = 0; i < plan.Count; i++)
                {
                    buttons[i].nButtonID = plan.ButtonIds[i];
                    buttons[i].pszButtonText = new PCWSTR((char*)labelHandles[i]);
                }

                // 取消按钮存在时允许 Esc/关闭，此时 TaskDialogIndirect 返回 IDCANCEL（2）。
                var flags = default(TASKDIALOG_FLAGS);
                if (plan.CancelButtonIndex >= 0)
                {
                    flags |= TASKDIALOG_FLAGS.TDF_ALLOW_DIALOG_CANCELLATION;
                }

                var config = new TASKDIALOGCONFIG
                {
                    cbSize = (uint)Marshal.SizeOf<TASKDIALOGCONFIG>(),
                    hwndParent = default,
                    hInstance = default,
                    dwFlags = flags,
                    dwCommonButtons = default,
                    pszWindowTitle = new PCWSTR((char*)titleHandle),
                    pszMainInstruction = default,
                    pszContent = new PCWSTR((char*)contentHandle),
                    cButtons = (uint)plan.Count,
                    pButtons = buttons,
                    nDefaultButton = plan.DefaultButtonId,
                    pfCallback = s_callback,
                };

                var result = PInvoke.TaskDialogIndirect(in config, out var resultId, out _, out _);
                if (result.Failed)
                {
                    return false;
                }

                buttonIndex = ResolveTaskDialogButtonIndex(plan, resultId);
                return true;
            }
        }
        catch (Exception)
        {
            // comctl32 v6 不可用（无 manifest）、入口点缺失或原生调用异常时回退 MessageBox。
            return false;
        }
        finally
        {
            for (var i = 0; i < labelHandles.Length; i++)
            {
                if (labelHandles[i] != 0)
                {
                    Marshal.FreeHGlobal(labelHandles[i]);
                }
            }

            if (titleHandle != 0)
            {
                Marshal.FreeHGlobal(titleHandle);
            }

            if (contentHandle != 0)
            {
                Marshal.FreeHGlobal(contentHandle);
            }

            if (buttonsHandle != 0)
            {
                Marshal.FreeHGlobal(buttonsHandle);
            }
        }
    }

    /// <summary>
    /// TaskDialog 回调：在 TDN_CREATED 时把对话框置顶，保留 MessageBox 的 MB_SYSTEMMODAL 行为。
    /// 对应 Go 版本 <c>taskDialogCreated</c>。
    /// </summary>
    private static HRESULT OnTaskDialogCallback(
        HWND hwnd,
        TASKDIALOG_NOTIFICATIONS notification,
        WPARAM wParam,
        LPARAM lParam,
        nint lpRefData)
    {
        if (notification == TASKDIALOG_NOTIFICATIONS.TDN_CREATED)
        {
            PInvoke.SetWindowPos(
                hwnd,
                HWND.HWND_TOPMOST,
                0,
                0,
                0,
                0,
                SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
        }

        return default;
    }
}
