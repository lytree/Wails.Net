using System.Collections.Concurrent;

namespace Wails.Net.Testing.Recording;

/// <summary>
/// 线程安全的调用记录器，收集 Mock 实现上发生的方法调用。
/// <para>
/// 遵循 AGENTS.md §3.2 线程安全约定：序号使用 <see cref="Interlocked.Increment(ref long)"/> 生成，
/// 记录存储使用 <see cref="ConcurrentQueue{T}"/>，可安全用于多线程测试场景。
/// </para>
/// </summary>
public sealed class CallRecorder
{
    /// <summary>
    /// 已记录的调用队列。
    /// </summary>
    private readonly ConcurrentQueue<CallRecord> _records = new();

    /// <summary>
    /// 调用序号生成器（线程安全）。
    /// </summary>
    private long _sequence;

    /// <summary>
    /// 空参数数组的共享实例，避免每次无参调用都分配。
    /// </summary>
    private static readonly object?[] EmptyArguments = [];

    /// <summary>
    /// 记录一次方法调用。
    /// </summary>
    /// <param name="member">成员名称，建议通过 <c>nameof</c> 传入。</param>
    /// <param name="arguments">调用参数，可为空。</param>
    /// <returns>本次调用生成的记录。</returns>
    public CallRecord Record(string member, params object?[] arguments)
    {
        var record = new CallRecord(
            Interlocked.Increment(ref _sequence),
            member,
            arguments.Length == 0 ? EmptyArguments : arguments,
            DateTime.UtcNow);
        _records.Enqueue(record);
        return record;
    }

    /// <summary>
    /// 获取当前所有调用记录的快照（按调用顺序）。
    /// </summary>
    /// <returns>调用记录只读列表。</returns>
    public IReadOnlyList<CallRecord> Snapshot() => _records.ToArray();

    /// <summary>
    /// 获取指定成员的所有调用记录。
    /// </summary>
    /// <param name="member">成员名称。</param>
    /// <returns>该成员的调用记录列表，未调用过时为空列表。</returns>
    public IReadOnlyList<CallRecord> CallsTo(string member) =>
        _records.Where(r => string.Equals(r.Member, member, StringComparison.Ordinal)).ToArray();

    /// <summary>
    /// 获取指定成员被调用的次数。
    /// </summary>
    /// <param name="member">成员名称。</param>
    /// <returns>调用次数。</returns>
    public int CountOf(string member) =>
        _records.Count(r => string.Equals(r.Member, member, StringComparison.Ordinal));

    /// <summary>
    /// 判断指定成员是否被调用过至少一次。
    /// </summary>
    /// <param name="member">成员名称。</param>
    /// <returns>被调用过返回 true。</returns>
    public bool WasCalled(string member) => CountOf(member) > 0;

    /// <summary>
    /// 清空所有调用记录（序号不重置，便于区分清空前后的调用）。
    /// </summary>
    public void Clear() => _records.Clear();

    /// <summary>
    /// 返回全部调用记录的多行文本，便于断言失败时排查。
    /// </summary>
    /// <returns>调用记录文本。</returns>
    public override string ToString() =>
        _records.IsEmpty ? "(无调用记录)" : string.Join(Environment.NewLine, _records.Select(r => r.ToString()));
}
