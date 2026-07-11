using System.IO;
using TUnit.Assertions;
using TUnit.Core;
using Wails.Net.Application.Icons;
using Wails.Net.Cli.Commands;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// IconCommand 的单元测试（TUnit）。
/// 验证 ICO 文件生成和编解码的完整性。
/// </summary>
[NotInParallel]
public sealed class IconCommandTests
{
    /// <summary>
    /// 创建一个简单的模拟 PNG 字节数组（仅用于测试 ICO 封装，非真实 PNG）。
    /// </summary>
    private static byte[] CreateMockPngData()
    {
        // PNG 文件签名 + 一些模拟数据
        return new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D };
    }

    [Test]
    public async Task GenerateIcoAsync_ValidPng_CreatesIcoFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"wails_icon_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var icoPath = Path.Combine(tempDir, "test.ico");
            var pngData = CreateMockPngData();

            await IconCommand.GenerateIcoAsync(pngData, icoPath);

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
            var pngData = CreateMockPngData();

            await IconCommand.GenerateIcoAsync(pngData, icoPath);

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
            var pngData = CreateMockPngData();

            await IconCommand.GenerateIcoAsync(pngData, icoPath);

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
            var pngData = CreateMockPngData();

            await IconCommand.GenerateIcoAsync(pngData, icoPath);

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
    public async Task GenerateIcoAsync_ValidPng_DecodedEntryContainsOriginalData()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"wails_icon_data_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var icoPath = Path.Combine(tempDir, "test.ico");
            var pngData = CreateMockPngData();

            await IconCommand.GenerateIcoAsync(pngData, icoPath);

            var icoBytes = await File.ReadAllBytesAsync(icoPath);
            var decoded = IconCommand.DecodeIco(icoBytes);

            var entry = decoded.Entries[0];
            await Assert.That(entry.Data.Length).IsEqualTo(pngData.Length);
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
