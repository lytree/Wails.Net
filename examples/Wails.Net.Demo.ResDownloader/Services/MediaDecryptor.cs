namespace Wails.Net.Demo.ResDownloader.Services;

/// <summary>
/// 加密媒体解密（对应 res-downloader 的「视频解密（视频号）」）。
/// <para>
/// 微信视频号的加密视频只对文件头部做一次异或：密钥由页面下发的 <c>decodeKey</c> 做 Base64 解码得到，
/// 与文件前 N 个字节逐字节异或即可还原。此实现与 res-downloader 的 <c>decodeWxFile</c> 等价。
/// </para>
/// </summary>
public static class MediaDecryptor
{
    /// <summary>
    /// 复制一份原文件并就地解密，返回解密后的文件路径。
    /// </summary>
    /// <param name="sourcePath">已下载的加密文件。</param>
    /// <param name="base64Key">Base64 编码的解密密钥。</param>
    /// <returns>解密后的文件路径。</returns>
    /// <exception cref="InvalidOperationException">参数或文件不合法时抛出。</exception>
    public static string DecryptFile(string sourcePath, string base64Key)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            throw new InvalidOperationException("请先下载该资源，再执行解密");
        }

        if (string.IsNullOrWhiteSpace(base64Key))
        {
            throw new InvalidOperationException("该资源没有可用的解密密钥");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(base64Key.Trim());
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("解密密钥格式不正确");
        }

        if (key.Length == 0)
        {
            throw new InvalidOperationException("解密密钥为空");
        }

        var directory = Path.GetDirectoryName(sourcePath) ?? AppPaths.DefaultDownloadDirectory;
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var extension = Path.GetExtension(sourcePath);
        var target = Path.Combine(directory, name + "_decrypt" + extension);

        File.Copy(sourcePath, target, overwrite: true);

        using var stream = new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var count = (int)Math.Min(key.Length, stream.Length);
        if (count <= 0)
        {
            throw new InvalidOperationException("文件为空，无法解密");
        }

        var buffer = new byte[count];
        var read = stream.Read(buffer, 0, count);
        for (var i = 0; i < read; i++)
        {
            buffer[i] ^= key[i % key.Length];
        }

        stream.Seek(0, SeekOrigin.Begin);
        stream.Write(buffer, 0, read);
        stream.Flush();

        return target;
    }
}
