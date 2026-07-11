using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;

namespace Wails.Net.Application.Services.Updater;

/// <summary>
/// 更新解压器，负责解压归档文件并执行安装。
/// 对应 Wails v3 Go 版本 extract.go 中的解压逻辑。
/// </summary>
public static class UpdateExtractor
{
    /// <summary>
    /// 异步解压归档文件到目标目录。
    /// 根据文件扩展名自动选择解压方式（.zip / .tar.gz / .tgz / .tar）。
    /// </summary>
    /// <param name="archivePath">归档文件路径。</param>
    /// <param name="targetDirectory">目标解压目录。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>表示解压操作的异步任务。</returns>
    /// <exception cref="FileNotFoundException">归档文件不存在。</exception>
    /// <exception cref="NotSupportedException">不支持的归档格式。</exception>
    public static async Task ExtractAsync(string archivePath, string targetDirectory, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException("归档文件不存在。", archivePath);
        }

        Directory.CreateDirectory(targetDirectory);

        var lowerPath = archivePath.ToLowerInvariant();

        if (lowerPath.EndsWith(".zip"))
        {
            ZipFile.ExtractToDirectory(archivePath, targetDirectory, overwriteFiles: true);
            return;
        }

        if (lowerPath.EndsWith(".tar.gz") || lowerPath.EndsWith(".tgz"))
        {
            await ExtractTarGzAsync(archivePath, targetDirectory, ct);
            return;
        }

        if (lowerPath.EndsWith(".tar"))
        {
            await ExtractTarAsync(archivePath, targetDirectory, ct);
            return;
        }

        throw new NotSupportedException($"不支持的归档格式：{Path.GetExtension(archivePath)}");
    }

    /// <summary>
    /// 异步安装更新。根据文件类型和操作系统选择安装方式：
    /// <list type="bullet">
    /// <item>Windows .msi：使用 msiexec 静默安装。</item>
    /// <item>Windows .exe：使用 /SILENT /SP- 静默安装。</item>
    /// <item>Linux .deb：使用 dpkg -i 安装。</item>
    /// <item>Linux .rpm：使用 rpm -i 安装。</item>
    /// <item>Linux .AppImage：chmod +x 后移动到 /usr/local/bin。</item>
    /// <item>其他：直接文件替换，委托给 HelperProcess。</item>
    /// </list>
    /// 对于直接文件替换，当前进程将退出并由 helper 进程接管。
    /// </summary>
    /// <param name="extractedPath">解压后的更新文件路径。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>表示安装操作的异步任务。</returns>
    /// <exception cref="FileNotFoundException">更新文件不存在。</exception>
    public static async Task InstallUpdateAsync(string extractedPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!File.Exists(extractedPath))
        {
            throw new FileNotFoundException("更新文件不存在。", extractedPath);
        }

        var ext = Path.GetExtension(extractedPath).ToLowerInvariant();

        if (OperatingSystem.IsWindows())
        {
            switch (ext)
            {
                case ".msi":
                    await RunProcessAsync("msiexec", $"/i \"{extractedPath}\" /quiet", ct);
                    return;
                case ".exe":
                    await RunProcessAsync(extractedPath, "/SILENT /SP-", ct);
                    return;
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            switch (ext)
            {
                case ".deb":
                    await RunProcessAsync("dpkg", $"-i \"{extractedPath}\"", ct);
                    return;
                case ".rpm":
                    await RunProcessAsync("rpm", $"-i \"{extractedPath}\"", ct);
                    return;
                case ".appimage":
                    await RunProcessAsync("chmod", $"+x \"{extractedPath}\"", ct);
                    var appDestPath = Path.Combine("/usr/local/bin", Path.GetFileName(extractedPath));
                    File.Move(extractedPath, appDestPath, overwrite: true);
                    return;
            }
        }

        // 非安装包格式，使用 helper 进程进行直接文件替换
        var targetPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("无法确定当前进程可执行文件路径。");
        HelperProcess.RelaunchAsHelper(extractedPath, targetPath);
    }

    /// <summary>
    /// 解压 .tar.gz 归档文件，带路径遍历保护。
    /// </summary>
    /// <param name="archivePath">归档文件路径。</param>
    /// <param name="targetDirectory">目标目录。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>表示解压操作的异步任务。</returns>
    private static async Task ExtractTarGzAsync(string archivePath, string targetDirectory, CancellationToken ct)
    {
        await using var fs = new FileStream(
            archivePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);
        await using var gz = new GZipStream(fs, CompressionMode.Decompress, leaveOpen: true);
        using var tarReader = new TarReader(gz, leaveOpen: true);

        var fullTargetDir = Path.GetFullPath(targetDirectory);

        TarEntry? entry;
        while ((entry = await tarReader.GetNextEntryAsync(cancellationToken: ct)) is not null)
        {
            ct.ThrowIfCancellationRequested();

            var normalizedEntryName = entry.Name.Replace('\\', '/');
            var fullPath = Path.GetFullPath(Path.Combine(targetDirectory, normalizedEntryName));

            if (!IsPathInDirectory(fullPath, fullTargetDir))
            {
                throw new IOException($"归档条目路径逃逸目标目录：{entry.Name}");
            }

            entry.ExtractToFile(fullPath, overwrite: true);
        }
    }

    /// <summary>
    /// 解压 .tar 归档文件，带路径遍历保护。
    /// </summary>
    /// <param name="archivePath">归档文件路径。</param>
    /// <param name="targetDirectory">目标目录。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>表示解压操作的异步任务。</returns>
    private static async Task ExtractTarAsync(string archivePath, string targetDirectory, CancellationToken ct)
    {
        await using var fs = new FileStream(
            archivePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);
        using var tarReader = new TarReader(fs, leaveOpen: true);

        var fullTargetDir = Path.GetFullPath(targetDirectory);

        TarEntry? entry;
        while ((entry = await tarReader.GetNextEntryAsync(cancellationToken: ct)) is not null)
        {
            ct.ThrowIfCancellationRequested();

            var normalizedEntryName = entry.Name.Replace('\\', '/');
            var fullPath = Path.GetFullPath(Path.Combine(targetDirectory, normalizedEntryName));

            if (!IsPathInDirectory(fullPath, fullTargetDir))
            {
                throw new IOException($"归档条目路径逃逸目标目录：{entry.Name}");
            }

            entry.ExtractToFile(fullPath, overwrite: true);
        }
    }

    /// <summary>
    /// 检查指定路径是否在目标目录内（路径遍历保护）。
    /// </summary>
    /// <param name="path">要检查的完整路径。</param>
    /// <param name="directory">目标目录的完整路径。</param>
    /// <returns>路径在目录内返回 true，否则返回 false。</returns>
    private static bool IsPathInDirectory(string path, string directory)
    {
        var separator = Path.DirectorySeparatorChar.ToString();
        var dirWithSeparator = directory.EndsWith(separator, StringComparison.Ordinal)
            ? directory
            : directory + separator;

        if (OperatingSystem.IsWindows())
        {
            return path.StartsWith(dirWithSeparator, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(path, directory, StringComparison.OrdinalIgnoreCase);
        }

        return path.StartsWith(dirWithSeparator, StringComparison.Ordinal) ||
               string.Equals(path, directory, StringComparison.Ordinal);
    }

    /// <summary>
    /// 异步运行外部进程并等待其退出。
    /// </summary>
    /// <param name="fileName">可执行文件路径。</param>
    /// <param name="arguments">命令行参数。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>表示进程执行操作的异步任务。</returns>
    /// <exception cref="InvalidOperationException">进程退出码非零。</exception>
    private static async Task RunProcessAsync(string fileName, string arguments, CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"安装进程以退出码 {process.ExitCode} 退出。");
        }
    }
}
