using Wails.Net.Application.Clipboard;

namespace Wails.Net.Application.Platform;

/// <summary>
/// macOS 平台剪贴板骨架实现（G7 阶段）。
/// 对应 Wails v3 Go 版本 <c>clipboard_darwin.go</c>。
/// <para>
/// 当前为占位实现，所有方法返回空值/no-op。
/// 后续阶段将集成 <c>NSPasteboard</c> / <c>NSImage</c> / <c>NSFilePromiseReceiver</c> 实现完整剪贴板支持。
/// </para>
/// </summary>
public sealed class MacOSClipboard : IClipboardImpl
{
    /// <inheritdoc />
    public void SetText(string text)
    {
        // TODO(G7-macOS): 使用 NSPasteboard.GeneralPasteboard.SetStringForType(text, NSPasteboard.NSStringType)
    }

    /// <inheritdoc />
    public string GetText()
    {
        // TODO(G7-macOS): 从 NSPasteboard.GeneralPasteboard 读取 NSStringType
        return string.Empty;
    }

    /// <inheritdoc />
    public void SetHTML(string html, string fallbackText)
    {
        // TODO(G7-macOS): 使用 NSPasteboard.SetDataForType(html, NSPasteboard.NSHTMLType)
    }

    /// <inheritdoc />
    public string GetHTML()
    {
        // TODO(G7-macOS): 从 NSPasteboard 读取 NSHTMLType
        return string.Empty;
    }

    /// <inheritdoc />
    public void SetImage(byte[] imageData)
    {
        // TODO(G7-macOS): 使用 NSImage.FromData(NSData.FromArray(imageData)) + NSPasteboard
    }

    /// <inheritdoc />
    public byte[]? GetImage()
    {
        // TODO(G7-macOS): 从 NSPasteboard 读取 NSTIFFType 转 byte[]
        return null;
    }

    /// <inheritdoc />
    public void SetFiles(string[] files)
    {
        // TODO(G7-macOS): 使用 NSPasteboard 写入 NSFilenamesPboardType
    }

    /// <inheritdoc />
    public string[] GetFiles()
    {
        // TODO(G7-macOS): 从 NSPasteboard 读取 NSFilenamesPboardType
        return Array.Empty<string>();
    }

    /// <inheritdoc />
    public void Clear()
    {
        // TODO(G7-macOS): 调用 NSPasteboard.ClearContents
    }
}
