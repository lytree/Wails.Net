namespace Wails.Net.Application.Hosting;

/// <summary>
/// 应用生命周期钩子集合（RH-HOST-02）。
/// <para>
/// 收敛 <see cref="Wails.Net.Application.Application"/> 中的两类用户钩子：
/// <list type="bullet">
/// <item>启动前 hook（<c>OnBeforeStart</c>）：在服务/传输层启动之前按注册顺序执行；</item>
/// <item>关闭任务（<c>OnShutdown</c>）：在关闭流程开始时执行，单个任务异常不影响其余任务。</item>
/// </list>
/// </para>
/// <para>
/// 对应 Wails v3 Go 版本 <c>application.go</c> 中的 <c>onStartup</c> / <c>shutdownTasks</c>。
/// </para>
/// </summary>
public sealed class ApplicationLifecycleHooks
{
    private readonly List<Action> _shutdownTasks = [];
    private readonly List<Action<Wails.Net.Application.Application>> _beforeStartHooks = [];

    /// <summary>
    /// 注册关闭任务。
    /// </summary>
    /// <param name="task">关闭时执行的操作，不能为 null。</param>
    public void AddShutdownTask(Action task)
    {
        ArgumentNullException.ThrowIfNull(task);
        _shutdownTasks.Add(task);
    }

    /// <summary>
    /// 批量注册关闭任务（构造期从 <see cref="Options.ApplicationOptions.ShutdownTasks"/> 合并）。
    /// </summary>
    /// <param name="tasks">关闭任务集合；为 null 时忽略。</param>
    public void AddShutdownTasks(IEnumerable<Action>? tasks)
    {
        if (tasks is null)
        {
            return;
        }

        _shutdownTasks.AddRange(tasks);
    }

    /// <summary>
    /// 注册启动前 hook。
    /// </summary>
    /// <param name="hook">hook 动作，参数为当前应用实例，不能为 null。</param>
    public void AddBeforeStartHook(Action<Wails.Net.Application.Application> hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        _beforeStartHooks.Add(hook);
    }

    /// <summary>
    /// 按注册顺序执行所有启动前 hook。
    /// 异常向上抛出（启动阶段失败应当中断启动，与抽取前行为一致）。
    /// </summary>
    /// <param name="application">当前应用实例。</param>
    public void RunBeforeStartHooks(Wails.Net.Application.Application application)
    {
        ArgumentNullException.ThrowIfNull(application);

        foreach (var hook in _beforeStartHooks)
        {
            hook(application);
        }
    }

    /// <summary>
    /// 执行所有关闭任务。单个任务抛出的异常被吞掉，以保证关闭流程继续。
    /// </summary>
    public void RunShutdownTasks()
    {
        foreach (var task in _shutdownTasks)
        {
            try
            {
                task();
            }
            catch
            {
                // 关闭任务中的异常不应中断关闭流程（与抽取前一致）。
            }
        }
    }

    /// <summary>
    /// 已注册的启动前 hook 数量（诊断/测试用）。
    /// </summary>
    public int BeforeStartHookCount => _beforeStartHooks.Count;

    /// <summary>
    /// 已注册的关闭任务数量（诊断/测试用）。
    /// </summary>
    public int ShutdownTaskCount => _shutdownTasks.Count;
}
