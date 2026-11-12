#!/usr/bin/env dotnet
#:sdk Cake.Sdk@6.2.0
#:property TreatWarningsAsErrors=false
#:property IsPackable=false

// ============================================================================
// Wails.Net 构建脚本（Cake SDK 文件式 DSL，cake.cs）
// ----------------------------------------------------------------------------
// 由 `dotnet new cakefile --name cake --IncludeExampleProject true` 生成骨架后，
// 将原 build/Wails.Net.Build（项目式 Cake.Sdk）的全部任务迁移至此。
// 命令行参数接口与任务行为保持不变，仅运行入口由 `dotnet run --project build/...`
// 改为 `dotnet run cake.cs -- ...`。
//
// 运行方式（工作目录为仓库根）：
//   dotnet run cake.cs                                     # 默认目标 Test
//   dotnet run cake.cs -- --target=Build                   # 构建解决方案
//   dotnet run cake.cs -- --target=Test --configuration=Release
//   dotnet run cake.cs -- --target=Pack --configuration=Release
//   dotnet run cake.cs -- --target=Dist --platform=windows --rid=all
//   dotnet run cake.cs -- --target=DistAll --dry-run --skip-frontend
//   dotnet run cake.cs -- --target=Dist-Linux --platform=linux --rid=linux-x64 --linux-formats=deb
//   dotnet run cake.cs -- --target=Build --solution=src/Wails.Net.Application/Wails.Net.Application.csproj
//                                       # 局部构建（未安装 Android 工作负载时的本机验证方式）
//
// 参考：https://cakebuild.net/docs/running-builds/runners/cake-sdk
// ============================================================================

using System.IO.Compression;
using System.Xml.Linq;
using Cake.Common.Tools.DotNet.Build;
using Cake.Common.Tools.DotNet.MSBuild;
using Cake.Common.Tools.DotNet.Pack;
using Cake.Common.Tools.DotNet.Publish;
using Cake.Common.Tools.DotNet.Restore;
using Cake.Common.Tools.DotNet.Run;

// === 命令行参数 ===
var target = Argument("target", "Default");
var configuration = Argument("configuration", "Release");
var platform = Argument("platform", "all");
var ridArgument = Argument("rid", "");
var customVersion = Argument("build-version", "");
var outputRoot = Argument("output", "artifacts/dist");
var skipFrontend = Argument("skip-frontend", false);
var dryRun = Argument("dry-run", false);
var rebuild = Argument("rebuild", false);
var linuxFormats = Argument("linux-formats", "targz");
// 可选的构建目标（默认为解决方案）：本地缺少 Android 工作负载时，可传单个工程做局部验证
var buildTarget = Argument("solution", "Wails.Net.slnx");

// === 常量 ===
const string DemoProject = "examples/Wails.Net.Demo/Wails.Net.Demo.csproj";
const string DemoAndroidProject = "examples/Wails.Net.Demo.Android/Wails.Net.Demo.Android.csproj";

// ============================================================================
// TASK: Clean（--rebuild 时执行）
// ============================================================================

Task("Clean")
    .WithCriteria(() => rebuild == true)
    .Does(() =>
    {
        Information("========== 清理构建输出 ==========");
        CleanDirectories($"src/**/bin/{configuration}");
        CleanDirectories($"src/**/obj/{configuration}");
        CleanDirectories($"tests/**/bin/{configuration}");
        CleanDirectories($"tests/**/obj/{configuration}");
    });

// ============================================================================
// TASK: Restore
// ============================================================================

Task("Restore")
    .IsDependentOn("Clean")
    .Does(() =>
    {
        Information("========== 还原 NuGet 包 ==========");
        DotNetRestore(buildTarget, new DotNetRestoreSettings
        {
            MSBuildSettings = CreateMSBuildSettings(),
        });
    });

// ============================================================================
// TASK: Frontend（前端资源构建，对齐 Tauri 构建能力）
// ============================================================================

Task("Frontend")
    .Does(() =>
    {
        if (skipFrontend)
        {
            Information("========== 跳过前端构建（--skip-frontend）==========");
            return;
        }

        var frontendDir = "frontend";
        var packageJson = System.IO.Path.Combine(frontendDir, "package.json");
        if (!System.IO.File.Exists(packageJson))
        {
            Information($"========== 跳过前端构建（{packageJson} 不存在）==========");
            return;
        }

        Information("========== 构建前端资源 ==========");

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

        Information($"包管理器: {packageManager}");

        var nodeModules = System.IO.Path.Combine(frontendDir, "node_modules");
        if (!System.IO.Directory.Exists(nodeModules))
        {
            Information("执行依赖安装...");
            var installExit = StartProcess(packageManager, new ProcessSettings
            {
                Arguments = installCmd,
                WorkingDirectory = frontendDir,
            });
            if (installExit != 0) throw new Exception($"前端依赖安装失败（退出码 {installExit}）");
        }
        else
        {
            Information("node_modules 已存在，跳过安装");
        }

        Information("执行前端构建...");
        var buildExit = StartProcess(packageManager, new ProcessSettings
        {
            Arguments = runCmd,
            WorkingDirectory = frontendDir,
        });
        if (buildExit != 0) throw new Exception($"前端构建失败（退出码 {buildExit}）");

        Information("前端构建完成");
    });

// ============================================================================
// TASK: Build
// ============================================================================

Task("Build")
    .IsDependentOn("Restore")
    .IsDependentOn("Frontend")
    .Does(() =>
    {
        Information("========== 构建解决方案 ==========");
        var settings = new DotNetBuildSettings
        {
            Configuration = configuration,
            NoRestore = true,
            MSBuildSettings = CreateMSBuildSettings(),
        };
        settings.MSBuildSettings.Properties["SkipFrontendBuild"] = new[] { "true" };
        DotNetBuild(buildTarget, settings);
    });

// ============================================================================
// TASK: Test（.NET 10 使用 dotnet run，非 dotnet test，详见 AGENTS.md §4.4）
// ============================================================================

Task("Test")
    .IsDependentOn("Build")
    .Does(ctx =>
    {
        var runSettings = new DotNetRunSettings
        {
            Configuration = configuration,
            NoBuild = true,
        };

        Information("========== 运行 Application 测试 ==========");
        DotNetRun("tests/Wails.Net.Application.Tests/Wails.Net.Application.Tests.csproj", runSettings);

        Information("========== 运行 CLI 测试 ==========");
        DotNetRun("tests/Wails.Net.Cli.Tests/Wails.Net.Cli.Tests.csproj", runSettings);

        Information("========== 运行 AssetServer 测试 ==========");
        DotNetRun("tests/Wails.Net.AssetServer.Tests/Wails.Net.AssetServer.Tests.csproj", runSettings);

        if (ctx.IsRunningOnWindows())
        {
            Information("========== 运行 Windows 平台测试 ==========");
            DotNetRun("tests/Wails.Net.Application.Windows.Tests/Wails.Net.Application.Windows.Tests.csproj", runSettings);
        }

        if (ctx.IsRunningOnLinux())
        {
            Information("========== 运行 Linux 平台测试（允许失败）==========");
            try
            {
                DotNetRun("tests/Wails.Net.Application.Linux.Tests/Wails.Net.Application.Linux.Tests.csproj", runSettings);
            }
            catch (Exception ex)
            {
                Warning($"Linux 平台测试失败（非阻塞）: {ex.Message}");
            }
        }

        Information("========== 运行 Android 平台测试（允许失败）==========");
        try
        {
            DotNetRun("tests/Wails.Net.Application.Android.Tests/Wails.Net.Application.Android.Tests.csproj", runSettings);
        }
        catch (Exception ex)
        {
            Warning($"Android 平台测试失败（非阻塞）: {ex.Message}");
        }
    });

// ============================================================================
// TASK: Pack（打包 NuGet 包）
// ============================================================================

Task("Pack")
    .IsDependentOn("Test")
    .Does(() =>
    {
        Information("========== 打包 NuGet 包 ==========");
        var packageOutput = "artifacts/packages";
        EnsureDirectoryExists(packageOutput);

        var packSettings = new DotNetPackSettings
        {
            Configuration = configuration,
            NoBuild = true,
            OutputDirectory = packageOutput,
            MSBuildSettings = CreateMSBuildSettings(),
        };
        packSettings.MSBuildSettings.Properties["SkipFrontendBuild"] = new[] { "true" };
        DotNetPack(buildTarget, packSettings);

        // 单独打包 Templates 项目（不在 slnx 中）
        DotNetPack("src/Wails.Net.Templates/Wails.Net.Templates.csproj", new DotNetPackSettings
        {
            Configuration = configuration,
            OutputDirectory = packageOutput,
            MSBuildSettings = CreateMSBuildSettings(),
        });
    });

// ============================================================================
// TASK: Dist-Windows
// ============================================================================

Task("Dist-Windows")
    .IsDependentOn("Pack")
    .WithCriteria(ctx => ShouldBuildPlatform(ctx, "windows"))
    .Does(ctx =>
    {
        var version = GetVersion();
        Information($"========== Windows 自包含构建 v{version} ==========");

        if (!System.IO.File.Exists(DemoProject))
        {
            Warning($"Demo 项目不存在: {DemoProject}，跳过 Windows 自包含构建");
            return;
        }

        var rids = ResolveRIDs(ctx, "windows", ridArgument);
        Information($"将构建 {rids.Count} 个 RID: {string.Join(", ", rids)}");

        foreach (var rid in rids)
        {
            Information($"----- 构建 Windows/{rid} -----");
            var outputDir = $"{outputRoot}/windows/{version}/{rid}";
            var zipPath = $"{outputRoot}/windows/{version}/Wails.Net.Demo-{version}-{rid}.zip";

            if (dryRun)
            {
                Information($"[DRY-RUN] 将输出到: {outputDir}");
                Information($"[DRY-RUN] 将创建 zip: {zipPath}");
                var drySignBackend = Environment.GetEnvironmentVariable("WAILS_SIGN_BACKEND");
                if (!string.IsNullOrEmpty(drySignBackend))
                    Information($"[DRY-RUN] 将使用 {drySignBackend} 签名 *.exe");
                continue;
            }

            EnsureDirectoryExists(outputDir);
            CleanDirectory(outputDir);

            var publishSettings = new DotNetPublishSettings
            {
                Configuration = configuration,
                Runtime = rid,
                SelfContained = true,
                OutputDirectory = outputDir,
                // 不能沿用 Restore 任务的产物：带 Runtime 的 publish 要求 assets 文件里
                // 存在形如 net10.0/<rid> 的目标，而未指定 RID 的 Restore 不会生成它，
                // 会报 NETSDK1047（assets file doesn't have a target for 'net10.0/win-x64'）。
                // 这里让 publish 自己按 RID 还原，多花几秒换取可重复的自包含构建。
                NoRestore = false,
                MSBuildSettings = CreateMSBuildSettings(),
            };
            publishSettings.MSBuildSettings.Properties["PublishSingleFile"] = new[] { "true" };
            publishSettings.MSBuildSettings.Properties["PublishTrimmed"] = new[] { "false" };
            publishSettings.MSBuildSettings.Properties["IncludeNativeLibrariesForSelfExtract"] = new[] { "true" };
            if (skipFrontend) publishSettings.MSBuildSettings.Properties["SkipFrontendBuild"] = new[] { "true" };

            DotNetPublish(DemoProject, publishSettings);

            Zip(outputDir, zipPath);
            Information($"已创建 zip: {zipPath}");

            // 签名步骤（环境变量门控，签名失败非阻塞）
            SignWindowsArtifacts(ctx, outputDir);
        }

        Information("========== Windows 构建完成 ==========");
    });

// ============================================================================
// TASK: Dist-Linux
// ============================================================================

Task("Dist-Linux")
    .IsDependentOn("Pack")
    .WithCriteria(ctx => ShouldBuildPlatform(ctx, "linux"))
    .Does(ctx =>
    {
        var version = GetVersion();
        var formats = ResolveLinuxFormats();
        Information($"========== Linux 自包含构建 v{version}（格式: {string.Join(", ", formats)}）==========");

        if (!System.IO.File.Exists(DemoProject))
        {
            Warning($"Demo 项目不存在: {DemoProject}，跳过 Linux 自包含构建");
            return;
        }

        var rids = ResolveRIDs(ctx, "linux", ridArgument);
        Information($"将构建 {rids.Count} 个 RID: {string.Join(", ", rids)}");

        foreach (var rid in rids)
        {
            Information($"----- 构建 Linux/{rid} -----");
            var outputDir = $"{outputRoot}/linux/{version}/{rid}";
            var tarGzPath = $"{outputRoot}/linux/{version}/Wails.Net.Demo-{version}-{rid}.tar.gz";
            var debPath = $"{outputRoot}/linux/{version}/Wails.Net.Demo-{version}-{rid}.deb";
            var rpmPath = $"{outputRoot}/linux/{version}/Wails.Net.Demo-{version}-{rid}.rpm";

            if (dryRun)
            {
                Information($"[DRY-RUN] 将输出到: {outputDir}");
                if (formats.Contains("targz")) Information($"[DRY-RUN] 将创建 tar.gz: {tarGzPath}");
                if (formats.Contains("deb")) Information($"[DRY-RUN] 将创建 deb: {debPath}");
                if (formats.Contains("rpm")) Information($"[DRY-RUN] 将创建 rpm: {rpmPath}");
                continue;
            }

            EnsureDirectoryExists(outputDir);
            CleanDirectory(outputDir);

            var publishSettings = new DotNetPublishSettings
            {
                Configuration = configuration,
                Runtime = rid,
                SelfContained = true,
                OutputDirectory = outputDir,
                // 不能沿用 Restore 任务的产物：带 Runtime 的 publish 要求 assets 文件里
                // 存在形如 net10.0/<rid> 的目标，而未指定 RID 的 Restore 不会生成它，
                // 会报 NETSDK1047（assets file doesn't have a target for 'net10.0/win-x64'）。
                // 这里让 publish 自己按 RID 还原，多花几秒换取可重复的自包含构建。
                NoRestore = false,
                MSBuildSettings = CreateMSBuildSettings(),
            };
            publishSettings.MSBuildSettings.Properties["PublishSingleFile"] = new[] { "true" };
            publishSettings.MSBuildSettings.Properties["PublishTrimmed"] = new[] { "false" };
            if (skipFrontend) publishSettings.MSBuildSettings.Properties["SkipFrontendBuild"] = new[] { "true" };

            DotNetPublish(DemoProject, publishSettings);

            if (formats.Contains("targz"))
            {
                CreateTarGz(outputDir, tarGzPath);
            }

            if (formats.Contains("deb"))
            {
                CreateDebPackage(ctx, outputDir, debPath, version, rid);
            }

            if (formats.Contains("rpm"))
            {
                CreateRpmPackage(ctx, outputDir, rpmPath, version, rid);
            }
        }

        Information("========== Linux 构建完成 ==========");
    });

// ============================================================================
// TASK: Dist-Android
// ============================================================================

Task("Dist-Android")
    .IsDependentOn("Pack")
    .WithCriteria(ctx => ShouldBuildPlatform(ctx, "android"))
    .Does(ctx =>
    {
        var version = GetVersion();
        Information($"========== Android APK 构建 v{version} ==========");

        if (!System.IO.File.Exists(DemoAndroidProject))
        {
            Warning($"Android Demo 项目不存在: {DemoAndroidProject}，跳过 Android 构建");
            return;
        }

        if (!dryRun)
        {
            Information("检查 .NET Android 工作负载...");
            var exitCode = StartProcess("dotnet", new ProcessSettings { Arguments = "workload list", Silent = true });
            if (exitCode != 0)
            {
                Error("无法执行 'dotnet workload list'，请确认 .NET SDK 已安装");
                return;
            }
            Information("已检查 .NET Android 工作负载");
        }

        var rids = ResolveRIDs(ctx, "android", ridArgument);
        Information($"将构建 {rids.Count} 个 RID: {string.Join(", ", rids)}");

        foreach (var rid in rids)
        {
            Information($"----- 构建 Android/{rid} -----");
            var outputDir = $"{outputRoot}/android/{version}/{rid}";

            if (dryRun)
            {
                Information($"[DRY-RUN] 将输出到: {outputDir}");
                continue;
            }

            EnsureDirectoryExists(outputDir);

            var publishSettings = new DotNetPublishSettings
            {
                Configuration = configuration,
                Runtime = rid,
                SelfContained = true,
                // 不能沿用 Restore 任务的产物：带 Runtime 的 publish 要求 assets 文件里
                // 存在形如 net10.0/<rid> 的目标，而未指定 RID 的 Restore 不会生成它，
                // 会报 NETSDK1047（assets file doesn't have a target for 'net10.0/win-x64'）。
                // 这里让 publish 自己按 RID 还原，多花几秒换取可重复的自包含构建。
                NoRestore = false,
                MSBuildSettings = CreateMSBuildSettings(),
            };
            publishSettings.MSBuildSettings.Properties["AndroidPackageFormat"] = new[] { "apk" };

            // 签名配置（从环境变量读取）
            var keystorePath = Environment.GetEnvironmentVariable("ANDROID_KEYSTORE_PATH");
            var keyAlias = Environment.GetEnvironmentVariable("ANDROID_KEY_ALIAS");
            var keyPass = Environment.GetEnvironmentVariable("ANDROID_KEY_PASS") ?? "";
            var storePass = Environment.GetEnvironmentVariable("ANDROID_STORE_PASS") ?? "";

            if (!string.IsNullOrEmpty(keystorePath) && !string.IsNullOrEmpty(keyAlias))
            {
                Information("使用正式签名（环境变量配置）");
                publishSettings.MSBuildSettings.Properties["AndroidKeyStore"] = new[] { "true" };
                publishSettings.MSBuildSettings.Properties["AndroidSigningKeyStore"] = new[] { keystorePath };
                publishSettings.MSBuildSettings.Properties["AndroidSigningKeyAlias"] = new[] { keyAlias };
                publishSettings.MSBuildSettings.Properties["AndroidSigningKeyPass"] = new[] { keyPass };
                publishSettings.MSBuildSettings.Properties["AndroidSigningStorePass"] = new[] { storePass };
            }
            else
            {
                Warning("未设置 ANDROID_KEYSTORE_PATH 等环境变量，使用 debug 签名");
                publishSettings.MSBuildSettings.Properties["AndroidKeyStore"] = new[] { "false" };
            }

            DotNetPublish(DemoAndroidProject, publishSettings);

            // 查找并复制 APK
            Information("正在查找生成的 APK 文件...");
            var projectDir = System.IO.Path.GetDirectoryName(DemoAndroidProject);
            var apkFiles = System.IO.Directory.GetFiles(
                System.IO.Path.Combine(projectDir!, "bin"),
                "*.apk",
                System.IO.SearchOption.AllDirectories);

            if (apkFiles.Length > 0)
            {
                var apkName = $"Wails.Net.Demo-{version}-{rid}.apk";
                var apkPath = System.IO.Path.Combine(outputDir, apkName);
                System.IO.File.Copy(apkFiles[0], apkPath, overwrite: true);
                Information($"已复制 APK: {apkPath}");
            }
            else
            {
                Warning("未找到生成的 APK 文件。请检查构建输出。");
            }
        }

        Information("========== Android 构建完成 ==========");
    });

// ============================================================================
// TASK: Dist（聚合三平台）
// ============================================================================

Task("Dist")
    .IsDependentOn("Dist-Windows")
    .IsDependentOn("Dist-Linux")
    .IsDependentOn("Dist-Android")
    .Does(() =>
    {
        var version = GetVersion();
        Information("========== 三平台统一构建 ==========");
        Information($"版本:      {version}");
        Information($"配置:      {configuration}");
        Information($"平台:      {platform}");
        Information($"输出根:    {outputRoot}");
        Information($"跳过前端:  {skipFrontend}");
        Information($"Dry-run:   {dryRun}");
        Information("所有平台构建完成！");
    });

// ============================================================================
// TASK: DistAll（一键全平台全 RID）
// ============================================================================

Task("DistAll")
    .IsDependentOn("Dist-Windows")
    .IsDependentOn("Dist-Linux")
    .IsDependentOn("Dist-Android")
    .Does(ctx =>
    {
        var version = GetVersion();
        Information("========== DistAll: 一键全平台全 RID 构建完成 ==========");
        Information($"版本:      {version}");
        Information($"配置:      {configuration}");
        Information($"输出根:    {outputRoot}");
        Information($"Linux 格式: {linuxFormats}");
        Information($"跳过前端:  {skipFrontend}");
        Information($"Dry-run:   {dryRun}");
        Information($"产物目录:  {outputRoot}/{{windows,linux,android}}/{version}/");

        // 汇总各平台构建的 RID 数量
        foreach (var plat in AllPlatforms())
        {
            var rids = ResolveRIDs(ctx, plat, ridArgument);
            Information($"  {plat}: {rids.Count} 个 RID ({string.Join(", ", rids)})");
        }
    });

// ============================================================================
// TASK: Default（默认目标 = Test）
// ============================================================================

Task("Default")
    .IsDependentOn("Test");

// ============================================================================
// 执行入口
// ============================================================================

RunTarget(target);

// ============================================================================
// 辅助函数
// ============================================================================

/// <summary>
/// 构造通用 MSBuild 设置：当通过 --build-version 显式指定版本时注入 WailsNetVersion，
/// 使 CI 无需依赖 -p:WailsNetVersion=...（文件式脚本不便透传 MSBuild 属性）。
/// </summary>
DotNetMSBuildSettings CreateMSBuildSettings()
{
    var settings = new DotNetMSBuildSettings();
    if (!string.IsNullOrEmpty(customVersion))
    {
        settings.Properties["WailsNetVersion"] = new[] { customVersion! };
    }
    return settings;
}

/// <summary>支持的三平台 RID 列表。</summary>
string[] GetPlatformRIDs(string plat) => plat switch
{
    "windows" => new[] { "win-x64", "win-x86", "win-arm64" },
    "linux" => new[] { "linux-x64", "linux-arm64" },
    "android" => new[] { "android-arm64", "android-x64", "android-arm" },
    _ => Array.Empty<string>(),
};

/// <summary>支持的三平台列表。</summary>
string[] AllPlatforms() => new[] { "windows", "linux", "android" };

/// <summary>
/// 解析 --rid 参数，返回指定平台要构建的 RID 列表。
/// 当 target 为 DistAll 且未显式指定 RID 时，默认构建该平台的所有 RID。
/// </summary>
List<string> ResolveRIDs(ICakeContext ctx, string plat, string ridArgument) =>
    ResolveRIDsCore(ctx, plat, ridArgument, target);

/// <summary>
/// 解析 --rid 参数（含 target 感知，供 DistAll 使用）。
/// </summary>
List<string> ResolveRIDsCore(ICakeContext ctx, string plat, string ridArgument, string currentTarget)
{
    var supported = GetPlatformRIDs(plat);
    var defaultRid = plat switch
    {
        "windows" => "win-x64",
        "linux" => "linux-x64",
        "android" => "android-arm64",
        _ => (string?)null,
    };
    defaultRid ??= "win-x64";

    // DistAll 目标：未显式指定 RID 时默认构建全 RID
    if (string.IsNullOrEmpty(ridArgument) && currentTarget == "DistAll") return new List<string>(supported);
    if (string.IsNullOrEmpty(ridArgument)) return new List<string> { defaultRid! };
    if (ridArgument == "all") return new List<string>(supported);

    var ridList = new List<string>();
    var parts = ridArgument.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    foreach (var rid in parts)
    {
        if (Array.IndexOf(supported, rid) >= 0)
        {
            ridList.Add(rid);
        }
        else
        {
            Warning($"平台 {plat} 不支持 RID: {rid}（支持的: {string.Join(", ", supported)}）");
        }
    }
    return ridList.Count == 0 ? new List<string> { defaultRid! } : ridList;
}

/// <summary>检查指定平台是否应构建（基于 --platform 参数；target 为 DistAll 时强制放行）。</summary>
bool ShouldBuildPlatform(ICakeContext ctx, string plat) =>
    platform == "all" || platform == plat || target == "DistAll";

/// <summary>解析 --linux-formats 参数，返回要生成的 Linux 包格式列表。</summary>
List<string> ResolveLinuxFormats()
{
    var supported = new[] { "targz", "deb", "rpm" };
    if (string.IsNullOrEmpty(linuxFormats) || linuxFormats.Equals("all", StringComparison.OrdinalIgnoreCase))
    {
        return new List<string>(supported);
    }

    var result = new List<string>();
    var parts = linuxFormats.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    foreach (var f in parts)
    {
        var lower = f.ToLowerInvariant();
        if (Array.IndexOf(supported, lower) >= 0)
        {
            if (!result.Contains(lower)) result.Add(lower);
        }
        else
        {
            Warning($"不支持的 Linux 格式: {f}（支持: {string.Join(", ", supported)}）");
        }
    }
    return result.Count == 0 ? new List<string> { "targz" } : result;
}

/// <summary>读取 Directory.Build.props 中的 WailsNetVersion（--build-version 优先）。</summary>
string GetVersion()
{
    if (!string.IsNullOrEmpty(customVersion)) return customVersion!;

    const string propsPath = "Directory.Build.props";
    if (!System.IO.File.Exists(propsPath)) return "unknown";

    var doc = XDocument.Load(propsPath);
    var root = doc.Root;
    if (root is null) return "unknown";

    var ns = root.GetDefaultNamespace();
    var propsGroup = root.Element(XName.Get("PropertyGroup", ns.NamespaceName));
    if (propsGroup is null) return "unknown";

    var versionElem = propsGroup.Element(XName.Get("WailsNetVersion", ns.NamespaceName));
    return versionElem?.Value.Trim() ?? "unknown";
}

/// <summary>创建 tar.gz 压缩文件。</summary>
void CreateTarGz(string sourceDir, string tarGzPath)
{
    var parent = System.IO.Path.GetDirectoryName(tarGzPath);
    if (!string.IsNullOrEmpty(parent)) System.IO.Directory.CreateDirectory(parent!);
    if (System.IO.File.Exists(tarGzPath)) System.IO.File.Delete(tarGzPath);

    var tempTar = System.IO.Path.GetTempFileName();
    try
    {
        System.IO.File.Delete(tempTar);
        if (!System.IO.Directory.Exists(sourceDir))
        {
            Warning($"源目录不存在，跳过 tar.gz: {sourceDir}");
            return;
        }
        System.Formats.Tar.TarFile.CreateFromDirectory(sourceDir, tempTar, includeBaseDirectory: false);
        using var srcStream = System.IO.File.OpenRead(tempTar);
        using var dstStream = System.IO.File.Create(tarGzPath);
        using var gzip = new GZipStream(dstStream, CompressionLevel.Optimal);
        srcStream.CopyTo(gzip);
    }
    finally
    {
        if (System.IO.File.Exists(tempTar)) System.IO.File.Delete(tempTar);
    }
    Information($"已创建 tar.gz: {tarGzPath}");
}

/// <summary>Windows 产物签名（环境变量门控，签名失败非阻塞）。</summary>
void SignWindowsArtifacts(ICakeContext ctx, string outputDir)
{
    var signBackend = Environment.GetEnvironmentVariable("WAILS_SIGN_BACKEND");
    if (string.IsNullOrEmpty(signBackend)) return;

    Information($"----- 签名 {outputDir} 中的产物 -----");
    var exePath = System.IO.Directory.GetFiles(outputDir, "*.exe", System.IO.SearchOption.TopDirectoryOnly)
        .FirstOrDefault();
    if (exePath == null)
    {
        Warning($"未在 {outputDir} 找到 .exe 文件，跳过签名");
        return;
    }

    var tsUrl = Environment.GetEnvironmentVariable("WAILS_SIGN_TIMESTAMP_URL") ?? "http://timestamp.digicert.com";
    if (signBackend.Equals("signtool", StringComparison.OrdinalIgnoreCase))
    {
        var certPath = Environment.GetEnvironmentVariable("WAILS_SIGN_CERT_PATH");
        var certPwd = Environment.GetEnvironmentVariable("WAILS_SIGN_CERT_PASSWORD") ?? string.Empty;
        if (string.IsNullOrEmpty(certPath)) return; // 已由启动处提示
        var args = $"sign /f \"{certPath}\" /p \"{certPwd}\" /tr \"{tsUrl}\" /td sha256 /fd sha256 \"{exePath}\"";
        var exitCode = StartProcess("signtool", new ProcessSettings { Arguments = args });
        if (exitCode != 0) throw new CakeException($"signtool 退出码 {exitCode}，签名失败（已配置 WAILS_SIGN_BACKEND=signtool，禁止发布未签名产物）");
        else Information($"已签名: {exePath}");
    }
    else if (signBackend.Equals("azuresigntool", StringComparison.OrdinalIgnoreCase))
    {
        var kvu = Environment.GetEnvironmentVariable("WAILS_SIGN_AKV_URL");
        var kvc = Environment.GetEnvironmentVariable("WAILS_SIGN_AKV_CERT");
        var kvi = Environment.GetEnvironmentVariable("WAILS_SIGN_AZURE_CLIENT_ID");
        var kvs = Environment.GetEnvironmentVariable("WAILS_SIGN_AZURE_CLIENT_SECRET");
        var kvt = Environment.GetEnvironmentVariable("WAILS_SIGN_AZURE_TENANT_ID");
        if (string.IsNullOrEmpty(kvu) || string.IsNullOrEmpty(kvc)) return; // 已由启动处提示
        var args = $"sign -kvu \"{kvu}\" -kvc \"{kvc}\" -kvi \"{kvi}\" -kvs \"{kvs}\" -kvt \"{kvt}\" -tr \"{tsUrl}\" -td sha256 -fd sha256 \"{exePath}\"";
        var exitCode = StartProcess("AzureSignTool", new ProcessSettings { Arguments = args });
        if (exitCode != 0) throw new CakeException($"AzureSignTool 退出码 {exitCode}，签名失败（已配置 WAILS_SIGN_BACKEND=azuresigntool，禁止发布未签名产物）");
        else Information($"已签名: {exePath}");
    }
    else
    {
        Warning($"不支持的签名后端: {signBackend}（支持: signtool, azuresigntool）");
    }
}

/// <summary>创建 Debian 包（.deb），调用 dpkg-deb --build。</summary>
void CreateDebPackage(ICakeContext ctx, string outputDir, string debPath, string version, string rid)
{
    var appName = "Wails.Net.Demo";
    var exeName = FindLinuxExe(outputDir, appName);
    if (exeName is null)
    {
        Warning($"未在 {outputDir} 找到 Linux 可执行文件，跳过 .deb 生成");
        return;
    }

    var pkgDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wailsnet-deb-{Guid.NewGuid():N}");
    try
    {
        var debianDir = System.IO.Path.Combine(pkgDir, "DEBIAN");
        var usrBin = System.IO.Path.Combine(pkgDir, "usr", "bin");
        var appsDir = System.IO.Path.Combine(pkgDir, "usr", "share", "applications");
        var iconDir = System.IO.Path.Combine(pkgDir, "usr", "share", "icons", "hicolor", "256x256", "apps");
        System.IO.Directory.CreateDirectory(debianDir);
        System.IO.Directory.CreateDirectory(usrBin);
        System.IO.Directory.CreateDirectory(appsDir);
        System.IO.Directory.CreateDirectory(iconDir);

        CopyAllFiles(outputDir, usrBin);
        StartProcess("chmod", new ProcessSettings { Arguments = $"+x \"{System.IO.Path.Combine(usrBin, exeName)}\"" });

        var arch = rid.Contains("arm64") ? "arm64" : (rid.Contains("x86") ? "i386" : "amd64");
        var control = $"""
Package: {appName}
Version: {version}
Architecture: {arch}
Maintainer: Wails.Net
Depends: libgtk-4-1, libwebkitgtk-6.0-4
Section: utils
Priority: optional
Description: {appName} application
 {appName} built by Wails.Net
""";
        System.IO.File.WriteAllText(System.IO.Path.Combine(debianDir, "control"), control);

        System.IO.File.WriteAllText(System.IO.Path.Combine(debianDir, "postinst"),
            $"#!/bin/sh\nchmod +x /usr/bin/{exeName}\n");
        StartProcess("chmod", new ProcessSettings { Arguments = $"+x \"{System.IO.Path.Combine(debianDir, "postinst")}\"" });

        System.IO.File.WriteAllText(System.IO.Path.Combine(appsDir, $"{appName}.desktop"),
            $"[Desktop Entry]\nType=Application\nName={appName}\nExec={appName}\nIcon={appName}\nCategories=Utility;\nTerminal=false\n");

        EnsureDirectoryExists(System.IO.Path.GetDirectoryName(debPath)!);
        var exitCode = StartProcess("dpkg-deb",
            new ProcessSettings { Arguments = $"--build \"{pkgDir}\" \"{System.IO.Path.GetFullPath(debPath)}\"" });
        if (exitCode != 0)
        {
            Warning($"dpkg-deb 退出码 {exitCode}，.deb 生成失败");
        }
        else
        {
            Information($"已创建 deb: {debPath}");
        }
    }
    finally
    {
        if (System.IO.Directory.Exists(pkgDir))
        {
            try { System.IO.Directory.Delete(pkgDir, recursive: true); } catch { }
        }
    }
}

/// <summary>创建 RPM 包（.rpm），调用 rpmbuild -bb。</summary>
void CreateRpmPackage(ICakeContext ctx, string outputDir, string rpmPath, string version, string rid)
{
    var appName = "Wails.Net.Demo";
    var exeName = FindLinuxExe(outputDir, appName);
    if (exeName is null)
    {
        Warning($"未在 {outputDir} 找到 Linux 可执行文件，跳过 .rpm 生成");
        return;
    }

    var topDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wailsnet-rpm-{Guid.NewGuid():N}");
    try
    {
        foreach (var sub in new[] { "BUILD", "RPMS", "SOURCES", "SPECS", "SRPMS" })
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(topDir, sub));
        }

        var sourcesDir = System.IO.Path.Combine(topDir, "SOURCES");
        System.IO.File.Copy(System.IO.Path.Combine(outputDir, exeName),
            System.IO.Path.Combine(sourcesDir, exeName), overwrite: true);
        System.IO.File.WriteAllText(System.IO.Path.Combine(sourcesDir, $"{appName}.desktop"),
            $"[Desktop Entry]\nType=Application\nName={appName}\nExec={appName}\nIcon={appName}\nCategories=Utility;\nTerminal=false\n");

        var spec = $$"""
Name:           {{appName}}
Version:        {{version}}
Release:        1%{?dist}
Summary:        {{appName}} application
License:        Proprietary
Group:          Applications/Utility
Requires:       gtk4, webkitgtk6.0

%description
{{appName}} built by Wails.Net

%install
install -D -m 0755 %{_sourcedir}/{{exeName}} %{buildroot}/usr/bin/{{exeName}}
install -D -m 0644 %{_sourcedir}/{{appName}}.desktop %{buildroot}/usr/share/applications/{{appName}}.desktop

%files
/usr/bin/{{exeName}}
/usr/share/applications/{{appName}}.desktop
""";
        var specPath = System.IO.Path.Combine(topDir, "SPECS", $"{appName}.spec");
        System.IO.File.WriteAllText(specPath, spec);

        EnsureDirectoryExists(System.IO.Path.GetDirectoryName(rpmPath)!);
        var exitCode = StartProcess("rpmbuild",
            new ProcessSettings { Arguments = $"--define \"_topdir {topDir}\" -bb \"{specPath}\"" });
        if (exitCode != 0)
        {
            Warning($"rpmbuild 退出码 {exitCode}，.rpm 生成失败");
        }
        else
        {
            var rpmFiles = System.IO.Directory.GetFiles(System.IO.Path.Combine(topDir, "RPMS"), "*.rpm", System.IO.SearchOption.AllDirectories);
            if (rpmFiles.Length > 0)
            {
                System.IO.File.Copy(rpmFiles[0], System.IO.Path.GetFullPath(rpmPath), overwrite: true);
                Information($"已创建 rpm: {rpmPath}");
            }
            else
            {
                Warning("rpmbuild 执行完成但未找到生成的 .rpm 文件");
            }
        }
    }
    finally
    {
        if (System.IO.Directory.Exists(topDir))
        {
            try { System.IO.Directory.Delete(topDir, recursive: true); } catch { }
        }
    }
}

/// <summary>在目录中查找 Linux 可执行文件（无扩展名的文件）。</summary>
string? FindLinuxExe(string dir, string appName)
{
    var match = System.IO.Path.Combine(dir, appName);
    if (System.IO.File.Exists(match)) return appName;
    foreach (var f in System.IO.Directory.GetFiles(dir, "*", System.IO.SearchOption.TopDirectoryOnly))
    {
        if (string.IsNullOrEmpty(System.IO.Path.GetExtension(f))) return System.IO.Path.GetFileName(f);
    }
    return null;
}

/// <summary>复制目录下所有文件到目标目录（保留相对路径）。</summary>
void CopyAllFiles(string sourceDir, string destDir)
{
    if (!System.IO.Directory.Exists(sourceDir)) return;
    foreach (var file in System.IO.Directory.GetFiles(sourceDir, "*", System.IO.SearchOption.AllDirectories))
    {
        var rel = System.IO.Path.GetRelativePath(sourceDir, file);
        var dest = System.IO.Path.Combine(destDir, rel);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest)!);
        System.IO.File.Copy(file, dest, overwrite: true);
    }
}
