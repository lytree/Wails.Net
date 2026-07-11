using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Wails.Net.Cli.Build;

/// <summary>
/// 打包结果。
/// </summary>
public sealed class PackageResult
{
    /// <summary>是否成功。</summary>
    public bool Success { get; set; }

    /// <summary>错误消息。</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>打包输出文件路径。</summary>
    public string? OutputPath { get; set; }

    /// <summary>校验和文件路径（若生成）。</summary>
    public string? ChecksumPath { get; set; }
}

/// <summary>
/// 打包器，将发布产物打包为可分发的压缩包或安装程序。
/// 对应 Wails v3 Go 版本 internal/project/package.go 中的打包逻辑。
/// </summary>
public sealed class Packager
{
    /// <summary>
    /// tar 块大小（字节）。
    /// </summary>
    private const int TarBlockSize = 512;

    /// <summary>
    /// 将指定发布目录打包为可分发文件。
    /// </summary>
    /// <param name="publishDir">发布输出目录。</param>
    /// <param name="options">打包选项。</param>
    /// <returns>打包结果。</returns>
    public async Task<PackageResult> PackageAsync(string publishDir, PackageOptions options)
    {
        if (!Directory.Exists(publishDir))
        {
            return new PackageResult
            {
                Success = false,
                ErrorMessage = $"发布目录不存在：{publishDir}",
            };
        }

        Directory.CreateDirectory(options.OutputDirectory);

        var extension = GetExtension(options.Format);
        var fileName = $"{options.AppName}-{options.Version}{extension}";
        var outputPath = Path.Combine(options.OutputDirectory, fileName);

        // 删除已存在的输出文件
        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        try
        {
            switch (options.Format)
            {
                case PackageFormat.Zip:
                    ZipFile.CreateFromDirectory(publishDir, outputPath, CompressionLevel.Optimal, includeBaseDirectory: false);
                    break;

                case PackageFormat.TarGz:
                    await CreateTarGzAsync(publishDir, outputPath, options.IncludeSymbols);
                    break;

                case PackageFormat.Nsis:
                case PackageFormat.AppImage:
                    return new PackageResult
                    {
                        Success = false,
                        ErrorMessage = $"格式 {options.Format} 暂未实现，请使用 zip 或 targz",
                    };

                default:
                    return new PackageResult
                    {
                        Success = false,
                        ErrorMessage = $"不支持的打包格式：{options.Format}",
                    };
            }
        }
        catch (Exception ex)
        {
            return new PackageResult
            {
                Success = false,
                ErrorMessage = $"打包失败：{ex.Message}",
            };
        }

        var result = new PackageResult
        {
            Success = true,
            OutputPath = outputPath,
        };

        if (options.GenerateChecksum)
        {
            result.ChecksumPath = await GenerateChecksumAsync(outputPath, options);
        }

        return result;
    }

    /// <summary>
    /// 获取打包格式的文件扩展名。
    /// </summary>
    /// <param name="format">打包格式。</param>
    /// <returns>扩展名（含前导点）。</returns>
    private static string GetExtension(PackageFormat format) => format switch
    {
        PackageFormat.Zip => ".zip",
        PackageFormat.TarGz => ".tar.gz",
        PackageFormat.Nsis => ".exe",
        PackageFormat.AppImage => ".AppImage",
        _ => ".zip",
    };

    /// <summary>
    /// 创建 tar.gz 压缩包。
    /// </summary>
    /// <param name="sourceDir">源目录。</param>
    /// <param name="outputPath">输出路径。</param>
    /// <param name="includeSymbols">是否包含调试符号文件。</param>
    private static async Task CreateTarGzAsync(string sourceDir, string outputPath, bool includeSymbols)
    {
        await using var outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await using var gzipStream = new GZipStream(outputStream, CompressionLevel.Optimal);
        await WriteTarAsync(gzipStream, sourceDir, includeSymbols);
    }

    /// <summary>
    /// 将目录内容写入 tar 流。
    /// </summary>
    /// <param name="output">输出流。</param>
    /// <param name="sourceDir">源目录。</param>
    /// <param name="includeSymbols">是否包含调试符号文件。</param>
    private static async Task WriteTarAsync(Stream output, string sourceDir, bool includeSymbols)
    {
        var baseDir = new DirectoryInfo(sourceDir);
        var allFiles = baseDir.EnumerateFiles("*", SearchOption.AllDirectories);

        foreach (var file in allFiles)
        {
            var relativePath = Path.GetRelativePath(sourceDir, file.FullName);
            // 统一为正斜杠（tar 规范）
            relativePath = relativePath.Replace('\\', '/');

            if (!includeSymbols && IsSymbolFile(relativePath))
            {
                continue;
            }

            await WriteTarEntryAsync(output, file, relativePath);
        }

        // 写入两个空块作为文件结束标记
        var endBlock = new byte[TarBlockSize * 2];
        await output.WriteAsync(endBlock);
    }

    /// <summary>
    /// 写入单个 tar 条目。
    /// </summary>
    /// <param name="output">输出流。</param>
    /// <param name="file">文件信息。</param>
    /// <param name="archivePath">归档内路径。</param>
    private static async Task WriteTarEntryAsync(Stream output, FileInfo file, string archivePath)
    {
        var header = CreateTarHeader(file, archivePath);
        await output.WriteAsync(header);

        var fileBytes = await File.ReadAllBytesAsync(file.FullName);
        await output.WriteAsync(fileBytes);

        // 填充至块大小对齐
        var remainder = fileBytes.Length % TarBlockSize;
        if (remainder > 0)
        {
            var padding = new byte[TarBlockSize - remainder];
            await output.WriteAsync(padding);
        }
    }

    /// <summary>
    /// 创建 tar 文件头（512 字节）。
    /// </summary>
    /// <param name="file">文件信息。</param>
    /// <param name="archivePath">归档内路径。</param>
    /// <returns>512 字节 tar 头。</returns>
    private static byte[] CreateTarHeader(FileInfo file, string archivePath)
    {
        var header = new byte[TarBlockSize];

        // name (0-99, 100 bytes)
        var nameBytes = Encoding.UTF8.GetBytes(archivePath);
        var nameLength = Math.Min(nameBytes.Length, 99);
        Array.Copy(nameBytes, 0, header, 0, nameLength);

        // mode (100-107, 8 bytes) - 默认 0644（八进制）= 420（十进制）
        WriteOctal(header, 100, 8, 420);

        // uid (108-115, 8 bytes)
        WriteOctal(header, 108, 8, 0);

        // gid (116-123, 8 bytes)
        WriteOctal(header, 116, 8, 0);

        // size (124-135, 12 bytes)
        WriteOctal(header, 124, 12, file.Length);

        // mtime (136-147, 12 bytes) - Unix 时间戳
        var mtime = new DateTimeOffset(file.LastWriteTimeUtc).ToUnixTimeSeconds();
        WriteOctal(header, 136, 12, mtime);

        // chksum (148-155, 8 bytes) - 先填充空格
        for (var i = 148; i < 156; i++)
        {
            header[i] = (byte)' ';
        }

        // typeflag (156, 1 byte) - 普通文件 '0'
        header[156] = (byte)'0';

        // linkname (157-256, 100 bytes) - 空

        // magic (257-262, 6 bytes) - "ustar\0"
        var magic = Encoding.ASCII.GetBytes("ustar\0");
        Array.Copy(magic, 0, header, 257, 6);

        // version (263-264, 2 bytes) - "00"
        header[263] = (byte)'0';
        header[264] = (byte)'0';

        // 计算校验和（所有字节的简单相加，包括校验和位置的空格）
        var checksum = 0;
        foreach (var b in header)
        {
            checksum += b;
        }
        WriteOctal(header, 148, 7, checksum);
        header[155] = (byte)' '; // 校验和字段以空格结尾

        return header;
    }

    /// <summary>
    /// 将数值以八进制 ASCII 形式写入 tar 头部字段。
    /// </summary>
    /// <param name="buffer">头部缓冲区。</param>
    /// <param name="offset">字段起始偏移。</param>
    /// <param name="length">字段长度。</param>
    /// <param name="value">要写入的数值。</param>
    private static void WriteOctal(byte[] buffer, int offset, int length, long value)
    {
        var octal = Convert.ToString(value, 8);
        var bytes = Encoding.ASCII.GetBytes(octal);

        // 字段格式：数值后跟空字节填充
        // 可写入的字节数 = length - 1（保留一个空字节）
        var maxWrite = length - 1;
        var writeLength = Math.Min(bytes.Length, maxWrite);

        // 先清零字段
        for (var i = 0; i < length; i++)
        {
            buffer[offset + i] = 0;
        }

        Array.Copy(bytes, 0, buffer, offset, writeLength);
    }

    /// <summary>
    /// 判断文件是否为调试符号文件。
    /// </summary>
    /// <param name="path">文件相对路径。</param>
    /// <returns>是调试符号文件返回 true。</returns>
    private static bool IsSymbolFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".pdb" or ".dbg";
    }

    /// <summary>
    /// 生成 SHA256 校验和文件。
    /// </summary>
    /// <param name="packagePath">包文件路径。</param>
    /// <param name="options">打包选项。</param>
    /// <returns>校验和文件路径。</returns>
    private static async Task<string> GenerateChecksumAsync(string packagePath, PackageOptions options)
    {
        await using var stream = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = await SHA256.HashDataAsync(stream);
        var hashHex = Convert.ToHexStringLower(hash);

        var fileName = Path.GetFileName(packagePath);
        var checksumFileName = $"{fileName}.sha256";
        var checksumPath = Path.Combine(options.OutputDirectory, checksumFileName);

        var content = $"{hashHex}  {fileName}{Environment.NewLine}";
        await File.WriteAllTextAsync(checksumPath, content);

        return checksumPath;
    }
}
