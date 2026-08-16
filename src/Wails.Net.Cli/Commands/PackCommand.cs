using System.CommandLine;
using Wails.Net.Cli.Build;
using Wails.Net.Cli.Config;

namespace Wails.Net.Cli.Commands;

/// <summary>
/// pack 命令：打包 Wails.Net 项目为可分发应用。
/// 对应 Wails v3 Go 版本 cmd/wails3/package.go 中的打包逻辑。
/// 先调用 dotnet publish 生成产物，再打包为压缩包或安装程序。
/// 支持从 wails.json 加载应用名称、版本与构建钩子。
/// </summary>
internal sealed class PackCommand : CliCommandBase
{
    /// <summary>
    /// 创建 pack 命令实例。
    /// </summary>
    /// <returns>配置好的命令。</returns>
    public static Command Create()
    {
        var projectOption = new Option<FileInfo?>("--project");
        projectOption.Description = "项目文件路径（.csproj），默认使用当前目录的项目";

        var configurationOption = new Option<string>("--configuration");
        configurationOption.Description = "构建配置（Debug 或 Release）";
        configurationOption.DefaultValueFactory = _ => "Release";

        // SDK-4：--target 为统一入口（替代/兼容 --runtime），二者为同一选项的别名。
        // RID 经 Wails.Net.Sdk 的 RID→平台映射（SDK-1）自动切换平台引用。
        var runtimeOption = new Option<string?>("--target");
        runtimeOption.Description = "目标运行时标识（RID，如 win-x64、linux-x64、android-arm64）。SDK 依据 RID 前缀自动选择平台引用。兼容别名：--runtime";
        runtimeOption.Aliases.Add("--runtime");

        var selfContainedOption = new Option<bool>("--self-contained");
        selfContainedOption.Description = "是否发布为自包含应用（包含 .NET 运行时）";
        selfContainedOption.DefaultValueFactory = _ => false;

        var formatOption = new Option<string>("--format");
        formatOption.Description = "打包格式（zip、targz、nsis、appimage、deb、rpm）";
        formatOption.DefaultValueFactory = _ => OperatingSystem.IsWindows() ? "zip" : "targz";

        var outputOption = new Option<DirectoryInfo?>("--output");
        outputOption.Description = "打包输出目录（默认为 bin/packages）";

        // SDK-4：--all-targets 基于 RID 的多目标打包。
        var allTargetsOption = new Option<bool>("--all-targets");
        allTargetsOption.Description = "打包所有目标 RID（win-x64、linux-x64、android-arm64；macOS 主机额外加入 osx-*）。每 RID 先发布后打包，产物按 RID 分目录输出";
        allTargetsOption.DefaultValueFactory = _ => false;

        var appNameOption = new Option<string?>("--app-name");
        appNameOption.Description = "应用名称（默认取 wails.json 的 name 字段）";

        var versionOption = new Option<string?>("--version");
        versionOption.Description = "版本号（默认取 wails.json 的 version 字段）";

        var skipHooksOption = new Option<bool>("--skip-hooks");
        skipHooksOption.Description = "跳过 wails.json 中的 beforeBuildCommand / afterBuildCommand 钩子";
        skipHooksOption.DefaultValueFactory = _ => false;

        var skipFrontendOption = new Option<bool>("--skip-frontend");
        skipFrontendOption.Description = "跳过前端构建（frontend.buildCommand / installCommand）";
        skipFrontendOption.DefaultValueFactory = _ => false;

        var command = new Command("pack", "打包 Wails.Net 项目为可分发应用");
        command.Options.Add(projectOption);
        command.Options.Add(configurationOption);
        command.Options.Add(runtimeOption);
        command.Options.Add(selfContainedOption);
        command.Options.Add(formatOption);
        command.Options.Add(outputOption);
        command.Options.Add(allTargetsOption);
        command.Options.Add(appNameOption);
        command.Options.Add(versionOption);
        command.Options.Add(skipHooksOption);
        command.Options.Add(skipFrontendOption);

        command.Action = AsyncAction.Create(async (parseResult, _) =>
        {
            var project = parseResult.GetValue(projectOption);
            var configuration = parseResult.GetValue(configurationOption) ?? "Release";
            var runtime = parseResult.GetValue(runtimeOption);
            var selfContained = parseResult.GetValue(selfContainedOption);
            var format = parseResult.GetValue(formatOption) ?? (OperatingSystem.IsWindows() ? "zip" : "targz");
            var output = parseResult.GetValue(outputOption);
            var allTargets = parseResult.GetValue(allTargetsOption);
            var appName = parseResult.GetValue(appNameOption);
            var version = parseResult.GetValue(versionOption);
            var skipHooks = parseResult.GetValue(skipHooksOption);
            var skipFrontend = parseResult.GetValue(skipFrontendOption);

            var cmd = new PackCommand();
            return await cmd.ExecuteAsync(project, configuration, runtime, selfContained, format, output, allTargets, appName, version, skipHooks, skipFrontend);
        });

        return command;
    }

    /// <summary>
    /// 执行 pack 命令。
    /// </summary>
    /// <param name="project">项目文件。</param>
    /// <param name="configuration">构建配置。</param>
    /// <param name="runtime">运行时标识。</param>
    /// <param name="selfContained">是否自包含。</param>
    /// <param name="format">打包格式字符串。</param>
    /// <param name="output">输出目录。</param>
    /// <param name="allTargets">是否打包所有目标 RID（SDK-4）。</param>
    /// <param name="appName">应用名称（若为 null，则从 wails.json 读取）。</param>
    /// <param name="version">版本号（若为 null，则从 wails.json 读取）。</param>
    /// <param name="skipHooks">是否跳过构建钩子。</param>
    /// <param name="skipFrontend">是否跳过前端构建。</param>
    /// <returns>退出码。</returns>
    private async Task<int> ExecuteAsync(
        FileInfo? project,
        string configuration,
        string? runtime,
        bool selfContained,
        string format,
        DirectoryInfo? output,
        bool allTargets = false,
        string? appName = null,
        string? version = null,
        bool skipHooks = false,
        bool skipFrontend = false)
    {
        var projectPath = ResolveProjectPath(project);
        if (projectPath is null)
        {
            Error("未找到项目文件，请通过 --project 指定，或在项目目录中运行");
            return 1;
        }

        if (!TryParseFormat(format, out var packageFormat))
        {
            Error($"未知的打包格式：{format}（支持 zip、targz、nsis、appimage、deb、rpm）");
            return 1;
        }

        var workingDir = Path.GetDirectoryName(projectPath.FullName) ?? Directory.GetCurrentDirectory();

        // 加载 wails.json（若存在），并从中解析默认应用名称与版本
        var (config, configPath) = await ProjectConfig.FindAndLoadAsync(projectPath.FullName);
        if (config is not null)
        {
            Info($"加载配置：{configPath}");
        }

        appName ??= !string.IsNullOrWhiteSpace(config?.Name) ? config!.Name : "WailsApp";
        version ??= !string.IsNullOrWhiteSpace(config?.Version) ? config!.Version : "1.0.0";

        Info($"打包项目：{projectPath.FullName}");
        Info($"配置：{configuration}");
        if (!string.IsNullOrEmpty(runtime))
        {
            Info($"运行时：{runtime}");
        }
        Info($"自包含：{(selfContained ? "是" : "否")}");
        Info($"打包格式：{packageFormat}");
        Info($"应用名称：{appName}");
        Info($"版本号：{version}");
        if (allTargets)
        {
            Info($"全目标打包（RID）：{string.Join(", ", TargetDefinitions.GetAllTargets())}");
        }

        // 执行前端构建（install + build）
        if (!skipFrontend && config?.Frontend is { } frontend)
        {
            var frontendDir = Path.Combine(workingDir, frontend.Dir);
            if (Directory.Exists(frontendDir))
            {
                // 包管理器自动检测（pnpm 优先，npm 回退）；install/build 优先使用 wails.json
                // 显式命令，缺失时回退工具链默认值（install 在 pnpm 工作区根执行）。
                FrontendToolchain? toolchain = null;
                try
                {
                    toolchain = await FrontendToolchain.DetectAsync();
                }
                catch (InvalidOperationException ex)
                {
                    if (string.IsNullOrWhiteSpace(frontend.InstallCommand) ||
                        string.IsNullOrWhiteSpace(frontend.BuildCommand))
                    {
                        Error(ex.Message);
                        return 3;
                    }

                    Warn($"未检测到 pnpm/npm，使用 wails.json 中显式命令：{ex.Message}");
                }

                if (!string.IsNullOrWhiteSpace(frontend.InstallCommand))
                {
                    Info($"安装前端依赖：{frontend.InstallCommand}");
                    var installResult = await BuildHooks.ExecuteAsync(
                        frontend.InstallCommand, frontendDir, streamOutput: true);
                    if (!installResult.Success)
                    {
                        // 输出已在执行过程中流式打印，此处不再重复
                        Error($"前端依赖安装失败：{installResult.ErrorMessage}");
                        return 3;
                    }
                }
                else if (toolchain is not null)
                {
                    Info($"安装前端依赖（{toolchain.PackageManager}，工作区根）");
                    var code = await toolchain.InstallAsync(frontendDir);
                    if (code != 0)
                    {
                        Error($"前端依赖安装失败（退出码 {code}）");
                        return 3;
                    }
                }

                if (!string.IsNullOrWhiteSpace(frontend.BuildCommand))
                {
                    Info($"构建前端：{frontend.BuildCommand}");
                    var frontendBuild = await BuildHooks.ExecuteAsync(
                        frontend.BuildCommand, frontendDir, streamOutput: true);
                    if (!frontendBuild.Success)
                    {
                        // 输出已在执行过程中流式打印，此处不再重复
                        Error($"前端构建失败：{frontendBuild.ErrorMessage}");
                        return 3;
                    }
                }
                else if (toolchain is not null)
                {
                    Info($"构建前端（{toolchain.PackageManager} build）");
                    var code = await toolchain.BuildAsync(frontendDir);
                    if (code != 0)
                    {
                        Error($"前端构建失败（退出码 {code}）");
                        return 3;
                    }
                }
            }
        }

        // 执行 beforeBuildCommand 钩子
        if (!skipHooks && !string.IsNullOrWhiteSpace(config?.BeforeBuildCommand))
        {
            Info($"执行 beforeBuildCommand：{config!.BeforeBuildCommand}");
            var beforeResult = await BuildHooks.ExecuteAsync(config.BeforeBuildCommand, workingDir);
            if (!beforeResult.Success)
            {
                Error($"beforeBuildCommand 失败：{beforeResult.ErrorMessage}");
                if (!string.IsNullOrEmpty(beforeResult.Output))
                {
                    Info(beforeResult.Output);
                }
                return 4;
            }
        }

        // 第一步：发布项目
        var builder = new ProjectBuilder();
        var packager = new Packager();

        // SDK-4：--all-targets 多目标打包。每 RID 先发布再打包，产物按 RID 分目录输出。
        if (allTargets)
        {
            var targets = TargetDefinitions.GetAllTargets();
            var outBase = output?.FullName ?? Path.Combine(workingDir, "bin", "packages-all");
            Info($"--all-targets：打包 {targets.Length} 个目标 RID：{string.Join(", ", targets)}");

            var published = new List<(string Rid, string PublishDir)>();
            foreach (var rid in targets)
            {
                Info($"----- 发布 {rid} -----");
                var publishOut = Path.Combine(outBase, "publish", rid);
                var publishResult = await builder.PublishAsync(
                    projectPath, configuration, rid, selfContained, publishOut);
                if (!publishResult.Success)
                {
                    Error($"发布失败（{rid}）：{publishResult.ErrorMessage}");
                    if (!string.IsNullOrEmpty(publishResult.BuildLog))
                    {
                        Info(publishResult.BuildLog);
                    }
                    return 2;
                }
                published.Add((rid, publishResult.OutputPath!));
            }

            // 执行 afterBuildCommand 钩子（仅一次）
            if (!skipHooks && !string.IsNullOrWhiteSpace(config?.AfterBuildCommand))
            {
                Info($"执行 afterBuildCommand：{config!.AfterBuildCommand}");
                var afterResult = await BuildHooks.ExecuteAsync(config.AfterBuildCommand, workingDir);
                if (!afterResult.Success)
                {
                    Warn($"afterBuildCommand 失败：{afterResult.ErrorMessage}");
                    if (!string.IsNullOrEmpty(afterResult.Output))
                    {
                        Info(afterResult.Output);
                    }
                }
            }

            foreach (var (rid, publishDir) in published)
            {
                Info($"----- 打包 {rid} -----");
                var packageOut = Path.Combine(outBase, rid);
                var exitCode = await PackageAsync(
                    projectPath, packageFormat, packageOut, appName, version, publishDir, config,
                    prefix: $"[{rid}] ");
                if (exitCode != 0)
                {
                    return exitCode;
                }
            }

            Success($"全目标打包完成：{outBase}");
            return 0;
        }

        // 单目标（默认或 --target/--runtime 指定）
        var singlePublish = await builder.PublishAsync(
            projectPath, configuration, runtime, selfContained);
        if (!singlePublish.Success)
        {
            Error($"发布失败：{singlePublish.ErrorMessage}");
            if (!string.IsNullOrEmpty(singlePublish.BuildLog))
            {
                Info(singlePublish.BuildLog);
            }
            return 2;
        }

        Success($"发布成功：{singlePublish.OutputPath}");

        // 执行 afterBuildCommand 钩子
        if (!skipHooks && !string.IsNullOrWhiteSpace(config?.AfterBuildCommand))
        {
            Info($"执行 afterBuildCommand：{config!.AfterBuildCommand}");
            var afterResult = await BuildHooks.ExecuteAsync(config.AfterBuildCommand, workingDir);
            if (!afterResult.Success)
            {
                Warn($"afterBuildCommand 失败：{afterResult.ErrorMessage}");
                if (!string.IsNullOrEmpty(afterResult.Output))
                {
                    Info(afterResult.Output);
                }
                // afterBuildCommand 失败不视为打包失败
            }
        }

        // 第二步：打包发布产物
        return await PackageAsync(
            projectPath, packageFormat, output?.FullName ?? "bin/packages", appName, version,
            singlePublish.OutputPath!, config);
    }

    /// <summary>
    /// 打包单个发布产物目录。
    /// </summary>
    /// <param name="projectPath">项目文件（用于解析 bundle 配置）。</param>
    /// <param name="packageFormat">打包格式。</param>
    /// <param name="packageOutputDir">打包输出目录。</param>
    /// <param name="appName">应用名称。</param>
    /// <param name="version">版本号。</param>
    /// <param name="publishDir">发布产物目录。</param>
    /// <param name="config">已加载的 wails.json 配置（可空）。</param>
    /// <param name="prefix">输出消息前缀（多目标打包时用于标注 RID）。</param>
    /// <returns>退出码：0 成功；3 打包失败。</returns>
    private async Task<int> PackageAsync(
        FileInfo projectPath,
        PackageFormat packageFormat,
        string packageOutputDir,
        string appName,
        string version,
        string publishDir,
        ProjectConfig? config,
        string prefix = "")
    {
        var options = new PackageOptions
        {
            Format = packageFormat,
            OutputDirectory = packageOutputDir,
            AppName = appName,
            Version = version,
        };

        // 应用 wails.json 中的 bundle 配置到 PackageOptions（不覆盖命令行参数）
        if (config is not null)
        {
            Packager.ApplyBundleConfig(options, config.Bundle);
        }

        var packager = new Packager();
        var packageResult = await packager.PackageAsync(publishDir, options);

        if (!packageResult.Success)
        {
            Error($"{prefix}打包失败：{packageResult.ErrorMessage}");
            return 3;
        }

        Success($"{prefix}打包成功：{packageResult.OutputPath}");
        if (!string.IsNullOrEmpty(packageResult.ChecksumPath))
        {
            Info($"{prefix}校验和文件：{packageResult.ChecksumPath}");
        }

        return 0;
    }

    /// <summary>
    /// 解析打包格式字符串。
    /// </summary>
    /// <param name="format">格式字符串。</param>
    /// <param name="packageFormat">解析后的枚举值。</param>
    /// <returns>是否解析成功。</returns>
    private static bool TryParseFormat(string format, out PackageFormat packageFormat)
    {
        switch (format.ToLowerInvariant())
        {
            case "zip":
                packageFormat = PackageFormat.Zip;
                return true;
            case "targz":
            case "tar.gz":
                packageFormat = PackageFormat.TarGz;
                return true;
            case "nsis":
                packageFormat = PackageFormat.Nsis;
                return true;
            case "appimage":
                packageFormat = PackageFormat.AppImage;
                return true;
            case "deb":
                packageFormat = PackageFormat.Deb;
                return true;
            case "rpm":
                packageFormat = PackageFormat.Rpm;
                return true;
            default:
                packageFormat = PackageFormat.Zip;
                return false;
        }
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
}
