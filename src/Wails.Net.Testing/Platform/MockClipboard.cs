using Wails.Net.Application.Clipboard;
using Wails.Net.Application.Managers;
using Wails.Net.Testing.Recording;

namespace Wails.Net.Testing.Platform;

/// <summary>
/// 无头 Mock 剪贴板实现，内部维护内存态剪贴板内容并记录全部调用。
/// <para>
/// 同时实现 <see cref="IClipboardImpl"/>（平台剪贴板契约）与 <see cref="IClipboardManager"/>
/// （应用层剪贴板管理器契约）——两者成员完全兼容，单一实现即可同时满足两条契约，
/// 使 <see cref="WailsTestHostBuilder"/> 能将其作为 <c>IClipboardManager</c> 注入到
/// <see cref="Application.ClipboardManager"/>，让剪贴板往返与调用记录在无 GUI 的 CI 中可验。
/// </para>
/// <para>
/// 与 <c>Wails.Net.Application.Platform.ServerMode.ServerClipboard</c> 的区别：
/// ServerClipboard 是生产降级桩（Get 恒返回空），
/// MockClipboard 是测试替身（Set → Get 往返一致）。
/// </para>
/// </summary>
public sealed class MockClipboard : IClipboardImpl, IClipboardManager
{
    /// <summary>
    /// 用于保护内存态内容的同步锁。
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    /// 调用记录器。
    /// </summary>
    private readonly CallRecorder _recorder;

    /// <summary>
    /// 当前文本内容。
    /// </summary>
    private string _text = string.Empty;

    /// <summary>
    /// 当前 HTML 内容。
    /// </summary>
    private string _html = string.Empty;

    /// <summary>
    /// 当前图片内容。
    /// </summary>
    private byte[]? _image;

    /// <summary>
    /// 当前文件列表内容。
    /// </summary>
    private string[] _files = [];

    /// <summary>
    /// 构造 Mock 剪贴板实例。
    /// </summary>
    /// <param name="recorder">调用记录器；为 null 时内部新建独立记录器。</param>
    public MockClipboard(CallRecorder? recorder = null)
    {
        _recorder = recorder ?? new CallRecorder();
    }

    /// <summary>
    /// 获取调用记录器，供测试断言调用序列。
    /// </summary>
    public CallRecorder Recorder => _recorder;

    /// <summary>
    /// 获取当前调用记录快照。
    /// </summary>
    public IReadOnlyList<CallRecord> Calls => _recorder.Snapshot();

    /// <inheritdoc />
    public void SetText(string text)
    {
        _recorder.Record(nameof(SetText), text);
        lock (_gate)
        {
            _text = text ?? string.Empty;
        }
    }

    /// <inheritdoc />
    public string GetText()
    {
        _recorder.Record(nameof(GetText));
        lock (_gate)
        {
            return _text;
        }
    }

    /// <inheritdoc />
    public void SetHTML(string html, string fallbackText)
    {
        _recorder.Record(nameof(SetHTML), html, fallbackText);
        lock (_gate)
        {
            _html = html ?? string.Empty;

            // 与平台实现一致：写入 HTML 时同步写入纯文本回退内容。
            _text = fallbackText ?? string.Empty;
        }
    }

    /// <inheritdoc />
    public string GetHTML()
    {
        _recorder.Record(nameof(GetHTML));
        lock (_gate)
        {
            return _html;
        }
    }

    /// <inheritdoc />
    public void SetImage(byte[] imageData)
    {
        _recorder.Record(nameof(SetImage), imageData);
        lock (_gate)
        {
            // 复制一份，避免调用方后续修改数组影响剪贴板内容。
            _image = imageData is null ? null : [.. imageData];
        }
    }

    /// <inheritdoc />
    public byte[]? GetImage()
    {
        _recorder.Record(nameof(GetImage));
        lock (_gate)
        {
            return _image is null ? null : [.. _image];
        }
    }

    /// <inheritdoc />
    public void SetFiles(string[] files)
    {
        _recorder.Record(nameof(SetFiles), files);
        lock (_gate)
        {
            _files = files is null ? [] : [.. files];
        }
    }

    /// <inheritdoc />
    public string[] GetFiles()
    {
        _recorder.Record(nameof(GetFiles));
        lock (_gate)
        {
            return [.. _files];
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        _recorder.Record(nameof(Clear));
        lock (_gate)
        {
            _text = string.Empty;
            _html = string.Empty;
            _image = null;
            _files = [];
        }
    }
}
