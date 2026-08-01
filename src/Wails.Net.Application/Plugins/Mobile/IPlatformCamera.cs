namespace Wails.Net.Application.Plugins.Mobile;

/// <summary>
/// 平台相机抽象接口。
/// 对应 Tauri v2 的 <c>@tauri-apps/plugin-camera</c>。
/// <para>
/// Android 实现委托到 <c>AndroidX.Camera</c> 或 <c>MediaStore.ACTION_IMAGE_CAPTURE</c> Intent；
/// 桌面 / Server 模式下为降级实现（<see cref="CameraPlugin.NullCameraImpl"/>，返回空字节数组）。
/// </para>
/// </summary>
public interface IPlatformCamera
{
    /// <summary>
    /// 检查相机硬件是否可用。
    /// </summary>
    /// <returns>
    /// 可用性状态字符串，对齐 Tauri v2：<c>available</c> / <c>none</c> / <c>denied</c>。
    /// </returns>
    string CheckAvailability();

    /// <summary>
    /// 启动相机拍照，返回 JPEG 字节数据。
    /// </summary>
    /// <param name="cancellationToken">取消令牌，用户取消或超时时触发。</param>
    /// <returns>JPEG 字节数组；失败或用户取消时返回空字节数组。</returns>
    Task<byte[]> CaptureAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 取消正在进行的拍照操作。
    /// </summary>
    void Cancel();
}

/// <summary>
/// camera.capture 命令的返回结果。
/// 对应 Tauri v2 <c>@tauri-apps/plugin-camera</c> 的返回结构。
/// </summary>
public sealed class CameraCaptureResult
{
    /// <summary>是否成功捕获图像。</summary>
    public bool Success { get; set; }

    /// <summary>Base64 编码的 JPEG 图像数据（成功时非空）。</summary>
    public string Base64Data { get; set; } = string.Empty;

    /// <summary>错误描述（失败时非空）。</summary>
    public string? Error { get; set; }
}
