using System.Buffers.Binary;
using System.Globalization;

namespace Wails.Net.Application.Streams;

/// <summary>
/// 出站帧：一次 poll 响应中承载的一帧。
/// 对应 Wails v3 Go 版本 <c>v3/pkg/application/stream_session.go</c> 中的 <c>outFrame</c>。
/// </summary>
/// <param name="ConnId">连接 id。</param>
/// <param name="Kind">帧类型（见 <see cref="StreamFrameKind"/>）。</param>
/// <param name="Payload">帧载荷字节；控制帧可为 null。</param>
internal readonly record struct OutFrame(uint ConnId, byte Kind, byte[]? Payload);

/// <summary>
/// Streams 线协议的编解码：poll 响应编码、batch 体解码、header 解析。
/// 对应 Wails v3 Go 版本 <c>v3/pkg/application/stream_transport.go</c> 中的
/// <c>encodeStreamFrames</c> / <c>decodeStreamBatch</c> 与 header 解析辅助函数。
/// </summary>
internal static class StreamFrameCodec
{
    /// <summary>
    /// 响应体魔数，标识帧格式版本：<c>(0x57,0x53,0x31,0x00)</c> = "WS1\0"。
    /// 被投递了陈旧缓存 runtime 的页面会醒目地校验失败，而非误解析。对应 Go <c>streamMagic</c>。
    /// </summary>
    public static readonly byte[] Magic = [0x57, 0x53, 0x31, 0x00];

    /// <summary>
    /// 编码一个 poll 响应体：
    /// <c>magic(4) | flags(1, bit0=more) | count(4 BE) | count × ( connID(4 BE) | kind(1) | len(4 BE) | payload )</c>。
    /// 对应 Go <c>encodeStreamFrames</c>。
    /// </summary>
    /// <param name="frames">要编码的帧列表（非空）。</param>
    /// <param name="more">队列中是否仍有未投递帧（置 flags bit0）。</param>
    /// <returns>编码后的响应体字节。</returns>
    public static byte[] Encode(IReadOnlyList<OutFrame> frames, bool more)
    {
        var size = Magic.Length + 1 + 4;
        for (var i = 0; i < frames.Count; i++)
        {
            size += StreamConstants.FrameHeaderBytes + (frames[i].Payload?.Length ?? 0);
        }

        var buf = new byte[size];
        var span = buf.AsSpan();

        Magic.AsSpan().CopyTo(span);
        span[4] = more ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt32BigEndian(span[5..], (uint)frames.Count);

        var off = StreamConstants.FrameHeaderBytes;
        foreach (var frame in frames)
        {
            BinaryPrimitives.WriteUInt32BigEndian(span[off..], frame.ConnId);
            span[off + 4] = frame.Kind;
            var payload = frame.Payload;
            var length = payload?.Length ?? 0;
            BinaryPrimitives.WriteUInt32BigEndian(span[(off + 5)..], (uint)length);
            if (length > 0)
            {
                payload!.AsSpan().CopyTo(span[(off + StreamConstants.FrameHeaderBytes)..]);
            }

            off += StreamConstants.FrameHeaderBytes + length;
        }

        return buf;
    }

    /// <summary>
    /// 解码 batch 体：<c>count(4 BE) | count × ( len(4 BE) | payload )</c>。
    /// 对应 Go <c>decodeStreamBatch</c>；解析完成后偏移必须恰好等于 body 长度，否则非法。
    /// </summary>
    /// <param name="body">请求体字节。</param>
    /// <param name="frames">成功时输出各帧载荷（切片指向 <paramref name="body"/>）。</param>
    /// <returns>解析成功返回 true；否则返回 false。</returns>
    public static bool TryDecodeBatch(byte[] body, out List<byte[]> frames)
    {
        frames = [];

        if (body.Length < 4)
        {
            return false;
        }

        var count = BinaryPrimitives.ReadUInt32BigEndian(body);
        if (count == 0 || count > StreamConstants.MaxBatchCount)
        {
            return false;
        }

        frames = new List<byte[]>((int)count);
        var off = 4;
        for (uint i = 0; i < count; i++)
        {
            if (body.Length - off < 4)
            {
                return false;
            }

            var n = (int)BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(off));
            off += 4;
            // n < 0 在 64 位 int 上不可达，但它是 32 位构建下唯一成立的守卫：
            // 线上长度超过 int.MaxValue 会回绕成负数，切片会 panic 而非被拒绝。
            if (n < 0 || n > body.Length - off)
            {
                return false;
            }

            var payload = new byte[n];
            Array.Copy(body, off, payload, 0, n);
            frames.Add(payload);
            off += n;
        }

        return off == body.Length;
    }

    /// <summary>
    /// 大小写不敏感地读取 header。对应 Go 的 <c>req.Header.Get</c>（Go 的 header 查找本身大小写不敏感）。
    /// </summary>
    /// <param name="headers">请求头字典。</param>
    /// <param name="name">header 名称。</param>
    /// <returns>header 值；不存在时返回 null。</returns>
    public static string? GetHeader(IReadOnlyDictionary<string, string> headers, string name)
    {
        if (headers.TryGetValue(name, out var value))
        {
            return value;
        }

        foreach (var pair in headers)
        {
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// 解析 u32 header（十进制）。对应 Go <c>strconv.ParseUint(v, 10, 32)</c>。
    /// </summary>
    /// <param name="raw">原始 header 值。</param>
    /// <param name="value">解析结果。</param>
    /// <returns>解析成功返回 true。</returns>
    public static bool TryParseUInt32(string? raw, out uint value)
    {
        return uint.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// 解析 u64 header（十进制）。对应 Go <c>strconv.ParseUint(v, 10, 64)</c>。
    /// </summary>
    /// <param name="raw">原始 header 值。</param>
    /// <param name="value">解析结果。</param>
    /// <returns>解析成功返回 true。</returns>
    public static bool TryParseUInt64(string? raw, out ulong value)
    {
        return ulong.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// 解析 u8 header（十进制）。对应 Go <c>strconv.ParseUint(v, 10, 8)</c>。
    /// </summary>
    /// <param name="raw">原始 header 值。</param>
    /// <param name="value">解析结果。</param>
    /// <returns>解析成功返回 true。</returns>
    public static bool TryParseUInt8(string? raw, out byte value)
    {
        if (byte.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>
    /// 解析十进制 int header（含负数）。对应 Go <c>strconv.Atoi</c>。
    /// </summary>
    /// <param name="raw">原始 header 值。</param>
    /// <param name="value">解析结果。</param>
    /// <returns>解析成功返回 true。</returns>
    public static bool TryParseInt32(string? raw, out int value)
    {
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
