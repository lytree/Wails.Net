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

        var runtimeOption = new Option<string?>("--runtime");
        runtimeOption.Description = "目标运行时标识（如 win-x64、linux-x64）";

        var selfContainedOption = new Option<bool>("--self-contained");
        selfContainedOption.Description = "是否发布为自包含应用（包含 .NET 运行时）";
        selfContainedOption.DefaultValueFactory = _ => false;

        var formatOption = new Option<string>("--format");
        formatOption.Description = "打包格式（zip、targz、nsis、appimage）";
        formatOption.DefaultValueFactory = _ => OperatingSystem.IsWindows() ? "zip" : "targz";

        var outputOption = new Option<DirectoryInfo?>("--output");
        outputOption.Description = "打包输出目录（默认为 bin/packages）";

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
            var appName = parseResult.GetValue(appNameOption);
            var version = parseResult.GetValue(versionOption);
            var skipHooks = parseResult.GetValue(skipHooksOption);
            var skipFrontend = parseResult.GetValue(skipFrontendOption);

            var cmd = new PackCommand();
            return await cmd.ExecuteAsync(project, configuration, runtime, selfContained, format, output, appName, version, skipHooks, skipFrontend);
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
        string? appName,
        string? version,
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
            Error($"未知的打包格式：{format}（支持 zip、targz、nsis、appimage）");
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

        // 执行前端构建（install + build）
        if (!skipFrontend && config?.Frontend is { } frontend)
        {
            var frontendDir = Path.Combine(workingDir, frontend.Dir);
            if (Directory.Exists(frontendDir))
            {
                if (!string.IsNullOrWhiteSpace(frontend.InstallCommand))
                {
                    Info($"安装前端依赖：{frontend.InstallCommand}");
                    var installResult = await BuildHooks.ExecuteAsync(frontend.InstallCommand, frontendDir);
                    if (!installResult.Success)
                    {
                        Error($"前端依赖安装失败：{installResult.ErrorMessage}");
                        if (!string.IsNullOrEmpty(installResult.Output))
                        {
                            Info(installResult.Output);
                        }
                        return 3;
                    }
                }

                if (!string.IsNullOrWhiteSpace(frontend.BuildCommand))
                {
                    Info($"构建前端：{frontend.BuildCommand}");
                    var frontendBuild = await BuildHooks.ExecuteAsync(frontend.BuildCommand, frontendDir);
                    if (!frontendBuild.Success)
                    {
                        Error($"前端构建失败：{frontendBuild.ErrorMessage}");
                        if (!string.IsNullOrEmpty(frontendBuild.Output))
                        {
                            Info(frontendBuild.Output);
                        }
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
        var publishResult = await builder.PublishAsync(
            projectPath,
            configuration,
            runtime,
            selfContained);

        if (!publishResult.Success)
        {
            Error($"发布失败：{publishResult.ErrorMessage}");
            if (!string.IsNullOrEmpty(publishResult.BuildLog))
            {
                Info(publishResult.BuildLog);
            }
            return 2;
        }

        Success($"发布成功：{publishResult.OutputPath}");

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
        var options = new PackageOptions
        {
            Format = packageFormat,
            OutputDirectory = output?.FullName ?? "bin/packages",
            AppName = appName,
            Version = version,
        };

        // 应用 wails.json 中的 bundle 配置到 PackageOptions（不覆盖命令行参数）
        if (config is not null)
        {
            Packager.ApplyBundleConfig(options, config.Bundle);
        }

        var packager = new Packager();
        var packageResult = await packager.PackageAsync(publishResult.OutputPath!, options);

        if (!packageResult.Success)
        {
            Error($"打包失败：{packageResult.ErrorMessage}");
            return 3;
        }

        Success($"打包成功：{packageResult.OutputPath}");
        if (!string.IsNullOrEmpty(packageResult.ChecksumPath))
        {
            Info($"校验和文件：{packageResult.ChecksumPath}");
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
