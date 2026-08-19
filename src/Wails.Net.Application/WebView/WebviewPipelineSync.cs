using Microsoft.Extensions.Logging;

namespace Wails.Net.Application.Webview;

/// <summary>
/// WebView 回调同步上下文下的安全同步包装（R2-4 融合审计）。
/// <para>
/// WebView 平台回调（<c>WebResourceRequested</c>、<c>ShouldInterceptRequest</c>、
/// <c>URISchemeRequest</c>、<c>JavascriptInterface.Invoke</c> 等）是同步 API：
/// 必须在当前调用线程上立即返回响应，无法直接 await。
/// </para>
/// <para>
/// 直接调用 <c>task.GetAwaiter().GetResult()</c> 会阻塞调用线程等待任务完成，
/// 若任务内部 <c>await</c> 试图返回到调用线程的 <see cref="SynchronizationContext"/>（如 Linux GTK MainLoop、
/// macOS RunLoop、Android 主线程的 UI dispatcher），则会发生死锁。
/// </para>
/// <para>
/// 本扩展使用 <see cref="Task.Run"/> 强制把工作推入线程池，
/// 同时 <see cref="WebviewRequestPipeline"/> 内部所有 <c>await</c> 都使用
/// <see cref="Task.ConfigureAwait(bool)"/><c>(false)</c> 不捕获同步上下文，
/// 因此线程池线程可以独立完成全部工作并把结果返回给阻塞的调用线程，**不会死锁**。
/// </para>
/// <para>
/// 异常会被捕获并转换为 <see cref="WebviewResponse.InternalError"/>，
/// 调用方无需额外 try/catch。
/// </para>
/// <para>
/// 对应 docs/architecture/fusion-architecture-audit.md §3.7 / R2-4：消除 UI 回调的同步阻塞死锁风险。
/// </para>
/// </summary>
public static class WebviewPipelineSync
{
    /// <summary>
    /// 同步执行 pipeline 任务（在线程池线程上启动，调用线程安全阻塞等待）。
    /// </summary>
    /// <param name="task">要同步执行的 pipeline 任务。</param>
    /// <returns>响应对象；异常时返回 500 响应。</returns>
    public static WebviewResponse RunOnThreadPool(this Task<WebviewResponse> task)
    {
        ArgumentNullException.ThrowIfNull(task);
        try
        {
            return Task.Run(async () => await task.ConfigureAwait(false)).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            return WebviewResponse.InternalError("请求已取消");
        }
        catch (Exception ex)
        {
            return WebviewResponse.InternalError(ex.Message);
        }
    }

    /// <summary>
    /// 同步执行 pipeline 任务并记录异常日志。
    /// </summary>
    /// <param name="task">要同步执行的 pipeline 任务。</param>
    /// <param name="logger">日志记录器；可为 null。</param>
    /// <returns>响应对象；异常时返回 500 响应。</returns>
    public static WebviewResponse RunOnThreadPool(this Task<WebviewResponse> task, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(task);
        try
        {
            return Task.Run(async () => await task.ConfigureAwait(false)).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            return WebviewResponse.InternalError("请求已取消");
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "WebviewRequestPipeline 同步执行失败");
            return WebviewResponse.InternalError(ex.Message);
        }
    }
}
