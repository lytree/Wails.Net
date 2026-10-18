using System.Net.Sockets;

namespace Wails.Net.Cli.Build;

/// <summary>
/// 前端开发服务器 HTTP 就绪探测。
/// <para>
/// 对应 Wails v3 beta.28 的 <c>internal/commands/watcher.go</c>：<c>applyFrontendReadiness</c>
/// 为 <c>wails3 task common:dev:frontend</c> 后台步骤注入
/// <c>process.Readiness{HTTP: frontendURL, Timeout: "60s"}</c>，使 <c>wails dev</c> 在启动应用
/// 之前先等待前端 dev server 就绪（v3.0.0-beta.26 / PR #6150）。
/// 探测目标 URL 对应 <c>internal/commands/dev.go</c> 写入、<c>watcher.go</c> 读取的
/// <c>FRONTEND_DEVSERVER_URL</c> 环境变量。
/// </para>
/// <para>
/// 就绪判定：HTTP 2xx/3xx 视为就绪；连接被拒、请求超时、4xx/5xx 均视为未就绪并继续轮询，
/// 直至达到总超时。<see cref="WaitForReadyAsync"/> 的探测动作以委托注入，便于单元测试。
/// </para>
/// </summary>
internal static class FrontendDevServerReadiness
{
    /// <summary>默认就绪等待总超时（60 秒），对齐 Go 的 <c>Readiness.Timeout = "60s"</c>。</summary>
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    /// <summary>默认轮询间隔（250 毫秒），通过 <see cref="Task.Delay(TimeSpan, CancellationToken)"/> 避免忙等。</summary>
    internal static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>单次探测请求的超时上限（5 秒），避免单次请求耗尽总超时预算。</summary>
    internal static readonly TimeSpan DefaultPerAttemptTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 单次探测结果状态分类（对应“连接被拒 / 超时 / 5xx 分别处理”的要求）。
    /// </summary>
    internal enum ProbeStatus
    {
        /// <summary>服务已就绪（HTTP 2xx/3xx）。</summary>
        Ready,

        /// <summary>连接被拒绝（服务尚未监听端口）。</summary>
        ConnectionRefused,

        /// <summary>单次探测请求超时。</summary>
        Timeout,

        /// <summary>服务返回 5xx（已监听但响应错误）。</summary>
        ServerError,

        /// <summary>服务返回 4xx（已监听但请求被拒）。</summary>
        ClientError,

        /// <summary>其它连接错误（DNS、网络不可达等）。</summary>
        OtherError,
    }

    /// <summary>
    /// 单次探测结果。
    /// </summary>
    /// <param name="Status">状态分类。</param>
    /// <param name="Detail">细节描述（如 <c>HTTP 200</c> 或异常消息），用于失败诊断。</param>
    internal readonly record struct ProbeResult(ProbeStatus Status, string Detail)
    {
        /// <summary>是否已就绪（等价于 <see cref="ProbeStatus.Ready"/>）。</summary>
        internal bool IsReady => Status == ProbeStatus.Ready;
    }

    /// <summary>
    /// 就绪等待的最终结果。
    /// </summary>
    /// <param name="Ready">是否在超时前就绪。</param>
    /// <param name="Message">结果描述（就绪时为成功详情，失败时为超时诊断信息）。</param>
    /// <param name="LastStatus">最后一次探测的状态分类。</param>
    /// <param name="Attempts">实际探测次数。</param>
    internal sealed record ReadinessWaitResult(bool Ready, string Message, ProbeStatus LastStatus, int Attempts);

    /// <summary>
    /// 轮询等待前端 dev server 就绪。
    /// <para>
    /// 委托 <paramref name="probe"/> 返回单次探测结果；当其抛出的
    /// <see cref="OperationCanceledException"/> 由 <paramref name="cancellationToken"/> 引起时向外传播，
    /// 由总超时引起时记为该次未就绪并最终返回失败结果。
    /// </para>
    /// </summary>
    /// <param name="probe">单次探测委托（应自行捕获网络异常并转换为 <see cref="ProbeResult"/>）。</param>
    /// <param name="timeout">总超时；非正值表示不设总超时。</param>
    /// <param name="pollInterval">轮询间隔；负值按 0 处理。</param>
    /// <param name="cancellationToken">外部取消令牌（如 Ctrl+C），触发时抛出 <see cref="OperationCanceledException"/>。</param>
    /// <returns>就绪等待结果。</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> 被取消时抛出。</exception>
    internal static async Task<ReadinessWaitResult> WaitForReadyAsync(
        Func<CancellationToken, Task<ProbeResult>> probe,
        TimeSpan timeout,
        TimeSpan pollInterval,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(probe);

        if (pollInterval < TimeSpan.Zero)
        {
            pollInterval = TimeSpan.Zero;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout > TimeSpan.Zero)
        {
            timeoutCts.CancelAfter(timeout);
        }

        var attempts = 0;
        var last = new ProbeResult(ProbeStatus.Timeout, "尚未开始探测");

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            attempts++;
            try
            {
                last = await probe(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 外部取消（Ctrl+C）向外传播；总超时取消在此归为“未就绪”。
                throw;
            }
            catch (OperationCanceledException)
            {
                last = new ProbeResult(ProbeStatus.Timeout, "就绪等待超时");
            }

            if (last.IsReady)
            {
                return new ReadinessWaitResult(true, last.Detail, last.Status, attempts);
            }

            if (timeoutCts.IsCancellationRequested)
            {
                return new ReadinessWaitResult(false, BuildTimeoutMessage(last, timeout), last.Status, attempts);
            }

            try
            {
                await Task.Delay(pollInterval, timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return new ReadinessWaitResult(false, BuildTimeoutMessage(last, timeout), last.Status, attempts);
            }
        }
    }

    /// <summary>
    /// 执行单次 HTTP 探测（GET 请求）。
    /// 对应 Go 的 <c>process.Readiness{HTTP: url}</c> 就绪检查。
    /// </summary>
    /// <param name="client">HTTP 客户端（调用方应将其 <c>Timeout</c> 设为无限，改由本方法的单次超时控制）。</param>
    /// <param name="url">探测目标 URL。</param>
    /// <param name="perAttemptTimeout">单次请求超时；非正值表示不限。</param>
    /// <param name="cancellationToken">外部取消令牌。</param>
    /// <returns>单次探测结果；网络异常转换为对应的 <see cref="ProbeStatus"/>，不向外抛出。</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> 被取消时抛出。</exception>
    internal static async Task<ProbeResult> ProbeHttpOnceAsync(
        HttpClient client,
        Uri url,
        TimeSpan perAttemptTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(url);

        using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (perAttemptTimeout > TimeSpan.Zero)
        {
            attemptCts.CancelAfter(perAttemptTimeout);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, attemptCts.Token)
                .ConfigureAwait(false);

            var code = (int)response.StatusCode;
            if (code is >= 200 and < 400)
            {
                return new ProbeResult(ProbeStatus.Ready, $"HTTP {code}");
            }

            return code >= 500
                ? new ProbeResult(ProbeStatus.ServerError, $"HTTP {code}")
                : new ProbeResult(ProbeStatus.ClientError, $"HTTP {code}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 外部取消向外传播，不与单次请求超时混淆。
            throw;
        }
        catch (OperationCanceledException)
        {
            return new ProbeResult(ProbeStatus.Timeout, "请求超时");
        }
        catch (HttpRequestException ex) when (ex.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionRefused })
        {
            return new ProbeResult(ProbeStatus.ConnectionRefused, "连接被拒绝");
        }
        catch (HttpRequestException ex)
        {
            return new ProbeResult(ProbeStatus.OtherError, ex.Message);
        }
    }

    /// <summary>
    /// 构造超时失败信息（含最后状态与细节，便于诊断）。
    /// </summary>
    /// <param name="last">最后一次探测结果。</param>
    /// <param name="timeout">总超时。</param>
    /// <returns>中文失败描述。</returns>
    private static string BuildTimeoutMessage(ProbeResult last, TimeSpan timeout)
        => $"等待 {timeout.TotalSeconds:0.#}s 超时，最后一次探测：{DescribeStatus(last.Status)}（{last.Detail}）";

    /// <summary>
    /// 将状态分类转换为中文描述。
    /// </summary>
    /// <param name="status">状态分类。</param>
    /// <returns>中文描述。</returns>
    internal static string DescribeStatus(ProbeStatus status) => status switch
    {
        ProbeStatus.Ready => "已就绪",
        ProbeStatus.ConnectionRefused => "连接被拒绝",
        ProbeStatus.Timeout => "请求超时",
        ProbeStatus.ServerError => "服务返回 5xx",
        ProbeStatus.ClientError => "服务返回 4xx",
        _ => "连接失败",
    };
}
