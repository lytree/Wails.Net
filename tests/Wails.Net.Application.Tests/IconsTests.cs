using System.Buffers.Binary;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using Wails.Net.Application.Icons;

namespace Wails.Net.Application.Tests;

/// <summary>
/// icons 包（ICO 编解码、多尺寸图标）单元测试。
/// 对应问题 10.2/3.11：icons 包 ICO 编解码、多尺寸图标支持。
/// </summary>
[NotInParallel]
public sealed class IconsTests
{
    /// <summary>
    /// PNG 文件签名，用于构造测试 PNG 数据。
    /// </summary>
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    /// <summary>
    /// 创建测试用 PNG 数据（PNG 签名 + 随机像素数据）。
    /// </summary>
    /// <param name="size">图像数据的近似字节数。</param>
    /// <returns>PNG 格式字节数组。</returns>
    private static byte[] CreateTestPng(int size = 64)
    {
        var data = new byte[size];
        PngSignature.CopyTo(data, 0);
        for (int i = PngSignature.Length; i < data.Length; i++)
        {
            data[i] = (byte)(i & 0xFF);
        }

        return data;
    }

    // ========== IcoEncoder 测试 ==========

    [Test]
    public async Task Encode_SingleEntry_ProducesValidIcoHeader()
    {
        // Arrange
        var icon = new MultiSizeIcon();
        icon.AddPng(32, CreateTestPng());

        // Act
        var bytes = icon.ToIcoBytes();

        // Assert
        await Assert.That(bytes.Length).IsGreaterThan(22); // 6 header + 16 entry + data
        await Assert.That((int)BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0, 2))).IsEqualTo(0); // reserved
        await Assert.That((int)BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2, 2))).IsEqualTo(1); // type=ICO
        await Assert.That((int)BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4, 2))).IsEqualTo(1); // count=1
    }

    [Test]
    public async Task Encode_MultipleEntries_WritesCorrectCount()
    {
        // Arrange
        var icon = new MultiSizeIcon();
        icon.AddPng(16, CreateTestPng(32));
        icon.AddPng(32, CreateTestPng(64));
        icon.AddPng(48, CreateTestPng(96));

        // Act
        var bytes = icon.ToIcoBytes();

        // Assert
        await Assert.That((int)BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4, 2))).IsEqualTo(3);
    }

    [Test]
    public async Task Encode_256Size_WritesZeroInDirectoryEntry()
    {
        // Arrange
        var icon = new MultiSizeIcon();
        icon.AddPng(256, CreateTestPng(128));

        // Act
        var bytes = icon.ToIcoBytes();

        // Assert: 256 尺寸在 ICONDIRENTRY 中存储为 0
        await Assert.That(bytes[6]).IsEqualTo((byte)0); // width=0 表示 256
        await Assert.That(bytes[7]).IsEqualTo((byte)0); // height=0 表示 256
    }

    [Test]
    public async Task Encode_NullEntries_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.That(() => IcoEncoder.Encode(null!)).ThrowsExactly<ArgumentNullException>();
    }

    // ========== IcoDecoder 测试 ==========

    [Test]
    public async Task Decode_ValidIcoData_ReturnsCorrectEntryCount()
    {
        // Arrange
        var icon = new MultiSizeIcon();
        icon.AddPng(16, CreateTestPng(32));
        icon.AddPng(32, CreateTestPng(64));
        var icoBytes = icon.ToIcoBytes();

        // Act
        var decoded = IcoDecoder.Decode(icoBytes);

        // Assert
        await Assert.That(decoded.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Decode_PngEntry_DetectsPngFormat()
    {
        // Arrange
        var icon = new MultiSizeIcon();
        icon.AddPng(32, CreateTestPng(64));
        var icoBytes = icon.ToIcoBytes();

        // Act
        var decoded = IcoDecoder.Decode(icoBytes);

        // Assert
        await Assert.That(decoded.Entries[0].Format).IsEqualTo(IconImageFormat.Png);
    }

    [Test]
    public async Task Decode_Entry_PreservesWidthHeight()
    {
        // Arrange
        var icon = new MultiSizeIcon();
        icon.AddPng(48, CreateTestPng(96));
        var icoBytes = icon.ToIcoBytes();

        // Act
        var decoded = IcoDecoder.Decode(icoBytes);

        // Assert
        await Assert.That(decoded.Entries[0].ActualWidth).IsEqualTo(48);
        await Assert.That(decoded.Entries[0].ActualHeight).IsEqualTo(48);
    }

    [Test]
    public async Task Decode_256Size_ParsesZeroAs256()
    {
        // Arrange
        var icon = new MultiSizeIcon();
        icon.AddPng(256, CreateTestPng(128));
        var icoBytes = icon.ToIcoBytes();

        // Act
        var decoded = IcoDecoder.Decode(icoBytes);

        // Assert
        await Assert.That(decoded.Entries[0].ActualWidth).IsEqualTo(256);
        await Assert.That(decoded.Entries[0].ActualHeight).IsEqualTo(256);
    }

    [Test]
    public async Task Decode_NullData_ThrowsArgumentNullException()
    {
        await Assert.That(() => IcoDecoder.Decode((byte[])null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task Decode_ShortData_ThrowsArgumentException()
    {
        var shortData = new byte[] { 0, 1, 2 };
        await Assert.That(() => IcoDecoder.Decode(shortData)).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task Decode_WrongType_ThrowsArgumentException()
    {
        // type=2 (CUR) should be rejected
        var data = new byte[] { 0, 0, 2, 0, 0, 0 };
        await Assert.That(() => IcoDecoder.Decode(data)).ThrowsExactly<ArgumentException>();
    }

    // ========== Round-trip 测试 ==========

    [Test]
    public async Task RoundTrip_EncodeThenDecode_PreservesData()
    {
        // Arrange
        var png16 = CreateTestPng(32);
        var png32 = CreateTestPng(64);
        var png256 = CreateTestPng(256);
        var icon = new MultiSizeIcon();
        icon.AddPng(16, png16);
        icon.AddPng(32, png32);
        icon.AddPng(256, png256);

        // Act
        var icoBytes = icon.ToIcoBytes();
        var decoded = IcoDecoder.Decode(icoBytes);

        // Assert
        await Assert.That(decoded.Count).IsEqualTo(3);
        await Assert.That(decoded.FindExactSize(16)?.Data).IsEquivalentTo(png16);
        await Assert.That(decoded.FindExactSize(32)?.Data).IsEquivalentTo(png32);
        await Assert.That(decoded.FindExactSize(256)?.Data).IsEquivalentTo(png256);
    }

    // ========== MultiSizeIcon 测试 ==========

    [Test]
    public async Task FindClosestSize_ReturnsNearestEntry()
    {
        // Arrange
        var icon = new MultiSizeIcon();
        icon.AddPng(16, CreateTestPng(32));
        icon.AddPng(32, CreateTestPng(64));
        icon.AddPng(48, CreateTestPng(96));

        // Act
        var result = icon.FindClosestSize(24);

        // Assert: 16 比 32 更接近 24... 实际上 |16-24|=8, |32-24|=8，取第一个匹配
        await Assert.That(result).IsNotNull();
        var diff16 = Math.Abs(16 - 24);
        var diff32 = Math.Abs(32 - 24);
        await Assert.That(diff16).IsEqualTo(diff32); // 8 == 8
        // 应该返回 16（第一个遇到的最近匹配）
        await Assert.That(result!.ActualWidth).IsEqualTo(16);
    }

    [Test]
    public async Task FindClosestSize_ExactMatch_ReturnsExact()
    {
        // Arrange
        var icon = new MultiSizeIcon();
        icon.AddPng(16, CreateTestPng(32));
        icon.AddPng(48, CreateTestPng(96));

        // Act
        var result = icon.FindClosestSize(48);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.ActualWidth).IsEqualTo(48);
    }

    [Test]
    public async Task FindExactSize_NoMatch_ReturnsNull()
    {
        // Arrange
        var icon = new MultiSizeIcon();
        icon.AddPng(16, CreateTestPng(32));

        // Act
        var result = icon.FindExactSize(64);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task FindClosestSize_EmptyIcon_ReturnsNull()
    {
        // Arrange
        var icon = new MultiSizeIcon();

        // Act
        var result = icon.FindClosestSize(32);

        // Assert
        await Assert.That(result).IsNull();
    }

    // ========== IcoEncoder.EncodeFromPngs 测试 ==========

    [Test]
    public async Task EncodeFromPngs_MultipleSizes_ProducesValidIco()
    {
        // Arrange
        var pngs = new Dictionary<int, byte[]>
        {
            [16] = CreateTestPng(32),
            [32] = CreateTestPng(64),
            [48] = CreateTestPng(96),
        };

        // Act
        var icoBytes = IcoEncoder.EncodeFromPngs(pngs);
        var decoded = IcoDecoder.Decode(icoBytes);

        // Assert
        await Assert.That(decoded.Count).IsEqualTo(3);
        await Assert.That(decoded.FindExactSize(16)).IsNotNull();
        await Assert.That(decoded.FindExactSize(32)).IsNotNull();
        await Assert.That(decoded.FindExactSize(48)).IsNotNull();
    }

    [Test]
    public async Task EncodeSinglePng_ProducesSingleEntryIco()
    {
        // Arrange
        var png = CreateTestPng(64);

        // Act
        var icoBytes = IcoEncoder.EncodeSinglePng(png, 32);
        var decoded = IcoDecoder.Decode(icoBytes);

        // Assert
        await Assert.That(decoded.Count).IsEqualTo(1);
        await Assert.That(decoded.Entries[0].ActualWidth).IsEqualTo(32);
        await Assert.That(decoded.Entries[0].Format).IsEqualTo(IconImageFormat.Png);
    }
}
