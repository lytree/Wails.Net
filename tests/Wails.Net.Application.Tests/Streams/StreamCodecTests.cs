using TUnit.Core;
using Wails.Net.Application.Streams;

namespace Wails.Net.Application.Tests.Streams;

/// <summary>
/// <see cref="StreamFrameCodec"/> 与 <see cref="StreamConstants"/> 的单元测试（TUnit）。
/// 对应 Wails v3 Go 版本 <c>stream_transport.go</c> 中的 <c>encodeStreamFrames</c> /
/// <c>decodeStreamBatch</c>。
/// </summary>
[NotInParallel]
public sealed class StreamCodecTests
{
    [Test]
    public async Task Encode_MixedFrames_RoundTripsWithoutTrailingBytes()
    {
        var big = new byte[70000];
        for (var i = 0; i < big.Length; i++)
        {
            big[i] = (byte)(i % 251);
        }

        List<OutFrame> frames =
        [
            new(1u, StreamFrameKind.Data, big),
            new(2u, StreamFrameKind.Open, null),
        ];

        var body = StreamFrameCodec.Encode(frames, more: true);

        // 期望大小：magic(4) + flags(1) + count(4) + (9 + 70000) + (9 + 0)
        const int expected = 4 + 1 + 4 + (9 + 70000) + 9;
        await Assert.That(body.Length).IsEqualTo(expected);

        await Assert.That(body[0]).IsEqualTo((byte)0x57);
        await Assert.That(body[1]).IsEqualTo((byte)0x53);
        await Assert.That(body[2]).IsEqualTo((byte)0x31);
        await Assert.That(body[3]).IsEqualTo((byte)0x00);
        await Assert.That(body[4]).IsEqualTo((byte)1); // bit0 = more
        await Assert.That(StreamTestSupport.ReadUInt32BigEndian(body, 5)).IsEqualTo(2u);

        // frame 0
        var off = 9;
        await Assert.That(StreamTestSupport.ReadUInt32BigEndian(body, off)).IsEqualTo(1u);
        await Assert.That(body[off + 4]).IsEqualTo(StreamFrameKind.Data);
        await Assert.That(StreamTestSupport.ReadUInt32BigEndian(body, off + 5)).IsEqualTo(70000u);
        await Assert.That(body.AsSpan(off + 9, 70000).SequenceEqual(big)).IsTrue();
        off += 9 + 70000;

        // frame 1
        await Assert.That(StreamTestSupport.ReadUInt32BigEndian(body, off)).IsEqualTo(2u);
        await Assert.That(body[off + 4]).IsEqualTo(StreamFrameKind.Open);
        await Assert.That(StreamTestSupport.ReadUInt32BigEndian(body, off + 5)).IsEqualTo(0u);
        off += 9;

        // 无尾随字节
        await Assert.That(off).IsEqualTo(body.Length);
    }

    [Test]
    public async Task Encode_NoMore_ClearsFlagBit()
    {
        List<OutFrame> frames = [new(7u, StreamFrameKind.Close, null)];
        var body = StreamFrameCodec.Encode(frames, more: false);

        await Assert.That(body[4]).IsEqualTo((byte)0);
        await Assert.That(StreamTestSupport.ReadUInt32BigEndian(body, 5)).IsEqualTo(1u);
        await Assert.That(body[9 + 4]).IsEqualTo(StreamFrameKind.Close);
    }

    [Test]
    public async Task DecodeBatch_ValidBody_RoundTrips()
    {
        byte[][] inputs = [[1, 2, 3], [], [9, 8, 7, 6]];
        var body = StreamTestSupport.BuildBatch(inputs);

        var ok = StreamFrameCodec.TryDecodeBatch(body, out var frames);

        await Assert.That(ok).IsTrue();
        await Assert.That(frames.Count).IsEqualTo(3);
        await Assert.That(frames[0].SequenceEqual(inputs[0])).IsTrue();
        await Assert.That(frames[1].Length).IsEqualTo(0);
        await Assert.That(frames[2].SequenceEqual(inputs[2])).IsTrue();
    }

    [Test]
    public async Task DecodeBatch_TooShort_ReturnsFalse()
    {
        await Assert.That(StreamFrameCodec.TryDecodeBatch([1, 2, 3], out _)).IsFalse();
    }

    [Test]
    public async Task DecodeBatch_ZeroCount_ReturnsFalse()
    {
        var body = StreamTestSupport.BuildBatch([[]]);
        // 手工把 count 改为 0
        StreamTestSupport.WriteUInt32BigEndian(body, 0, 0);
        await Assert.That(StreamFrameCodec.TryDecodeBatch(body, out _)).IsFalse();
    }

    [Test]
    public async Task DecodeBatch_TrailingBytes_ReturnsFalse()
    {
        var body = StreamTestSupport.BuildBatch([[1, 2]]);
        var extended = new byte[body.Length + 1];
        body.CopyTo(extended, 0);
        await Assert.That(StreamFrameCodec.TryDecodeBatch(extended, out _)).IsFalse();
    }

    [Test]
    public async Task DecodeBatch_LengthOverruns_ReturnsFalse()
    {
        var body = StreamTestSupport.BuildBatch([[1, 2]]);
        // 把第一帧长度改成大于剩余字节
        StreamTestSupport.WriteUInt32BigEndian(body, 4, 99);
        await Assert.That(StreamFrameCodec.TryDecodeBatch(body, out _)).IsFalse();
    }

    [Test]
    public async Task Constants_AlignWithWailsImplementation()
    {
        int outQueueBytes = StreamConstants.OutQueueBytes;
        int outQueueDepth = StreamConstants.OutQueueDepth;
        int maxResponseBytes = StreamConstants.MaxResponseBytes;
        int inQueueDepth = StreamConstants.InQueueDepth;
        long outQueueBytesGlobal = StreamConstants.OutQueueBytesGlobal;
        long outQueueDepthGlobal = StreamConstants.OutQueueDepthGlobal;
        long maxConnectionsGlobal = StreamConstants.MaxConnectionsGlobal;
        int maxFrameBytes = StreamConstants.MaxFrameBytes;
        int maxSessionsPerWindow = StreamConstants.MaxSessionsPerWindow;
        int frameHeaderBytes = StreamConstants.FrameHeaderBytes;

        await Assert.That(outQueueBytes).IsEqualTo(8 << 20);
        await Assert.That(outQueueDepth).IsEqualTo(256);
        await Assert.That(maxResponseBytes).IsEqualTo(1 << 20);
        await Assert.That(inQueueDepth).IsEqualTo(256);
        await Assert.That(outQueueBytesGlobal).IsEqualTo(256L << 20);
        await Assert.That(outQueueDepthGlobal).IsEqualTo(8192L);
        await Assert.That(maxConnectionsGlobal).IsEqualTo(4096L);
        await Assert.That(maxFrameBytes).IsEqualTo(64 << 20);
        await Assert.That(maxSessionsPerWindow).IsEqualTo(16);
        await Assert.That(frameHeaderBytes).IsEqualTo(9);

        await Assert.That(StreamConstants.HoldTimeout).IsEqualTo(TimeSpan.FromSeconds(20));
        await Assert.That(StreamConstants.SessionTtl).IsEqualTo(TimeSpan.FromSeconds(60));
        await Assert.That(StreamConstants.SessionSweep).IsEqualTo(TimeSpan.FromSeconds(20));
        await Assert.That(StreamConstants.SessionGrace).IsEqualTo(TimeSpan.FromMinutes(10));
    }

    [Test]
    public async Task GetHeader_IsCaseInsensitive()
    {
        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase)
        {
            ["X-Wails-Stream-Session"] = "abc",
        };

        await Assert.That(StreamFrameCodec.GetHeader(headers, "x-wails-stream-session")).IsEqualTo("abc");
        await Assert.That(StreamFrameCodec.GetHeader(headers, "missing")).IsNull();
    }
}
