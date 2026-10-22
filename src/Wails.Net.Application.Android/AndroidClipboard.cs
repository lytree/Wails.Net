using System.Collections.Generic;
using Android.Content;
using Android.Net;
using Android.Provider;
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
#if ANDROID
        if (_clipboardManager is null || imageData is null || imageData.Length == 0)
        {
            return;
        }

        // Android 剪贴板基于 URI：把图片写入 MediaStore 后以 content:// URI 放入剪贴板。
        // API 29+ 使用分区存储（IS_PENDING 两阶段写入），无需 WRITE_EXTERNAL_STORAGE；
        // 更低版本需要外部存储权限，保持 no-op（由宿主应用自行写入 URI）。
        if (!OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            return;
        }

        var context = global::Android.App.Application.Context;
        var resolver = context?.ContentResolver;
        if (resolver is null)
        {
            return;
        }

        using var values = new ContentValues();
        values.Put(MediaStore.Images.Media.InterfaceConsts.DisplayName, $"wails-clipboard-{Guid.NewGuid():N}.png");
        values.Put(MediaStore.Images.Media.InterfaceConsts.MimeType, "image/png");
        values.Put(MediaStore.Images.Media.InterfaceConsts.RelativePath, "Pictures/WailsNet");
        values.Put(MediaStore.Images.Media.InterfaceConsts.IsPending, 1);

        var collection = MediaStore.Images.Media.ExternalContentUri;
        if (collection is null)
        {
            return;
        }

        var uri = resolver.Insert(collection, values);
        if (uri is null)
        {
            return;
        }

        try
        {
            using (var output = resolver.OpenOutputStream(uri))
            {
                if (output is null)
                {
                    resolver.Delete(uri, null, null);
                    return;
                }

                output.Write(imageData, 0, imageData.Length);
            }

            // 完成待写入状态，使其他应用可读取。
            using var complete = new ContentValues();
            complete.Put(MediaStore.Images.Media.InterfaceConsts.IsPending, 0);
            resolver.Update(uri, complete, null, null);

            _clipboardManager.PrimaryClip = ClipData.NewUri(resolver, "image", uri);
        }
        catch
        {
            // 写入失败时清理半成品条目。
            try
            {
                resolver.Delete(uri, null, null);
            }
            catch
            {
                // 清理失败忽略。
            }
        }
#endif
    }

    /// <inheritdoc />
    public byte[]? GetImage()
    {
#if ANDROID
        var clip = _clipboardManager?.PrimaryClip;
        if (clip is null || clip.ItemCount == 0)
        {
            return null;
        }

        var uri = clip.GetItemAt(0)?.Uri;
        if (uri is null)
        {
            return null;
        }

        var resolver = global::Android.App.Application.Context?.ContentResolver;
        if (resolver is null)
        {
            return null;
        }

        try
        {
            using var input = resolver.OpenInputStream(uri);
            if (input is null)
            {
                return null;
            }

            using var buffer = new System.IO.MemoryStream();
            input.CopyTo(buffer);
            return buffer.ToArray();
        }
        catch
        {
            return null;
        }
#else
        return null;
#endif
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
