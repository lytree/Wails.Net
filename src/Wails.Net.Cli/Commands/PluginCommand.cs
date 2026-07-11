using System.CommandLine;
using System.Xml.Linq;

namespace Wails.Net.Cli.Commands;

/// <summary>
/// plugin 命令：管理 Wails.Net 项目插件引用。
/// 对应 Tauri v2 的 plugin 管理体验。
/// 支持子命令：plugin add &lt;name&gt;、plugin remove &lt;name&gt;、plugin list。
/// </summary>
internal sealed class PluginCommand : CliCommandBase
{
    /// <summary>
    /// 内置可识别的插件名 → NuGet 包标识映射。
    /// 用户也可以通过完整包名（含点号）直接安装第三方包。
    /// </summary>
    private static readonly Dictionary<string, string> BuiltInPlugins = new(StringComparer.OrdinalIgnoreCase)
    {
        ["filesystem"] = "Wails.Net.Plugins.FileSystem",
        ["fs"] = "Wails.Net.Plugins.FileSystem",
        ["clipboard"] = "Wails.Net.Plugins.Clipboard",
        ["notification"] = "Wails.Net.Plugins.Notification",
        ["dialog"] = "Wails.Net.Plugins.Dialog",
        ["tray"] = "Wails.Net.Plugins.Tray",
        ["sqlite"] = "Wails.Net.Plugins.Sqlite",
        ["sql"] = "Wails.Net.Plugins.Sqlite",
        ["shell"] = "Wails.Net.Plugins.Shell",
        ["updater"] = "Wails.Net.Plugins.Updater",
        ["autostart"] = "Wails.Net.Plugins.Autostart",
        ["store"] = "Wails.Net.Plugins.Store",
        ["http"] = "Wails.Net.Plugins.Http",
        ["websocket"] = "Wails.Net.Plugins.WebSocket",
        ["log"] = "Wails.Net.Plugins.Log",
        ["os"] = "Wails.Net.Plugins.OsInfo",
        ["path"] = "Wails.Net.Plugins.Path",
        ["process"] = "Wails.Net.Plugins.Process",
        ["cookie"] = "Wails.Net.Plugins.Cookie",
        ["globalshortcut"] = "Wails.Net.Plugins.GlobalShortcut",
        ["shortcut"] = "Wails.Net.Plugins.GlobalShortcut",
        ["deeplink"] = "Wails.Net.Plugins.DeepLink",
        ["windowstate"] = "Wails.Net.Plugins.WindowState",
        ["positioner"] = "Wails.Net.Plugins.Positioner",
        ["power"] = "Wails.Net.Plugins.PowerManagement",
        ["appinfo"] = "Wails.Net.Plugins.AppInfo",
        ["localization"] = "Wails.Net.Plugins.Localization",
        ["fileassociation"] = "Wails.Net.Plugins.FileAssociation",
        ["upload"] = "Wails.Net.Plugins.Upload",
        ["stronghold"] = "Wails.Net.Plugins.Stronghold",
        ["persisted-scope"] = "Wails.Net.Plugins.PersistedScope",
        ["scope"] = "Wails.Net.Plugins.PersistedScope",
        ["localhost"] = "Wails.Net.Plugins.Localhost",
        ["fs-watch"] = "Wails.Net.Plugins.FsWatch",
        ["fswatch"] = "Wails.Net.Plugins.FsWatch",
        ["opener"] = "Wails.Net.Plugins.Opener",
        ["open"] = "Wails.Net.Plugins.Opener",
    };

    /// <summary>
    /// 创建 plugin 命令实例。
    /// </summary>
    /// <returns>配置好的命令。</returns>
    public static Command Create()
    {
        var command = new Command("plugin", "管理 Wails.Net 项目插件引用");

        var addCommand = CreateAddCommand();
        var removeCommand = CreateRemoveCommand();
        var listCommand = CreateListCommand();

        command.Subcommands.Add(addCommand);
        command.Subcommands.Add(removeCommand);
        command.Subcommands.Add(listCommand);

        return command;
    }

    /// <summary>
    /// 创建 plugin add 子命令。
    /// </summary>
    /// <returns>add 子命令。</returns>
    private static Command CreateAddCommand()
    {
        var nameArgument = new Argument<string>("name");
        nameArgument.Description = "插件名称（如 filesystem）或 NuGet 包标识";

        var projectOption = new Option<FileInfo?>("--project");
        projectOption.Description = "项目文件路径（.csproj），默认使用当前目录的项目";

        var versionOption = new Option<string?>("--version");
        versionOption.Description = "指定版本（未指定时使用最新版本）";

        var command = new Command("add", "向项目添加插件引用");
        command.Arguments.Add(nameArgument);
        command.Options.Add(projectOption);
        command.Options.Add(versionOption);

        command.Action = AsyncAction.Create(async (parseResult, _) =>
        {
            var name = parseResult.GetValue(nameArgument);
            var project = parseResult.GetValue(projectOption);
            var version = parseResult.GetValue(versionOption);

            var cmd = new PluginCommand();
            return await cmd.AddAsync(name!, project, version);
        });

        return command;
    }

    /// <summary>
    /// 创建 plugin remove 子命令。
    /// </summary>
    /// <returns>remove 子命令。</returns>
    private static Command CreateRemoveCommand()
    {
        var nameArgument = new Argument<string>("name");
        nameArgument.Description = "插件名称或 NuGet 包标识";

        var projectOption = new Option<FileInfo?>("--project");
        projectOption.Description = "项目文件路径（.csproj），默认使用当前目录的项目";

        var command = new Command("remove", "从项目移除插件引用");
        command.Arguments.Add(nameArgument);
        command.Options.Add(projectOption);

        command.Action = AsyncAction.Create(async (parseResult, _) =>
        {
            var name = parseResult.GetValue(nameArgument);
            var project = parseResult.GetValue(projectOption);

            var cmd = new PluginCommand();
            return await cmd.RemoveAsync(name!, project);
        });

        return command;
    }

    /// <summary>
    /// 创建 plugin list 子命令。
    /// </summary>
    /// <returns>list 子命令。</returns>
    private static Command CreateListCommand()
    {
        var projectOption = new Option<FileInfo?>("--project");
        projectOption.Description = "项目文件路径（.csproj），默认使用当前目录的项目";

        var command = new Command("list", "列出项目中的插件引用");
        command.Options.Add(projectOption);

        command.Action = AsyncAction.Create(async (parseResult, _) =>
        {
            var project = parseResult.GetValue(projectOption);

            var cmd = new PluginCommand();
            return await cmd.ListAsync(project);
        });

        return command;
    }

    /// <summary>
    /// 执行 plugin add：向 .csproj 添加 PackageReference。
    /// </summary>
    /// <param name="name">插件名称或 NuGet 包标识。</param>
    /// <param name="project">项目文件。</param>
    /// <param name="version">指定版本（可空）。</param>
    /// <returns>退出码。</returns>
    private async Task<int> AddAsync(string name, FileInfo? project, string? version)
    {
        var projectPath = ResolveProjectPath(project);
        if (projectPath is null)
        {
            Error("未找到项目文件，请通过 --project 指定，或在项目目录中运行");
            return 1;
        }

        var packageId = ResolvePackageId(name);
        if (packageId is null)
        {
            Error($"未知插件：{name}");
            Info($"内置插件：{string.Join(", ", BuiltInPlugins.Keys)}");
            Info("如需安装第三方插件，请使用完整 NuGet 包名");
            return 1;
        }

        Info($"向项目 {projectPath.Name} 添加插件：{packageId}");
        if (!string.IsNullOrEmpty(version))
        {
            Info($"版本：{version}");
        }

        // 通过 dotnet add package 命令添加，交由 .NET SDK 处理 CPM 和版本解析
        var args = new List<string>
        {
            "add",
            projectPath.FullName,
            "package",
            packageId,
        };

        if (!string.IsNullOrEmpty(version))
        {
            args.Add("--version");
            args.Add(version);
        }

        var (exitCode, output) = await RunDotnetAsync(args);
        if (exitCode != 0)
        {
            Error($"添加插件失败：dotnet add package 退出码 {exitCode}");
            if (!string.IsNullOrEmpty(output))
            {
                Info(output);
            }
            return 2;
        }

        Success($"插件 {packageId} 已添加");
        return 0;
    }

    /// <summary>
    /// 执行 plugin remove：从 .csproj 移除 PackageReference。
    /// </summary>
    /// <param name="name">插件名称或 NuGet 包标识。</param>
    /// <param name="project">项目文件。</param>
    /// <returns>退出码。</returns>
    private async Task<int> RemoveAsync(string name, FileInfo? project)
    {
        var projectPath = ResolveProjectPath(project);
        if (projectPath is null)
        {
            Error("未找到项目文件，请通过 --project 指定，或在项目目录中运行");
            return 1;
        }

        var packageId = ResolvePackageId(name) ?? name;

        Info($"从项目 {projectPath.Name} 移除插件：{packageId}");

        var args = new List<string>
        {
            "remove",
            projectPath.FullName,
            "package",
            packageId,
        };

        var (exitCode, output) = await RunDotnetAsync(args);
        if (exitCode != 0)
        {
            // 如果包不存在，dotnet remove 会返回非零退出码，但视为非错误
            if (output.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("未找到", StringComparison.OrdinalIgnoreCase))
            {
                Warn($"项目中未引用插件 {packageId}");
                return 0;
            }

            Error($"移除插件失败：dotnet remove package 退出码 {exitCode}");
            if (!string.IsNullOrEmpty(output))
            {
                Info(output);
            }
            return 2;
        }

        Success($"插件 {packageId} 已移除");
        return 0;
    }

    /// <summary>
    /// 执行 plugin list：列出 .csproj 中的所有 PackageReference。
    /// </summary>
    /// <param name="project">项目文件。</param>
    /// <returns>退出码。</returns>
    private async Task<int> ListAsync(FileInfo? project)
    {
        var projectPath = ResolveProjectPath(project);
        if (projectPath is null)
        {
            Error("未找到项目文件，请通过 --project 指定，或在项目目录中运行");
            return 1;
        }

        await Task.CompletedTask;

        try
        {
            var doc = XDocument.Load(projectPath.FullName);
            var packageRefs = doc.Descendants("PackageReference")
                .Select(e => new
                {
                    Include = e.Attribute("Include")?.Value ?? string.Empty,
                    Version = e.Attribute("Version")?.Value,
                })
                .Where(p => !string.IsNullOrEmpty(p.Include))
                .OrderBy(p => p.Include)
                .ToList();

            if (packageRefs.Count == 0)
            {
                Info("项目中没有任何 NuGet 包引用");
                return 0;
            }

            Info($"项目 {projectPath.Name} 的 NuGet 包引用：");
            foreach (var pkg in packageRefs)
            {
                var versionStr = pkg.Version is not null ? $" [v{pkg.Version}]" : string.Empty;
                Info($"  - {pkg.Include}{versionStr}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Error($"读取项目文件失败：{ex.Message}");
            return 2;
        }
    }

    /// <summary>
    /// 将插件简名解析为 NuGet 包标识。
    /// 若 name 已包含点号或与任何内置简名不匹配，则视为完整包名原样返回。
    /// </summary>
    /// <param name="name">用户输入的插件名。</param>
    /// <returns>NuGet 包标识，若无法识别则返回 null。</returns>
    internal static string? ResolvePackageId(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (BuiltInPlugins.TryGetValue(name, out var packageId))
        {
            return packageId;
        }

        // 含点号视为完整 NuGet 包名
        return name.Contains('.') ? name : null;
    }

    /// <summary>
    /// 解析项目文件路径。
    /// </summary>
    /// <param name="project">用户指定的项目文件。</param>
    /// <returns>项目文件路径，若未找到则返回 null。</returns>
    private static FileInfo? ResolveProjectPath(FileInfo? project)
    {
        if (project is not null)
        {
            return project.Exists ? project : null;
        }

        var currentDir = Directory.GetCurrentDirectory();
        var csprojFiles = Directory.GetFiles(currentDir, "*.csproj");
        return csprojFiles.Length == 1 ? new FileInfo(csprojFiles[0]) : null;
    }

    /// <summary>
    /// 运行 dotnet 命令并捕获输出。
    /// </summary>
    /// <param name="args">参数列表。</param>
    /// <returns>(退出码, 标准输出+错误输出)。</returns>
    private static async Task<(int ExitCode, string Output)> RunDotnetAsync(IReadOnlyList<string> args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        try
        {
            using var proc = new System.Diagnostics.Process { StartInfo = psi };
            proc.Start();

            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var combined = string.IsNullOrEmpty(stderr) ? stdout : $"{stdout}\n{stderr}";
            return (proc.ExitCode, combined);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
