using TUnit.Assertions;
using TUnit.Core;
using Wails.Net.Cli.Commands;
using SkiaSharp;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// IconCommand 的单元测试（TUnit）。
/// 验证 ICO 文件生成、尺寸校验和编解码的完整性。
/// </summary>
[NotInParallel]
public sealed class IconCommandTests
{
    /// <summary>
    /// 生成一个真实 PNG 字节数组（纯色图像），供 SkiaSharp 解码与缩放使用。
    /// </summary>
    /// <param name="width">图像宽度。</param>
    /// <param name="height">图像高度。</param>
    /// <returns>PNG 文件字节。</returns>
    private static byte[] CreatePngData(int width = 64, int height = 64)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Red);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    [Test]
    public async Task GenerateIcoAsync_ValidPng_CreatesIcoFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"wails_icon_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var icoPath = Path.Combine(tempDir, "test.ico");

            await IconCommand.GenerateIcoAsync(CreatePngData(), icoPath, new[] { 256 });

            await Assert.That(File.Exists(icoPath)).IsTrue();

            var icoBytes = await File.ReadAllBytesAsync(icoPath);
            await Assert.That(icoBytes.Length).IsGreaterThan(0);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* 忽略 */ }
        }
    }

    [Test]
    public async Task GenerateIcoAsync_ValidPng_IcoStartsWithCorrectHeader()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"wails_icon_hdr_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var icoPath = Path.Combine(tempDir, "test.ico");

            await IconCommand.GenerateIcoAsync(CreatePngData(), icoPath, new[] { 256 });

            var icoBytes = await File.ReadAllBytesAsync(icoPath);

            // ICO 文件头：前 4 字节是 reserved (0) + type (1)
            await Assert.That(icoBytes[0]).IsEqualTo((byte)0);
            await Assert.That(icoBytes[1]).IsEqualTo((byte)0);
            await Assert.That(icoBytes[2]).IsEqualTo((byte)1); // ICO type = 1
            await Assert.That(icoBytes[3]).IsEqualTo((byte)0);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* 忽略 */ }
        }
    }

    [Test]
    public async Task GenerateIcoAsync_ValidPng_ContainsOneEntry()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"wails_icon_cnt_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var icoPath = Path.Combine(tempDir, "test.ico");

            await IconCommand.GenerateIcoAsync(CreatePngData(), icoPath, new[] { 256 });

            var icoBytes = await File.ReadAllBytesAsync(icoPath);
            var decoded = IconCommand.DecodeIco(icoBytes);

            await Assert.That(decoded.Count).IsEqualTo(1);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* 忽略 */ }
        }
    }

    [Test]
    public async Task GenerateIcoAsync_ValidPng_DecodedEntryHasCorrectSize()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"wails_icon_sz_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var icoPath = Path.Combine(tempDir, "test.ico");

            await IconCommand.GenerateIcoAsync(CreatePngData(), icoPath, new[] { 256 });

            var icoBytes = await File.ReadAllBytesAsync(icoPath);
            var decoded = IconCommand.DecodeIco(icoBytes);

            await Assert.That(decoded.Count).IsEqualTo(1);

            // Width=0 在 ICO 中表示 256
            var entry = decoded.Entries[0];
            await Assert.That(entry.ActualWidth).IsEqualTo(256);
            await Assert.That(entry.ActualHeight).IsEqualTo(256);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* 忽略 */ }
        }
    }

    [Test]
    public async Task GenerateIcoAsync_ValidPng_DecodedEntryContainsPngData()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"wails_icon_data_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var icoPath = Path.Combine(tempDir, "test.ico");

            await IconCommand.GenerateIcoAsync(CreatePngData(), icoPath, new[] { 256 });

            var icoBytes = await File.ReadAllBytesAsync(icoPath);
            var decoded = IconCommand.DecodeIco(icoBytes);

            var entry = decoded.Entries[0];
            await Assert.That(entry.Data.Length).IsGreaterThan(0);
            await Assert.That(entry.Data[0]).IsEqualTo((byte)0x89);
            await Assert.That(entry.Data[1]).IsEqualTo((byte)0x50); // 'P'
            await Assert.That(entry.Data[2]).IsEqualTo((byte)0x4E); // 'N'
            await Assert.That(entry.Data[3]).IsEqualTo((byte)0x47); // 'G'
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* 忽略 */ }
        }
    }

    [Test]
    public async Task GenerateIcoAsync_MultipleSizes_CreatesOneEntryPerSize()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"wails_icon_multi_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var icoPath = Path.Combine(tempDir, "test.ico");

            await IconCommand.GenerateIcoAsync(CreatePngData(), icoPath, new[] { 16, 32, 48 });

            var icoBytes = await File.ReadAllBytesAsync(icoPath);
            var decoded = IconCommand.DecodeIco(icoBytes);

            await Assert.That(decoded.Count).IsEqualTo(3);
            await Assert.That(decoded.Entries[0].ActualWidth).IsEqualTo(16);
            await Assert.That(decoded.Entries[1].ActualWidth).IsEqualTo(32);
            await Assert.That(decoded.Entries[2].ActualWidth).IsEqualTo(48);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* 忽略 */ }
        }
    }

    [Test]
    public async Task ParseSizes_ValidInput_ReturnsNonZeroSizes()
    {
        var sizes = IconCommand.ParseSizes("1,16,256");
        await Assert.That(sizes.Count).IsEqualTo(3);
        await Assert.That(sizes[0]).IsEqualTo(1);
        await Assert.That(sizes[2]).IsEqualTo(256);

        var withZero = IconCommand.ParseSizes("0,16");
        await Assert.That(withZero.Count).IsEqualTo(1);
        await Assert.That(withZero[0]).IsEqualTo(16);
    }

    [Test]
    [Arguments("-16")]
    [Arguments("257")]
    [Arguments("1000000000")]
    [Arguments("0,0")]
    [Arguments("")]
    [Arguments("bad")]
    public async Task ParseSizes_InvalidInput_Throws(string sizes)
    {
        await Assert.That(() => IconCommand.ParseSizes(sizes))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task ExecuteAsync_InvalidSizes_RejectsBeforeReadingInputAndPreservesOutput()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"wails_icon_invalid_{Guid.NewGuid():N}");
        var outputDir = Path.Combine(tempDir, "icons");
        Directory.CreateDirectory(outputDir);

        try
        {
            var existingIco = Path.Combine(outputDir, "icon.ico");
            await File.WriteAllTextAsync(existingIco, "previous icon");

            // 源文件不存在，但尺寸校验应先失败，且不触碰已有输出。
            var cmd = new IconCommand();
            var exitCode = await cmd.ExecuteAsync(
                new FileInfo(Path.Combine(tempDir, "missing.png")),
                new DirectoryInfo(outputDir),
                "-16");

            await Assert.That(exitCode).IsEqualTo(1);
            await Assert.That(await File.ReadAllTextAsync(existingIco)).IsEqualTo("previous icon");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* 忽略 */ }
        }
    }

    [Test]
    public async Task DecodeIco_EmptyData_ThrowsOrReturnsEmpty()
    {
        // 空数据应抛出异常（IcoDecoder 对无效数据抛出）
        await Assert.That(() => IconCommand.DecodeIco(Array.Empty<byte>()))
            .ThrowsExactly<ArgumentException>();
    }
}
