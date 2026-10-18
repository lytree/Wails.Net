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
        formatOption.Description = "打包格式（zip、targz、nsis、appimage、deb、rpm、msix、appx）";
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

        // P2-16：MSIX 打包参数（对应 Wails v3 internal/flags/msix.go 的 ToolMSIX）。
        var publisherOption = new Option<string?>("--publisher");
        publisherOption.Description = "MSIX 包发布者标识（如 CN=CompanyName）；默认取 wails.json 的 bundle.windows.publisher 或 CN={公司名}";

        var certificateOption = new Option<FileInfo?>("--cert");
        certificateOption.Description = "MSIX 签名证书路径（.pfx）；提供后使用 Windows SDK 的 signtool 签名";

        var certificatePasswordOption = new Option<string?>("--cert-password");
        certificatePasswordOption.Description = "MSIX 签名证书密码（可选）";

        var useMsixToolOption = new Option<bool>("--use-msix-tool");
        useMsixToolOption.Description = "使用 Microsoft MSIX Packaging Tool 打包（默认使用 Windows SDK 的 MakeAppx.exe）";
        useMsixToolOption.DefaultValueFactory = _ => false;

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
        command.Options.Add(publisherOption);
        command.Options.Add(certificateOption);
        command.Options.Add(certificatePasswordOption);
        command.Options.Add(useMsixToolOption);

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
            var publisher = parseResult.GetValue(publisherOption);
            var certificate = parseResult.GetValue(certificateOption);
            var certificatePassword = parseResult.GetValue(certificatePasswordOption);
            var useMsixTool = parseResult.GetValue(useMsixToolOption);

            var cmd = new PackCommand();
            return await cmd.ExecuteAsync(project, configuration, runtime, selfContained, format, output,
                allTargets, appName, version, skipHooks, skipFrontend,
                publisher, certificate, certificatePassword, useMsixTool);
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
    /// <param name="publisher">MSIX 发布者标识（P2-16，可选）。</param>
    /// <param name="certificate">MSIX 签名证书（P2-16，可选）。</param>
    /// <param name="certificatePassword">MSIX 签名证书密码（P2-16，可选）。</param>
    /// <param name="usePackagingTool">是否使用 Microsoft MSIX Packaging Tool（P2-16，默认 MakeAppx）。</param>
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
        bool skipFrontend = false,
        string? publisher = null,
        FileInfo? certificate = null,
        string? certificatePassword = null,
        bool usePackagingTool = false)
    {
        var projectPath = ResolveProjectPath(project);
        if (projectPath is null)
        {
            Error("未找到项目文件，请通过 --project 指定，或在项目目录中运行");
            return 1;
        }

        // P2-16：msix/appx 不在 PackageFormat 枚举内（枚举由 PackageOptions 定义，本次不改动），
        // 故先解析扩展名；其余格式仍走 TryParseFormat。
        var msixExtension = MsixPackager.TryResolveExtension(format);
        PackageFormat? packageFormat = null;
        if (msixExtension is null)
        {
            if (!TryParseFormat(format, out var parsedFormat))
            {
                Error($"未知的打包格式：{format}（支持 zip、targz、nsis、appimage、deb、rpm、msix、appx）");
                return 1;
            }

            packageFormat = parsedFormat;
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
        Info($"打包格式：{(msixExtension is null ? packageFormat!.Value.ToString() : "msix")}");
        Info($"应用名称：{appName}");
        Info($"版本号：{version}");
        if (allTargets)
        {
            Info($"全目标打包（RID）：{string.Join(", ", TargetDefinitions.GetAllTargets())}");
        }

        // P2-23：macOS 输出准备（对应 Go tool_prepare_macos_output.go）。
        // 仅 macOS 主机 + macOS 目标时实际生效；其它平台为 no-op（不改变现有流程）。
        if (IsMacOsTarget(runtime))
        {
            var preparer = new MacOsOutputPreparer();
            var prepareResult = await preparer.PrepareAsync(
                workingDir, Path.Combine(workingDir, MacOsOutputPreparer.DefaultDirectoryName));
            if (!prepareResult.Success)
            {
                Error($"macOS 输出准备失败：{prepareResult.ErrorMessage}");
                return 5;
            }

            if (prepareResult.LocalOutputPath is not null)
            {
                Info($"macOS 输出已重定位到本地文件系统：{prepareResult.LocalOutputPath}");
            }
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
                // P2-16：MSIX/Appx 仅支持 Windows 目标，非 win-* RID 跳过（避免无 .exe 的发布目录报错）。
                if (msixExtension is not null && !rid.StartsWith("win-", StringComparison.OrdinalIgnoreCase))
                {
                    Warn($"[{rid}] MSIX/Appx 仅支持 Windows 目标，跳过该 RID");
                    continue;
                }

                Info($"----- 打包 {rid} -----");
                var packageOut = Path.Combine(outBase, rid);
                var exitCode = await PackageAsync(
                    projectPath, packageFormat, packageOut, appName, version, publishDir, config,
                    prefix: $"[{rid}] ",
                    msix: CreateMsixCliOptions(
                        msixExtension, publisher, certificate, certificatePassword, usePackagingTool, rid));
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
            singlePublish.OutputPath!, config,
            msix: CreateMsixCliOptions(
                msixExtension, publisher, certificate, certificatePassword, usePackagingTool, runtime));
    }

    /// <summary>
    /// 打包单个发布产物目录。
    /// </summary>
    /// <param name="projectPath">项目文件（用于解析 bundle 配置）。</param>
    /// <param name="packageFormat">打包格式（MSIX/Appx 时为 null，改走 <paramref name="msix"/>）。</param>
    /// <param name="packageOutputDir">打包输出目录。</param>
    /// <param name="appName">应用名称。</param>
    /// <param name="version">版本号。</param>
    /// <param name="publishDir">发布产物目录。</param>
    /// <param name="config">已加载的 wails.json 配置（可空）。</param>
    /// <param name="prefix">输出消息前缀（多目标打包时用于标注 RID）。</param>
    /// <param name="msix">MSIX 打包参数（P2-16，非 msix 格式时为 null）。</param>
    /// <returns>退出码：0 成功；3 打包失败。</returns>
    private async Task<int> PackageAsync(
        FileInfo projectPath,
        PackageFormat? packageFormat,
        string packageOutputDir,
        string appName,
        string version,
        string publishDir,
        ProjectConfig? config,
        string prefix = "",
        MsixCliOptions? msix = null)
    {
        // P2-16：MSIX/Appx 走独立打包器（PackageFormat 枚举不含 MSIX）。
        if (msix is not null)
        {
            return await PackageMsixAsync(
                packageOutputDir, appName, version, publishDir, config, msix, prefix);
        }

        var options = new PackageOptions
        {
            Format = packageFormat!.Value,
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
    /// 以 MSIX/Appx 格式打包发布产物（P2-16）。
    /// 对应 Wails v3 Go 版本 internal/commands/msix.go 中的 <c>ToolMSIX</c>。
    /// 工具不可用时降级为结构化目录（AppxManifest.xml + Assets + 可执行文件）并返回清晰错误。
    /// </summary>
    /// <param name="packageOutputDir">打包输出目录。</param>
    /// <param name="appName">应用名称。</param>
    /// <param name="version">版本号。</param>
    /// <param name="publishDir">发布产物目录。</param>
    /// <param name="config">已加载的 wails.json 配置（可空，用于补齐产品信息）。</param>
    /// <param name="msix">MSIX 打包参数。</param>
    /// <param name="prefix">输出消息前缀（多目标打包时用于标注 RID）。</param>
    /// <returns>退出码：0 成功；3 打包失败。</returns>
    private async Task<int> PackageMsixAsync(
        string packageOutputDir,
        string appName,
        string version,
        string publishDir,
        ProjectConfig? config,
        MsixCliOptions msix,
        string prefix)
    {
        var executableName = Packager.FindMainExecutable(publishDir, appName);
        if (executableName is null)
        {
            Error($"{prefix}MSIX 打包失败：在发布目录中未找到主可执行文件（.exe）：{publishDir}");
            return 3;
        }

        var companyName = !string.IsNullOrWhiteSpace(config?.Bundle?.Windows?.Publisher)
            ? config!.Bundle!.Windows!.Publisher!
            : "Wails.Net";

        var options = new MsixPackageOptions
        {
            ProductName = appName,
            ProductIdentifier = config?.Bundle?.Identifier ?? appName,
            ProductVersion = version,
            ProductCompany = companyName,
            ProductDescription = config?.Bundle?.LongDescription ?? config?.Bundle?.ShortDescription,
            Publisher = msix.Publisher ?? config?.Bundle?.Windows?.Publisher ?? string.Empty,
            ProcessorArchitecture = msix.Architecture,
            ExecutableName = executableName,
            ExecutablePath = Path.Combine(publishDir, executableName),
            OutputPath = Path.Combine(packageOutputDir, $"{appName}-{version}{msix.Extension}"),
            CertificatePath = msix.CertificatePath ?? string.Empty,
            CertificatePassword = msix.CertificatePassword ?? string.Empty,
            UseMsixPackagingTool = msix.UsePackagingTool,
        };

        var packager = new MsixPackager();
        var result = await packager.PackageAsync(options);

        if (!result.Success)
        {
            if (result.StructureDirectory is not null)
            {
                Warn($"{prefix}MSIX 结构化目录（含 AppxManifest.xml，可用于手动打包）：{result.StructureDirectory}");
            }

            Error($"{prefix}MSIX 打包失败：{result.ErrorMessage}");
            return 3;
        }

        if (result.Degraded && !string.IsNullOrEmpty(result.ErrorMessage))
        {
            Warn($"{prefix}{result.ErrorMessage}");
        }

        Success($"{prefix}MSIX 打包成功：{result.OutputPath}");
        return 0;
    }

    /// <summary>
    /// 构造 MSIX 打包参数（P2-16）；非 msix/appx 格式返回 null。
    /// </summary>
    /// <param name="msixExtension">MSIX/Appx 扩展名（null 表示非 MSIX 格式）。</param>
    /// <param name="publisher">发布者标识（可选）。</param>
    /// <param name="certificate">签名证书（可选）。</param>
    /// <param name="certificatePassword">签名证书密码（可选）。</param>
    /// <param name="usePackagingTool">是否使用 MSIX Packaging Tool。</param>
    /// <param name="runtime">目标 RID（可空，用于推导处理器架构）。</param>
    /// <returns>MSIX 打包参数；非 MSIX 格式返回 null。</returns>
    private static MsixCliOptions? CreateMsixCliOptions(
        string? msixExtension,
        string? publisher,
        FileInfo? certificate,
        string? certificatePassword,
        bool usePackagingTool,
        string? runtime)
    {
        return msixExtension is null
            ? null
            : new MsixCliOptions
            {
                Extension = msixExtension,
                Publisher = publisher,
                CertificatePath = certificate?.FullName,
                CertificatePassword = certificatePassword,
                UsePackagingTool = usePackagingTool,
                Architecture = MsixPackager.ResolveArchitecture(runtime),
            };
    }

    /// <summary>
    /// 判断目标是否为 macOS（P2-23：macOS 输出准备仅在 macOS 主机 + macOS 目标时生效）。
    /// </summary>
    /// <param name="runtime">目标 RID（可空）。</param>
    /// <returns>目标为 macOS 返回 true。</returns>
    private static bool IsMacOsTarget(string? runtime)
    {
        return runtime is not null
            ? runtime.StartsWith("osx", StringComparison.OrdinalIgnoreCase)
            : OperatingSystem.IsMacOS();
    }

    /// <summary>
    /// MSIX 打包的 CLI 参数集合（P2-16），避免打包方法参数膨胀。
    /// </summary>
    private sealed class MsixCliOptions
    {
        /// <summary>包扩展名（.msix / .appx）。</summary>
        public string Extension { get; init; } = MsixPackager.MsixExtension;

        /// <summary>发布者标识（对应 Go <c>--publisher</c>）。</summary>
        public string? Publisher { get; init; }

        /// <summary>签名证书路径（对应 Go <c>--cert</c>）。</summary>
        public string? CertificatePath { get; init; }

        /// <summary>签名证书密码（对应 Go <c>--cert-password</c>）。</summary>
        public string? CertificatePassword { get; init; }

        /// <summary>是否使用 MSIX Packaging Tool（对应 Go <c>--use-msix-tool</c>）。</summary>
        public bool UsePackagingTool { get; init; }

        /// <summary>处理器架构（由目标 RID 推导，对应 Go <c>--arch</c>）。</summary>
        public string Architecture { get; init; } = "x64";
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
