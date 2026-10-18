using System.Security.Cryptography;
using System.Text;

namespace Wails.Net.Cli.Build;

/// <summary>
/// macOS 输出准备决策输入（纯数据，便于在非 macOS 主机上单测判定逻辑）。
/// 对应 Wails v3 Go 版本 internal/commands/tool_prepare_macos_output.go 的判定分支。
/// </summary>
public sealed class MacOsOutputProbe
{
    /// <summary>输出目录是否位于项目根目录内（仅项目内生成的输出会被重定位）。</summary>
    public bool InsideProject { get; init; }

    /// <summary>输出目录本身是否已是符号链接。</summary>
    public bool IsSymlink { get; init; }

    /// <summary>是否已带本地标记文件（<c>.wails-provider-checked</c>，且内容与解析后的路径一致）。</summary>
    public bool IsLocalMarked { get; init; }

    /// <summary>目录是否位于 file provider（<c>com.apple.fileprovider.fpfs</c>）挂载下。</summary>
    public bool IsFileProvider { get; init; }
}

/// <summary>
/// macOS 输出准备动作。对应 Go <c>ToolPrepareMacOSOutput</c> 的决策出口。
/// </summary>
public enum MacOsOutputAction
{
    /// <summary>输出目录在项目外：保持用户选择，仅确保目录存在。</summary>
    CreateDirectoryOnly,

    /// <summary>已是符号链接：确保链接目标位于本地文件系统。</summary>
    EnsureLinkTargetLocal,

    /// <summary>已标记为本地：无需处理。</summary>
    AlreadyLocal,

    /// <summary>位于 file provider 下：重定位到本地文件系统并建立符号链接（含 bin 目录链接）。</summary>
    Relocate,

    /// <summary>普通目录：写入本地标记后原样使用。</summary>
    MarkLocal,
}

/// <summary>
/// macOS 输出准备结果。
/// </summary>
public sealed class MacOsOutputPrepareResult
{
    /// <summary>是否因非 macOS 主机而跳过（no-op）。</summary>
    public bool Skipped { get; init; }

    /// <summary>是否成功。</summary>
    public bool Success { get; init; }

    /// <summary>执行的决策动作。</summary>
    public MacOsOutputAction Action { get; init; }

    /// <summary>错误消息（失败时非空）。</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>重定位后的本地输出路径（仅 Relocate 动作非空）。</summary>
    public string? LocalOutputPath { get; init; }
}

/// <summary>
/// macOS 输出准备：当输出目录位于 file provider（如 iCloud Drive）挂载下、
/// 会为 <c>.app</c> 附加不可签名元数据时，把生成输出重定位到本地文件系统并在原位置建立符号链接。
/// 对应 Wails v3 Go 版本 internal/commands/tool_prepare_macos_output.go（<c>ToolPrepareMacOSOutput</c>）。
/// <para>
/// 判定逻辑（<see cref="Decide"/>、<see cref="IsProjectOutputDirectory"/>）为纯函数，
/// 可在任意平台单测；file provider 的真实探测依赖 macOS 的 <c>xattr</c>，
/// 且真实 OS 变更（重命名/符号链接）仅在 macOS 上发生——本机为 Windows，未做真机验证。
/// </para>
/// </summary>
public sealed class MacOsOutputPreparer
{
    /// <summary>file provider 附加的 xattr 属性名（Go 命中即判定为 file provider 目录）。</summary>
    public const string FileProviderAttribute = "com.apple.fileprovider.fpfs#P";

    /// <summary>Finder 元数据属性名（Go 命中即判定为 file provider 目录）。</summary>
    public const string FinderInfoAttribute = "com.apple.FinderInfo";

    /// <summary>本地输出标记文件名（内容为解析后的输出路径）。对应 Go <c>.wails-provider-checked</c>。</summary>
    public const string LocalMarkerFileName = ".wails-provider-checked";

    /// <summary>本地输出根目录环境变量。对应 Go <c>WAILS_MACOS_OUTPUT_ROOT</c>。</summary>
    public const string LocalRootEnvironmentVariable = "WAILS_MACOS_OUTPUT_ROOT";

    /// <summary>默认构建输出目录名（对应 Go flags 的 <c>--dir bin</c>）。</summary>
    public const string DefaultDirectoryName = "bin";

    /// <summary>
    /// 判断输出目录是否位于项目根目录内。
    /// 对应 Go <c>isProjectOutputDirectory</c>（tool_prepare_macos_output.go）。
    /// </summary>
    /// <param name="projectRoot">项目根目录（绝对路径）。</param>
    /// <param name="binPath">构建输出目录（绝对路径）。</param>
    /// <returns>位于项目内返回 true，位于项目外返回 false。</returns>
    /// <exception cref="InvalidOperationException">输出目录等于项目根目录时抛出。</exception>
    public static bool IsProjectOutputDirectory(string projectRoot, string binPath)
    {
        var relative = Path.GetRelativePath(projectRoot, binPath);

        // 不同盘符/根（如 C: 与 D:）时相对路径为绝对路径，视为项目外。
        if (Path.IsPathRooted(relative))
        {
            return false;
        }

        if (relative == ".")
        {
            throw new InvalidOperationException("构建输出目录不能是项目根目录");
        }

        return relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    /// <summary>
    /// 决策函数：根据探测结果决定处理动作（纯逻辑）。
    /// 对应 Go <c>ToolPrepareMacOSOutput</c> 的判定树（项目外 / 已有链接 / 已标记 / file provider / 普通目录）。
    /// </summary>
    /// <param name="probe">探测结果。</param>
    /// <returns>应执行的动作。</returns>
    public static MacOsOutputAction Decide(MacOsOutputProbe probe)
    {
        // 项目外的显式目录保留用户选择（Go：!insideProject → os.MkdirAll 后返回）。
        if (!probe.InsideProject)
        {
            return MacOsOutputAction.CreateDirectoryOnly;
        }

        // 已是符号链接：确保链接目标位于本地文件系统（Go：isOutputLink → ensureMacOSOutputIsLocal）。
        if (probe.IsSymlink)
        {
            return MacOsOutputAction.EnsureLinkTargetLocal;
        }

        // 已带本地标记：原样使用（Go：macOSOutputIsLocal → return nil）。
        if (probe.IsLocalMarked)
        {
            return MacOsOutputAction.AlreadyLocal;
        }

        // file provider 目录：重定位 + 符号链接（Go：relocateMacOSOutput）。
        if (probe.IsFileProvider)
        {
            return MacOsOutputAction.Relocate;
        }

        // 普通目录：写入本地标记后原样使用（Go：markMacOSOutputLocal）。
        return MacOsOutputAction.MarkLocal;
    }

    /// <summary>
    /// 准备 macOS 构建输出目录。
    /// 非 macOS 主机直接返回 no-op（对应 Go 的 <c>runtime.GOOS != "darwin"</c> 早退）。
    /// </summary>
    /// <param name="projectRoot">项目根目录。</param>
    /// <param name="binDirectory">构建输出目录。</param>
    /// <returns>准备结果。</returns>
    public async Task<MacOsOutputPrepareResult> PrepareAsync(string projectRoot, string binDirectory)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return new MacOsOutputPrepareResult
            {
                Skipped = true,
                Success = true,
                Action = MacOsOutputAction.MarkLocal,
            };
        }

        var root = Path.GetFullPath(projectRoot);
        var binPath = Path.GetFullPath(binDirectory);

        bool insideProject;
        try
        {
            insideProject = IsProjectOutputDirectory(root, binPath);
        }
        catch (InvalidOperationException ex)
        {
            return Failure(ex.Message);
        }

        var isSymlink = IsSymlinkDirectory(binPath);
        var isMarkedLocal = !isSymlink && IsMarkedLocal(binPath);
        // 仅在需要判定「是否 file provider」时才执行 xattr 探针（普通路径/已标记路径无需探测）。
        var isFileProvider = insideProject && !isSymlink && !isMarkedLocal &&
            await IsFileProviderDirectoryAsync(binPath);

        var probe = new MacOsOutputProbe
        {
            InsideProject = insideProject,
            IsSymlink = isSymlink,
            IsLocalMarked = isMarkedLocal,
            IsFileProvider = isFileProvider,
        };

        var action = Decide(probe);
        switch (action)
        {
            case MacOsOutputAction.CreateDirectoryOnly:
                Directory.CreateDirectory(binPath);
                return Success(action);

            case MacOsOutputAction.AlreadyLocal:
                return Success(action);

            case MacOsOutputAction.MarkLocal:
                Directory.CreateDirectory(binPath);
                MarkLocal(binPath);
                return Success(action);

            case MacOsOutputAction.EnsureLinkTargetLocal:
            {
                var error = await EnsureLocalAsync(binPath);
                return error is null ? Success(action) : Failure(error, action);
            }

            case MacOsOutputAction.Relocate:
            default:
            {
                var (error, localPath) = RelocateMacOsOutput(root, binPath);
                if (error is not null)
                {
                    return Failure(error, MacOsOutputAction.Relocate);
                }

                return new MacOsOutputPrepareResult
                {
                    Success = true,
                    Action = MacOsOutputAction.Relocate,
                    LocalOutputPath = localPath,
                };
            }
        }
    }

    /// <summary>
    /// 构造成功结果。
    /// </summary>
    /// <param name="action">执行的动作。</param>
    /// <returns>成功结果。</returns>
    private static MacOsOutputPrepareResult Success(MacOsOutputAction action) => new()
    {
        Success = true,
        Action = action,
    };

    /// <summary>
    /// 构造失败结果。
    /// </summary>
    /// <param name="message">错误消息。</param>
    /// <param name="action">已尝试的动作。</param>
    /// <returns>失败结果。</returns>
    private static MacOsOutputPrepareResult Failure(string message, MacOsOutputAction action = MacOsOutputAction.MarkLocal) => new()
    {
        Success = false,
        Action = action,
        ErrorMessage = message,
    };

    /// <summary>
    /// 判定路径是否为「指向现存目录」的符号链接。对应 Go <c>isOutputLink</c>。
    /// </summary>
    /// <param name="path">目录路径。</param>
    /// <returns>是符号链接返回 true。</returns>
    private static bool IsSymlinkDirectory(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.ReparsePoint) != 0 && Directory.Exists(path);
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// 判定目录是否已带本地标记。对应 Go <c>macOSOutputIsLocal</c>。
    /// </summary>
    /// <param name="path">目录路径。</param>
    /// <returns>已标记返回 true。</returns>
    private static bool IsMarkedLocal(string path)
    {
        var resolved = ResolvePath(path);
        if (resolved is null)
        {
            return false;
        }

        try
        {
            var marker = Path.Combine(resolved, LocalMarkerFileName);
            return File.Exists(marker) && File.ReadAllText(marker) == resolved;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// 写入本地标记文件。对应 Go <c>markMacOSOutputLocal</c>。
    /// </summary>
    /// <param name="path">目录路径。</param>
    private static void MarkLocal(string path)
    {
        var resolved = ResolvePath(path) ?? Path.GetFullPath(path);
        File.WriteAllText(Path.Combine(resolved, LocalMarkerFileName), resolved);
    }

    /// <summary>
    /// 解析路径的最终目标（近似 Go <c>filepath.EvalSymlinks</c>）。
    /// </summary>
    /// <param name="path">目录路径。</param>
    /// <returns>解析后的绝对路径；解析失败返回 null。</returns>
    private static string? ResolvePath(string path)
    {
        try
        {
            var absolute = Path.GetFullPath(path);
            var target = new DirectoryInfo(absolute).ResolveLinkTarget(returnFinalTarget: true);
            return target?.FullName ?? absolute;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// 确保指定输出目录位于本地文件系统（若非本地则写入标记，若仍在 file provider 下则报错）。
    /// 对应 Go <c>ensureMacOSOutputIsLocal</c>。
    /// </summary>
    /// <param name="path">输出目录。</param>
    /// <returns>错误消息；成功返回 null。</returns>
    private static async Task<string?> EnsureLocalAsync(string path)
    {
        if (IsMarkedLocal(path))
        {
            return null;
        }

        if (await IsFileProviderDirectoryAsync(path))
        {
            return $"macOS 输出 {path} 会重新附加 Finder 元数据；请选择未同步的输出目录";
        }

        MarkLocal(path);
        return null;
    }

    /// <summary>
    /// 重定位输出目录到本地文件系统并建立符号链接。
    /// 对应 Go <c>relocateMacOSOutput</c>。
    /// </summary>
    /// <param name="projectRoot">项目根目录。</param>
    /// <param name="binPath">输出目录。</param>
    /// <returns>(错误消息, 本地输出路径)。</returns>
    private static (string? Error, string? LocalPath) RelocateMacOsOutput(string projectRoot, string binPath)
    {
        var outputRoot = LocalOutputRoot();

        // Go 使用 unix.Stat 的设备号比较判定同一文件系统；.NET 无对应 API，
        // 此处以挂载根近似（跨卷时 Directory.Move 亦会失败并返回清晰错误）。
        if (!string.Equals(Path.GetPathRoot(binPath), Path.GetPathRoot(outputRoot), StringComparison.OrdinalIgnoreCase))
        {
            return ("无法跨卷重定位构建输出；请将 WAILS_MACOS_OUTPUT_ROOT 指向项目所在卷上未同步的目录", null);
        }

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(binPath));
        var suffix = Convert.ToHexStringLower(digest.AsSpan(0, 8));
        var target = Path.Combine(outputRoot, $"{Path.GetFileName(projectRoot)}-{suffix}");

        if (Directory.Exists(target) || File.Exists(target))
        {
            return ($"本地构建输出已存在：{target}；请先清理该目录后重试", null);
        }

        try
        {
            Directory.Move(binPath, target);
        }
        catch (IOException ex)
        {
            return ($"把构建输出移出 file provider 失败：{ex.Message}", null);
        }

        try
        {
            // 在原位置建立指向本地目录的链接，保留项目惯用的 bin 路径。
            Directory.CreateSymbolicLink(binPath, target);
        }
        catch (IOException ex)
        {
            try
            {
                Directory.Move(target, binPath);
            }
            catch (IOException)
            {
                return ($"创建本地输出链接失败：{ex.Message}；且无法还原原目录，输出仍位于 {target}", null);
            }

            return ($"在项目内创建本地输出链接失败：{ex.Message}", null);
        }

        MarkLocal(target);
        return (null, target);
    }

    /// <summary>
    /// 解析本地输出根目录。对应 Go <c>localMacOSOutputRoot</c>（默认 <c>~/Library/Application Support/Wails/build-output</c>）。
    /// </summary>
    /// <returns>本地输出根目录绝对路径。</returns>
    private static string LocalOutputRoot()
    {
        var configured = Environment.GetEnvironmentVariable(LocalRootEnvironmentVariable);
        string outputRoot;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            outputRoot = configured;
        }
        else
        {
            // 对应 Go os.UserConfigDir：darwin 下为 $HOME/Library/Application Support。
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            outputRoot = Path.Combine(home, "Library", "Application Support", "Wails", "build-output");
        }

        outputRoot = Path.GetFullPath(outputRoot);
        Directory.CreateDirectory(outputRoot);
        return outputRoot;
    }

    /// <summary>
    /// 探测目录是否位于 file provider 挂载下。
    /// 对应 Go <c>isFileProviderDirectory</c>：创建 <c>wails-probe-*.app</c> 探针，
    /// 等待 2500ms 后读取 <c>xattr</c>，命中 fpfs/FinderInfo 属性即判定。
    /// <para>依赖 macOS 的 <c>xattr</c> 命令，未在非 macOS 主机验证。</para>
    /// </summary>
    /// <param name="binPath">输出目录。</param>
    /// <returns>位于 file provider 下返回 true。</returns>
    private static async Task<bool> IsFileProviderDirectoryAsync(string binPath)
    {
        Directory.CreateDirectory(binPath);
        var probe = Path.Combine(binPath, $"wails-probe-{Guid.NewGuid():N}.app");
        try
        {
            Directory.CreateDirectory(probe);
            // file provider 异步附加 FinderInfo；Go 固定等待 2500ms 后探测。
            await Task.Delay(2500);

            var (exitCode, output) = await CliProcess.RunAsync("xattr", probe);
            if (exitCode != 0)
            {
                throw new InvalidOperationException($"读取 macOS 签名元数据失败：{output}");
            }

            var attributes = output.Split(
                '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return attributes.Contains(FileProviderAttribute, StringComparer.Ordinal) ||
                attributes.Contains(FinderInfoAttribute, StringComparer.Ordinal);
        }
        finally
        {
            try
            {
                if (Directory.Exists(probe))
                {
                    Directory.Delete(probe, recursive: true);
                }
            }
            catch (IOException)
            {
                // 探针清理失败不影响判定结果。
            }
        }
    }
}
