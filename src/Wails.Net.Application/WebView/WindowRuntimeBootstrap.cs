namespace Wails.Net.Application.Webview;

/// <summary>
/// 窗口运行时引导协调器（RH-IPC-02）。
/// <para>
/// 统一四平台「运行时注入」与「WindowRuntimeReady 派发」的语义。改造前各平台各写一套：
/// Windows 在 <c>NavigationCompleted</c> 派发、Linux 在 runtime 注入完成后立即派发（早于导航完成）、
/// macOS 在 <c>DidFinishNavigation</c> 派发、Android 在 <c>OnPageFinished</c> 派发，
/// 同一事件在四个平台上代表四种不同时刻，订阅方无法写出跨平台一致的初始化逻辑。
/// </para>
/// <para>
/// 统一后的语义：<b>导航完成 + 运行时注入完成，二者皆满足后派发</b>，且每个窗口生命周期内仅派发一次。
/// 平台只需在各自的原生回调里调用 <see cref="MarkNavigationCompleted"/> /
/// <see cref="MarkRuntimeInjected"/>，无需自行比对标志位。
/// </para>
/// <para>
/// <b>Android 特例</b>：其对 <c>wails.runtime.js</c> 的供给走资源拦截（<c>WailsWebViewClient</c>），
/// 不存在独立的 document-start 注入步骤，故构造时传 <c>runtimeInjectionRequired: false</c>，
/// 只等导航完成即派发，与改造前 <c>OnPageFinished</c> 的时机一致。
/// </para>
/// <para>
/// <b>线程安全</b>：状态位全部用 <see cref="Interlocked"/> 维护；原生回调可能来自 UI 线程与线程池，
/// <see cref="MarkNavigationCompleted"/> 与 <see cref="MarkRuntimeInjected"/> 的到达顺序不确定。
/// </para>
/// </summary>
public sealed class WindowRuntimeBootstrap
{
    private readonly uint _windowId;
    private readonly Action<uint> _dispatchReady;
    private readonly Func<string?> _scriptFactory;
    private readonly bool _runtimeInjectionRequired;

    /// <summary>脚本是否已被取走（保证一个窗口生命周期内只注册一次 document-start 脚本）。</summary>
    private int _scriptConsumed;

    /// <summary>运行时是否已注入完成（1 = 是）。</summary>
    private int _injected;

    /// <summary>导航是否已完成（1 = 是）。</summary>
    private int _navigationCompleted;

    /// <summary>WindowRuntimeReady 是否已派发（1 = 是）。</summary>
    private int _readyDispatched;

    /// <summary>
    /// 构造窗口运行时引导协调器。
    /// </summary>
    /// <param name="windowId">窗口 ID，派发 ready 事件时回传给 <paramref name="dispatchReady"/>。</param>
    /// <param name="dispatchReady">派发 WindowRuntimeReady 的委托（通常为
    /// <c>id =&gt; Application.Get()?.DispatchWindowEvent(id, (uint)WindowEventType.WindowRuntimeReady)</c>）。</param>
    /// <param name="scriptFactory">运行时脚本生成委托（通常为 <c>() =&gt; Application.Get()?.GenerateRuntimeJs(false)</c>）；
    /// 不需要注入的平台可传 null。</param>
    /// <param name="runtimeInjectionRequired">是否需要独立的 document-start 注入步骤。
    /// 为 false 时（Android）只等导航完成即派发。</param>
    public WindowRuntimeBootstrap(
        uint windowId,
        Action<uint> dispatchReady,
        Func<string?>? scriptFactory = null,
        bool runtimeInjectionRequired = true)
    {
        ArgumentNullException.ThrowIfNull(dispatchReady);

        if (runtimeInjectionRequired && scriptFactory is null)
        {
            throw new ArgumentNullException(nameof(scriptFactory), "需要运行时注入的平台必须提供脚本生成委托。");
        }

        _windowId = windowId;
        _dispatchReady = dispatchReady;
        _scriptFactory = scriptFactory ?? (static () => null);
        _runtimeInjectionRequired = runtimeInjectionRequired;

        // 无需注入的平台：注入条件从构造起即视为满足。
        _injected = runtimeInjectionRequired ? 0 : 1;
    }

    /// <summary>
    /// 获取运行时脚本，供平台以「document start」时机注册。
    /// <para>
    /// 仅在首次调用且脚本非空时返回 true；重复调用返回 false（保证只注册一次）。
    /// 脚本生成失败时返回 false 并允许后续重试。
    /// </para>
    /// </summary>
    /// <param name="script">输出的运行时脚本。</param>
    /// <returns>需要本次注册时返回 true。</returns>
    public bool TryGetScript(out string script)
    {
        script = string.Empty;

        if (!_runtimeInjectionRequired)
        {
            return false;
        }

        if (Interlocked.Exchange(ref _scriptConsumed, 1) != 0)
        {
            return false;
        }

        string? js;
        try
        {
            js = _scriptFactory();
        }
        catch
        {
            // 脚本生成失败视为未注入，允许后续导航时重试。
            js = null;
        }

        if (string.IsNullOrEmpty(js))
        {
            Volatile.Write(ref _scriptConsumed, 0);
            return false;
        }

        script = js;
        return true;
    }

    /// <summary>
    /// 标记运行时已注入完成（平台注册 document-start 脚本成功后调用）。
    /// </summary>
    public void MarkRuntimeInjected()
    {
        if (!_runtimeInjectionRequired)
        {
            // 无需注入的平台恒为已注入，忽略平台误调用。
            return;
        }

        Volatile.Write(ref _injected, 1);
        TryDispatchReady();
    }

    /// <summary>
    /// 标记导航完成（平台原生「页面加载完成」回调中调用）。
    /// </summary>
    public void MarkNavigationCompleted()
    {
        Volatile.Write(ref _navigationCompleted, 1);
        TryDispatchReady();
    }

    /// <summary>
    /// 重置状态，用于 WebView 控制器重建（如 Windows 浏览器进程故障恢复）：
    /// 新控制器需要重新注入脚本，并应重新派发一次 ready。
    /// </summary>
    public void ResetInjection()
    {
        Volatile.Write(ref _scriptConsumed, 0);
        Volatile.Write(ref _injected, _runtimeInjectionRequired ? 0 : 1);
        Volatile.Write(ref _navigationCompleted, 0);
        Volatile.Write(ref _readyDispatched, 0);
    }

    /// <summary>
    /// 两个前置条件均满足时派发 WindowRuntimeReady，且仅派发一次。
    /// </summary>
    private void TryDispatchReady()
    {
        if (Volatile.Read(ref _injected) != 1 || Volatile.Read(ref _navigationCompleted) != 1)
        {
            return;
        }

        if (Interlocked.Exchange(ref _readyDispatched, 1) != 0)
        {
            return;
        }

        try
        {
            _dispatchReady(_windowId);
        }
        catch
        {
            // ready 派发失败不应影响 WebView 生命周期；订阅方异常由事件处理器自行处理。
        }
    }
}
