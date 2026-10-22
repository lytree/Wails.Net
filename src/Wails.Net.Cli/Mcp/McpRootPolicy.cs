namespace Wails.Net.Cli.Mcp;

/// <summary>
/// MCP 允许根目录策略：解析根目录、限制路径在根内、校验命令行参数中的路径标志。
/// 对应 Wails v3 Go 版本 <c>internal/commands/mcp.go</c> 的
/// <c>resolveMCPRoot</c> / <c>resolveMCPPath</c> / <c>isMCPPathWithin</c> / <c>validateMCPArgs</c>。
/// </summary>
internal sealed class McpRootPolicy
{
    /// <summary>单条命令行参数的最大字符数。</summary>
    public const int MaxArgumentLength = 4096;

    /// <summary>命令行参数最大数量。</summary>
    public const int MaxArgumentCount = 64;

    /// <summary>视为路径值的命令行标志。</summary>
    private static readonly string[] PathFlags =
    [
        "-p",
        "--p",
        "--project",
        "-o",
        "--o",
        "--output",
        "-d",
        "--d",
        "--directory",
    ];

    private McpRootPolicy(string root)
    {
        Root = root;
    }

    /// <summary>解析后的允许根目录绝对路径。</summary>
    public string Root { get; }

    /// <summary>
    /// 创建根目录策略：默认当前目录，解析符号链接并要求目录存在。
    /// </summary>
    /// <param name="input">显式指定的根目录；null/空表示当前目录。</param>
    /// <returns>策略实例。</returns>
    /// <exception cref="InvalidOperationException">根目录无法解析或不是目录。</exception>
    public static McpRootPolicy Create(string? input)
    {
        var root = string.IsNullOrWhiteSpace(input) ? Directory.GetCurrentDirectory() : input;
        string absolute;
        try
        {
            absolute = Path.GetFullPath(root);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidOperationException($"无法解析 MCP 根目录 \"{root}\"：{ex.Message}", ex);
        }

        absolute = ResolveFinalLink(absolute);
        if (!Directory.Exists(absolute))
        {
            throw new InvalidOperationException($"MCP 根目录 \"{absolute}\" 不存在或不是目录");
        }

        return new McpRootPolicy(TrimTrailingSeparator(absolute));
    }

    /// <summary>
    /// 解析相对根目录的路径并限制在根内。
    /// </summary>
    /// <param name="input">相对或绝对路径；null/空表示根目录。</param>
    /// <returns>绝对路径。</returns>
    /// <exception cref="InvalidOperationException">路径越出允许根目录。</exception>
    public string ResolvePath(string? input) => ResolvePath(Root, input);

    /// <summary>
    /// 解析相对指定基准目录的路径并限制在允许根目录内。
    /// </summary>
    /// <param name="baseDirectory">相对路径的基准目录（须位于根内）。</param>
    /// <param name="input">相对或绝对路径；null/空表示基准目录。</param>
    /// <returns>绝对路径。</returns>
    /// <exception cref="InvalidOperationException">路径越出允许根目录。</exception>
    public string ResolvePath(string baseDirectory, string? input)
    {
        var combined = string.IsNullOrEmpty(input)
            ? baseDirectory
            : Path.IsPathRooted(input)
                ? input
                : Path.Combine(baseDirectory, input);

        string absolute;
        try
        {
            absolute = Path.GetFullPath(combined);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidOperationException($"路径 \"{input}\" 无法解析：{ex.Message}", ex);
        }

        absolute = TrimTrailingSeparator(absolute);
        if (!IsWithin(Root, absolute))
        {
            throw new InvalidOperationException($"路径 \"{input}\" 越出允许的 MCP 根目录");
        }

        // 对最长已存在前缀解析符号链接，防止「中间目录为符号链接」绕过根限制。
        var existing = FindExistingPrefix(absolute);
        if (existing is not null)
        {
            var resolved = ResolveFinalLink(existing);
            if (!PathEquals(resolved, existing))
            {
                var remainder = Path.GetRelativePath(existing, absolute);
                var redirected = remainder == "."
                    ? resolved
                    : Path.GetFullPath(Path.Combine(resolved, remainder));
                if (!IsWithin(Root, redirected))
                {
                    throw new InvalidOperationException($"路径 \"{input}\" 经符号链接解析后越出允许的 MCP 根目录");
                }
            }
        }

        return absolute;
    }

    /// <summary>
    /// 校验命令参数：数量、长度、NUL 字符，以及路径标志的值必须位于允许根目录内。
    /// </summary>
    /// <param name="baseDirectory">路径标志的基准目录。</param>
    /// <param name="args">待校验的参数列表。</param>
    /// <exception cref="InvalidOperationException">参数非法。</exception>
    public void ValidateArguments(string baseDirectory, IReadOnlyList<string> args)
    {
        if (args.Count > MaxArgumentCount)
        {
            throw new InvalidOperationException($"命令行参数过多：最多 {MaxArgumentCount} 个");
        }

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            ValidateArgument(arg);

            if (!TrySplitPathFlag(arg, out var flag, out var value, out var hasValue))
            {
                continue;
            }

            if (!hasValue)
            {
                if (i + 1 >= args.Count)
                {
                    throw new InvalidOperationException($"标志 \"{flag}\" 需要路径值");
                }

                value = args[++i];
                ValidateArgument(value);
            }

            if (string.IsNullOrEmpty(value) || value.StartsWith('-'))
            {
                throw new InvalidOperationException($"标志 \"{flag}\" 需要路径值");
            }

            ResolvePath(baseDirectory, value);
        }
    }

    /// <summary>
    /// 判断路径是否位于根目录内（含根自身）。
    /// </summary>
    /// <param name="root">根目录。</param>
    /// <param name="path">待判断路径。</param>
    /// <returns>位于根内时为 <c>true</c>。</returns>
    public static bool IsWithin(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }

    private static void ValidateArgument(string arg)
    {
        if (arg.Contains('\0'))
        {
            throw new InvalidOperationException("命令行参数不得包含 NUL 字符");
        }

        if (arg.Length > MaxArgumentLength)
        {
            throw new InvalidOperationException($"命令行参数长度不得超过 {MaxArgumentLength} 个字符");
        }
    }

    private static bool TrySplitPathFlag(
        string arg,
        out string flag,
        out string value,
        out bool hasValue)
    {
        foreach (var candidate in PathFlags)
        {
            if (string.Equals(arg, candidate, StringComparison.Ordinal))
            {
                flag = candidate;
                value = string.Empty;
                hasValue = false;
                return true;
            }

            if (arg.StartsWith(candidate + "=", StringComparison.Ordinal))
            {
                flag = candidate;
                value = arg[(candidate.Length + 1)..];
                hasValue = true;
                return true;
            }
        }

        flag = string.Empty;
        value = string.Empty;
        hasValue = false;
        return false;
    }

    private static string? FindExistingPrefix(string path)
    {
        var current = path;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(current) || File.Exists(current))
            {
                return current;
            }

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || PathEquals(parent, current))
            {
                return null;
            }

            current = parent;
        }

        return null;
    }

    private static string ResolveFinalLink(string path)
    {
        try
        {
            FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            return target is null ? path : TrimTrailingSeparator(Path.GetFullPath(target.FullName));
        }
        catch (IOException)
        {
            return path;
        }
        catch (UnauthorizedAccessException)
        {
            return path;
        }
    }

    private static string TrimTrailingSeparator(string path)
    {
        var root = Path.GetPathRoot(path);
        if (!string.IsNullOrEmpty(root) && path.Length <= root.Length)
        {
            return path;
        }

        return Path.TrimEndingDirectorySeparator(path);
    }

    private static bool PathEquals(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            comparison);
    }
}
