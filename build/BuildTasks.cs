using Cake.Common;
using Cake.Common.Diagnostics;
using Cake.Common.IO;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Build;
using Cake.Common.Tools.DotNet.MSBuild;
using Cake.Common.Tools.DotNet.Pack;
using Cake.Common.Tools.DotNet.Publish;
using Cake.Common.Tools.DotNet.Restore;
using Cake.Common.Tools.DotNet.Run;
using Cake.Core.IO;
using Cake.Frosting;

namespace Wails.Net.Build;

// ============================================================================
// TASK: Clean（对应 build.cake 的 Clean）
// ============================================================================

[TaskName("Clean")]
public sealed class CleanTask : FrostingTask<BuildContext>
{
    public override bool ShouldRun(BuildContext context) => context.Rebuild;

    public override void Run(BuildContext context)
    {
        context.Information("========== 清理构建输出 ==========");
        context.CleanDirectories($"src/**/bin/{context.Configuration}");
        context.CleanDirectories($"src/**/obj/{context.Configuration}");
        context.CleanDirectories($"tests/**/bin/{context.Configuration}");
        context.CleanDirectories($"tests/**/obj/{context.Configuration}");
    }
}

// ============================================================================
// TASK: Restore
// ============================================================================

[TaskName("Restore")]
[IsDependentOn(typeof(CleanTask))]
public sealed class RestoreTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        context.Information("========== 还原 NuGet 包 ==========");
        context.DotNetRestore(BuildContext.SolutionFile);
    }
}

// ============================================================================
// TASK: Frontend（前端资源构建，对齐 Tauri 构建能力）
// ============================================================================

[TaskName("Frontend")]
public sealed class FrontendTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        if (context.SkipFrontend)
        {
            context.Information("========== 跳过前端构建（--skip-frontend）==========");
            return;
        }

        var frontendDir = "frontend";
        var packageJson = System.IO.Path.Combine(frontendDir, "package.json");
        if (!System.IO.File.Exists(packageJson))
        {
            context.Information($"========== 跳过前端构建（{packageJson} 不存在）==========");
            return;
        }

        context.Information("========== 构建前端资源 ==========");

        // 检测包管理器（优先级：pnpm > yarn > npm）
        string packageManager, installCmd, runCmd;
        if (System.IO.File.Exists(System.IO.Path.Combine(frontendDir, "pnpm-lock.yaml")))
        {
            packageManager = "pnpm";
            installCmd = "install";
            runCmd = "run build";
        }
        else if (System.IO.File.Exists(System.IO.Path.Combine(frontendDir, "yarn.lock")))
        {
            packageManager = "yarn";
            installCmd = "install";
            runCmd = "build";
        }
        else
        {
            packageManager = "npm";
            installCmd = "install";
            runCmd = "run build";
        }

        context.Information($"包管理器: {packageManager}");

        var nodeModules = System.IO.Path.Combine(frontendDir, "node_modules");
        if (!System.IO.Directory.Exists(nodeModules))
        {
            context.Information("执行依赖安装...");
            var installExit = context.StartProcess(packageManager, new ProcessSettings
            {
                Arguments = installCmd,
                WorkingDirectory = frontendDir,
            });
            if (installExit != 0) throw new Exception($"前端依赖安装失败（退出码 {installExit}）");
        }
        else
        {
            context.Information("node_modules 已存在，跳过安装");
        }

        context.Information("执行前端构建...");
        var buildExit = context.StartProcess(packageManager, new ProcessSettings
        {
            Arguments = runCmd,
            WorkingDirectory = frontendDir,
        });
        if (buildExit != 0) throw new Exception($"前端构建失败（退出码 {buildExit}）");

        context.Information("前端构建完成");
    }
}

// ============================================================================
// TASK: Build
// ============================================================================

[TaskName("Build")]
[IsDependentOn(typeof(RestoreTask))]
[IsDependentOn(typeof(FrontendTask))]
public sealed class BuildTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        context.Information("========== 构建解决方案 ==========");
        var settings = new DotNetBuildSettings
        {
            Configuration = context.Configuration,
            NoRestore = true,
            MSBuildSettings = new DotNetMSBuildSettings(),
        };
        settings.MSBuildSettings.Properties["SkipFrontendBuild"] = new[] { "true" };
        context.DotNetBuild(BuildContext.SolutionFile, settings);
    }
}

// ============================================================================
// TASK: Test
// ============================================================================

[TaskName("Test")]
[IsDependentOn(typeof(BuildTask))]
public sealed class TestTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        var runSettings = new DotNetRunSettings { Configuration = context.Configuration, NoBuild = true };

        context.Information("========== 运行 Application 测试 ==========");
        context.DotNetRun("tests/Wails.Net.Application.Tests/Wails.Net.Application.Tests.csproj", runSettings);

        context.Information("========== 运行 CLI 测试 ==========");
        context.DotNetRun("tests/Wails.Net.Cli.Tests/Wails.Net.Cli.Tests.csproj", runSettings);

        context.Information("========== 运行 AssetServer 测试 ==========");
        context.DotNetRun("tests/Wails.Net.AssetServer.Tests/Wails.Net.AssetServer.Tests.csproj", runSettings);

        if (context.IsRunningOnWindows())
        {
            context.Information("========== 运行 Windows 平台测试 ==========");
            context.DotNetRun("tests/Wails.Net.Application.Windows.Tests/Wails.Net.Application.Windows.Tests.csproj", runSettings);
        }

        if (context.IsRunningOnLinux())
        {
            context.Information("========== 运行 Linux 平台测试（允许失败）==========");
            try
            {
                context.DotNetRun("tests/Wails.Net.Application.Linux.Tests/Wails.Net.Application.Linux.Tests.csproj", runSettings);
            }
            catch (Exception ex)
            {
                context.Warning($"Linux 平台测试失败（非阻塞）: {ex.Message}");
            }
        }

        context.Information("========== 运行 Android 平台测试（允许失败）==========");
        try
        {
            context.DotNetRun("tests/Wails.Net.Application.Android.Tests/Wails.Net.Application.Android.Tests.csproj", runSettings);
        }
        catch (Exception ex)
        {
            context.Warning($"Android 平台测试失败（非阻塞）: {ex.Message}");
        }
    }
}

// ============================================================================
// TASK: Pack（打包 NuGet 包）
// ============================================================================

[TaskName("Pack")]
[IsDependentOn(typeof(TestTask))]
public sealed class PackTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        context.Information("========== 打包 NuGet 包 ==========");
        var packageOutput = "artifacts/packages";
        context.EnsureDirectoryExists(packageOutput);

        var packSettings = new DotNetPackSettings
        {
            Configuration = context.Configuration,
            NoBuild = true,
            OutputDirectory = packageOutput,
            MSBuildSettings = new DotNetMSBuildSettings(),
        };
        packSettings.MSBuildSettings.Properties["SkipFrontendBuild"] = new[] { "true" };
        context.DotNetPack(BuildContext.SolutionFile, packSettings);

        // 单独打包 Templates 项目（不在 slnx 中）
        context.DotNetPack("src/Wails.Net.Templates/Wails.Net.Templates.csproj", new DotNetPackSettings
        {
            Configuration = context.Configuration,
            OutputDirectory = packageOutput,
        });
    }
}

// ============================================================================
// TASK: Dist-Windows（对应 build.cake 的 Dist-Windows）
// ============================================================================

[TaskName("Dist-Windows")]
[IsDependentOn(typeof(PackTask))]
public sealed class DistWindowsTask : FrostingTask<BuildContext>
{
    public override bool ShouldRun(BuildContext context) => context.ShouldBuildPlatform("windows");

    public override void Run(BuildContext context)
    {
        var version = context.GetVersion();
        context.Information($"========== Windows 自包含构建 v{version} ==========");

        if (!System.IO.File.Exists(BuildContext.DemoProject))
        {
            context.Warning($"Demo 项目不存在: {BuildContext.DemoProject}，跳过 Windows 自包含构建");
            return;
        }

        var rids = context.ResolveRIDs("windows", context.RidArg);
        context.Information($"将构建 {rids.Count} 个 RID: {string.Join(", ", rids)}");

        foreach (var rid in rids)
        {
            context.Information($"----- 构建 Windows/{rid} -----");
            var outputDir = $"{context.OutputRoot}/windows/{version}/{rid}";
            var zipPath = $"{context.OutputRoot}/windows/{version}/Wails.Net.Demo-{version}-{rid}.zip";

            if (context.DryRun)
            {
                context.Information($"[DRY-RUN] 将输出到: {outputDir}");
                context.Information($"[DRY-RUN] 将创建 zip: {zipPath}");
                continue;
            }

            context.EnsureDirectoryExists(outputDir);
            context.CleanDirectory(outputDir);

            var settings = new DotNetPublishSettings
            {
                Configuration = context.Configuration,
                Runtime = rid,
                SelfContained = true,
                OutputDirectory = outputDir,
                NoRestore = true,
                MSBuildSettings = new DotNetMSBuildSettings(),
            };
            settings.MSBuildSettings.Properties["PublishSingleFile"] = new[] { "true" };
            settings.MSBuildSettings.Properties["PublishTrimmed"] = new[] { "false" };
            settings.MSBuildSettings.Properties["IncludeNativeLibrariesForSelfExtract"] = new[] { "true" };
            if (context.SkipFrontend) settings.MSBuildSettings.Properties["SkipFrontendBuild"] = new[] { "true" };

            context.DotNetPublish(BuildContext.DemoProject, settings);

            context.Zip(outputDir, zipPath);
            context.Information($"已创建 zip: {zipPath}");
        }

        context.Information("========== Windows 构建完成 ==========");
    }
}

// ============================================================================
// TASK: Dist-Linux（对应 build.cake 的 Dist-Linux）
// ============================================================================

[TaskName("Dist-Linux")]
[IsDependentOn(typeof(PackTask))]
public sealed class DistLinuxTask : FrostingTask<BuildContext>
{
    public override bool ShouldRun(BuildContext context) => context.ShouldBuildPlatform("linux");

    public override void Run(BuildContext context)
    {
        var version = context.GetVersion();
        context.Information($"========== Linux 自包含构建 v{version} ==========");

        if (!System.IO.File.Exists(BuildContext.DemoProject))
        {
            context.Warning($"Demo 项目不存在: {BuildContext.DemoProject}，跳过 Linux 自包含构建");
            return;
        }

        var rids = context.ResolveRIDs("linux", context.RidArg);
        context.Information($"将构建 {rids.Count} 个 RID: {string.Join(", ", rids)}");

        foreach (var rid in rids)
        {
            context.Information($"----- 构建 Linux/{rid} -----");
            var outputDir = $"{context.OutputRoot}/linux/{version}/{rid}";
            var tarGzPath = $"{context.OutputRoot}/linux/{version}/Wails.Net.Demo-{version}-{rid}.tar.gz";

            if (context.DryRun)
            {
                context.Information($"[DRY-RUN] 将输出到: {outputDir}");
                context.Information($"[DRY-RUN] 将创建 tar.gz: {tarGzPath}");
                continue;
            }

            context.EnsureDirectoryExists(outputDir);
            context.CleanDirectory(outputDir);

            var settings = new DotNetPublishSettings
            {
                Configuration = context.Configuration,
                Runtime = rid,
                SelfContained = true,
                OutputDirectory = outputDir,
                NoRestore = true,
                MSBuildSettings = new DotNetMSBuildSettings(),
            };
            settings.MSBuildSettings.Properties["PublishSingleFile"] = new[] { "true" };
            settings.MSBuildSettings.Properties["PublishTrimmed"] = new[] { "false" };
            if (context.SkipFrontend) settings.MSBuildSettings.Properties["SkipFrontendBuild"] = new[] { "true" };

            context.DotNetPublish(BuildContext.DemoProject, settings);

            context.CreateTarGz(outputDir, tarGzPath);
            context.Information($"已创建 tar.gz: {tarGzPath}");
        }

        context.Information("========== Linux 构建完成 ==========");
    }
}

// ============================================================================
// TASK: Dist-Android（对应 build.cake 的 Dist-Android）
// ============================================================================

[TaskName("Dist-Android")]
[IsDependentOn(typeof(PackTask))]
public sealed class DistAndroidTask : FrostingTask<BuildContext>
{
    public override bool ShouldRun(BuildContext context) => context.ShouldBuildPlatform("android");

    public override void Run(BuildContext context)
    {
        var version = context.GetVersion();
        context.Information($"========== Android APK 构建 v{version} ==========");

        if (!System.IO.File.Exists(BuildContext.DemoAndroidProject))
        {
            context.Warning($"Android Demo 项目不存在: {BuildContext.DemoAndroidProject}，跳过 Android 构建");
            return;
        }

        if (!context.DryRun)
        {
            context.Information("检查 .NET Android 工作负载...");
            var exitCode = context.StartProcess("dotnet", new ProcessSettings { Arguments = "workload list", Silent = true });
            if (exitCode != 0)
            {
                context.Error("无法执行 'dotnet workload list'，请确认 .NET SDK 已安装");
                return;
            }
            context.Information("已检查 .NET Android 工作负载");
        }

        var rids = context.ResolveRIDs("android", context.RidArg);
        context.Information($"将构建 {rids.Count} 个 RID: {string.Join(", ", rids)}");

        foreach (var rid in rids)
        {
            context.Information($"----- 构建 Android/{rid} -----");
            var outputDir = $"{context.OutputRoot}/android/{version}/{rid}";

            if (context.DryRun)
            {
                context.Information($"[DRY-RUN] 将输出到: {outputDir}");
                continue;
            }

            context.EnsureDirectoryExists(outputDir);

            var settings = new DotNetPublishSettings
            {
                Configuration = context.Configuration,
                Runtime = rid,
                SelfContained = true,
                NoRestore = true,
                MSBuildSettings = new DotNetMSBuildSettings(),
            };
            settings.MSBuildSettings.Properties["AndroidPackageFormat"] = new[] { "apk" };

            // 签名配置（从环境变量读取）
            var keystorePath = Environment.GetEnvironmentVariable("ANDROID_KEYSTORE_PATH");
            var keyAlias = Environment.GetEnvironmentVariable("ANDROID_KEY_ALIAS");
            var keyPass = Environment.GetEnvironmentVariable("ANDROID_KEY_PASS") ?? "";
            var storePass = Environment.GetEnvironmentVariable("ANDROID_STORE_PASS") ?? "";

            if (!string.IsNullOrEmpty(keystorePath) && !string.IsNullOrEmpty(keyAlias))
            {
                context.Information("使用正式签名（环境变量配置）");
                settings.MSBuildSettings.Properties["AndroidKeyStore"] = new[] { "true" };
                settings.MSBuildSettings.Properties["AndroidSigningKeyStore"] = new[] { keystorePath };
                settings.MSBuildSettings.Properties["AndroidSigningKeyAlias"] = new[] { keyAlias };
                settings.MSBuildSettings.Properties["AndroidSigningKeyPass"] = new[] { keyPass };
                settings.MSBuildSettings.Properties["AndroidSigningStorePass"] = new[] { storePass };
            }
            else
            {
                context.Warning("未设置 ANDROID_KEYSTORE_PATH 等环境变量，使用 debug 签名");
                settings.MSBuildSettings.Properties["AndroidKeyStore"] = new[] { "false" };
            }

            context.DotNetPublish(BuildContext.DemoAndroidProject, settings);

            // 查找并复制 APK
            context.Information("正在查找生成的 APK 文件...");
            var projectDir = System.IO.Path.GetDirectoryName(BuildContext.DemoAndroidProject);
            var apkFiles = System.IO.Directory.GetFiles(
                System.IO.Path.Combine(projectDir!, "bin"),
                "*.apk",
                SearchOption.AllDirectories);

            if (apkFiles.Length > 0)
            {
                var apkName = $"Wails.Net.Demo-{version}-{rid}.apk";
                var apkPath = System.IO.Path.Combine(outputDir, apkName);
                System.IO.File.Copy(apkFiles[0], apkPath, overwrite: true);
                context.Information($"已复制 APK: {apkPath}");
            }
            else
            {
                context.Warning("未找到生成的 APK 文件。请检查构建输出。");
            }
        }

        context.Information("========== Android 构建完成 ==========");
    }
}

// ============================================================================
// TASK: Dist（聚合三平台，对应 build.cake 的 Dist 入口）
// ============================================================================

[TaskName("Dist")]
[IsDependentOn(typeof(DistWindowsTask))]
[IsDependentOn(typeof(DistLinuxTask))]
[IsDependentOn(typeof(DistAndroidTask))]
public sealed class DistTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        var version = context.GetVersion();
        context.Information("========== 三平台统一构建 ==========");
        context.Information($"版本:      {version}");
        context.Information($"配置:      {context.Configuration}");
        context.Information($"平台:      {context.Platform}");
        context.Information($"输出根:    {context.OutputRoot}");
        context.Information($"跳过前端:  {context.SkipFrontend}");
        context.Information($"Dry-run:   {context.DryRun}");
        context.Information("所有平台构建完成！");
    }
}

// ============================================================================
// TASK: Default（默认目标 = Test）
// ============================================================================

[TaskName("Default")]
[IsDependentOn(typeof(TestTask))]
public sealed class DefaultTask : FrostingTask<BuildContext>
{
    // Default 仅作为默认入口，实际依赖 TestTask
}
