using System.Diagnostics;
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
                    await PackageNsisAsync(publishDir, outputPath, options);
                    break;

                case PackageFormat.AppImage:
                    await PackageAppImageAsync(publishDir, outputPath, options);
                    break;

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
    /// 使用 NSIS 创建 Windows 安装程序。
    /// 对应 Wails v3 Go 版本 internal/project/package.go 中的 NSIS 打包逻辑。
    /// </summary>
    /// <param name="sourceDir">发布输出目录。</param>
    /// <param name="outputExePath">安装程序输出路径。</param>
    /// <param name="options">打包选项。</param>
    /// <exception cref="PlatformNotSupportedException">非 Windows 平台。</exception>
    /// <exception cref="FileNotFoundException">未找到 makensis 或主可执行文件。</exception>
    private static async Task PackageNsisAsync(string sourceDir, string outputExePath, PackageOptions options)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("NSIS 安装程序只能在 Windows 上生成");
        }

        var makensis = FindExecutableInPath("makensis.exe") ?? FindNsisInCommonLocations();
        if (makensis is null)
        {
            throw new FileNotFoundException(
                "未找到 makensis.exe，请安装 NSIS（https://nsis.sourceforge.io/）并确保其在 PATH 中");
        }

        var exeName = FindMainExecutable(sourceDir, options.AppName)
            ?? throw new FileNotFoundException($"在发布目录中未找到主可执行文件：{sourceDir}");

        var scriptPath = Path.Combine(Path.GetTempPath(), $"wailsnet-{Guid.NewGuid():N}.nsi");
        try
        {
            var script = GenerateNsisScript(
                options.AppName, options.Version, options.Publisher,
                Path.GetFullPath(outputExePath), Path.GetFullPath(sourceDir), exeName);
            await File.WriteAllTextAsync(scriptPath, script);

            var (exitCode, output) = await RunProcessAsync(makensis, scriptPath);
            if (exitCode != 0)
            {
                throw new InvalidOperationException($"makensis 退出码 {exitCode}：{output}");
            }
        }
        finally
        {
            if (File.Exists(scriptPath))
            {
                try { File.Delete(scriptPath); } catch { }
            }
        }
    }

    /// <summary>
    /// 生成 NSIS 脚本内容。
    /// </summary>
    /// <param name="appName">应用名称。</param>
    /// <param name="version">版本号。</param>
    /// <param name="publisher">发布者。</param>
    /// <param name="outputExePath">安装程序输出路径。</param>
    /// <param name="sourceDir">源文件目录。</param>
    /// <param name="exeName">主可执行文件名。</param>
    /// <returns>NSIS 脚本内容。</returns>
    internal static string GenerateNsisScript(
        string appName, string version, string publisher,
        string outputExePath, string sourceDir, string exeName)
    {
        // NSIS 脚本中的路径使用反斜杠
        sourceDir = sourceDir.Replace('/', '\\');
        outputExePath = outputExePath.Replace('/', '\\');

        return $$"""
; NSIS 脚本 - 由 Wails.Net 自动生成
!define APPNAME "{{appName}}"
!define APPVERSION "{{version}}"
!define APPPUBLISHER "{{publisher}}"

Name "${APPNAME}"
OutFile "{{outputExePath}}"
InstallDir "$PROGRAMFILES64\${APPNAME}"
RequestExecutionLevel admin

Page directory
Page instfiles

Section "Install"
    SetOutPath $INSTDIR
    File /r "{{sourceDir}}\*.*"

    CreateDirectory "$SMPROGRAMS\${APPNAME}"
    CreateShortcut "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk" "$INSTDIR\{{exeName}}"
    CreateShortcut "$DESKTOP\${APPNAME}.lnk" "$INSTDIR\{{exeName}}"

    WriteUninstaller "$INSTDIR\uninstall.exe"

    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayName" "${APPNAME}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "UninstallString" "$INSTDIR\uninstall.exe"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayVersion" "${APPVERSION}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "Publisher" "${APPPUBLISHER}"
SectionEnd

Section "Uninstall"
    Delete "$INSTDIR\uninstall.exe"
    RMDir /r "$INSTDIR"
    Delete "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk"
    RMDir "$SMPROGRAMS\${APPNAME}"
    Delete "$DESKTOP\${APPNAME}.lnk"
    DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}"
SectionEnd
""";
    }

    /// <summary>
    /// 在 PATH 环境变量中查找可执行文件。
    /// </summary>
    /// <param name="fileName">可执行文件名（含扩展名）。</param>
    /// <returns>完整路径，若未找到则返回 null。</returns>
    internal static string? FindExecutableInPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var separator = OperatingSystem.IsWindows() ? ';' : ':';
        foreach (var dir in path.Split(separator, StringSplitOptions.RemoveEmptyEntries))
        {
            var fullPath = Path.Combine(dir, fileName);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return null;
    }

    /// <summary>
    /// 在常见安装位置查找 NSIS。
    /// </summary>
    /// <returns>makensis.exe 路径，若未找到则返回 null。</returns>
    private static string? FindNsisInCommonLocations()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NSIS", "makensis.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "NSIS", "makensis.exe"),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// 在发布目录中查找主可执行文件（Windows）。
    /// </summary>
    /// <param name="sourceDir">发布目录。</param>
    /// <param name="appName">应用名称。</param>
    /// <returns>可执行文件名（不含路径），若未找到则返回 null。</returns>
    internal static string? FindMainExecutable(string sourceDir, string appName)
    {
        var exeFiles = Directory.GetFiles(sourceDir, "*.exe");
        if (exeFiles.Length == 0)
        {
            return null;
        }

        // 优先匹配应用名称
        var match = Array.Find(exeFiles, f =>
            string.Equals(Path.GetFileNameWithoutExtension(f), appName, StringComparison.OrdinalIgnoreCase));

        return match is not null ? Path.GetFileName(match) : Path.GetFileName(exeFiles[0]);
    }

    /// <summary>
    /// 使用 appimagetool 创建 AppImage 包。
    /// 对应 Wails v3 Go 版本 internal/project/package.go 中的 Linux 打包逻辑。
    /// </summary>
    /// <param name="sourceDir">发布输出目录。</param>
    /// <param name="outputPath">AppImage 输出路径。</param>
    /// <param name="options">打包选项。</param>
    /// <exception cref="PlatformNotSupportedException">非 Linux 平台。</exception>
    /// <exception cref="FileNotFoundException">未找到 appimagetool 或主可执行文件。</exception>
    private static async Task PackageAppImageAsync(string sourceDir, string outputPath, PackageOptions options)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("AppImage 只能在 Linux 上生成");
        }

        var appimagetool = FindExecutableInPath("appimagetool")
            ?? FindExecutableInPath("appimagetool-x86_64.AppImage");
        if (appimagetool is null)
        {
            throw new FileNotFoundException(
                "未找到 appimagetool，请安装 appimagetool（https://github.com/AppImage/AppImageKit）并确保其在 PATH 中");
        }

        var appDir = Path.Combine(Path.GetTempPath(), $"wailsnet-appdir-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(appDir);

            // 创建 usr/bin 目录并复制发布产物
            var usrBin = Path.Combine(appDir, "usr", "bin");
            Directory.CreateDirectory(usrBin);
            CopyDirectory(sourceDir, usrBin);

            // 查找主可执行文件
            var exeName = FindLinuxExecutable(usrBin, options.AppName)
                ?? throw new FileNotFoundException($"在发布目录中未找到主可执行文件：{sourceDir}");

            // 设置可执行权限
            SetExecutablePermission(Path.Combine(usrBin, exeName));

            // 创建 AppRun 脚本
            var appRunPath = Path.Combine(appDir, "AppRun");
            await File.WriteAllTextAsync(appRunPath, GenerateAppRunScript(exeName));
            SetExecutablePermission(appRunPath);

            // 创建 .desktop 文件
            var desktopPath = Path.Combine(appDir, $"{options.AppName}.desktop");
            await File.WriteAllTextAsync(desktopPath, GenerateDesktopFile(options.AppName));

            // 创建图标（1x1 透明 PNG 占位符）
            var iconPath = Path.Combine(appDir, $"{options.AppName}.png");
            await File.WriteAllBytesAsync(iconPath, MinimalPngIcon);

            // 运行 appimagetool
            var (exitCode, output) = await RunProcessAsync(appimagetool, appDir, Path.GetFullPath(outputPath));
            if (exitCode != 0)
            {
                throw new InvalidOperationException($"appimagetool 退出码 {exitCode}：{output}");
            }
        }
        finally
        {
            if (Directory.Exists(appDir))
            {
                try { Directory.Delete(appDir, recursive: true); } catch { }
            }
        }
    }

    /// <summary>
    /// 生成 AppRun 脚本内容。
    /// </summary>
    /// <param name="exeName">主可执行文件名。</param>
    /// <returns>AppRun 脚本内容。</returns>
    internal static string GenerateAppRunScript(string exeName)
    {
        return $$"""
#!/bin/sh
SELF=$(readlink -f "$0")
HERE=${SELF%/*}
export UNION_PRELOAD="${HERE}"
export LD_PRELOAD="${HERE}/libunionpreload.so"
exec "${HERE}/usr/bin/{{exeName}}" "$@"
""";
    }

    /// <summary>
    /// 生成 .desktop 文件内容。
    /// </summary>
    /// <param name="appName">应用名称。</param>
    /// <returns>.desktop 文件内容。</returns>
    internal static string GenerateDesktopFile(string appName)
    {
        return $$"""
[Desktop Entry]
Type=Application
Name={{appName}}
Exec={{appName}}
Icon={{appName}}
Categories=Utility;
Terminal=false
""";
    }

    /// <summary>
    /// 在发布目录中查找 Linux 可执行文件。
    /// </summary>
    /// <param name="sourceDir">发布目录。</param>
    /// <param name="appName">应用名称。</param>
    /// <returns>可执行文件名（不含路径），若未找到则返回 null。</returns>
    internal static string? FindLinuxExecutable(string sourceDir, string appName)
    {
        // 优先匹配应用名称（无扩展名）
        var match = Path.Combine(sourceDir, appName);
        if (File.Exists(match))
        {
            return appName;
        }

        // 查找没有扩展名的文件（通常是 Linux 可执行文件）
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.TopDirectoryOnly))
        {
            if (string.IsNullOrEmpty(Path.GetExtension(file)))
            {
                return Path.GetFileName(file);
            }
        }

        return null;
    }

    /// <summary>
    /// 递归复制目录。
    /// </summary>
    /// <param name="sourceDir">源目录。</param>
    /// <param name="destDir">目标目录。</param>
    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.TopDirectoryOnly))
        {
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.TopDirectoryOnly))
        {
            CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
        }
    }

    /// <summary>
    /// 设置文件的 Linux 可执行权限（chmod +x）。
    /// </summary>
    /// <param name="filePath">文件路径。</param>
    private static void SetExecutablePermission(string filePath)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "chmod",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("+x");
            psi.ArgumentList.Add(filePath);

            using var proc = Process.Start(psi);
            proc?.WaitForExit();
        }
        catch
        {
            // 忽略权限设置错误
        }
    }

    /// <summary>
    /// 运行外部进程并捕获输出。
    /// </summary>
    /// <param name="fileName">可执行文件路径。</param>
    /// <param name="arguments">参数列表。</param>
    /// <returns>(退出码, 标准输出+错误输出)。</returns>
    private static async Task<(int ExitCode, string Output)> RunProcessAsync(string fileName, params string[] arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        try
        {
            using var proc = new Process { StartInfo = psi };
            proc.Start();

            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var combined = string.IsNullOrEmpty(stderr) ? stdout : $"{stdout}{Environment.NewLine}{stderr}";
            return (proc.ExitCode, combined);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }

    /// <summary>
    /// 最小 PNG 图标字节数据（1x1 透明像素），用于 AppImage 占位图标。
    /// </summary>
    private static readonly byte[] MinimalPngIcon =
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    };

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
