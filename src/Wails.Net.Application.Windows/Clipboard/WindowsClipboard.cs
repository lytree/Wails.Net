using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using Wails.Net.Application.Clipboard;
using WinFormsClipboard = System.Windows.Forms.Clipboard;

namespace Wails.Net.Application.Clipboard;

/// <summary>
/// Windows 剪贴板实现，基于 System.Windows.Forms.Clipboard（Win32 剪贴板 API 的托管封装）。
/// 对应 Go 版 clipboard_windows.go。
/// </summary>
public sealed class WindowsClipboard : IClipboardImpl
{
    /// <inheritdoc />
    public void SetText(string text)
    {
        WinFormsClipboard.SetText(text);
    }

    /// <inheritdoc />
    public string GetText()
    {
        return WinFormsClipboard.ContainsText() ? WinFormsClipboard.GetText() : string.Empty;
    }

    /// <inheritdoc />
    public void SetHTML(string html, string fallbackText)
    {
        // 使用 DataObject 同时设置 HTML 和回退文本，确保两种格式均可用。
        var data = new DataObject();
        data.SetText(html, TextDataFormat.Html);
        data.SetText(fallbackText, TextDataFormat.Text);
        WinFormsClipboard.SetDataObject(data, true);
    }

    /// <inheritdoc />
    public string GetHTML()
    {
        return WinFormsClipboard.ContainsText(TextDataFormat.Html)
            ? WinFormsClipboard.GetText(TextDataFormat.Html)
            : string.Empty;
    }

    /// <inheritdoc />
    public void SetImage(byte[] imageData)
    {
        if (imageData is null || imageData.Length == 0)
        {
            return;
        }

        using var stream = new MemoryStream(imageData);
        using var image = Image.FromStream(stream);
        WinFormsClipboard.SetImage(image);
    }

    /// <inheritdoc />
    public byte[]? GetImage()
    {
        if (!WinFormsClipboard.ContainsImage())
        {
            return null;
        }

        using var image = WinFormsClipboard.GetImage();
        if (image is null)
        {
            return null;
        }

        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    /// <inheritdoc />
    public void Clear()
    {
        WinFormsClipboard.Clear();
    }
}
