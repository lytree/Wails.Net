using System.Diagnostics;
using System.Text;
using Wails.Net.Application.Clipboard;

namespace Wails.Net.Application.Clipboard;

/// <summary>
/// Linux 剪贴板实现，基于 xclip/xsel 命令行工具。
/// 对应 Wails v3 Go 版本 clipboard_linux.go。
/// 优先使用 xclip（支持 MIME 类型），不可用时回退到 xsel（仅支持纯文本）。
/// 两者均不可用时操作为空操作，不抛出异常。
/// </summary>
public sealed class LinuxClipboard : IClipboardImpl
{
    /// <summary>
    /// xclip 命令名称。
    /// </summary>
    private const string XclipTool = "xclip";

    /// <summary>
    /// xsel 命令名称。
    /// </summary>
    private const string XselTool = "xsel";

    /// <summary>
    /// 剪贴板文本 MIME 目标类型。
    /// </summary>
    private const string TextTarget = "UTF8_STRING";

    /// <summary>
    /// 剪贴板 HTML MIME 目标类型。
    /// </summary>
    private const string HtmlTarget = "text/html";

    /// <summary>
    /// 剪贴板 PNG 图片 MIME 目标类型。
    /// </summary>
    private const string ImageTarget = "image/png";

    /// <summary>
    /// 剪贴板文件 URI 列表 MIME 目标类型。
    /// </summary>
    private const string UriListTarget = "text/uri-list";

    /// <summary>
    /// 设置剪贴板文本内容。
    /// 通过 xclip 或 xsel 将文本写入系统剪贴板。
    /// </summary>
    /// <param name="text">要设置的文本。</param>
    public void SetText(string text)
    {
        var data = Encoding.UTF8.GetBytes(text);
        if (!WriteWithXclip(TextTarget, data))
        {
            WriteWithXsel(data);
        }
    }

    /// <summary>
    /// 获取剪贴板文本内容。
    /// 通过 xclip 或 xsel 从系统剪贴板读取文本。
    /// </summary>
    /// <returns>剪贴板中的文本，读取失败返回空字符串。</returns>
    public string GetText()
    {
        var data = ReadWithXclip(TextTarget);
        if (data is null || data.Length == 0)
        {
            data = ReadWithXsel();
        }

        if (data is null || data.Length == 0)
        {
            return string.Empty;
        }

        return Encoding.UTF8.GetString(data);
    }

    /// <summary>
    /// 设置剪贴板 HTML 内容。
    /// 通过 xclip 将 HTML 内容写入系统剪贴板（xclip 支持 MIME 类型）。
    /// xsel 不支持 MIME 类型，回退为写入纯文本回退内容。
    /// </summary>
    /// <param name="html">要设置的 HTML 内容。</param>
    /// <param name="fallbackText">不支持 HTML 时的回退文本。</param>
    public void SetHTML(string html, string fallbackText)
    {
        // 尝试用 xclip 设置 HTML 内容。
        var htmlData = Encoding.UTF8.GetBytes(html);
        if (WriteWithXclip(HtmlTarget, htmlData))
        {
            return;
        }

        // xclip 不可用时，回退为设置纯文本回退内容。
        var fallbackData = Encoding.UTF8.GetBytes(fallbackText);
        if (!WriteWithXclip(TextTarget, fallbackData))
        {
            WriteWithXsel(fallbackData);
        }
    }

    /// <summary>
    /// 获取剪贴板 HTML 内容。
    /// 通过 xclip 从系统剪贴板读取 HTML 内容（xclip 支持 MIME 类型）。
    /// </summary>
    /// <returns>剪贴板中的 HTML 内容，读取失败返回空字符串。</returns>
    public string GetHTML()
    {
        var data = ReadWithXclip(HtmlTarget);
        if (data is null || data.Length == 0)
        {
            return string.Empty;
        }

        return Encoding.UTF8.GetString(data);
    }

    /// <summary>
    /// 设置剪贴板图片。
    /// 通过 xclip 将 PNG 图片数据写入系统剪贴板（xclip 支持 MIME 类型）。
    /// xsel 不支持图片 MIME 类型，因此 xclip 不可用时操作为空操作。
    /// </summary>
    /// <param name="imageData">图片字节数据。</param>
    public void SetImage(byte[] imageData)
    {
        if (imageData is null || imageData.Length == 0)
        {
            return;
        }

        // 尝试用 xclip 设置图片。
        WriteWithXclip(ImageTarget, imageData);
    }

    /// <summary>
    /// 获取剪贴板图片。
    /// 通过 xclip 从系统剪贴板读取 PNG 图片数据（xclip 支持 MIME 类型）。
    /// </summary>
    /// <returns>图片字节数据，读取失败返回 null。</returns>
    public byte[]? GetImage()
    {
        return ReadWithXclip(ImageTarget);
    }

    /// <summary>
    /// 清空剪贴板内容。
    /// 通过写入空字符串实现清空。
    /// </summary>
    public void Clear()
    {
        var emptyData = Array.Empty<byte>();
        if (!WriteWithXclip(TextTarget, emptyData))
        {
            WriteWithXsel(emptyData);
        }
    }

    /// <summary>
    /// 设置剪贴板文件列表。
    /// 通过 xclip 设置 text/uri-list 目标，每行一个 file:// URI。
    /// 对应 Wails v3 Go 版本 clipboard_linux.go 中的 SetFiles。
    /// </summary>
    /// <param name="files">文件路径数组。</param>
    public void SetFiles(string[] files)
    {
        if (files is null || files.Length == 0)
        {
            return;
        }

        // 构建 text/uri-list 格式：每行一个 file:// URI，以换行结尾。
        var uris = files.Select(f => new Uri(f).AbsoluteUri);
        var uriList = string.Join("\n", uris) + "\n";
        var data = Encoding.UTF8.GetBytes(uriList);
        WriteWithXclip(UriListTarget, data);
    }

    /// <summary>
    /// 获取剪贴板文件列表。
    /// 从 text/uri-list 目标读取并解析 file:// URI。
    /// 对应 Wails v3 Go 版本 clipboard_linux.go 中的 GetFiles。
    /// </summary>
    /// <returns>文件路径数组，若剪贴板无文件则返回空数组。</returns>
    public string[] GetFiles()
    {
        var data = ReadWithXclip(UriListTarget);
        if (data is null || data.Length == 0)
        {
            return Array.Empty<string>();
        }

        var uriList = Encoding.UTF8.GetString(data);
        var files = new List<string>();
        foreach (var line in uriList.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                // 将 file:// URI 转换为本地路径。
                files.Add(Uri.UnescapeDataString(trimmed["file://".Length..]));
            }
        }

        return files.ToArray();
    }

    /// <summary>
    /// 通过 xclip 向剪贴板写入数据。
    /// </summary>
    /// <param name="target">MIME 目标类型（如 "UTF8_STRING"、"text/html"、"image/png"）。</param>
    /// <param name="data">要写入的字节数据。</param>
    /// <returns>是否成功。</returns>
    private static bool WriteWithXclip(string target, byte[] data)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = XclipTool,
                Arguments = $"-selection clipboard -t {target}",
                UseShellExecute = false,
                RedirectStandardInput = true,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            process.StandardInput.BaseStream.Write(data, 0, data.Length);
            process.StandardInput.BaseStream.Close();
            process.WaitForExit(2000);
            return process.ExitCode == 0;
        }
        catch
        {
            // xclip 不可用或执行失败时返回 false。
            return false;
        }
    }

    /// <summary>
    /// 通过 xclip 从剪贴板读取数据。
    /// </summary>
    /// <param name="target">MIME 目标类型。</param>
    /// <returns>读取到的字节数据，失败返回 null。</returns>
    private static byte[]? ReadWithXclip(string target)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = XclipTool,
                Arguments = $"-selection clipboard -t {target} -o",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            using var ms = new MemoryStream();
            process.StandardOutput.BaseStream.CopyTo(ms);
            process.WaitForExit(2000);
            return process.ExitCode == 0 ? ms.ToArray() : null;
        }
        catch
        {
            // xclip 不可用或执行失败时返回 null。
            return null;
        }
    }

    /// <summary>
    /// 通过 xsel 向剪贴板写入数据（仅支持纯文本）。
    /// </summary>
    /// <param name="data">要写入的字节数据。</param>
    /// <returns>是否成功。</returns>
    private static bool WriteWithXsel(byte[] data)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = XselTool,
                Arguments = "--clipboard --input",
                UseShellExecute = false,
                RedirectStandardInput = true,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            process.StandardInput.BaseStream.Write(data, 0, data.Length);
            process.StandardInput.BaseStream.Close();
            process.WaitForExit(2000);
            return process.ExitCode == 0;
        }
        catch
        {
            // xsel 不可用或执行失败时返回 false。
            return false;
        }
    }

    /// <summary>
    /// 通过 xsel 从剪贴板读取数据（仅支持纯文本）。
    /// </summary>
    /// <returns>读取到的字节数据，失败返回 null。</returns>
    private static byte[]? ReadWithXsel()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = XselTool,
                Arguments = "--clipboard --output",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            using var ms = new MemoryStream();
            process.StandardOutput.BaseStream.CopyTo(ms);
            process.WaitForExit(2000);
            return process.ExitCode == 0 ? ms.ToArray() : null;
        }
        catch
        {
            // xsel 不可用或执行失败时返回 null。
            return null;
        }
    }
}
