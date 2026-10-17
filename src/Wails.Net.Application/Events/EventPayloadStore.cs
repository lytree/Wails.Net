using System.Security.Cryptography;

namespace Wails.Net.Application.Events;

/// <summary>
/// 超大事件载荷的宿主侧有界存储（事件投递硬化 P0-1 / Layer 3，对应 Wails v3 Go 版本
/// <c>v3/pkg/application/event_payload_store.go</c>，见 PR #5930）。
/// <para>
/// <b>解决的问题</b>：超过 <see cref="MaxInlinePayloadBytes"/> 字节的事件载荷若拼接进
/// <c>evaluateJavaScript</c> 源码，WebKit 会改用进程外共享内存传输，其中一个进程会一直
/// 持有这些内存区域（macOS 宿主进程在 100 ev/s × 1 MB 场景下可达 11.5 GB；Linux 则是
/// WebKitGTK 的 Web 进程膨胀）。本存储把大载荷停放在宿主侧，前端通过
/// <c>GET <see cref="PathPrefix"/>&lt;id&gt;</c> 一次性取回，从而完全不经过 eval 通道。
/// </para>
/// <para>
/// <b>语义（与 Go 版一致）</b>：
/// <list type="bullet">
/// <item>id 为 16 字节密码学随机数的 32 位小写十六进制字符串。</item>
/// <item>TTL <see cref="Ttl"/>（30s），清扫周期为其 1/4，避免边界处条目近乎加倍滞留。</item>
/// <item>总容量上限 <see cref="MaxStoreBytes"/>（64 MiB）；溢出时 <see cref="TryPut"/> 返回
/// false，调用方回退到内联投递（代价是恢复共享内存滞留，但优于丢弃或无限增长）。</item>
/// <item>读取即删（one-shot），并按窗口绑定：跨窗口或陈旧 id 取回失败。</item>
/// <item>窗口关闭时 <see cref="DropWindow"/> 立即释放该窗口的停放条目。</item>
/// </list>
/// </para>
/// <para>
/// <b>线程安全</b>：内部以单锁保护字典与字节计数，可在任意线程调用。
/// </para>
/// </summary>
internal sealed class EventPayloadStore : IDisposable
{
    /// <summary>
    /// 可直接内联进 eval 源码的最大事件 JSON 字节数。
    /// 8192 为三种引擎中膝点最低的 macOS（8–16 KB；WebKitGTK 为 64–128 KB，WebView2 无膝点）
    /// 实测 0% 滞留的最大值，因此单值即可在所有平台安全使用。
    /// </summary>
    public const int MaxInlinePayloadBytes = 8192;

    /// <summary>
    /// 事件载荷取回端点路径前缀（前端 fetch <c>&lt;前缀&gt;&lt;id&gt;</c>）。
    /// </summary>
    public const string PathPrefix = "/wails/eventpayload/";

    /// <summary>
    /// id 的十六进制长度（16 随机字节）。
    /// </summary>
    public const int IdLength = 32;

    /// <summary>
    /// 停放载荷的总字节上限（64 MiB）。
    /// </summary>
    private const int MaxStoreBytes = 64 * 1024 * 1024;

    /// <summary>
    /// 未取回载荷的存活时长。
    /// </summary>
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 清扫周期（<see cref="Ttl"/> 的 1/4）。
    /// </summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMilliseconds(7500);

    /// <summary>
    /// 保护 <see cref="_items"/> / <see cref="_bytes"/> / <see cref="_closed"/> / <see cref="_reaper"/> 的锁。
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    /// id → 停放载荷。
    /// </summary>
    private readonly Dictionary<string, ParkedPayload> _items = new(StringComparer.Ordinal);

    /// <summary>
    /// 当前停放字节总数。
    /// </summary>
    private int _bytes;

    /// <summary>
    /// 是否已关闭（关闭后拒绝新载荷）。
    /// </summary>
    private bool _closed;

    /// <summary>
    /// 清扫定时器（首次 <see cref="TryPut"/> 时懒启动）。
    /// </summary>
    private Timer? _reaper;

    /// <summary>
    /// 停放载荷记录。
    /// </summary>
    /// <param name="Data">载荷字节。</param>
    /// <param name="WindowId">绑定窗口 ID。</param>
    /// <param name="Created">停放时刻（UTC）。</param>
    private readonly record struct ParkedPayload(byte[] Data, uint WindowId, DateTimeOffset Created);

    /// <summary>
    /// 当前停放的载荷条数（用于观测与测试）。
    /// </summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _items.Count;
            }
        }
    }

    /// <summary>
    /// 当前停放的字节总数（用于观测与测试）。
    /// </summary>
    public int Bytes
    {
        get
        {
            lock (_gate)
            {
                return _bytes;
            }
        }
    }

    /// <summary>
    /// 停放一个载荷并返回其 id。
    /// 存储已关闭或已满时返回 false，调用方必须回退到内联投递。
    /// </summary>
    /// <param name="windowId">载荷绑定的目标窗口 ID。</param>
    /// <param name="data">载荷字节（UTF-8 JSON）。</param>
    /// <param name="id">成功时输出 id（32 位小写十六进制）。</param>
    /// <returns>成功停放返回 true。</returns>
    public bool TryPut(uint windowId, byte[] data, out string id)
    {
        ArgumentNullException.ThrowIfNull(data);

        id = string.Empty;
        if (data.Length == 0)
        {
            return false;
        }

        var generated = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

        lock (_gate)
        {
            // 关闭后拒绝：清扫器已停止，此时停放将永远无法回收。
            if (_closed || _bytes + data.Length > MaxStoreBytes)
            {
                return false;
            }

            _items[generated] = new ParkedPayload(data, windowId, DateTimeOffset.UtcNow);
            _bytes += data.Length;

            // 懒启动清扫器。
            _reaper ??= new Timer(Reap, null, SweepInterval, SweepInterval);
        }

        id = generated;
        return true;
    }

    /// <summary>
    /// 一次性取回并删除指定 id 的载荷。
    /// <paramref name="windowId"/> 非 0 时必须与载荷绑定的窗口一致；
    /// 传 0 表示跳过窗口校验（供不携带窗口标识的平台使用）。
    /// </summary>
    /// <param name="id">载荷 id。</param>
    /// <param name="windowId">请求方窗口 ID；0 表示跳过校验。</param>
    /// <returns>载荷字节；不存在、窗口不匹配或已取回时返回 null。</returns>
    public byte[]? Take(string id, uint windowId)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        lock (_gate)
        {
            if (!_items.TryGetValue(id, out var item))
            {
                return null;
            }

            if (windowId != 0 && item.WindowId != windowId)
            {
                return null;
            }

            _items.Remove(id);
            _bytes -= item.Data.Length;
            return item.Data;
        }
    }

    /// <summary>
    /// 丢弃指定窗口停放的载荷（窗口销毁时调用，避免等待 TTL 到期）。
    /// </summary>
    /// <param name="windowId">窗口 ID。</param>
    public void DropWindow(uint windowId)
    {
        lock (_gate)
        {
            if (_items.Count == 0)
            {
                return;
            }

            List<string>? stale = null;
            foreach (var kv in _items)
            {
                if (kv.Value.WindowId == windowId)
                {
                    (stale ??= []).Add(kv.Key);
                }
            }

            if (stale is null)
            {
                return;
            }

            foreach (var id in stale)
            {
                if (_items.Remove(id, out var item))
                {
                    _bytes -= item.Data.Length;
                }
            }
        }
    }

    /// <summary>
    /// 关闭存储：拒绝后续载荷、清空所有条目并停止清扫器。可安全重复调用。
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _closed = true;
            _items.Clear();
            _bytes = 0;

            _reaper?.Dispose();
            _reaper = null;
        }
    }

    /// <summary>
    /// 校验 id 形状：必须恰为 <see cref="IdLength"/> 位小写十六进制。
    /// 先校验形状可避免畸形长键进入字典查找。
    /// </summary>
    /// <param name="id">待校验的 id。</param>
    /// <returns>形状合法返回 true。</returns>
    public static bool IsValidId(string? id)
    {
        if (id is null || id.Length != IdLength)
        {
            return false;
        }

        foreach (var c in id)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 清扫过期条目（达到截止时刻即视为过期，而非严格超过）。
    /// </summary>
    private void Reap(object? state)
    {
        lock (_gate)
        {
            if (_items.Count == 0)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            List<string>? expired = null;
            foreach (var kv in _items)
            {
                if (now >= kv.Value.Created + Ttl)
                {
                    (expired ??= []).Add(kv.Key);
                }
            }

            if (expired is null)
            {
                return;
            }

            foreach (var id in expired)
            {
                if (_items.Remove(id, out var item))
                {
                    _bytes -= item.Data.Length;
                }
            }
        }
    }
}
