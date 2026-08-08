using System.CommandLine;
using System.Xml.Linq;
using Wails.Net.Cli.Build;
using Wails.Net.Cli.Scaffolding;

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

        var newCommand = CreateNewCommand();
        var addCommand = CreateAddCommand();
        var removeCommand = CreateRemoveCommand();
        var listCommand = CreateListCommand();
        var buildCommand = CreateBuildCommand();
        var publishCommand = CreatePublishCommand();

        command.Subcommands.Add(newCommand);
        command.Subcommands.Add(addCommand);
        command.Subcommands.Add(removeCommand);
        command.Subcommands.Add(listCommand);
        command.Subcommands.Add(buildCommand);
        command.Subcommands.Add(publishCommand);

        return command;
    }

    /// <summary>
    /// 创建 plugin new 子命令：生成插件「前后端一体双包」脚手架。
    /// 对应 docs/development/plugin-platform-split.md 的插件拆分模型。
    /// </summary>
    /// <returns>new 子命令。</returns>
    private static Command CreateNewCommand()
    {
        var nameArgument = new Argument<string>("name");
        nameArgument.Description = "插件名称（PascalCase 或 kebab-case，如 Updater / file-system）";

        var prefixOption = new Option<string?>("--prefix");
        prefixOption.Description = "命令前缀（默认取 kebab-case 插件名，如 updater / file-system）";

        var platformOption = new Option<string>("--platform");
        platformOption.Description = "插件平台类型：desktop（桌面通用）/ mobile（移动端）/ platform-special（平台特有）";
        platformOption.DefaultValueFactory = _ => "desktop";

        var forceOption = new Option<bool>("--force");
        forceOption.Description = "目录已存在时覆盖生成";
        forceOption.DefaultValueFactory = _ => false;

        var command = new Command("new", "生成插件前后端双包脚手架");
        command.Arguments.Add(nameArgument);
        command.Options.Add(prefixOption);
        command.Options.Add(platformOption);
        command.Options.Add(forceOption);

        command.Action = AsyncAction.Create(async (parseResult, _) =>
        {
            var name = parseResult.GetValue(nameArgument);
            var prefix = parseResult.GetValue(prefixOption);
            var platform = parseResult.GetValue(platformOption) ?? "desktop";
            var force = parseResult.GetValue(forceOption);

            var cmd = new PluginCommand();
            return await cmd.ExecuteNewAsync(name!, prefix, platform, force);
        });

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
    /// 创建 plugin build 子命令：构建插件双包（后端 dotnet pack + 前端 pnpm build）。
    /// </summary>
    /// <returns>build 子命令。</returns>
    private static Command CreateBuildCommand()
    {
        var pluginOption = new Option<string?>("--plugin");
        pluginOption.Description = "插件短名（如 updater）或完整包名；未指定时构建仓库中所有插件";

        var backendOnlyOption = new Option<bool>("--backend-only");
        backendOnlyOption.Description = "仅构建后端 NuGet 包（dotnet pack）";
        backendOnlyOption.DefaultValueFactory = _ => false;

        var frontendOnlyOption = new Option<bool>("--frontend-only");
        frontendOnlyOption.Description = "仅构建前端 npm 包（pnpm build）";
        frontendOnlyOption.DefaultValueFactory = _ => false;

        var configurationOption = new Option<string>("--configuration");
        configurationOption.Description = "dotnet pack 构建配置（Debug 或 Release）";
        configurationOption.DefaultValueFactory = _ => "Release";

        var outputOption = new Option<DirectoryInfo?>("--output");
        outputOption.Description = "NuGet 包输出目录（默认 <仓库根>/artifacts/nupkg）";

        var command = new Command("build", "构建插件双包（NuGet + npm）");
        command.Options.Add(pluginOption);
        command.Options.Add(backendOnlyOption);
        command.Options.Add(frontendOnlyOption);
        command.Options.Add(configurationOption);
        command.Options.Add(outputOption);

        command.Action = AsyncAction.Create(async (parseResult, _) =>
        {
            var pluginName = parseResult.GetValue(pluginOption);
            var backendOnly = parseResult.GetValue(backendOnlyOption);
            var frontendOnly = parseResult.GetValue(frontendOnlyOption);
            var configuration = parseResult.GetValue(configurationOption) ?? "Release";
            var output = parseResult.GetValue(outputOption);

            if (backendOnly && frontendOnly)
            {
                Error("--backend-only 与 --frontend-only 互斥，请仅指定其中一个");
                return 1;
            }

            var cmd = new PluginCommand();
            return await cmd.ExecuteBuildAsync(pluginName, configuration, output?.FullName, backendOnly, frontendOnly);
        });

        return command;
    }

    /// <summary>
    /// 创建 plugin publish 子命令：发布插件双包（NuGet push + pnpm publish）。
    /// </summary>
    /// <returns>publish 子命令。</returns>
    private static Command CreatePublishCommand()
    {
        var pluginOption = new Option<string?>("--plugin");
        pluginOption.Description = "插件短名（如 updater）或完整包名；未指定时发布仓库中所有插件";

        var skipBuildOption = new Option<bool>("--skip-build");
        skipBuildOption.Description = "跳过发布前构建，直接发布已有产物";
        skipBuildOption.DefaultValueFactory = _ => false;

        var sourceOption = new Option<string?>("--source");
        sourceOption.Description = "NuGet 源地址（默认 https://api.nuget.org/v3/index.json）";

        var apiKeyOption = new Option<string?>("--api-key");
        apiKeyOption.Description = "NuGet API Key（未提供时读取环境变量 NUGET_API_KEY）";

        var dryRunOption = new Option<bool>("--dry-run");
        dryRunOption.Description = "演练模式：打印将要执行的命令，但不真正发布";
        dryRunOption.DefaultValueFactory = _ => false;

        var backendOnlyOption = new Option<bool>("--backend-only");
        backendOnlyOption.Description = "仅发布后端 NuGet 包（仅纯后端插件可用）";
        backendOnlyOption.DefaultValueFactory = _ => false;

        var frontendOnlyOption = new Option<bool>("--frontend-only");
        frontendOnlyOption.Description = "仅发布前端 npm 包（仅纯前端插件可用）";
        frontendOnlyOption.DefaultValueFactory = _ => false;

        var configurationOption = new Option<string>("--configuration");
        configurationOption.Description = "dotnet pack 构建配置（Debug 或 Release）";
        configurationOption.DefaultValueFactory = _ => "Release";

        var outputOption = new Option<DirectoryInfo?>("--output");
        outputOption.Description = "NuGet 包输出目录（默认 <仓库根>/artifacts/nupkg）";

        var command = new Command("publish", "发布插件双包到 NuGet 与 npm");
        command.Options.Add(pluginOption);
        command.Options.Add(skipBuildOption);
        command.Options.Add(sourceOption);
        command.Options.Add(apiKeyOption);
        command.Options.Add(dryRunOption);
        command.Options.Add(backendOnlyOption);
        command.Options.Add(frontendOnlyOption);
        command.Options.Add(configurationOption);
        command.Options.Add(outputOption);

        command.Action = AsyncAction.Create(async (parseResult, _) =>
        {
            var pluginName = parseResult.GetValue(pluginOption);
            var skipBuild = parseResult.GetValue(skipBuildOption);
            var source = parseResult.GetValue(sourceOption);
            var apiKey = parseResult.GetValue(apiKeyOption);
            var dryRun = parseResult.GetValue(dryRunOption);
            var backendOnly = parseResult.GetValue(backendOnlyOption);
            var frontendOnly = parseResult.GetValue(frontendOnlyOption);
            var configuration = parseResult.GetValue(configurationOption) ?? "Release";
            var output = parseResult.GetValue(outputOption);

            if (backendOnly && frontendOnly)
            {
                Error("--backend-only 与 --frontend-only 互斥，请仅指定其中一个");
                return 1;
            }

            var cmd = new PluginCommand();
            return await cmd.ExecutePublishAsync(
                pluginName, configuration, output?.FullName, source, apiKey, skipBuild, dryRun, backendOnly, frontendOnly);
        });

        return command;
    }

    /// <summary>
    /// 执行 plugin new：在仓库中生成插件前后端双包脚手架。
    /// </summary>
    /// <param name="name">插件名称。</param>
    /// <param name="prefix">命令前缀（可空，默认 kebab-case 插件名）。</param>
    /// <param name="platform">平台类型：desktop / mobile / platform-special。</param>
    /// <param name="force">已存在时覆盖。</param>
    /// <returns>退出码。</returns>
    private async Task<int> ExecuteNewAsync(string name, string? prefix, string platform, bool force)
    {
        if (!PluginScaffolder.IsValidPluginName(name))
        {
            Error("插件名称仅允许字母与数字，且首字符不能为数字（如 Updater、FileSystem）");
            return 1;
        }

        if (!PluginScaffolder.IsValidPlatform(platform))
        {
            Error($"不支持的平台类型：{platform}");
            Info($"支持的平台：{string.Join(", ", PluginScaffolder.GetSupportedPlatforms())}");
            return 1;
        }

        var repoRoot = PluginBuilder.FindRepoRoot(Directory.GetCurrentDirectory());
        if (repoRoot is null)
        {
            Error("未找到 Wails.Net 仓库根（含 Directory.Build.props）。请在仓库内运行本命令");
            return 1;
        }

        var commandPrefix = string.IsNullOrWhiteSpace(prefix)
            ? PluginBuilder.ToKebabCase(name)
            : prefix.Trim().ToLowerInvariant();

        Info($"生成插件：{name}（{PluginBuilder.ToPascalCase(name)}）");
        Info($"命令前缀：{commandPrefix}");
        Info($"平台类型：{platform}");
        Info($"仓库根：{repoRoot}");

        var scaffolder = new PluginScaffolder();
        var result = await scaffolder.ScaffoldAsync(name, commandPrefix, platform, repoRoot, force);

        if (!result.Success)
        {
            Error($"脚手架失败：{result.ErrorMessage}");
            return 2;
        }

        Success("插件脚手架生成完成。创建的文件：");
        foreach (var file in result.CreatedFiles)
        {
            Info($"  - {file}");
        }

        Info(string.Empty);
        Info("后续步骤：");
        Info($"  dotnet sln Wails.Net.slnx add src/Wails.Net.Plugins.{PluginBuilder.ToPascalCase(name)}/Wails.Net.Plugins.{PluginBuilder.ToPascalCase(name)}.csproj");
        Info($"  dotnet sln Wails.Net.slnx add tests/Wails.Net.Plugins.{PluginBuilder.ToPascalCase(name)}.Tests/Wails.Net.Plugins.{PluginBuilder.ToPascalCase(name)}.Tests.csproj");
        Info($"  cd packages/wails-net-plugin-{commandPrefix} && pnpm install && pnpm build");
        Info($"  wails plugin build --plugin {commandPrefix}");
        Info("然后：在 Program.cs 中注册插件（后端 UsePlugin，前端 import @wails-net/plugin-{name}）");

        return 0;
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
    /// 执行 plugin build：构建一个或多个插件的双包。
    /// </summary>
    /// <param name="pluginName">插件短名（可空，空则构建全部）。</param>
    /// <param name="configuration">构建配置。</param>
    /// <param name="outputDir">NuGet 输出目录（可空）。</param>
    /// <param name="backendOnly">仅后端。</param>
    /// <param name="frontendOnly">仅前端。</param>
    /// <returns>退出码。</returns>
    private async Task<int> ExecuteBuildAsync(
        string? pluginName,
        string configuration,
        string? outputDir,
        bool backendOnly,
        bool frontendOnly)
    {
        var plugins = PluginBuilder.DiscoverPlugins(pluginName);
        if (plugins.Count == 0)
        {
            Error(pluginName is null
                ? "未找到任何插件。请确认当前目录位于 Wails.Net 仓库（含 src/ 与 packages/）中"
                : $"未找到插件：{pluginName}");
            var all = PluginBuilder.DiscoverPlugins();
            if (all.Count > 0)
            {
                Info($"仓库中的插件：{string.Join(", ", all.Select(p => p.Name))}");
            }
            return 1;
        }

        var builder = new PluginBuilder();
        foreach (var plugin in plugins)
        {
            Info($"===== 构建插件：{plugin.Name} =====");
            var code = await builder.BuildAsync(plugin, configuration, outputDir, backendOnly, frontendOnly);
            if (code != 0)
            {
                Error($"插件 {plugin.Name} 构建失败（退出码 {code}）");
                return code;
            }
        }

        Success($"插件构建完成：{string.Join(", ", plugins.Select(p => p.Name))}");
        return 0;
    }

    /// <summary>
    /// 执行 plugin publish：发布一个或多个插件的双包（含版本一致性硬校验）。
    /// </summary>
    /// <param name="pluginName">插件短名（可空，空则发布全部）。</param>
    /// <param name="configuration">构建配置。</param>
    /// <param name="outputDir">NuGet 输出目录（可空）。</param>
    /// <param name="source">NuGet 源地址（可空）。</param>
    /// <param name="apiKey">NuGet API Key（可空）。</param>
    /// <param name="skipBuild">跳过发布前构建。</param>
    /// <param name="dryRun">演练模式。</param>
    /// <param name="backendOnly">仅后端。</param>
    /// <param name="frontendOnly">仅前端。</param>
    /// <returns>退出码。</returns>
    private async Task<int> ExecutePublishAsync(
        string? pluginName,
        string configuration,
        string? outputDir,
        string? source,
        string? apiKey,
        bool skipBuild,
        bool dryRun,
        bool backendOnly,
        bool frontendOnly)
    {
        var plugins = PluginBuilder.DiscoverPlugins(pluginName);
        if (plugins.Count == 0)
        {
            Error(pluginName is null
                ? "未找到任何插件。请确认当前目录位于 Wails.Net 仓库（含 src/ 与 packages/）中"
                : $"未找到插件：{pluginName}");
            var all = PluginBuilder.DiscoverPlugins();
            if (all.Count > 0)
            {
                Info($"仓库中的插件：{string.Join(", ", all.Select(p => p.Name))}");
            }
            return 1;
        }

        if (dryRun)
        {
            Warn("演练模式（--dry-run）：将打印发布计划，不真正上传");
        }

        var publisher = new PluginPublisher();
        foreach (var plugin in plugins)
        {
            Info($"===== 发布插件：{plugin.Name} =====");
            var code = await publisher.PublishAsync(
                plugin, configuration, outputDir, source, apiKey, skipBuild, dryRun, backendOnly, frontendOnly);
            if (code != 0)
            {
                Error($"插件 {plugin.Name} 发布失败（退出码 {code}）");
                return code;
            }
        }

        Success($"插件发布完成：{string.Join(", ", plugins.Select(p => p.Name))}");
        return 0;
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
