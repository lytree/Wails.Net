namespace Wails.Net.Application.Plugins.Mobile;

/// <summary>
/// 平台 NFC 抽象接口。
/// 对应 Tauri v2 的 <c>@tauri-apps/plugin-nfc</c>。
/// <para>
/// Android 实现委托到 <c>Android.Nfc</c> API；
/// 桌面 / Server 模式下为降级实现（<see cref="NfcPlugin.NullNfcImpl"/>，返回空字符串）。
/// </para>
/// </summary>
public interface IPlatformNfc
{
    /// <summary>
    /// 读取 NFC 标签数据。
    /// </summary>
    /// <returns>读取到的字符串数据；无数据时返回空字符串。</returns>
    Task<string> ReadAsync();

    /// <summary>
    /// 向 NFC 标签写入数据。
    /// </summary>
    /// <param name="data">要写入的字符串数据。</param>
    /// <returns>表示异步写入操作的任务。</returns>
    Task WriteAsync(string data);

    /// <summary>
    /// 取消正在进行的 NFC 操作。
    /// </summary>
    void Cancel();
}
