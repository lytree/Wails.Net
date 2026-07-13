using System.Collections.Generic;
using Android.Content;
using Android.Net;
using Wails.Net.Application.Clipboard;
using Uri = Android.Net.Uri;

namespace Wails.Net.Application.Clipboard;

/// <summary>
/// Android 平台剪贴板实现。
/// 对应 Wails v3 Go 版本 clipboard.go，通过 <c>ClipboardManager</c> + <c>ClipData</c> 操作系统剪贴板。
/// 文本和 HTML 使用 <c>ClipData.NewPlainText</c> / <c>ClipData.NewHtmlText</c>。
/// 文件列表使用 <c>ClipData.NewRawUri</c> 携带 <c>file://</c> URI。
/// 图片不支持直接设置原始字节（Android 剪贴板基于 URI），相关方法为 no-op。
/// </summary>
public sealed class AndroidClipboard : IClipboardImpl
{
    /// <summary>
    /// Android 系统剪贴板管理器，延迟获取。
    /// </summary>
    private readonly ClipboardManager? _clipboardManager;

    /// <summary>
    /// 构造 AndroidClipboard 实例，从全局 Context 获取 ClipboardManager。
    /// </summary>
    public AndroidClipboard()
    {
        var context = global::Android.App.Application.Context;
        _clipboardManager = context?.GetSystemService(Context.ClipboardService) as ClipboardManager;
    }

    /// <inheritdoc />
    public void SetText(string text)
    {
        if (_clipboardManager is null)
        {
            return;
        }

        var clip = ClipData.NewPlainText("text", text);
        _clipboardManager.PrimaryClip = clip;
    }

    /// <inheritdoc />
    public string GetText()
    {
        var clip = _clipboardManager?.PrimaryClip;
        if (clip is null || clip.ItemCount == 0)
        {
            return string.Empty;
        }

        return clip.GetItemAt(0)?.Text ?? string.Empty;
    }

    /// <inheritdoc />
    public void SetHTML(string html, string fallbackText)
    {
        if (_clipboardManager is null)
        {
            return;
        }

        var clip = ClipData.NewHtmlText("html", fallbackText, html);
        _clipboardManager.PrimaryClip = clip;
    }

    /// <inheritdoc />
    public string GetHTML()
    {
        var clip = _clipboardManager?.PrimaryClip;
        if (clip is null || clip.ItemCount == 0)
        {
            return string.Empty;
        }

        return clip.GetItemAt(0)?.HtmlText ?? string.Empty;
    }

    /// <inheritdoc />
    public void SetImage(byte[] imageData)
    {
        // Android 剪贴板基于 URI 而非原始字节，设置图片需先将字节写入 ContentProvider 并获取 URI。
        // 骨架实现为 no-op，完整实现需注入 ContentResolver。
    }

    /// <inheritdoc />
    public byte[]? GetImage()
    {
        // Android 剪贴板图片需通过 URI + ContentResolver 读取。
        // 骨架实现返回 null。
        return null;
    }

    /// <inheritdoc />
    public void SetFiles(string[] files)
    {
        if (_clipboardManager is null || files is null || files.Length == 0)
        {
            return;
        }

        ClipData? clip = null;
        foreach (var file in files)
        {
            var uri = Uri.Parse("file://" + file);
            if (uri is null)
            {
                continue;
            }

            if (clip is null)
            {
                clip = ClipData.NewRawUri("files", uri);
            }
            else
            {
                clip.AddItem(new ClipData.Item(uri));
            }
        }

        if (clip is not null)
        {
            _clipboardManager.PrimaryClip = clip;
        }
    }

    /// <inheritdoc />
    public string[] GetFiles()
    {
        var clip = _clipboardManager?.PrimaryClip;
        if (clip is null || clip.ItemCount == 0)
        {
            return System.Array.Empty<string>();
        }

        var files = new List<string>();
        for (var i = 0; i < clip.ItemCount; i++)
        {
            var uri = clip.GetItemAt(i)?.Uri;
            if (uri is not null)
            {
                var path = uri.Path;
                if (!string.IsNullOrEmpty(path))
                {
                    files.Add(path);
                }
            }
        }

        return files.ToArray();
    }

    /// <inheritdoc />
    public void Clear()
    {
        if (_clipboardManager is null)
        {
            return;
        }

        _clipboardManager.PrimaryClip = null;
    }
}
