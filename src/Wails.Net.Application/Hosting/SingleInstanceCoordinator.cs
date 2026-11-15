namespace Wails.Net.Application.Hosting;

/// <summary>
/// 单实例协调器（RH-HOST-02）。
/// <para>
/// 对应 Wails v3 Go 版本 <c>application.go</c> 中的 <c>SingleInstance</c> 处理：
/// 首实例获取平台锁；非首实例把命令行参数转交已运行实例后立即退出。
/// </para>
/// <para>
/// 从 <see cref="Wails.Net.Application.Application.Run"/> 抽出，使"进程级唯一性"成为
/// 可单测的独立单元（平台锁的获取通过 <see cref="Platform.IPlatformApp"/> 注入）。
/// </para>
/// </summary>
public sealed class SingleInstanceCoordinator
{
    private readonly Options.ApplicationOptions _options;
    private readonly Action<int> _exitProcess;

    /// <summary>
    /// 构造单实例协调器。
    /// </summary>
    /// <param name="options">应用选项（读取 <see cref="Options.ApplicationOptions.SingleInstance"/> 等）。</param>
    /// <param name="exitProcess">退出进程动作，默认 <see cref="System.Environment.Exit(int)"/>；测试可注入。</param>
    public SingleInstanceCoordinator(Options.ApplicationOptions options, Action<int>? exitProcess = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        _exitProcess = exitProcess ?? System.Environment.Exit;
    }

    /// <summary>
    /// 当前进程是否为首实例；未启用单实例模式下恒为 true。
    /// </summary>
    public bool IsFirstInstance { get; private set; } = true;

    /// <summary>
    /// 执行单实例检查。
    /// <para>
    /// 未启用 <see cref="Options.ApplicationOptions.SingleInstance"/> 时直接返回；
    /// 启用且平台返回"非首实例"时，把命令行参数转交给已运行实例，然后以
    /// <see cref="Options.ApplicationOptions.SingleInstanceExitCode"/> 退出当前进程（不返回）。
    /// </para>
    /// </summary>
    /// <param name="platformApp">平台应用实例；为 null 时视为首实例（无法获取平台锁）。</param>
    public void Acquire(Platform.IPlatformApp? platformApp)
    {
        if (!_options.SingleInstance)
        {
            return;
        }

        var uniqueId = _options.SingleInstanceUniqueID ?? _options.Name;

        if (platformApp is not null)
        {
            IsFirstInstance = platformApp.AcquireSingleInstanceLock(uniqueId);
        }

        if (IsFirstInstance)
        {
            return;
        }

        // 非首实例：通知已运行的前一个实例后退出。
        var args = System.Environment.GetCommandLineArgs();
        platformApp?.NotifySingleInstance(args);
        _exitProcess(_options.SingleInstanceExitCode);
    }
}
