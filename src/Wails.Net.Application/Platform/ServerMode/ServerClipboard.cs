using Wails.Net.Application.Clipboard;

namespace Wails.Net.Application.Platform.ServerMode;

/// <summary>
/// Server 模式下的剪贴板桩实现。
/// 所有剪贴板操作均为空操作，适用于无头运行场景。
/// </summary>
public class ServerClipboard : IClipboardImpl
{
    /// <inheritdoc />
    public void SetText(string text)
    {
        // Server 模式下不支持设置剪贴板文本。
    }

    /// <inheritdoc />
    public string GetText() => "";

    /// <inheritdoc />
    public void SetHTML(string html, string fallbackText)
    {
        // Server 模式下不支持设置剪贴板 HTML。
    }

    /// <inheritdoc />
    public string GetHTML() => "";

    /// <inheritdoc />
    public void SetImage(byte[] imageData)
    {
        // Server 模式下不支持设置剪贴板图片。
    }

    /// <inheritdoc />
    public byte[]? GetImage() => null;

    /// <inheritdoc />
    public void SetFiles(string[] files)
    {
        // Server 模式下不支持设置剪贴板文件。
    }

    /// <inheritdoc />
    public string[] GetFiles() => Array.Empty<string>();

    /// <inheritdoc />
    public void Clear()
    {
        // Server 模式下不支持清空剪贴板。
    }
}
