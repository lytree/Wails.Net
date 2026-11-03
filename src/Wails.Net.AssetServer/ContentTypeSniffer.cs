using System.Buffers.Binary;
using System.Runtime.CompilerServices;

// 纯函数嗅探器无平台依赖，无条件暴露给测试程序集。
[assembly: InternalsVisibleTo("Wails.Net.AssetServer.Tests")]

namespace Wails.Net.AssetServer;

/// <summary>
/// 内容类型嗅探器，依据资源内容的前 512 字节推断 MIME 类型。
/// <para>
/// 对应 Wails v3 Go 版本资源服务器在扩展名无法确定类型时回退到
/// <c>http.DetectContentType</c> 的行为（上游 PR #6398）。实现逐条对齐 Go
/// <c>net/http/internal/sniff.go</c>（WHATWG MIME Sniffing 算法）：签名顺序、掩码匹配、
/// HTML 标签终止符判定、文本控制字符集合与返回的 charset 均与 Go 一致。
/// </para>
/// <para>
/// 仅在 <see cref="AssetServer.GetMimeType"/> 返回默认 <c>application/octet-stream</c>
/// （无扩展名或未知扩展名）时使用；已知扩展名映射始终优先。
/// </para>
/// </summary>
internal static class ContentTypeSniffer
{
    /// <summary>
    /// 嗅探使用的最大字节数（与 Go 的 <c>SniffLen</c> 一致）。
    /// </summary>
    private const int SniffLength = 512;

    /// <summary>
    /// 无法识别内容时返回的默认 MIME 类型。
    /// </summary>
    internal const string DefaultContentType = "application/octet-stream";

    /// <summary>
    /// HTML 标签签名（跳过前导空白后比较，大小写不敏感，且后一个字节必须是
    /// 标签终止符 <c> </c> 或 <c>&gt;</c>）。与 Go <c>sniff.go</c> 中的 <c>htmlSig</c> 列表一致。
    /// </summary>
    private static readonly string[] HtmlSignatures =
    [
        "<!DOCTYPE HTML",
        "<HTML",
        "<HEAD",
        "<SCRIPT",
        "<IFRAME",
        "<H1",
        "<DIV",
        "<FONT",
        "<TABLE",
        "<A",
        "<STYLE",
        "<TITLE",
        "<B",
        "<BODY",
        "<BR",
        "<P",
        "<!--"
    ];

    /// <summary>
    /// 嗅探指定内容对应的 MIME 类型。
    /// </summary>
    /// <param name="content">资源内容，最多使用前 512 字节。</param>
    /// <returns>
    /// 识别出的 MIME 类型；无法识别时返回 <c>application/octet-stream</c>。
    /// </returns>
    internal static string Detect(ReadOnlySpan<byte> content)
    {
        if (content.Length > SniffLength)
        {
            content = content[..SniffLength];
        }

        // 第一个非空白字节的位置（HTML / XML 签名允许前导空白）。
        var firstNonWs = 0;
        while (firstNonWs < content.Length && IsWhitespace(content[firstNonWs]))
        {
            firstNonWs++;
        }

        var trimmed = content[firstNonWs..];

        // HTML：签名后必须紧跟标签终止符（空格或 '>'）。
        foreach (var signature in HtmlSignatures)
        {
            if (MatchesHtmlSignature(trimmed, signature))
            {
                return "text/html; charset=utf-8";
            }
        }

        if (StartsWith(trimmed, "<?xml"))
        {
            return "text/xml; charset=utf-8";
        }

        if (StartsWith(content, "%PDF-"))
        {
            return "application/pdf";
        }

        if (StartsWith(content, "%!PS-Adobe-"))
        {
            return "application/postscript";
        }

        // UTF BOM。
        if (StartsWith(content, "\xFE\xFF"))
        {
            return "text/plain; charset=utf-16be";
        }

        if (StartsWith(content, "\xFF\xFE"))
        {
            return "text/plain; charset=utf-16le";
        }

        if (StartsWith(content, "\xEF\xBB\xBF"))
        {
            return "text/plain; charset=utf-8";
        }

        // 图像格式。
        if (StartsWith(content, "\x00\x00\x01\x00") || StartsWith(content, "\x00\x00\x02\x00"))
        {
            return "image/x-icon";
        }

        if (StartsWith(content, "BM"))
        {
            return "image/bmp";
        }

        if (StartsWith(content, "GIF87a") || StartsWith(content, "GIF89a"))
        {
            return "image/gif";
        }

        if (IsWebp(content))
        {
            return "image/webp";
        }

        if (StartsWith(content, "\x89PNG\x0D\x0A\x1A\x0A"))
        {
            return "image/png";
        }

        if (StartsWith(content, "\xFF\xD8\xFF"))
        {
            return "image/jpeg";
        }

        // 音视频格式（顺序与 WHATWG / Go sniff.go 一致）。
        if (IsAiff(content))
        {
            return "audio/aiff";
        }

        if (StartsWith(content, "ID3"))
        {
            return "audio/mpeg";
        }

        if (StartsWith(content, "OggS\x00"))
        {
            return "application/ogg";
        }

        if (StartsWith(content, "MThd\x00\x00\x00\x06"))
        {
            return "audio/midi";
        }

        if (IsAvi(content))
        {
            return "video/avi";
        }

        if (IsWave(content))
        {
            return "audio/wave";
        }

        if (IsMp4(content))
        {
            return "video/mp4";
        }

        if (StartsWith(content, "\x1A\x45\xDF\xA3"))
        {
            return "video/webm";
        }

        // 字体格式。
        if (IsEmbeddedOpenType(content))
        {
            return "application/vnd.ms-fontobject";
        }

        if (StartsWith(content, "\x00\x01\x00\x00"))
        {
            return "font/ttf";
        }

        if (StartsWith(content, "OTTO"))
        {
            return "font/otf";
        }

        if (StartsWith(content, "ttcf"))
        {
            return "font/collection";
        }

        if (StartsWith(content, "wOFF"))
        {
            return "font/woff";
        }

        if (StartsWith(content, "wOF2"))
        {
            return "font/woff2";
        }

        // 压缩格式。
        if (StartsWith(content, "\x1F\x8B\x08"))
        {
            return "application/x-gzip";
        }

        if (StartsWith(content, "PK\x03\x04"))
        {
            return "application/zip";
        }

        if (StartsWith(content, "Rar!\x1A\x07\x00") ||
            StartsWith(content, "Rar!\x1A\x07\x01\x00"))
        {
            return "application/x-rar-compressed";
        }

        // WebAssembly。
        if (StartsWith(content, "\x00\x61\x73\x6D"))
        {
            return "application/wasm";
        }

        // 最后：文本嗅探（与 Go textSig 一致，仅排除控制字符，不校验 UTF-8）。
        if (IsPlainText(trimmed))
        {
            return "text/plain; charset=utf-8";
        }

        return DefaultContentType;
    }

    /// <summary>
    /// 判断内容是否以指定 ASCII 签名开头（大小写敏感，<paramref name="signature"/>
    /// 可包含 <c>\x00</c> 等非打印字符）。
    /// </summary>
    /// <param name="data">待检查内容。</param>
    /// <param name="signature">签名，每个字符按单字节比较。</param>
    /// <returns>匹配返回 true。</param>
    private static bool StartsWith(ReadOnlySpan<byte> data, string signature)
    {
        if (data.Length < signature.Length)
        {
            return false;
        }

        for (var i = 0; i < signature.Length; i++)
        {
            if (data[i] != (byte)signature[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 匹配 HTML 标签签名（对应 Go <c>htmlSig.match</c>）：
    /// 签名大小写不敏感，且签名之后必须紧跟标签终止符（空格或 <c>&gt;</c>）。
    /// </summary>
    /// <param name="data">已跳过前导空白的内容。</param>
    /// <param name="signature">HTML 标签签名（大写）。</param>
    /// <returns>匹配返回 true。</returns>
    private static bool MatchesHtmlSignature(ReadOnlySpan<byte> data, string signature)
    {
        if (data.Length < signature.Length + 1)
        {
            return false;
        }

        for (var i = 0; i < signature.Length; i++)
        {
            var actual = data[i];
            if (actual is >= (byte)'a' and <= (byte)'z')
            {
                actual -= 32;
            }

            if (actual != (byte)signature[i])
            {
                return false;
            }
        }

        // 后面的字节必须是标签终止符（与 Go 的 isTT 一致）。
        var next = data[signature.Length];
        return next == (byte)' ' || next == (byte)'>';
    }

    /// <summary>
    /// 判断是否为 WebP：<c>RIFF????WEBPVP</c>（其中 ???? 为文件长度，掩码忽略）。
    /// </summary>
    /// <param name="data">待检查内容。</param>
    /// <returns>是 WebP 返回 true。</returns>
    private static bool IsWebp(ReadOnlySpan<byte> data)
    {
        return data.Length >= 14 && StartsWith(data, "RIFF") && StartsWith(data[8..], "WEBPVP");
    }

    /// <summary>
    /// 判断是否为 AIFF：<c>FORM????AIFF</c>。
    /// </summary>
    /// <param name="data">待检查内容。</param>
    /// <returns>是 AIFF 返回 true。</returns>
    private static bool IsAiff(ReadOnlySpan<byte> data)
    {
        return data.Length >= 12 && StartsWith(data, "FORM") && StartsWith(data[8..], "AIFF");
    }

    /// <summary>
    /// 判断是否为 AVI：<c>RIFF????AVI </c>。
    /// </summary>
    /// <param name="data">待检查内容。</param>
    /// <returns>是 AVI 返回 true。</returns>
    private static bool IsAvi(ReadOnlySpan<byte> data)
    {
        return data.Length >= 12 && StartsWith(data, "RIFF") && StartsWith(data[8..], "AVI ");
    }

    /// <summary>
    /// 判断是否为 WAVE：<c>RIFF????WAVE</c>。
    /// </summary>
    /// <param name="data">待检查内容。</param>
    /// <returns>是 WAVE 返回 true。</returns>
    private static bool IsWave(ReadOnlySpan<byte> data)
    {
        return data.Length >= 12 && StartsWith(data, "RIFF") && StartsWith(data[8..], "WAVE");
    }

    /// <summary>
    /// 判断是否为 MP4（对齐 Go <c>mp4Sig.match</c>）：
    /// 盒长度合法（≥12、4 字节对齐且位于嗅探窗口内）、<c>ftyp</c> 位于第 5–8 字节，
    /// 且 major brand 之后（跳过版本号 4 字节）的某个品牌以 <c>mp4</c> 开头。
    /// </summary>
    /// <param name="data">待检查内容。</param>
    /// <returns>是 MP4 返回 true。</returns>
    private static bool IsMp4(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12)
        {
            return false;
        }

        var boxSize = BinaryPrimitives.ReadUInt32BigEndian(data);
        if (boxSize < 12 || boxSize % 4 != 0 || data.Length < boxSize)
        {
            return false;
        }

        if (!StartsWith(data[4..], "ftyp"))
        {
            return false;
        }

        for (var offset = 8; offset < (int)boxSize; offset += 4)
        {
            // 跳过 major brand（第 12–16 字节为版本号，与 Go 一致）。
            if (offset == 12)
            {
                continue;
            }

            if (StartsWith(data[offset..], "mp4"))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 判断是否为嵌入式 OpenType（EOT）：34 个 <c>\x00</c> 后紧跟 <c>LP</c>。
    /// </summary>
    /// <param name="data">待检查内容。</param>
    /// <returns>是 EOT 返回 true。</returns>
    private static bool IsEmbeddedOpenType(ReadOnlySpan<byte> data)
    {
        if (data.Length < 36)
        {
            return false;
        }

        for (var i = 0; i < 34; i++)
        {
            if (data[i] != 0)
            {
                return false;
            }
        }

        return StartsWith(data[34..], "LP");
    }

    /// <summary>
    /// 判断内容是否为纯文本（对应 Go <c>textSig.match</c>）：
    /// 自第一个非空白字节起不得出现控制字符（<c>0x09/0x0A/0x0C/0x0D/0x1B/0x7F</c> 除外）。
    /// </summary>
    /// <param name="data">已跳过前导空白的内容。</param>
    /// <returns>是纯文本返回 true。</returns>
    private static bool IsPlainText(ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            if (b <= 0x08 ||
                b == 0x0B ||
                (b >= 0x0E && b <= 0x1A) ||
                (b >= 0x1C && b <= 0x1F))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 判断字节是否为空白字符（与 Go <c>isWS</c> 一致：空格、制表、换行、换页、回车）。
    /// </summary>
    /// <param name="b">字节值。</param>
    /// <returns>是空白返回 true。</returns>
    private static bool IsWhitespace(byte b)
    {
        return b == (byte)' ' ||
               b == (byte)'\t' ||
               b == (byte)'\n' ||
               b == (byte)'\f' ||
               b == (byte)'\r';
    }
}
