using System.Diagnostics;
using System.Text;

namespace Wails.Net.Cli.Mcp;

/// <summary>
/// 有界文本缓冲：写入超过上限后丢弃多余内容并标记截断。
/// 对应 Wails v3 Go 版本 <c>internal/commands/mcp.go</c> 的 <c>cappedBuffer</c> 与 <c>mcpJob.Write</c>。
/// </summary>
internal sealed class CappedTextBuffer
{
    private readonly object _gate = new();
    private readonly StringBuilder _builder = new();
    private readonly int _capacity;

    /// <summary>
    /// 使用指定字符上限构造缓冲。
    /// </summary>
    /// <param name="capacity">字符上限。</param>
    public CappedTextBuffer(int capacity)
    {
        _capacity = capacity;
    }

    /// <summary>是否已发生截断。</summary>
    public bool Truncated { get; private set; }

    /// <summary>当前累计内容。</summary>
    public string Text
    {
        get
        {
            lock (_gate)
            {
                return _builder.ToString();
            }
        }
    }

    /// <summary>
    /// 追加文本；超出上限部分被丢弃。
    /// </summary>
    /// <param name="text">待追加文本。</param>
    public void Append(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        lock (_gate)
        {
            if (_builder.Length >= _capacity)
            {
                Truncated = true;
                return;
            }

            var take = Math.Min(text.Length, _capacity - _builder.Length);
            _builder.Append(text, 0, take);
            if (take < text.Length)
            {
                Truncated = true;
            }
        }
    }
}

/// <summary>
/// MCP 后台任务状态快照。
/// </summary>
/// <param name="JobId">任务 ID。</param>
/// <param name="State">任务状态：running / stopping / stopped / failed / completed。</param>
/// <param name="Path">任务工作目录。</param>
/// <param name="Output">有界输出。</param>
internal sealed record McpJobSnapshot(string JobId, string State, string Path, string Output);

/// <summary>
/// MCP 后台任务：包装一个 CLI 子进程，缓存有界输出并支持停止。
/// 对应 Wails v3 Go 版本 <c>internal/commands/mcp.go</c> 的 <c>mcpJob</c>。
/// </summary>
internal sealed class McpJob : IDisposable
{
    /// <summary>单个任务的最大输出字符数（约 2 MiB）。</summary>
    public const int MaxOutputCharacters = 2 * 1024 * 1024;

    private readonly object _gate = new();
    private readonly CappedTextBuffer _output = new(MaxOutputCharacters);
    private readonly CancellationTokenSource _cancellation;
    private Process? _process;

    /// <summary>
    /// 创建任务并为服务端生命周期建立取消链接。
    /// </summary>
    /// <param name="workingDirectory">工作目录。</param>
    /// <param name="lifetime">服务端生命周期令牌。</param>
    public McpJob(string workingDirectory, CancellationToken lifetime)
    {
        WorkingDirectory = workingDirectory;
        Id = McpToken.Generate();
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
    }

    /// <summary>任务 ID。</summary>
    public string Id { get; }

    /// <summary>任务工作目录。</summary>
    public string WorkingDirectory { get; }

    /// <summary>任务状态。</summary>
    public string State { get; private set; } = "running";

    /// <summary>任务完成时间；未完成时为 null。</summary>
    public DateTimeOffset? FinishedAt { get; private set; }

    /// <summary>任务取消令牌。</summary>
    public CancellationToken Token => _cancellation.Token;

    /// <summary>是否已结束（无论成功、失败或停止）。</summary>
    public bool IsFinished
    {
        get
        {
            lock (_gate)
            {
                return FinishedAt is not null;
            }
        }
    }

    /// <summary>
    /// 读取任务完成时间。
    /// </summary>
    /// <param name="finishedAt">完成时间。</param>
    /// <returns>任务已结束时为 <c>true</c>。</returns>
    public bool TryGetFinishedAt(out DateTimeOffset finishedAt)
    {
        lock (_gate)
        {
            if (FinishedAt is { } value)
            {
                finishedAt = value;
                return true;
            }
        }

        finishedAt = default;
        return false;
    }

    /// <summary>
    /// 绑定子进程，供停止时终止进程树。
    /// </summary>
    /// <param name="process">子进程。</param>
    public void Attach(Process process)
    {
        lock (_gate)
        {
            _process = process;
        }
    }

    /// <summary>
    /// 追加子进程输出（超过上限被截断）。
    /// </summary>
    /// <param name="text">输出文本。</param>
    public void Append(string? text) => _output.Append(text);

    /// <summary>
    /// 请求停止任务：状态置为 stopping，取消令牌并终止进程树。
    /// </summary>
    public void RequestStop()
    {
        Process? process;
        lock (_gate)
        {
            if (State == "running")
            {
                State = "stopping";
            }

            process = _process;
        }

        try
        {
            _cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 已完成任务重复停止时忽略
        }

        try
        {
            process?.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // 进程已退出或无权终止：状态由 Finish 决定
        }
    }

    /// <summary>
    /// 记录任务结束状态。
    /// </summary>
    /// <param name="canceled">是否因取消而结束。</param>
    /// <param name="exitCode">进程退出码。</param>
    public void Finish(bool canceled, int exitCode)
    {
        lock (_gate)
        {
            if (FinishedAt is not null)
            {
                return;
            }

            State = State == "stopping" || canceled
                ? "stopped"
                : exitCode == 0
                    ? "completed"
                    : "failed";
            FinishedAt = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// 生成状态快照。
    /// </summary>
    /// <returns>任务快照。</returns>
    public McpJobSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new McpJobSnapshot(Id, State, WorkingDirectory, _output.Text);
        }
    }

    /// <summary>
    /// 释放取消令牌。
    /// </summary>
    public void Dispose() => _cancellation.Dispose();
}

/// <summary>
/// MCP 后台任务表：限制并发/保留任务数量，满载时回收最旧的已结束任务。
/// 对应 Wails v3 Go 版本 <c>internal/commands/mcp.go</c> 的 <c>mcpJobs</c>。
/// </summary>
internal sealed class McpJobManager : IDisposable
{
    /// <summary>同时存在（运行中 + 保留）的任务上限。</summary>
    public const int MaxJobs = 32;

    private readonly object _gate = new();
    private readonly Dictionary<string, McpJob> _jobs = new(StringComparer.Ordinal);

    /// <summary>
    /// 注册任务：运行中任务达上限或无法回收旧任务时拒绝。
    /// </summary>
    /// <param name="job">待注册任务。</param>
    /// <returns>注册成功时为 <c>true</c>。</returns>
    public bool TryAdd(McpJob job)
    {
        lock (_gate)
        {
            if (ActiveCountLocked() >= MaxJobs)
            {
                return false;
            }

            while (_jobs.Count >= MaxJobs)
            {
                if (!ReapOldestFinishedLocked())
                {
                    return false;
                }
            }

            _jobs[job.Id] = job;
            return true;
        }
    }

    /// <summary>
    /// 按 ID 查询任务。
    /// </summary>
    /// <param name="id">任务 ID。</param>
    /// <returns>任务实例；不存在时为 null。</returns>
    public McpJob? Get(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        lock (_gate)
        {
            return _jobs.TryGetValue(id, out var job) ? job : null;
        }
    }

    /// <summary>
    /// 停止全部任务并释放资源。
    /// </summary>
    public void Dispose()
    {
        List<McpJob> jobs;
        lock (_gate)
        {
            jobs = _jobs.Values.ToList();
            _jobs.Clear();
        }

        foreach (var job in jobs)
        {
            job.RequestStop();
            job.Dispose();
        }
    }

    private int ActiveCountLocked() => _jobs.Values.Count(job => !job.IsFinished);

    private bool ReapOldestFinishedLocked()
    {
        string? oldestId = null;
        DateTimeOffset oldest = default;
        foreach (var (id, job) in _jobs)
        {
            if (!job.TryGetFinishedAt(out var finishedAt))
            {
                continue;
            }

            if (oldestId is null || finishedAt < oldest)
            {
                oldestId = id;
                oldest = finishedAt;
            }
        }

        if (oldestId is null)
        {
            return false;
        }

        if (_jobs.Remove(oldestId, out var removed))
        {
            removed.Dispose();
        }

        return true;
    }
}
