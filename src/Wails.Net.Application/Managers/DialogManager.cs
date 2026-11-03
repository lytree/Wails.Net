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
    /// 应用运行状态判定（可为 null，表示不做运行前拦截）。
    /// 对应 Wails v3.1.0 #6390：在 <c>Run</c> 之前请求的对话框直接返回“未选择”，
    /// 避免在 GTK 等平台等待尚未启动的主循环而永久挂起。
    /// </summary>
    private Func<bool>? _isRunning;

    /// <summary>
    /// 使用指定的平台应用构造 DialogManager 实例。
    /// </summary>
    /// <param name="platformApp">平台应用实例，可为 null（Server 模式）。</param>
    /// <param name="isRunning">应用运行状态判定；为 null 时不做运行前拦截。</param>
    public DialogManager(IPlatformApp? platformApp, Func<bool>? isRunning = null)
    {
        _platformApp = platformApp;
        _isRunning = isRunning;
    }

    /// <summary>
    /// 绑定应用运行状态判定（仅当尚未绑定时生效）。
    /// DI 模式下 DialogManager 由容器创建，需由 <c>Application</c> 在初始化时补挂。
    /// </summary>
    /// <param name="isRunning">应用运行状态判定。</param>
    internal void AttachRunningState(Func<bool> isRunning)
    {
        ArgumentNullException.ThrowIfNull(isRunning);
        _isRunning ??= isRunning;
    }

    /// <summary>
    /// 应用尚未运行（主循环未启动）时，对话框无法显示，直接返回“未选择”。
    /// </summary>
    private bool IsBlockedByNotRunning => _isRunning is not null && !_isRunning();

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
        if (IsBlockedByNotRunning)
        {
            return -1; // Run 之前无法显示对话框（v3.1.0 #6390）
        }

        if (_platformApp is null)
        {
            return 0; // Server 模式下返回默认值
        }

        return await _platformApp.ShowMessageDialog(title, message, style, buttons);
    }

    /// <summary>
    /// 显示消息对话框，支持自定义按钮标签、默认/取消按钮与选中回调。
    /// 直接转发到平台实现，由平台负责在对话框关闭后触发按钮回调。
    /// </summary>
    /// <param name="options">消息对话框选项。</param>
    /// <returns>被点击按钮的索引；对话框被取消且未定义取消按钮时返回 -1。</returns>
    public async Task<int> ShowMessageDialog(MessageDialogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (IsBlockedByNotRunning)
        {
            return -1; // Run 之前无法显示对话框（v3.1.0 #6390）
        }

        if (_platformApp is null)
        {
            return 0; // Server 模式下返回默认值
        }

        return await _platformApp.ShowMessageDialog(options);
    }

    /// <summary>
    /// 打开文件对话框。
    /// </summary>
    /// <param name="options">打开文件对话框选项。</param>
    /// <returns>选中的文件路径，可为 null。</returns>
    public async Task<string?> OpenFileDialog(OpenFileDialogOptions options)
    {
        if (IsBlockedByNotRunning)
        {
            return null; // Run 之前无法显示对话框（v3.1.0 #6390）
        }

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
        if (IsBlockedByNotRunning)
        {
            return null; // Run 之前无法显示对话框（v3.1.0 #6390）
        }

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
        if (IsBlockedByNotRunning)
        {
            return null; // Run 之前无法显示对话框（v3.1.0 #6390）
        }

        if (_platformApp is null)
        {
            return null;
        }

        return await _platformApp.OpenMultipleFilesDialog(options);
    }
}
