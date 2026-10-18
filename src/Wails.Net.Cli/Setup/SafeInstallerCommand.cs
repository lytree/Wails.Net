using System.Text.RegularExpressions;

namespace Wails.Net.Cli.Setup;

/// <summary>
/// 已批准的包管理器命令解析器。
/// 对应 Wails v3 Go 版本 <c>internal/setupwizard/wizard.go:1208-1336</c> 的
/// <c>safeInstallers</c> / <c>safeElevatedInstallers</c> / <c>safeInstallArgument</c> /
/// <c>validInstallArguments</c> / <c>hasSubcommand</c> / <c>hasPackageInstallSubcommand</c> /
/// <c>isSafeInstallShape</c> / <c>getSafeCommand</c> / <c>isCommandAllowed</c>。
/// <para>
/// 安全约束（beta.24「仅批准的包管理器命令」）：只有白名单内的包管理器及其固定子命令形态
/// 可通过校验；特权提升包装器（<c>sudo</c> / <c>pkexec</c> / <c>doas</c>）禁止携带任何标志位，
/// 其后继 token 必须是被批准且支持提权的安装器。任何任意 Shell 命令
/// （如 <c>rm -rf /</c>、<c>curl | sh</c>）一律被拒绝，从而杜绝命令注入与配置/替换原语。
/// </para>
/// </summary>
internal static partial class SafeInstallerCommand
{
    /// <summary>
    /// 受批准的可执行文件名 → 规范化名称（对应 Go <c>safeInstallers</c>）。
    /// 请求中的可执行名仍会被重新映射为常量，避免把请求字符串直接当作可执行文件。
    /// </summary>
    private static readonly Dictionary<string, string> SafeInstallers = new(StringComparer.Ordinal)
    {
        ["apt"] = "apt",
        ["apt-get"] = "apt-get",
        ["apk"] = "apk",
        ["dnf"] = "dnf",
        ["yum"] = "yum",
        ["rpm-ostree"] = "rpm-ostree",
        ["pacman"] = "pacman",
        ["zypper"] = "zypper",
        ["emerge"] = "emerge",
        ["xbps-install"] = "xbps-install",
        ["eopkg"] = "eopkg",
        ["nix-env"] = "nix-env",
        ["brew"] = "brew",
        ["port"] = "port",
        ["snap"] = "snap",
        ["flatpak"] = "flatpak",
        ["winget"] = "winget",
        ["choco"] = "choco",
        ["scoop"] = "scoop",
        ["sdkmanager"] = "sdkmanager",
        ["xcodebuild"] = "xcodebuild",
        ["xcode-select"] = "xcode-select",
        ["sudo"] = "sudo",
        ["pkexec"] = "pkexec",
        ["doas"] = "doas",
    };

    /// <summary>
    /// 支持特权提升的安装器集合（对应 Go <c>safeElevatedInstallers</c>）。
    /// 只有这些安装器可出现在 <c>sudo</c> / <c>pkexec</c> / <c>doas</c> 之后。
    /// </summary>
    private static readonly HashSet<string> SafeElevatedInstallers = new(StringComparer.Ordinal)
    {
        "apt", "apt-get", "apk", "dnf", "yum",
        "rpm-ostree", "pacman", "zypper", "emerge",
        "xbps-install", "eopkg", "nix-env",
    };

    /// <summary>
    /// 安装参数允许的字符集（对应 Go <c>safeInstallArgument</c>）。
    /// 刻意保持极小字母表，防止攻击者把安装器选项变成配置文件或命令替换原语。
    /// Android SDK 包名使用 <c>';'</c>（例如 <c>ndk;26.3.11579264</c>），故予以包含。
    /// </summary>
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9+._:@/;=#,\-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeInstallArgumentRegex();

    /// <summary>
    /// 校验参数列表是否全部符合安全字符集且非空（对应 Go <c>validInstallArguments</c>）。
    /// </summary>
    /// <param name="args">参数列表。</param>
    /// <returns>全部有效时返回 true。</returns>
    internal static bool AreValidInstallArguments(string[] args)
    {
        if (args.Length == 0)
        {
            return false;
        }

        foreach (var arg in args)
        {
            if (!SafeInstallArgumentRegex().IsMatch(arg))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 校验 <c>&lt;subcommand&gt; &lt;package...&gt;</c> 形态（对应 Go <c>hasSubcommand</c>）。
    /// </summary>
    private static bool HasSubcommand(string[] args, string subcommand)
        => args.Length >= 2 && args[0] == subcommand && AreValidInstallArguments(args[1..]);

    /// <summary>
    /// 校验 <c>&lt;subcommand&gt; [&lt;optionalFlag&gt;] &lt;package...&gt;</c> 形态
    /// （对应 Go <c>hasPackageInstallSubcommand</c>）。
    /// </summary>
    private static bool HasPackageInstallSubcommand(string[] args, string subcommand, string optionalFlag)
    {
        if (args.Length < 2 || args[0] != subcommand)
        {
            return false;
        }

        var packageArgs = args[1..];
        if (optionalFlag.Length > 0 && packageArgs.Length >= 2 && packageArgs[0] == optionalFlag)
        {
            packageArgs = packageArgs[1..];
        }

        return AreValidInstallArguments(packageArgs);
    }

    /// <summary>
    /// 校验命令是否具有已批准的包管理器安装形态（对应 Go <c>isSafeInstallShape</c>）。
    /// </summary>
    /// <param name="command">可执行文件名（已确认位于白名单内）。</param>
    /// <param name="args">可执行文件之后的参数列表。</param>
    /// <returns>形态合法时返回 true。</returns>
    internal static bool IsSafeInstallShape(string command, string[] args)
    {
        switch (command)
        {
            case "apt":
                return HasSubcommand(args, "install");
            case "apt-get":
            case "dnf":
            case "yum":
                return HasPackageInstallSubcommand(args, "install", "-y");
            case "apk":
                return HasSubcommand(args, "add");
            case "rpm-ostree":
                return HasSubcommand(args, "install");
            case "pacman":
                if (args.Length < 2 || args[0] != "-S")
                {
                    return false;
                }

                var pacmanArgs = args[1..];
                if (pacmanArgs.Length >= 2 && pacmanArgs[0] == "--noconfirm")
                {
                    pacmanArgs = pacmanArgs[1..];
                }

                return AreValidInstallArguments(pacmanArgs);
            case "zypper":
                return HasSubcommand(args, "in");
            case "eopkg":
                return HasSubcommand(args, "it");
            case "nix-env":
                return args.Length >= 2 && args[0] == "-iA" && AreValidInstallArguments(args[1..]);
            case "xbps-install":
                return args.Length >= 2 && args[0] == "-Sy" && AreValidInstallArguments(args[1..]);
            case "emerge":
                return AreValidInstallArguments(args);
            case "brew":
                if (args.Length < 2 || args[0] != "install")
                {
                    return false;
                }

                if (args[1] == "--cask")
                {
                    return args.Length >= 3 && AreValidInstallArguments(args[2..]);
                }

                return AreValidInstallArguments(args[1..]);
            case "port":
            case "snap":
            case "flatpak":
            case "winget":
            case "choco":
            case "scoop":
                return HasSubcommand(args, "install");
            case "sdkmanager":
                return args.Length >= 2 && args[0] == "--install" && AreValidInstallArguments(args[1..]);
            case "xcodebuild":
                return args.Length == 2 && args[0] == "-downloadPlatform" && args[1] == "iOS";
            case "xcode-select":
                return args.Length == 1 && args[0] == "--install";
            default:
                return false;
        }
    }

    /// <summary>
    /// 校验并解析完整命令，返回常量可执行名与已校验参数（对应 Go <c>getSafeCommand</c>）。
    /// </summary>
    /// <param name="parts">命令 token 列表（含可执行文件名）。</param>
    /// <param name="command">解析成功时输出已批准命令；失败时为 null。</param>
    /// <returns>命令被批准时返回 true。</returns>
    internal static bool TryParse(IReadOnlyList<string> parts, out SafeInstallCommand? command)
    {
        command = null;
        if (parts.Count == 0)
        {
            return false;
        }

        var requested = parts[0];
        var rest = new string[parts.Count - 1];
        for (var i = 1; i < parts.Count; i++)
        {
            rest[i - 1] = parts[i];
        }

        if (requested is "sudo" or "pkexec" or "doas")
        {
            // 特权提升包装器不得携带标志位，其后继必须是受批准且支持提权的安装器。
            if (rest.Length < 2 || rest[0].StartsWith('-') || !SafeElevatedInstallers.Contains(rest[0]))
            {
                return false;
            }

            var elevatedArgs = rest[1..];
            if (!IsSafeInstallShape(rest[0], elevatedArgs))
            {
                return false;
            }

            command = new SafeInstallCommand(SafeInstallers[requested], SafeInstallers[rest[0]], elevatedArgs);
            return true;
        }

        if (!SafeInstallers.TryGetValue(requested, out var safeCmd))
        {
            return false;
        }

        if (!IsSafeInstallShape(requested, rest))
        {
            return false;
        }

        command = new SafeInstallCommand(safeCmd, null, rest);
        return true;
    }

    /// <summary>
    /// 命令（已拆分为 token）是否被批准（对应 Go <c>isCommandAllowed</c>）。
    /// </summary>
    /// <param name="parts">命令 token 列表。</param>
    /// <returns>被批准时返回 true。</returns>
    internal static bool IsCommandAllowed(IReadOnlyList<string> parts) => TryParse(parts, out _);

    /// <summary>
    /// 按空白拆分命令行后校验（对应 Go <c>strings.Fields(req.Command)</c> + <c>getSafeCommand</c>）。
    /// 仅供白名单校验使用，绝不据此执行任意用户提供的字符串。
    /// </summary>
    /// <param name="commandLine">命令行字符串。</param>
    /// <returns>被批准时返回 true。</returns>
    internal static bool IsCommandLineAllowed(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return false;
        }

        var parts = commandLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return IsCommandAllowed(parts);
    }
}

/// <summary>
/// 通过白名单校验的安装命令（对应 Go <c>getSafeCommand</c> 的三元返回）。
/// </summary>
/// <param name="Executable">规范化可执行文件名（如 <c>sudo</c> / <c>apt</c> / <c>winget</c>）。</param>
/// <param name="ElevatedExecutable">特权提升后的实际安装器（如 <c>apt</c>）；非提升命令为 null。</param>
/// <param name="Arguments">已校验的参数列表（不含可执行文件名本身）。</param>
internal sealed record SafeInstallCommand(string Executable, string? ElevatedExecutable, string[] Arguments)
{
    /// <summary>
    /// 构造实际执行所需的 argv；提升命令前置安装器名
    /// （对应 Go <c>handleInstallDependency</c> 中 <c>cmdArgs</c> 的组装逻辑）。
    /// </summary>
    /// <returns>argv 数组，不经过任何 shell 解析。</returns>
    internal string[] ToArgv()
    {
        if (ElevatedExecutable is null)
        {
            return Arguments;
        }

        var argv = new string[Arguments.Length + 1];
        argv[0] = ElevatedExecutable;
        Array.Copy(Arguments, 0, argv, 1, Arguments.Length);
        return argv;
    }

    /// <summary>
    /// 格式化为可读的命令预览（仅用于执行前告知用户，不用于执行）。
    /// </summary>
    /// <returns>命令字符串。</returns>
    internal string ToDisplayString() => string.Join(' ', new[] { Executable }.Concat(ToArgv()));
}
