namespace Wails.Net.Application.Plugins.Mobile;

/// <summary>
/// 平台条码扫描抽象接口。
/// 对应 Tauri v2 的 <c>@tauri-apps/plugin-barcode-scanner</c>。
/// <para>
/// Android 实现委托到 <c>Android.Intent</c> + 相机 API；
/// 桌面 / Server 模式下为降级实现（<see cref="BarcodeScannerPlugin.NullBarcodeScannerImpl"/>，返回空字符串）。
/// </para>
/// </summary>
public interface IPlatformBarcodeScanner
{
    /// <summary>
    /// 启动条码扫描，返回扫描结果。
    /// </summary>
    /// <returns>扫描到的条码字符串；无结果时返回空字符串。</returns>
    Task<string> ScanAsync();

    /// <summary>
    /// 取消正在进行的扫描。
    /// </summary>
    void Cancel();
}
