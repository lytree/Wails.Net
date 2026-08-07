using System.CommandLine;
using System.Diagnostics;
using Wails.Net.Cli.Build;
using Wails.Net.Cli.Config;

namespace Wails.Net.Cli.Commands;

/// <summary>
/// dev 命令：启动开发服务器与热更新。
/// 对应 Wails v3 Go 版本 cmd/wails3/dev.go。
/// 内部调用 dotnet watch 实现文件变更时自动重建与重启，
/// 并支持从 wails.json 加载 beforeDevCommand / afterDevCommand 钩子。
/// </summary>
internal sealed class DevCommand : CliCommandBase
{
    /// <summary>
    /// 创建 dev 命令实例。
    /// </summary>
    /// <returns>配置好的命令。</returns>
    public static Command Create()
    {
        var projectOption = new Option<FileInfo?>("--project");
        projectOption.Description = "项目文件路径（.csproj），默认使用当前目录的项目";

        var noHotReloadOption = new Option<bool>("--no-hot-reload");
        noHotReloadOption.Description = "禁用热更新，每次变更后完整重启";
        noHotReloadOption.DefaultValueFactory = _ => false;

        var verboseOption = new Option<bool>("--verbose");
        verboseOption.Description = "输出详细日志";
        verboseOption.DefaultValueFactory = _ => false;

        var skipHooksOption = new Option<bool>("--skip-hooks");
        skipHooksOption.Description = "跳过 wails.json 中的 beforeDevCommand / afterDevCommand 钩子";
        skipHooksOption.DefaultValueFactory = _ => false;

        // 对应 Tauri v2 / Wails v3 的 `tauri dev` / `wails dev` 行为。
        // Debug 模式默认行为：
        //   1) 启动前端 dev server（vite dev）在 frontend.dir
        //   2) 启动 dotnet watch（监听 .cs 变更）
        //   3) 注入 WAILS_DEBUG=true 环境变量（Program.cs 据此切换 UI 行为）
        //   4) --open-devtools 时同时设置 WAILS_OPEN_DEVTOOLS=true
        var openDevToolsOption = new Option<bool>("--open-devtools");
        openDevToolsOption.Description = "在 Debug 模式下自动打开 WebView2 DevTools（需 Program.cs 配合识别 WAILS_OPEN_DEVTOOLS 环境变量）";
        openDevToolsOption.DefaultValueFactory = _ => false;

        var frontendOnlyOption = new Option<bool>("--frontend-only");
        frontendOnlyOption.Description = "仅启动前端 dev server（不启动 .NET 后端，需在另一终端手动 dotnet run）";
        frontendOnlyOption.DefaultValueFactory = _ => false;

        var backendOnlyOption = new Option<bool>("--backend-only");
        backendOnlyOption.Description = "仅启动 .NET 后端（不启动前端 dev server，使用已构建的 frontend/dist）";
        backendOnlyOption.DefaultValueFactory = _ => false;

        var platformOption = new Option<string?>("--platform");
        platformOption.Description = "强制指定平台（windows/linux/android），覆盖 TFM 推断。";

        var command = new Command("dev", "启动开发服务器（热更新）");
        command.Options.Add(projectOption);
        command.Options.Add(noHotReloadOption);
        command.Options.Add(verboseOption);
        command.Options.Add(skipHooksOption);
        command.Options.Add(openDevToolsOption);
        command.Options.Add(frontendOnlyOption);
        command.Options.Add(backendOnlyOption);
        command.Options.Add(platformOption);

        command.Action = AsyncAction.Create(async (parseResult, ct) =>
        {
            var project = parseResult.GetValue(projectOption);
            var noHotReload = parseResult.GetValue(noHotReloadOption);
            var verbose = parseResult.GetValue(verboseOption);
            var skipHooks = parseResult.GetValue(skipHooksOption);
            var openDevTools = parseResult.GetValue(openDevToolsOption);
            var frontendOnly = parseResult.GetValue(frontendOnlyOption);
            var backendOnly = parseResult.GetValue(backendOnlyOption);
            var platform = parseResult.GetValue(platformOption);

            // 互斥校验：--frontend-only 与 --backend-only 不可同时指定
            if (frontendOnly && backendOnly)
            {
                Error("--frontend-only 与 --backend-only 互斥，请仅指定其中一个");
                return 1;
            }

            // 注入 Debug 模式环境变量（Program.cs 据此切换 UI 行为）
            // 注意：必须在 ProcessStartInfo 启动 dotnet watch 之前设置。
            Environment.SetEnvironmentVariable("WAILS_DEBUG", "true");
            if (openDevTools)
            {
                Environment.SetEnvironmentVariable("WAILS_OPEN_DEVTOOLS", "true");
            }
            if (!string.IsNullOrEmpty(platform))
            {
                Environment.SetEnvironmentVariable("WAILS_PLATFORM", platform);
            }

            var cmd = new DevCommand();
            return await cmd.ExecuteAsync(
                project, noHotReload, verbose, skipHooks,
                openDevTools, frontendOnly, backendOnly, platform, ct);
        });

        return command;
    }

    /// <summary>
    /// 执行 dev 命令。
    /// </summary>
    /// <param name="project">项目文件。</param>
    /// <param name="noHotReload">是否禁用热更新。</param>
    /// <param name="verbose">是否输出详细日志。</param>
    /// <param name="skipHooks">是否跳过 dev 钩子。</param>
    /// <param name="openDevTools">是否自动打开 DevTools（通过 WAILS_OPEN_DEVTOOLS 环境变量通知 Program.cs）。</param>
    /// <param name="frontendOnly">仅启动前端 dev server（不启动 dotnet watch）。</param>
    /// <param name="backendOnly">仅启动 dotnet watch（不启动前端 dev server，使用 frontend/dist）。</param>
    /// <param name="platform">强制指定平台（windows/linux/android）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>退出码。</returns>
    private async Task<int> ExecuteAsync(
        FileInfo? project,
        bool noHotReload,
        bool verbose,
        bool skipHooks,
        bool openDevTools,
        bool frontendOnly,
        bool backendOnly,
        string? platform,
        CancellationToken cancellationToken)
    {
        var projectPath = ResolveProjectPath(project);
        if (projectPath is null)
        {
            Error("未找到项目文件，请通过 --project 指定，或在项目目录中运行");
            return 1;
        }

        var workingDir = Path.GetDirectoryName(projectPath.FullName) ?? Directory.GetCurrentDirectory();

        // 用于统一终止前端开发服务器的取消源（dotnet watch 结束或 Ctrl+C 时取消）。
        using var devCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // 加载 wails.json（若存在）
        var (config, configPath) = await ProjectConfig.FindAndLoadAsync(projectPath.FullName);
        if (config is not null)
        {
            Info($"加载配置：{configPath}");
        }

        // 输出当前模式（参照 Tauri v2 / Wails v3 的 `tauri dev` / `wails dev` 行为）
        Info("================================================================");
        Info(" Wails.Net Debug 模式（参照 Tauri v2 / Wails v3 的 dev 命令）");
        Info("================================================================");
        Info($"项目：{projectPath.FullName}");
        Info($"模式：{(backendOnly ? "仅后端" : frontendOnly ? "仅前端" : "前端 + 后端")}");
        Info($"热更新：{(noHotReload ? "禁用" : "启用")}");
        if (openDevTools)
        {
            Info("DevTools：自动打开（通过 WAILS_OPEN_DEVTOOLS=true）");
        }
        if (!string.IsNullOrEmpty(platform))
        {
            Info($"强制平台：{platform}（通过 WAILS_PLATFORM={platform}）");
        }
        if (verbose)
        {
            Info("详细日志模式");
        }
        if (!string.IsNullOrWhiteSpace(config?.Frontend?.DevServerUrl))
        {
            Info($"前端开发服务器：{config!.Frontend!.DevServerUrl}");
        }
        Info("================================================================");

        // 执行 beforeDevCommand 钩子
        if (!skipHooks && !string.IsNullOrWhiteSpace(config?.BeforeDevCommand))
        {
            Info($"执行 beforeDevCommand：{config!.BeforeDevCommand}");
            var beforeResult = await BuildHooks.ExecuteAsync(config.BeforeDevCommand, workingDir, cancellationToken);
            if (!beforeResult.Success)
            {
                Error($"beforeDevCommand 失败：{beforeResult.ErrorMessage}");
                if (!string.IsNullOrEmpty(beforeResult.Output))
                {
                    Info(beforeResult.Output);
                }
                return 4;
            }
        }

        // 前端开发服务器（vite dev）与 dotnet watch 并行运行。
        // 任一结束（正常退出或 Ctrl+C）时统一终止另一个，避免孤立进程。
        var frontendDir = config?.Frontend is { } fe ? Path.Combine(workingDir, fe.Dir) : null;
        var devServerTask = Task.CompletedTask;
        var startFrontend = !backendOnly;
        FrontendToolchain? toolchain = null;
        if (startFrontend && frontendDir is not null && Directory.Exists(frontendDir))
        {
            try
            {
                toolchain = await FrontendToolchain.DetectAsync(cancellationToken);
                Info($"启动前端开发服务器（{toolchain.PackageManager} dev）…");
                devServerTask = Task.Run(async () =>
                {
                    try
                    {
                        await toolchain!.DevAsync(frontendDir, devCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        /* 正常终止 */
                    }
                    catch (Exception ex)
                    {
                        Warn($"前端开发服务器异常：{ex.Message}");
                    }
                }, devCts.Token);
            }
            catch (InvalidOperationException ex)
            {
                Warn($"未检测到 pnpm/npm，跳过前端开发服务器：{ex.Message}");
            }
            catch (OperationCanceledException)
            {
                Warn("开发模式已停止");
                return 0;
            }
        }
        else if (backendOnly)
        {
            Info("--backend-only：跳过前端 dev server，使用 frontend/dist 中已构建的资源");
        }

        // --frontend-only：仅启动前端，不启动 dotnet watch
        if (frontendOnly)
        {
            Info("--frontend-only：仅启动前端 dev server。");
            Info("请在另一终端运行：dotnet run --project <project>（或通过 IDE F5 启动）");
            // 等待前端 dev server 退出
            try
            {
                await devServerTask;
            }
            catch (OperationCanceledException)
            {
                /* 正常终止 */
            }

            // 执行 afterDevCommand 钩子
            if (!skipHooks && !string.IsNullOrWhiteSpace(config?.AfterDevCommand))
            {
                Info($"执行 afterDevCommand：{config!.AfterDevCommand}");
                var afterResult = await BuildHooks.ExecuteAsync(config.AfterDevCommand, workingDir);
                if (!afterResult.Success)
                {
                    Warn($"afterDevCommand 失败：{afterResult.ErrorMessage}");
                }
            }

            return 0;
        }

        var args = new List<string> { "watch", "--project", projectPath.FullName };

        if (noHotReload)
        {
            args.Add("--no-hot-reload");
        }

        if (verbose)
        {
            args.Add("--verbose");
        }

        var exitCode = 0;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                UseShellExecute = false,
                CreateNoWindow = false,
            };
            foreach (var a in args)
            {
                psi.ArgumentList.Add(a);
            }

            using var proc = new Process { StartInfo = psi };
            proc.Start();
            await proc.WaitForExitAsync(cancellationToken);
            exitCode = proc.ExitCode;
        }
        catch (OperationCanceledException)
        {
            Warn("开发模式已停止");
            exitCode = 0;
        }
        catch (Exception ex)
        {
            Error($"启动 dotnet watch 失败：{ex.Message}");
            return 2;
        }
        finally
        {
            // dotnet watch 结束（正常或 Ctrl+C）→ 终止前端开发服务器
            try
            {
                devCts.Cancel();
                await Task.WhenAny(devServerTask, Task.Delay(2000, CancellationToken.None));
            }
            catch
            {
                /* ignore */
            }
        }

        // 执行 afterDevCommand 钩子（仅在 dotnet watch 正常退出时）
        if (!skipHooks && !string.IsNullOrWhiteSpace(config?.AfterDevCommand))
        {
            Info($"执行 afterDevCommand：{config!.AfterDevCommand}");
            var afterResult = await BuildHooks.ExecuteAsync(config.AfterDevCommand, workingDir);
            if (!afterResult.Success)
            {
                Warn($"afterDevCommand 失败：{afterResult.ErrorMessage}");
            }
        }

        return exitCode;
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
