namespace Wails.Net.Application.Dialogs;

/// <summary>
/// 消息对话框按钮定义。
/// 对应 Wails v3 Go 版本 dialogs.go 中的 Button 结构，用于支持自定义按钮标签、
/// 默认/取消语义以及按钮被选中后的回调。
/// </summary>
public sealed class MessageDialogButton
{
    /// <summary>
    /// 按钮文本。
    /// </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// 是否为默认按钮（回车键触发）。多个按钮同时标记时以第一个为准。
    /// </summary>
    public bool IsDefault { get; init; }

    /// <summary>
    /// 是否为取消按钮（Esc 键或关闭对话框时回退到该按钮）。多个按钮同时标记时以第一个为准。
    /// </summary>
    public bool IsCancel { get; init; }

    /// <summary>
    /// 按钮被选中后执行的回调，可为 null。
    /// </summary>
    public Action? Callback { get; init; }
}

/// <summary>
/// 消息对话框选项。
/// 对应 Wails v3 Go 版本 dialogs.go 中的 MessageDialogOptions。
/// Windows 平台在询问样式（<see cref="DialogStyle.Question"/>）且提供按钮时使用
/// TaskDialogIndirect 渲染自定义标签；comctl32 v6 不可用时回退 MessageBox 并按标签/位置映射回调。
/// </summary>
public sealed class MessageDialogOptions
{
    /// <summary>
    /// 对话框标题。
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// 消息内容。
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// 对话框样式。
    /// </summary>
    public DialogStyle Style { get; init; } = DialogStyle.Info;

    /// <summary>
    /// 按钮集合，可为空集合（此时由平台决定默认按钮）。
    /// </summary>
    public IReadOnlyList<MessageDialogButton> Buttons { get; init; } = Array.Empty<MessageDialogButton>();

    /// <summary>
    /// 使用仅包含标签的按钮集合创建消息对话框选项（不包含默认/取消标记与回调）。
    /// </summary>
    /// <param name="title">对话框标题。</param>
    /// <param name="message">消息内容。</param>
    /// <param name="style">对话框样式。</param>
    /// <param name="buttons">按钮文本数组。</param>
    /// <returns>消息对话框选项。</returns>
    public static MessageDialogOptions FromLabels(string title, string message, DialogStyle style, string[] buttons)
    {
        ArgumentNullException.ThrowIfNull(buttons);

        var list = new MessageDialogButton[buttons.Length];
        for (var i = 0; i < buttons.Length; i++)
        {
            list[i] = new MessageDialogButton { Label = buttons[i] };
        }

        return new MessageDialogOptions
        {
            Title = title,
            Message = message,
            Style = style,
            Buttons = list,
        };
    }

    /// <summary>
    /// 获取按钮标签数组。
    /// </summary>
    /// <returns>按钮标签数组；按钮集合为空时返回空数组。</returns>
    public string[] GetButtonLabels()
    {
        if (Buttons.Count == 0)
        {
            return Array.Empty<string>();
        }

        var labels = new string[Buttons.Count];
        for (var i = 0; i < Buttons.Count; i++)
        {
            labels[i] = Buttons[i].Label;
        }

        return labels;
    }

    /// <summary>
    /// 触发指定索引按钮的回调。
    /// </summary>
    /// <param name="index">按钮索引；越界或负数时忽略（对话框被取消且未定义取消按钮时可能出现）。</param>
    public void InvokeButtonCallback(int index)
    {
        if ((uint)index >= (uint)Buttons.Count)
        {
            return;
        }

        Buttons[index].Callback?.Invoke();
    }
}
