using System.Diagnostics;
using Wails.Net.Application.Bindings;

namespace Wails.Net.Demo.SingleInstance.Services;

/// <summary>
/// 启动记录，包含序列号、启动类型、进程 ID、命令行参数与时间戳。
/// </summary>
/// <param name="Index">序号（从 1 开始）。</param>
/// <param name="Kind">启动类型（first / second）。</param>
/// <param name="ProcessId">进程 ID。</param>
/// <param name="Args">命令行参数数组。</param>
/// <param name="Time">启动时间戳。</param>
public sealed record LaunchRecord(int Index, string Kind, int ProcessId, string[] Args, DateTime Time);

/// <summary>
/// 单实例演示服务，记录首实例与所有二次启动尝试的历史。
/// 通过 [Binding] 暴露给前端：查询历史、查询当前进程信息、清空历史。
/// </summary>
public sealed class SingleInstanceLogService
{
    /// <summary>
    /// 启动历史列表，使用锁保证线程安全。
    /// </summary>
    private readonly List<LaunchRecord> _records = new();

    /// <summary>
    /// 历史记录锁。
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// 自增序号，使用 Interlocked 保证线程安全。
    /// </summary>
    private int _nextIndex = 0;

    /// <summary>
    /// 记录一次启动事件。
    /// </summary>
    /// <param name="args">命令行参数。</param>
    /// <param name="kind">启动类型（first / second），默认根据是否首条记录判断。</param>
    public void RecordLaunch(string[] args, string? kind = null)
    {
        int index;
        lock (_lock)
        {
            index = ++_nextIndex;
            var actualKind = kind ?? (index == 1 ? "first" : "second");
            _records.Add(new LaunchRecord(
                index,
                actualKind,
                Environment.ProcessId,
                args ?? Array.Empty<string>(),
                DateTime.Now));
            if (_records.Count > 50)
            {
                _records.RemoveAt(0);
            }
        }
    }

    /// <summary>
    /// 获取启动历史记录列表的副本。
    /// </summary>
    /// <returns>启动记录列表。</returns>
    [Binding]
    public List<LaunchRecord> GetHistory()
    {
        lock (_lock)
        {
            return new List<LaunchRecord>(_records);
        }
    }

    /// <summary>
    /// 清空启动历史记录。
    /// </summary>
    [Binding]
    public void ClearHistory()
    {
        lock (_lock)
        {
            _records.Clear();
            _nextIndex = 0;
        }
    }

    /// <summary>
    /// 获取当前进程信息：进程 ID、主模块文件名、启动时间、命令行参数。
    /// </summary>
    /// <returns>包含进程信息的字典。</returns>
    [Binding]
    public Dictionary<string, object> GetCurrentProcessInfo()
    {
        var info = new Dictionary<string, object>
        {
            ["processId"] = Environment.ProcessId,
            ["commandLine"] = Environment.CommandLine,
            ["args"] = Environment.GetCommandLineArgs(),
            ["machineName"] = Environment.MachineName,
            ["userName"] = Environment.UserName,
            ["osVersion"] = Environment.OSVersion.VersionString,
            ["startTime"] = DateTime.Now,
        };

        try
        {
            using var proc = Process.GetCurrentProcess();
            info["mainModuleFileName"] = proc.MainModule?.FileName ?? string.Empty;
            info["startTime"] = proc.StartTime;
        }
        catch
        {
            // 部分平台访问 MainModule 可能抛出异常，忽略
            info["mainModuleFileName"] = "（无法获取）";
        }

        return info;
    }

    /// <summary>
    /// 获取启动次数统计。
    /// </summary>
    /// <returns>包含总次数、首实例次数、二次实例次数的字典。</returns>
    [Binding]
    public Dictionary<string, int> GetStats()
    {
        lock (_lock)
        {
            return new Dictionary<string, int>
            {
                ["total"] = _records.Count,
                ["first"] = _records.Count(r => r.Kind == "first"),
                ["second"] = _records.Count(r => r.Kind == "second"),
            };
        }
    }
}
