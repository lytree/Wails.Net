using Wails.Net.Application.Clipboard;

namespace Wails.Net.Application.Platform;

/// <summary>
/// macOS 平台剪贴板实现。
/// 对应 Wails v3 Go 版本 <c>clipboard_darwin.go</c>，通过 <c>NSPasteboard</c> 提供剪贴板读写。
/// <para>
/// NSPasteboard 非线程安全，所有操作统一调度到主线程（参照 DevToys Clipboard.cs 的
/// <c>ThreadHelper.RunOnUIThreadAsync</c> 模式）。文件/图片读写采用 <c>ReadObjectsForClasses</c> /
/// <c>WriteObjects</c> + <c>NSImage.AsTiff</c> 标准做法。
/// 在非 macOS 目标（<c>#if !MACOS</c>）保留骨架行为（空值/no-op），保证任意宿主编译。
/// </para>
/// </summary>
public sealed class MacOSClipboard : IClipboardImpl
{
    /// <summary>
    /// 剪贴板访问锁，串行化并发访问。
    /// </summary>
    private readonly object _lock = new();

    /// <inheritdoc />
    public void SetText(string text)
    {
#if MACOS
        ArgumentNullException.ThrowIfNull(text);
        lock (_lock)
        {
            MacOSPlatformApp.DispatchOnMainThreadSync(() =>
            {
                var pasteboard = AppKit.NSPasteboard.GeneralPasteboard;
                pasteboard.ClearContents();
                pasteboard.SetStringForType(text, AppKit.NSPasteboardType.String);
            });
        }
#else
        // 非 macOS 目标：no-op 骨架。
#endif
    }

    /// <inheritdoc />
    public string GetText()
    {
#if MACOS
        lock (_lock)
        {
            return MacOSPlatformApp.DispatchOnMainThreadSync(() =>
            {
                var pasteboard = AppKit.NSPasteboard.GeneralPasteboard;
                if (pasteboard.CanReadObjectForClasses(
                        new[] { new ObjCRuntime.Class(typeof(Foundation.NSString)) }, null))
                {
                    var objects = pasteboard.ReadObjectsForClasses(
                        new[] { new ObjCRuntime.Class(typeof(Foundation.NSString)) }, null);
                    if (objects.Length > 0 && objects[0] is Foundation.NSString nsString)
                    {
                        return nsString.ToString();
                    }
                }

                return string.Empty;
            });
        }
#else
        return string.Empty;
#endif
    }

    /// <inheritdoc />
    public void SetHTML(string html, string fallbackText)
    {
#if MACOS
        ArgumentNullException.ThrowIfNull(html);
        lock (_lock)
        {
            MacOSPlatformApp.DispatchOnMainThreadSync(() =>
            {
                var pasteboard = AppKit.NSPasteboard.GeneralPasteboard;
                pasteboard.ClearContents();
                pasteboard.SetStringForType(html, AppKit.NSPasteboardType.Html);
                // 同时写入纯文本回退，保证粘贴到纯文本应用时可用。
                pasteboard.SetStringForType(fallbackText, AppKit.NSPasteboardType.String);
            });
        }
#else
        // 非 macOS 目标：no-op 骨架。
#endif
    }

    /// <inheritdoc />
    public string GetHTML()
    {
#if MACOS
        lock (_lock)
        {
            return MacOSPlatformApp.DispatchOnMainThreadSync(() =>
                AppKit.NSPasteboard.GeneralPasteboard.GetStringForType(AppKit.NSPasteboardType.Html) ?? string.Empty);
        }
#else
        return string.Empty;
#endif
    }

    /// <inheritdoc />
    public void SetImage(byte[] imageData)
    {
#if MACOS
        ArgumentNullException.ThrowIfNull(imageData);
        lock (_lock)
        {
            MacOSPlatformApp.DispatchOnMainThreadSync(() =>
            {
                using var data = Foundation.NSData.FromArray(imageData);
                using var image = AppKit.NSImage.FromData(data);
                if (image is null)
                {
                    return;
                }

                var pasteboard = AppKit.NSPasteboard.GeneralPasteboard;
                pasteboard.ClearContents();
                // NSImage 实现 INSPasteboardWriting，可直接写入（参照 DevToys SetClipboardImageAsync）。
                pasteboard.WriteObjects(new INSPasteboardWriting[] { image });
            });
        }
#else
        // 非 macOS 目标：no-op 骨架。
#endif
    }

    /// <inheritdoc />
    public byte[]? GetImage()
    {
#if MACOS
        lock (_lock)
        {
            return MacOSPlatformApp.DispatchOnMainThreadSync<byte[]?>(() =>
            {
                var pasteboard = AppKit.NSPasteboard.GeneralPasteboard;
                if (pasteboard.CanReadObjectForClasses(
                        new[] { new ObjCRuntime.Class(typeof(AppKit.NSImage)) }, null))
                {
                    var images = pasteboard.ReadObjectsForClasses(
                        new[] { new ObjCRuntime.Class(typeof(AppKit.NSImage)) }, null);
                    if (images.Length > 0 && images[0] is AppKit.NSImage imageFromPasteboard)
                    {
                        var tiff = imageFromPasteboard.AsTiff();
                        return tiff?.ToArray();
                    }
                }

                return null;
            });
        }
#else
        return null;
#endif
    }

    /// <inheritdoc />
    public void SetFiles(string[] files)
    {
#if MACOS
        ArgumentNullException.ThrowIfNull(files);
        lock (_lock)
        {
            MacOSPlatformApp.DispatchOnMainThreadSync(() =>
            {
                var pasteboard = AppKit.NSPasteboard.GeneralPasteboard;
                pasteboard.ClearContents();
                var fileList = files
                    .Where(f => !string.IsNullOrEmpty(f))
                    .Select(f => (INSPasteboardWriting)Foundation.NSUrl.FromFilename(f))
                    .ToArray();
                if (fileList.Length > 0)
                {
                    pasteboard.WriteObjects(fileList);
                }
            });
        }
#else
        // 非 macOS 目标：no-op 骨架。
#endif
    }

    /// <inheritdoc />
    public string[] GetFiles()
    {
#if MACOS
        lock (_lock)
        {
            return MacOSPlatformApp.DispatchOnMainThreadSync(() =>
            {
                var pasteboard = AppKit.NSPasteboard.GeneralPasteboard;
                if (pasteboard.CanReadObjectForClasses(
                        new[] { new ObjCRuntime.Class(typeof(Foundation.NSUrl)) }, null))
                {
                    var urls = pasteboard.ReadObjectsForClasses(
                        new[] { new ObjCRuntime.Class(typeof(Foundation.NSUrl)) }, null);
                    var files = new List<string>(urls.Length);
                    foreach (var urlObj in urls)
                    {
                        if (urlObj is Foundation.NSUrl { AbsoluteString: not null, Path: not null } url
                            && url.AbsoluteString.StartsWith("file:///", StringComparison.Ordinal))
                        {
                            files.Add(url.Path);
                        }
                    }

                    return files.ToArray();
                }

                return Array.Empty<string>();
            });
        }
#else
        return Array.Empty<string>();
#endif
    }

    /// <inheritdoc />
    public void Clear()
    {
#if MACOS
        lock (_lock)
        {
            MacOSPlatformApp.DispatchOnMainThreadSync(() =>
            {
                AppKit.NSPasteboard.GeneralPasteboard.ClearContents();
            });
        }
#else
        // 非 macOS 目标：no-op 骨架。
#endif
    }
}
