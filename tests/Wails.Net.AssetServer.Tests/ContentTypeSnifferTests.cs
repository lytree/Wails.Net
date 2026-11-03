using System.Text;
using TUnit.Core;

namespace Wails.Net.AssetServer.Tests;

/// <summary>
/// <see cref="ContentTypeSniffer" /> 纯函数测试（TUnit）。
/// 断言逐项对齐 Go <c>net/http/internal/sniff.go</c>（WHATWG MIME Sniffing）：
/// 签名顺序、掩码匹配、HTML 标签终止符、文本控制字符集合与 charset 后缀。
/// </summary>
[NotInParallel]
public sealed class ContentTypeSnifferTests
{
    /// <summary>
    /// 将 ASCII 字符串转换为字节数组（仅限单字节字符）。
    /// </summary>
    private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);

    /// <summary>
    /// 拼接多个字节片段。
    /// </summary>
    private static byte[] Concat(params byte[][] segments)
    {
        var result = new byte[segments.Sum(segment => segment.Length)];
        var offset = 0;
        foreach (var segment in segments)
        {
            segment.CopyTo(result, offset);
            offset += segment.Length;
        }
        return result;
    }

    // ========== 二进制格式签名 ==========

    [Test]
    public async Task Detect_PngHeader_ReturnsImagePng()
    {
        var data = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00 };
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("image/png");
    }

    [Test]
    public async Task Detect_JpegHeader_ReturnsImageJpeg()
    {
        var data = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("image/jpeg");
    }

    [Test]
    public async Task Detect_PdfHeader_ReturnsApplicationPdf()
    {
        var data = Ascii("%PDF-1.7\n");
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("application/pdf");
    }

    [Test]
    public async Task Detect_ZipHeader_ReturnsApplicationZip()
    {
        var data = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00 };
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("application/zip");
    }

    [Test]
    public async Task Detect_EmptyArchiveZipEntries_ReturnsOctetStream()
    {
        // Go 仅识别 PK\x03\x04（本地文件头）；PK\x05\x06（空归档目录）不被识别，
        // 且 0x05 属于控制字符，无法按文本嗅探。
        var data = new byte[] { 0x50, 0x4B, 0x05, 0x06, 0x00, 0x00 };
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("application/octet-stream");
    }

    [Test]
    public async Task Detect_Woff2Header_ReturnsFontWoff2()
    {
        var data = Concat(Ascii("wOF2"), new byte[8]);
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("font/woff2");
    }

    [Test]
    public async Task Detect_WoffHeader_ReturnsFontWoff()
    {
        var data = Concat(Ascii("wOFF"), new byte[8]);
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("font/woff");
    }

    [Test]
    public async Task Detect_WasmHeader_ReturnsApplicationWasm()
    {
        var data = new byte[] { 0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00 };
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("application/wasm");
    }

    [Test]
    public async Task Detect_GifHeader_ReturnsImageGif()
    {
        var data = Ascii("GIF89a");
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("image/gif");
    }

    [Test]
    public async Task Detect_BmpHeader_ReturnsImageBmp()
    {
        var data = Ascii("BM");
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("image/bmp");
    }

    [Test]
    public async Task Detect_IconHeader_ReturnsImageIcon()
    {
        var data = new byte[] { 0x00, 0x00, 0x01, 0x00, 0x01, 0x00 };
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("image/x-icon");
    }

    [Test]
    public async Task Detect_WebpHeader_ReturnsImageWebp()
    {
        var data = Concat(Ascii("RIFF"), new byte[4], Ascii("WEBPVP8 "));
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("image/webp");
    }

    [Test]
    public async Task Detect_RiffWaveHeader_ReturnsAudioWave()
    {
        var data = Concat(Ascii("RIFF"), new byte[4], Ascii("WAVEfmt "));
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("audio/wave");
    }

    [Test]
    public async Task Detect_RiffAviHeader_ReturnsVideoAvi()
    {
        var data = Concat(Ascii("RIFF"), new byte[4], Ascii("AVI LIST"));
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("video/avi");
    }

    [Test]
    public async Task Detect_FormAiffHeader_ReturnsAudioAiff()
    {
        var data = Concat(Ascii("FORM"), new byte[4], Ascii("AIFFCOMM"));
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("audio/aiff");
    }

    [Test]
    public async Task Detect_RiffUnknownSubtype_ReturnsOctetStreamNotWave()
    {
        // 任意 RIFF 不再被误判为 audio/wave（仅 RIFF????WAVE 命中）；
        // 长度字段为 0x00 控制字符，无法按文本嗅探。
        var data = Concat(Ascii("RIFF"), new byte[4], Ascii("XXXXdata"));
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("application/octet-stream");
    }

    [Test]
    public async Task Detect_MpegAudioHeader_ReturnsAudioMpeg()
    {
        var data = Ascii("ID3");
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("audio/mpeg");
    }

    [Test]
    public async Task Detect_OggHeaderWithNullByte_ReturnsApplicationOgg()
    {
        var data = new byte[] { 0x4F, 0x67, 0x67, 0x53, 0x00, 0x02 };
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("application/ogg");
    }

    [Test]
    public async Task Detect_OggWithoutNullByte_ReturnsTextPlain()
    {
        // 缺少第 5 字节 0x00 时不命中 Ogg 签名。
        var data = Ascii("OggS");
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("text/plain; charset=utf-8");
    }

    [Test]
    public async Task Detect_MidiHeader_ReturnsAudioMidi()
    {
        var data = new byte[] { 0x4D, 0x54, 0x68, 0x64, 0x00, 0x00, 0x00, 0x06, 0x00, 0x01 };
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("audio/midi");
    }

    [Test]
    public async Task Detect_Mp4Header_ReturnsVideoMp4()
    {
        // boxSize=24：ftyp | major brand=isom | version(跳过) | compatible brand=mp42 | padding。
        var data = Concat(
            new byte[] { 0x00, 0x00, 0x00, 0x18 },
            Ascii("ftyp"),
            Ascii("isom"),
            new byte[] { 0x00, 0x00, 0x02, 0x00 },
            Ascii("mp42"),
            new byte[4]);
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("video/mp4");
    }

    [Test]
    public async Task Detect_Mp4BoxSizeExceedsData_ReturnsOctetStream()
    {
        // 盒长度 24 但实际数据仅 12 字节，Go 拒绝匹配；0x00 触发控制字符 → octet-stream。
        var data = Concat(new byte[] { 0x00, 0x00, 0x00, 0x18 }, Ascii("ftypmp42"));
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("application/octet-stream");
    }

    [Test]
    public async Task Detect_WebmHeader_ReturnsVideoWebm()
    {
        var data = new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 0x01, 0x00 };
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("video/webm");
    }

    [Test]
    public async Task Detect_PostscriptHeader_ReturnsApplicationPostscript()
    {
        var data = Ascii("%!PS-Adobe-3.0\n");
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("application/postscript");
    }

    [Test]
    public async Task Detect_GzipHeader_ReturnsApplicationXGzip()
    {
        var data = new byte[] { 0x1F, 0x8B, 0x08, 0x00 };
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("application/x-gzip");
    }

    [Test]
    public async Task Detect_Rar4Header_ReturnsApplicationRarCompressed()
    {
        var data = new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00 };
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("application/x-rar-compressed");
    }

    [Test]
    public async Task Detect_Rar5Header_ReturnsApplicationRarCompressed()
    {
        var data = new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00 };
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("application/x-rar-compressed");
    }

    [Test]
    public async Task Detect_TtfHeader_ReturnsFontTtf()
    {
        var data = new byte[] { 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("font/ttf");
    }

    [Test]
    public async Task Detect_OtfHeader_ReturnsFontOtf()
    {
        var data = Concat(Ascii("OTTO"), new byte[8]);
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("font/otf");
    }

    [Test]
    public async Task Detect_FontCollectionHeader_ReturnsFontCollection()
    {
        var data = Concat(Ascii("ttcf"), new byte[] { 0x00, 0x01, 0x00, 0x00 });
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("font/collection");
    }

    [Test]
    public async Task Detect_EmbeddedOpenTypeHeader_ReturnsMsFontObject()
    {
        var data = Concat(new byte[34], Ascii("LP"));
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("application/vnd.ms-fontobject");
    }

    // ========== UTF BOM ==========

    [Test]
    public async Task Detect_Utf16BeBom_ReturnsTextPlainUtf16Be()
    {
        var data = new byte[] { 0xFE, 0xFF, 0x00, 0x68 };
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("text/plain; charset=utf-16be");
    }

    [Test]
    public async Task Detect_Utf16LeBom_ReturnsTextPlainUtf16Le()
    {
        var data = new byte[] { 0xFF, 0xFE, 0x68, 0x00 };
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("text/plain; charset=utf-16le");
    }

    [Test]
    public async Task Detect_Utf8Bom_ReturnsTextPlainUtf8()
    {
        var data = new byte[] { 0xEF, 0xBB, 0xBF, 0x68, 0x69 };
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("text/plain; charset=utf-8");
    }

    // ========== 文本类嗅探 ==========

    [Test]
    public async Task Detect_HtmlWithLeadingWhitespace_ReturnsTextHtml()
    {
        var data = Ascii("\n\t  <!DOCTYPE html><html></html>");
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("text/html; charset=utf-8");
    }

    [Test]
    public async Task Detect_HtmlWithoutLeadingWhitespace_ReturnsTextHtml()
    {
        var data = Ascii("<!DOCTYPE html>");
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("text/html; charset=utf-8");
    }

    [Test]
    public async Task Detect_HtmlTagWithoutDoctype_ReturnsTextHtml()
    {
        var data = Ascii("<html><head></head></html>");
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("text/html; charset=utf-8");
    }

    [Test]
    public async Task Detect_HtmlTagWithoutTagTerminator_ReturnsTextPlain()
    {
        // Go 要求签名后紧跟空格或 '>'。
        var data = Ascii("<html");
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("text/plain; charset=utf-8");
    }

    [Test]
    public async Task Detect_XmlDeclaration_ReturnsTextXml()
    {
        var data = Ascii("  <?xml version=\"1.0\"?><root/>");
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("text/xml; charset=utf-8");
    }

    [Test]
    public async Task Detect_PlainText_ReturnsTextPlain()
    {
        var data = Ascii("hello world");
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("text/plain; charset=utf-8");
    }

    // ========== 回退与边界 ==========

    [Test]
    public async Task Detect_UnknownBinaryWithControlBytes_ReturnsOctetStream()
    {
        var data = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x00 };
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("application/octet-stream");
    }

    [Test]
    public async Task Detect_HighBytesWithoutControl_ReturnsTextPlain()
    {
        // Go 的 textSig 不校验 UTF-8，仅排除控制字符。
        var data = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("text/plain; charset=utf-8");
    }

    [Test]
    public async Task Detect_EmptyData_ReturnsTextPlain()
    {
        await Assert.That(ContentTypeSniffer.Detect(Array.Empty<byte>())).IsEqualTo("text/plain; charset=utf-8");
    }

    [Test]
    public async Task Detect_OnlyWhitespace_ReturnsTextPlain()
    {
        var data = Ascii("   \r\n\t ");
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("text/plain; charset=utf-8");
    }

    [Test]
    public async Task Detect_OnlyFirst512BytesConsidered()
    {
        // 前 512 字节均为空白，HTML 签名落在截断窗口之外 → 文本嗅探。
        var data = Concat(new byte[512].Select(_ => (byte)' ').ToArray(), Ascii("<html>"));
        await Assert.That(ContentTypeSniffer.Detect(data)).IsEqualTo("text/plain; charset=utf-8");
    }
}
