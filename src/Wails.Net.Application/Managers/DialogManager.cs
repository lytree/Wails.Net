using Wails.Net.Application.Dialogs;
using Wails.Net.Application.Platform;

namespace Wails.Net.Application.Managers;

/// <summary>
/// 对话框管理器，负责显示消息对话框和文件对话框。
/// 对应 Wails v3 Go 版本中的 dialogManager。
/// 通过 IPlatformApp 委托给平台特定的对话框实现。
/// </summary>
public class DialogManager : IDialogManager
{
    /// <summary>
    /// 平台应用实例。
    /// </summary>
    private readonly IPlatformApp? _platformApp;

    /// <summary>
    /// 使用指定的平台应用构造 DialogManager 实例。
    /// </summary>
    /// <param name="platformApp">平台应用实例，可为 null（Server 模式）。</param>
    public DialogManager(IPlatformApp? platformApp)
    {
        _platformApp = platformApp;
    }

    /// <summary>
    /// 显示消息对话框。
    /// </summary>
    /// <param name="title">对话框标题。</param>
    /// <param name="message">消息内容。</param>
    /// <param name="style">对话框样式。</param>
    /// <param name="buttons">按钮文本数组。</param>
    /// <returns>被点击按钮的索引。</returns>
    public async Task<int> ShowMessageDialog(string title, string message, DialogStyle style, string[] buttons)
    {
        if (_platformApp is null)
        {
            return 0; // Server 模式下返回默认值
        }

        return await _platformApp.ShowMessageDialog(title, message, style, buttons);
    }

    /// <summary>
    /// 打开文件对话框。
    /// </summary>
    /// <param name="options">打开文件对话框选项。</param>
    /// <returns>选中的文件路径，可为 null。</returns>
    public async Task<string?> OpenFileDialog(OpenFileDialogOptions options)
    {
        if (_platformApp is null)
        {
            return null;
        }

        return await _platformApp.OpenFileDialog(options);
    }

    /// <summary>
    /// 保存文件对话框。
    /// </summary>
    /// <param name="options">保存文件对话框选项。</param>
    /// <returns>保存的文件路径，可为 null。</returns>
    public async Task<string?> SaveFileDialog(SaveFileDialogOptions options)
    {
        if (_platformApp is null)
        {
            return null;
        }

        return await _platformApp.SaveFileDialog(options);
    }

    /// <summary>
    /// 打开多文件选择对话框。
    /// </summary>
    /// <param name="options">打开文件对话框选项。</param>
    /// <returns>选中的文件路径数组，可为 null。</returns>
    public async Task<string[]?> OpenMultipleFilesDialog(OpenFileDialogOptions options)
    {
        if (_platformApp is null)
        {
            return null;
        }

        return await _platformApp.OpenMultipleFilesDialog(options);
    }
}
