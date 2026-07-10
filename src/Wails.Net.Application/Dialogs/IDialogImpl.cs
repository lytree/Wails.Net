namespace Wails.Net.Application.Dialogs;

/// <summary>
/// 对话框样式枚举。
/// </summary>
public enum DialogStyle
{
    /// <summary>
    /// 信息提示样式。
    /// </summary>
    Info = 0,

    /// <summary>
    /// 警告样式。
    /// </summary>
    Warning = 1,

    /// <summary>
    /// 错误样式。
    /// </summary>
    Error = 2,

    /// <summary>
    /// 询问样式。
    /// </summary>
    Question = 3
}

/// <summary>
/// 打开文件对话框选项。
/// </summary>
public class OpenFileDialogOptions
{
    /// <summary>
    /// 对话框标题，可为 null。
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// 初始目录，可为 null。
    /// </summary>
    public string? Directory { get; set; }

    /// <summary>
    /// 文件过滤器数组，可为 null。
    /// </summary>
    public string[]? Filters { get; set; }

    /// <summary>
    /// 是否允许选择文件。
    /// </summary>
    public bool AllowFiles { get; set; }

    /// <summary>
    /// 是否允许选择目录。
    /// </summary>
    public bool AllowDirectories { get; set; }

    /// <summary>
    /// 是否允许多选。
    /// </summary>
    public bool CanChooseMultiple { get; set; }

    /// <summary>
    /// 是否显示隐藏文件。
    /// </summary>
    public bool ShowHiddenFiles { get; set; }
}

/// <summary>
/// 保存文件对话框选项。
/// </summary>
public class SaveFileDialogOptions
{
    /// <summary>
    /// 对话框标题，可为 null。
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// 初始目录，可为 null。
    /// </summary>
    public string? Directory { get; set; }

    /// <summary>
    /// 默认文件名，可为 null。
    /// </summary>
    public string? Filename { get; set; }

    /// <summary>
    /// 文件过滤器数组，可为 null。
    /// </summary>
    public string[]? Filters { get; set; }

    /// <summary>
    /// 是否显示隐藏文件。
    /// </summary>
    public bool ShowHiddenFiles { get; set; }

    /// <summary>
    /// 是否允许创建目录。
    /// </summary>
    public bool CreateDirectories { get; set; }
}

/// <summary>
/// 消息对话框平台实现接口。
/// </summary>
public interface IMessageDialogImpl
{
    /// <summary>
    /// 同步显示消息对话框。
    /// </summary>
    /// <param name="title">对话框标题。</param>
    /// <param name="message">消息内容。</param>
    /// <param name="style">对话框样式。</param>
    /// <param name="buttons">按钮文本数组。</param>
    void ShowMessage(string title, string message, DialogStyle style, string[] buttons);

    /// <summary>
    /// 异步显示消息对话框。
    /// </summary>
    /// <param name="title">对话框标题。</param>
    /// <param name="message">消息内容。</param>
    /// <param name="style">对话框样式。</param>
    /// <param name="buttons">按钮文本数组。</param>
    /// <returns>被点击按钮的索引。</returns>
    Task<int> ShowMessageAsync(string title, string message, DialogStyle style, string[] buttons);
}

/// <summary>
/// 文件对话框平台实现接口。
/// </summary>
public interface IFileDialogImpl
{
    /// <summary>
    /// 同步打开文件对话框。
    /// </summary>
    /// <param name="options">打开文件对话框选项。</param>
    /// <returns>选中的文件路径，可为 null。</returns>
    string? OpenFileDialog(OpenFileDialogOptions options);

    /// <summary>
    /// 同步保存文件对话框。
    /// </summary>
    /// <param name="options">保存文件对话框选项。</param>
    /// <returns>保存的文件路径，可为 null。</returns>
    string? SaveFileDialog(SaveFileDialogOptions options);

    /// <summary>
    /// 同步打开多文件选择对话框。
    /// </summary>
    /// <param name="options">打开文件对话框选项。</param>
    /// <returns>选中的文件路径数组，可为 null。</returns>
    string[]? OpenMultipleFilesDialog(OpenFileDialogOptions options);

    /// <summary>
    /// 异步打开文件对话框。
    /// </summary>
    /// <param name="options">打开文件对话框选项。</param>
    /// <returns>选中的文件路径，可为 null。</returns>
    Task<string?> OpenFileDialogAsync(OpenFileDialogOptions options);

    /// <summary>
    /// 异步保存文件对话框。
    /// </summary>
    /// <param name="options">保存文件对话框选项。</param>
    /// <returns>保存的文件路径，可为 null。</returns>
    Task<string?> SaveFileDialogAsync(SaveFileDialogOptions options);

    /// <summary>
    /// 异步打开多文件选择对话框。
    /// </summary>
    /// <param name="options">打开文件对话框选项。</param>
    /// <returns>选中的文件路径数组，可为 null。</returns>
    Task<string[]?> OpenMultipleFilesDialogAsync(OpenFileDialogOptions options);
}
