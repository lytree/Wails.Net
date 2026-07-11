namespace Wails.Net.Application.Clipboard;

/// <summary>
/// 剪贴板平台实现接口。
/// </summary>
public interface IClipboardImpl
{
    /// <summary>
    /// 设置剪贴板文本内容。
    /// </summary>
    /// <param name="text">要设置的文本。</param>
    void SetText(string text);

    /// <summary>
    /// 获取剪贴板文本内容。
    /// </summary>
    /// <returns>剪贴板中的文本。</returns>
    string GetText();

    /// <summary>
    /// 设置剪贴板 HTML 内容。
    /// </summary>
    /// <param name="html">要设置的 HTML 内容。</param>
    /// <param name="fallbackText">不支持 HTML 时的回退文本。</param>
    void SetHTML(string html, string fallbackText);

    /// <summary>
    /// 获取剪贴板 HTML 内容。
    /// </summary>
    /// <returns>剪贴板中的 HTML 内容。</returns>
    string GetHTML();

    /// <summary>
    /// 设置剪贴板图片。
    /// </summary>
    /// <param name="imageData">图片字节数据。</param>
    void SetImage(byte[] imageData);

    /// <summary>
    /// 获取剪贴板图片。
    /// </summary>
    /// <returns>图片字节数据，可为 null。</returns>
    byte[]? GetImage();

    /// <summary>
    /// 设置剪贴板文件列表（对应 Win32 CF_HDROP）。
    /// 对应 Wails v3 Go 版本 clipboard.go 中的 SetFiles。
    /// </summary>
    /// <param name="files">文件路径数组。</param>
    void SetFiles(string[] files);

    /// <summary>
    /// 获取剪贴板文件列表。
    /// 对应 Wails v3 Go 版本 clipboard.go 中的 GetFiles。
    /// </summary>
    /// <returns>文件路径数组，若剪贴板无文件则返回空数组。</returns>
    string[] GetFiles();

    /// <summary>
    /// 清空剪贴板内容。
    /// </summary>
    void Clear();
}
