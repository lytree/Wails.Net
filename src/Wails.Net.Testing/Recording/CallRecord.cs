namespace Wails.Net.Testing.Recording;

/// <summary>
/// 单次 Mock 方法调用记录。
/// <para>
/// 对标 Tauri v2 <c>tauri::test</c> MockRuntime 的调用捕获能力：
/// 测试可以断言"某个平台方法是否被调用、以什么参数、调用了几次"，
/// 而不需要真实 GUI 环境。
/// </para>
/// </summary>
/// <param name="Sequence">调用序号（同一个 <see cref="CallRecorder"/> 内单调递增，从 1 开始）。</param>
/// <param name="Member">被调用的成员名称（通常通过 <c>nameof</c> 传入）。</param>
/// <param name="Arguments">调用参数快照，无参数时为空数组。</param>
/// <param name="TimestampUtc">调用发生的 UTC 时间。</param>
public readonly record struct CallRecord(
    long Sequence,
    string Member,
    IReadOnlyList<object?> Arguments,
    DateTime TimestampUtc)
{
    /// <summary>
    /// 返回便于阅读的调用描述，形如 <c>#3 SetTitle("hello")</c>。
    /// </summary>
    /// <returns>调用描述字符串。</returns>
    public override string ToString()
    {
        var args = string.Join(", ", Arguments.Select(FormatArgument));
        return $"#{Sequence} {Member}({args})";
    }

    /// <summary>
    /// 将单个参数格式化为便于阅读的字符串。
    /// </summary>
    /// <param name="value">参数值。</param>
    /// <returns>格式化后的字符串。</returns>
    private static string FormatArgument(object? value) => value switch
    {
        null => "null",
        string s => $"\"{s}\"",
        byte[] bytes => $"byte[{bytes.Length}]",
        System.Collections.IEnumerable enumerable and not string
            => $"[{string.Join(", ", enumerable.Cast<object?>().Select(FormatArgument))}]",
        _ => value.ToString() ?? string.Empty
    };
}
