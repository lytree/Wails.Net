using Wails.Net.Application.Clipboard;

namespace Wails.Net.Application.Clipboard;

/// <summary>
/// Linux 剪贴板实现，基于 GTK 剪贴板 API。
/// 对应 Go 版 clipboard_linux.go。
/// 当前为存根实现，完整的 GTK 剪贴板集成将在后续阶段实现。
/// </summary>
public sealed class LinuxClipboard : IClipboardImpl
{
    /// <inheritdoc />
    public void SetText(string text)
    {
        // TODO: 将在后续实现完整的 GTK 剪贴板集成
        // 将使用 Gdk.Display.Default.Clipboard 设置文本内容
    }

    /// <inheritdoc />
    public string GetText()
    {
        // TODO: 将在后续实现完整的 GTK 剪贴板集成
        // 将使用 Gdk.Display.Default.Clipboard 读取文本内容
        return string.Empty;
    }

    /// <inheritdoc />
    public void SetHTML(string html, string fallbackText)
    {
        // TODO: 将在后续实现完整的 GTK 剪贴板集成
        // 将使用 Gdk.Display.Default.Clipboard 设置 HTML 内容
    }

    /// <inheritdoc />
    public string GetHTML()
    {
        // TODO: 将在后续实现完整的 GTK 剪贴板集成
        // 将使用 Gdk.Display.Default.Clipboard 读取 HTML 内容
        return string.Empty;
    }

    /// <inheritdoc />
    public void SetImage(byte[] imageData)
    {
        // TODO: 将在后续实现完整的 GTK 剪贴板集成
        // 将使用 Gdk.Display.Default.Clipboard 设置图片内容
    }

    /// <inheritdoc />
    public byte[]? GetImage()
    {
        // TODO: 将在后续实现完整的 GTK 剪贴板集成
        // 将使用 Gdk.Display.Default.Clipboard 读取图片内容
        return null;
    }

    /// <inheritdoc />
    public void Clear()
    {
        // TODO: 将在后续实现完整的 GTK 剪贴板集成
        // 将使用 Gdk.Display.Default.Clipboard 清空剪贴板
    }
}
