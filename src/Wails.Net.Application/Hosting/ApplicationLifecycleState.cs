namespace Wails.Net.Application.Hosting;

/// <summary>
/// 应用生命周期状态（OPT-RUNTIME-002，2026-08-15 新增）。
/// <para>
/// 显式建模 Host/UI/Window/Runtime/IPC 就绪阶段，避免调用方误用
/// <see cref="IHostedService.StartAsync"/> 的返回语义（其仅表示初始化完成，不等价于 UI 就绪）。
/// 对应优化路线图 §5.3 的生命周期阶段定义：
/// HostStarted → PlatformStarted → FirstWindowCreated → RuntimeInjected → IpcReady → AppReady。
/// </para>
/// </summary>
public enum ApplicationLifecycleState
{
    /// <summary>
    /// 应用对象已创建（<see cref="Wails.Net.Application.Application"/> 构造完成），尚未运行。
    /// </summary>
    Created,

    /// <summary>
    /// 应用开始运行（<see cref="Wails.Net.Application.Application.Run"/> 已进入启动流程）。
    /// </summary>
    Starting,

    /// <summary>
    /// 平台消息循环已启动（<see cref="Wails.Net.Application.Application.Run"/> 即将进入
    /// <c>IPlatformApp.Run</c> 阻塞主循环；窗口可安全创建）。
    /// 这是 <see cref="Wails.Net.Application.Hosting.DesktopHostedService.StartAsync"/> 等待的就绪信号。
    /// </summary>
    PlatformStarted,

    /// <summary>
    /// 平台主循环运行中（用户可感知应用已就绪）。
    /// </summary>
    Running,

    /// <summary>
    /// 应用正在停止（<see cref="Wails.Net.Application.Application.Shutdown"/> 已触发）。
    /// </summary>
    Stopping,

    /// <summary>
    /// 应用已停止。
    /// </summary>
    Stopped,
}
