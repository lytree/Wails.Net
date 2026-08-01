using System.Collections.Concurrent;

namespace LyboxApp.Services.Core;

/// <summary>
/// 任务注册表。跟踪长时间运行的任务（下载、导入等）并向前端广播进度事件。
/// 对应 LYBox 的 ITaskRegistry 功能项。
/// </summary>
public class TaskRegistry
{
    private readonly ConcurrentDictionary<string, TaskInfo> _tasks = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cts = new();
    private readonly LyboxEventBus _bus;

    /// <summary>
    /// 初始化任务注册表。
    /// </summary>
    /// <param name="bus">事件总线。</param>
    public TaskRegistry(LyboxEventBus bus)
    {
        _bus = bus;
    }

    /// <summary>开始一个任务，返回任务信息（已广播）。</summary>
    public TaskInfo Start(string name)
    {
        var t = new TaskInfo
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Name = name,
            StartedAt = DateTime.Now,
        };
        _tasks[t.Id] = t;
        _cts[t.Id] = new CancellationTokenSource();
        Notify(t);
        return t;
    }

    /// <summary>获取任务的取消令牌。</summary>
    public CancellationToken Token(string id) =>
        _cts.TryGetValue(id, out var c) ? c.Token : CancellationToken.None;

    /// <summary>更新进度。</summary>
    public void Update(string id, double progress, string? detail = null)
    {
        if (_tasks.TryGetValue(id, out var t))
        {
            t.Progress = progress;
            if (detail is not null)
            {
                t.Detail = detail;
            }

            Notify(t);
        }
    }

    /// <summary>完成任务。</summary>
    public void Finish(string id, string status = "done", string? detail = null)
    {
        if (_tasks.TryGetValue(id, out var t))
        {
            t.Status = status;
            if (status == "done")
            {
                t.Progress = 100;
            }

            if (detail is not null)
            {
                t.Detail = detail;
            }

            Notify(t);
        }

        _cts.TryRemove(id, out _);
    }

    /// <summary>取消任务（触发对应 CancellationToken）。</summary>
    public void Cancel(string id)
    {
        if (_cts.TryGetValue(id, out var c))
        {
            try
            {
                c.Cancel();
            }
            catch
            {
                // 已取消
            }
        }

        if (_tasks.TryGetValue(id, out var t))
        {
            t.Status = "canceled";
            Notify(t);
        }
    }

    /// <summary>当前任务列表（按开始时间倒序）。</summary>
    public IReadOnlyList<TaskInfo> List() =>
        _tasks.Values.OrderByDescending(t => t.StartedAt).ToList();

    /// <summary>按 Id 获取任务。</summary>
    public TaskInfo? Get(string id) => _tasks.TryGetValue(id, out var t) ? t : null;

    private void Notify(TaskInfo t)
    {
        // 使用字典固定键名，避免事件序列化命名策略差异
        var payload = new Dictionary<string, object?>
        {
            ["id"] = t.Id,
            ["name"] = t.Name,
            ["status"] = t.Status,
            ["progress"] = t.Progress,
            ["detail"] = t.Detail,
            ["startedAt"] = t.StartedAt,
        };
        _bus.Emit("lybox:task-update", payload);
    }
}
