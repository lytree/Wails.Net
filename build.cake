// ============================================================================
// Wails.Net Cake 构建脚本（build.cake）
// ----------------------------------------------------------------------------
// 迁移自 scripts/build/*.fsx（BuildAll.fsx + BuildWindows/Linux/Android.fsx）
// 功能：Clean → Restore → Build → Test → Pack → Dist（三平台多 RID 自包含构建）
//
// 用法：
//   dotnet cake                                # 默认：Restore + Build + Test
//   dotnet cake --target=Dist                  # 全平台自包含构建
//   dotnet cake --target=Dist --platform=windows --rid=all
//   dotnet cake --target=Dist --platform=android --rid=android-arm64
//   dotnet cake --target=Test --dry-run
// ============================================================================

// ============================================================================
// 参数定义
// ============================================================================

var target = Argument("target", "Default");
var configuration = Argument("configuration", "Release");
var platform = Argument("platform", "all");
var ridArg = Argument("rid", "");
var customVersion = Argument("version", "");
var outputRoot = Argument("output", "artifacts/dist");
var skipFrontend = HasArgument("skip-frontend");
var dryRun = HasArgument("dry-run");
var rebuild = HasArgument("rebuild");
var solutionFile = "Wails.Net.slnx";
var demoProject = "examples/Wails.Net.Demo/Wails.Net.Demo.csproj";
var demoAndroidProject = "examples/Wails.Net.Demo.Android/Wails.Net.Demo.Android.csproj";

// ============================================================================
// 常量：平台 RID 映射（对应 BuildAll.fsx 的 platformRIDs/defaultRID）
// ============================================================================

var allPlatforms = new[] { "windows", "linux", "android" };

var platformRIDs = new Dictionary<string, string[]>
{
    ["windows"] = new[] { "win-x64", "win-x86", "win-arm64" },
    ["linux"] = new[] { "linux-x64", "linux-arm64" },
    ["android"] = new[] { "android-arm64", "android-x64", "android-arm" },
};

var defaultRID = new Dictionary<string, string>
{
    ["windows"] = "win-x64",
    ["linux"] = "linux-x64",
    ["android"] = "android-arm64",
};

// ============================================================================
// 辅助方法
// ============================================================================

/// <summary>
/// 读取 Directory.Build.props 中的 WailsNetVersion（对应 Common.fsx getVersion）。
/// </summary>
string GetVersion()
{
    if (!string.IsNullOrEmpty(customVersion))
    {
        return customVersion;
    }

    var propsPath = "Directory.Build.props";
    if (!System.IO.File.Exists(propsPath))
    {
        return "unknown";
    }

    var doc = System.Xml.Linq.XDocument.Load(propsPath);
    var root = doc.Root;
    if (root is null)
    {
        return "unknown";
    }

    var ns = root.GetDefaultNamespace();
    var propsGroup = root.Element(System.Xml.Linq.XName.Get("PropertyGroup", ns.NamespaceName));
    if (propsGroup is null)
    {
        return "unknown";
    }

    var versionElem = propsGroup.Element(System.Xml.Linq.XName.Get("WailsNetVersion", ns.NamespaceName));
    return versionElem?.Value.Trim() ?? "unknown";
}

/// <summary>
/// 解析 RID 参数，返回指定平台要构建的 RID 列表（对应 BuildAll.fsx resolveRIDs）。
/// </summary>
List<string> ResolveRIDs(string plat, string ridArgument)
{
    var supported = platformRIDs[plat];
    var defaultRid = defaultRID[plat];

    if (string.IsNullOrEmpty(ridArgument))
    {
        return new List<string> { defaultRid };
    }

    if (ridArgument == "all")
    {
        return new List<string>(supported);
    }

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

    return ridList.Count == 0 ? new List<string> { defaultRid } : ridList;
}

/// <summary>
/// 检查指定平台是否应构建（基于 --platform 参数）。
/// </summary>
bool ShouldBuildPlatform(string plat)
{
    return platform == "all" || platform == plat;
}

/// <summary>
/// 创建 tar.gz 压缩文件（对应 Common.fsx compressTarGz）。
/// </summary>
void CreateTarGz(string sourceDir, string tarGzPath)
{
    var parent = System.IO.Path.GetDirectoryName(tarGzPath);
    if (!string.IsNullOrEmpty(parent))
    {
        System.IO.Directory.CreateDirectory(parent);
    }

    if (System.IO.File.Exists(tarGzPath))
    {
        System.IO.File.Delete(tarGzPath);
    }

    // 先创建临时 .tar，再 gzip 压缩
    var tempTar = System.IO.Path.GetTempFileName();
    try
    {
        System.IO.File.Delete(tempTar);
        System.Formats.Tar.TarFile.CreateFromDirectory(sourceDir, tempTar, includeBaseDirectory: false);
        using var srcStream = System.IO.File.OpenRead(tempTar);
        using var dstStream = System.IO.File.Create(tarGzPath);
        using var gzip = new System.IO.Compression.GZipStream(dstStream, System.IO.Compression.CompressionLevel.Optimal);
        srcStream.CopyTo(gzip);
    }
    finally
    {
        if (System.IO.File.Exists(tempTar))
        {
            System.IO.File.Delete(tempTar);
        }
    }

    Information($"已创建 tar.gz: {tarGzPath}");
}

// ============================================================================
// TASK: Clean（对应 BuildAll.fsx Clean 逻辑）
// ============================================================================

Task("Clean")
    .WithCriteria(() => rebuild)
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
    DotNetRestore(solutionFile);
});

// ============================================================================
// TASK: Build
// ============================================================================

Task("Build")
    .IsDependentOn("Restore")
    .Does(() =>
{
    Information("========== 构建解决方案 ==========");
    var buildSettings = new DotNetBuildSettings
    {
        Configuration = configuration,
        NoRestore = true,
        MSBuildSettings = new DotNetMSBuildSettings(),
    };
    buildSettings.MSBuildSettings.Properties["SkipFrontendBuild"] = new[] { "true" };
    DotNetBuild(solutionFile, buildSettings);
});

// ============================================================================
// TASK: Test
// ============================================================================

Task("Test")
    .IsDependentOn("Build")
    .Does(() =>
{
    Information("========== 运行 Application 测试 ==========");
    DotNetRun("tests/Wails.Net.Application.Tests/Wails.Net.Application.Tests.csproj",
        new DotNetRunSettings { Configuration = configuration, NoBuild = true });

    Information("========== 运行 CLI 测试 ==========");
    DotNetRun("tests/Wails.Net.Cli.Tests/Wails.Net.Cli.Tests.csproj",
        new DotNetRunSettings { Configuration = configuration, NoBuild = true });
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
        MSBuildSettings = new DotNetMSBuildSettings(),
    };
    packSettings.MSBuildSettings.Properties["SkipFrontendBuild"] = new[] { "true" };
    DotNetPack(solutionFile, packSettings);

    // 单独打包 Templates 项目（不在 slnx 中）
    DotNetPack("src/Wails.Net.Templates/Wails.Net.Templates.csproj", new DotNetPackSettings
    {
        Configuration = configuration,
        OutputDirectory = packageOutput,
    });
});

// ============================================================================
// TASK: Dist-Windows（对应 BuildWindows.fsx）
// ============================================================================

Task("Dist-Windows")
    .IsDependentOn("Pack")
    .WithCriteria(() => ShouldBuildPlatform("windows"))
    .Does(() =>
{
    var version = GetVersion();
    Information($"========== Windows 自包含构建 v{version} ==========");

    if (!System.IO.File.Exists(demoProject))
    {
        Warning($"Demo 项目不存在: {demoProject}，跳过 Windows 自包含构建");
        return;
    }

    var rids = ResolveRIDs("windows", ridArg);
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
            NoRestore = true,
            MSBuildSettings = new DotNetMSBuildSettings(),
        };
        publishSettings.MSBuildSettings.Properties["PublishSingleFile"] = new[] { "true" };
        publishSettings.MSBuildSettings.Properties["PublishTrimmed"] = new[] { "false" };
        publishSettings.MSBuildSettings.Properties["IncludeNativeLibrariesForSelfExtract"] = new[] { "true" };
        if (skipFrontend)
        {
            publishSettings.MSBuildSettings.Properties["SkipFrontendBuild"] = new[] { "true" };
        }

        DotNetPublish(demoProject, publishSettings);

        // 压缩输出目录为 zip
        Zip(outputDir, zipPath);
        Information($"已创建 zip: {zipPath}");
    }

    Information("========== Windows 构建完成 ==========");
});

// ============================================================================
// TASK: Dist-Linux（对应 BuildLinux.fsx）
// ============================================================================

Task("Dist-Linux")
    .IsDependentOn("Pack")
    .WithCriteria(() => ShouldBuildPlatform("linux"))
    .Does(() =>
{
    var version = GetVersion();
    Information($"========== Linux 自包含构建 v{version} ==========");

    if (!System.IO.File.Exists(demoProject))
    {
        Warning($"Demo 项目不存在: {demoProject}，跳过 Linux 自包含构建");
        return;
    }

    var rids = ResolveRIDs("linux", ridArg);
    Information($"将构建 {rids.Count} 个 RID: {string.Join(", ", rids)}");

    foreach (var rid in rids)
    {
        Information($"----- 构建 Linux/{rid} -----");
        var outputDir = $"{outputRoot}/linux/{version}/{rid}";
        var tarGzPath = $"{outputRoot}/linux/{version}/Wails.Net.Demo-{version}-{rid}.tar.gz";

        if (dryRun)
        {
            Information($"[DRY-RUN] 将输出到: {outputDir}");
            Information($"[DRY-RUN] 将创建 tar.gz: {tarGzPath}");
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
            NoRestore = true,
            MSBuildSettings = new DotNetMSBuildSettings(),
        };
        publishSettings.MSBuildSettings.Properties["PublishSingleFile"] = new[] { "true" };
        publishSettings.MSBuildSettings.Properties["PublishTrimmed"] = new[] { "false" };
        if (skipFrontend)
        {
            publishSettings.MSBuildSettings.Properties["SkipFrontendBuild"] = new[] { "true" };
        }

        DotNetPublish(demoProject, publishSettings);

        // 压缩输出目录为 tar.gz
        CreateTarGz(outputDir, tarGzPath);
        Information($"已创建 tar.gz: {tarGzPath}");
    }

    Information("========== Linux 构建完成 ==========");
});

// ============================================================================
// TASK: Dist-Android（对应 BuildAndroid.fsx）
// ============================================================================

Task("Dist-Android")
    .IsDependentOn("Pack")
    .WithCriteria(() => ShouldBuildPlatform("android"))
    .Does(() =>
{
    var version = GetVersion();
    Information($"========== Android APK 构建 v{version} ==========");

    if (!System.IO.File.Exists(demoAndroidProject))
    {
        Warning($"Android Demo 项目不存在: {demoAndroidProject}，跳过 Android 构建");
        return;
    }

    // 检查 Android 工作负载
    if (!dryRun)
    {
        Information("检查 .NET Android 工作负载...");
        var exitCode = StartProcess("dotnet", new ProcessSettings
        {
            Arguments = "workload list",
            Silent = true,
        });
        if (exitCode != 0)
        {
            Error("无法执行 'dotnet workload list'，请确认 .NET SDK 已安装");
            return;
        }
        Information("已检查 .NET Android 工作负载");
    }

    var rids = ResolveRIDs("android", ridArg);
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
            NoRestore = true,
            MSBuildSettings = new DotNetMSBuildSettings(),
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

        DotNetPublish(demoAndroidProject, publishSettings);

        // 查找并复制 APK
        Information("正在查找生成的 APK 文件...");
        var projectDir = System.IO.Path.GetDirectoryName(demoAndroidProject);
        var apkFiles = System.IO.Directory.GetFiles(
            System.IO.Path.Combine(projectDir!, "bin"),
            "*.apk",
            SearchOption.AllDirectories);

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
// TASK: Dist（聚合三平台，对应 BuildAll.fsx build 入口）
// 通过 IsDependentOn 依赖三平台 Task，各 Task 内部用 WithCriteria 判断是否执行
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
// TASK: Default（默认目标 = Test）
// ============================================================================

Task("Default")
    .IsDependentOn("Test");

// ============================================================================
// 执行
// ============================================================================

RunTarget(target);
