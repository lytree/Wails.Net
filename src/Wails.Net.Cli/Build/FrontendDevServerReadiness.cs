using System.Net.Sockets;

namespace Wails.Net.Cli.Build;

/// <summary>
/// 前端开发服务器 TCP 就绪探测。
/// <para>
/// 对应 Wails v3 <c>v3.0.0</c> 的 <c>v3/internal/commands/watcher.go</c>：
/// <c>applyFrontendReadiness</c> 为 <c>wails3 task common:dev:frontend</c> 后台步骤注入
/// <c>process.Readiness{TCP: host:port, Timeout: "60s"}</c>，使 <c>wails dev</c> 在启动应用
/// 之前先等待前端 dev server 就绪。
/// </para>
/// <para>
/// v3.0.0-beta.33（PR #6209）起由 HTTP 探测改为 <b>TCP 探测</b>：旧 HTTP 探测不带 <c>Accept</c>
/// 头且要求 <c>200</c>，Vite 等 dev server 会返回 <c>404</c>，导致 <c>wails dev</c> 卡满 60s。
/// 「端口可连接」已足以把主进程拦住，直到前端真正开始监听。
/// </para>
/// <para>
/// 就绪判定：TCP 连接成功即视为就绪；连接被拒、连接超时、其它 Socket 错误均视为未就绪并继续轮询，
/// 直至达到总超时。<see cref="WaitForReadyAsync"/> 的探测动作与 <see cref="ProbeTcpOnceAsync"/>
/// 的连接动作均以委托注入，便于单元测试。
/// </para>
/// </summary>
internal static class FrontendDevServerReadiness
{
    /// <summary>默认就绪等待总超时（60 秒），对齐 Go 的 <c>Readiness.Timeout = "60s"</c>。</summary>
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    /// <summary>默认轮询间隔（250 毫秒），通过 <see cref="Task.Delay(TimeSpan, CancellationToken)"/> 避免忙等。</summary>
    internal static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>单次探测连接的超时上限（5 秒），避免单次连接耗尽总超时预算。</summary>
    internal static readonly TimeSpan DefaultPerAttemptTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 单次探测结果状态分类。
    /// </summary>
    internal enum ProbeStatus
    {
        /// <summary>端口已接受 TCP 连接（服务已就绪）。</summary>
        Ready,

        /// <summary>连接被拒绝（服务尚未监听端口）。</summary>
        ConnectionRefused,

        /// <summary>单次连接超时。</summary>
        Timeout,

        /// <summary>其它连接错误（DNS 解析失败、网络不可达等）。</summary>
        OtherError,
    }

    /// <summary>
    /// 单次探测结果。
    /// </summary>
    /// <param name="Status">状态分类。</param>
    /// <param name="Detail">细节描述（如 <c>TCP localhost:5173 已接受连接</c> 或异常消息），用于失败诊断。</param>
    internal readonly record struct ProbeResult(ProbeStatus Status, string Detail)
    {
        /// <summary>是否已就绪（等价于 <see cref="ProbeStatus.Ready"/>）。</summary>
        internal bool IsReady => Status == ProbeStatus.Ready;
    }

    /// <summary>
    /// TCP 探测目标（主机 + 端口），由前端 dev server URL 解析而来。
    /// </summary>
    /// <param name="Host">主机名（如 <c>localhost</c>、<c>127.0.0.1</c>）。</param>
    /// <param name="Port">端口（URL 未显式指定时按 scheme 取 80/443）。</param>
    internal readonly record struct ProbeTarget(string Host, int Port)
    {
        /// <inheritdoc />
        public override string ToString() => $"{Host}:{Port}";
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
    /// 将前端 dev server URL 解析为 TCP 探测目标。
    /// 对应 Go <c>frontendTCPTarget</c>：仅接受不带片段（fragment）的绝对 HTTP(S) URL，
    /// 未显式指定端口时按 scheme 取 80/443。
    /// </summary>
    /// <param name="url">前端 dev server URL。</param>
    /// <param name="target">解析出的主机与端口。</param>
    /// <returns>URL 合法返回 true。</returns>
    internal static bool TryCreateTarget(Uri url, out ProbeTarget target)
    {
        ArgumentNullException.ThrowIfNull(url);

        target = default;
        if (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        if (string.IsNullOrEmpty(url.Host) || !string.IsNullOrEmpty(url.Fragment))
        {
            return false;
        }

        var port = url.Port;
        if (port <= 0)
        {
            port = url.Scheme == Uri.UriSchemeHttps ? 443 : 80;
        }

        target = new ProbeTarget(url.Host, port);
        return true;
    }

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
    /// 执行单次 TCP 探测（尝试连接并立即断开，不发送任何 HTTP 请求）。
    /// 对应 Go <c>process.Readiness{TCP: host:port}</c> 的就绪检查。
    /// </summary>
    /// <param name="target">探测目标（主机 + 端口）。</param>
    /// <param name="perAttemptTimeout">单次连接超时；非正值表示不限。</param>
    /// <param name="cancellationToken">外部取消令牌。</param>
    /// <param name="connect">
    /// 可选的连接委托（测试注入用，默认使用 <see cref="TcpClient"/> 真实连接）。
    /// 成功返回即视为就绪，抛出 <see cref="SocketException"/> 由本方法分类。
    /// </param>
    /// <returns>单次探测结果；网络异常转换为对应的 <see cref="ProbeStatus"/>，不向外抛出。</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> 被取消时抛出。</exception>
    internal static async Task<ProbeResult> ProbeTcpOnceAsync(
        ProbeTarget target,
        TimeSpan perAttemptTimeout,
        CancellationToken cancellationToken = default,
        Func<ProbeTarget, CancellationToken, Task>? connect = null)
    {
        using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (perAttemptTimeout > TimeSpan.Zero)
        {
            attemptCts.CancelAfter(perAttemptTimeout);
        }

        try
        {
            await (connect ?? ConnectTcpAsync)(target, attemptCts.Token).ConfigureAwait(false);
            return new ProbeResult(ProbeStatus.Ready, $"TCP {target} 已接受连接");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 外部取消向外传播，不与单次连接超时混淆。
            throw;
        }
        catch (OperationCanceledException)
        {
            return new ProbeResult(ProbeStatus.Timeout, $"连接 {target} 超时");
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
        {
            return new ProbeResult(ProbeStatus.ConnectionRefused, $"连接 {target} 被拒绝");
        }
        catch (SocketException ex)
        {
            return new ProbeResult(ProbeStatus.OtherError, ex.Message);
        }
    }

    /// <summary>
    /// 默认 TCP 连接动作：连接成功后立即关闭（就绪闸门不需要建立会话）。
    /// </summary>
    private static async Task ConnectTcpAsync(ProbeTarget target, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(target.Host, target.Port, cancellationToken).ConfigureAwait(false);
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
        ProbeStatus.Timeout => "连接超时",
        _ => "连接失败",
    };
}
