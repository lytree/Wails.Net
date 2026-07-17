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
                var drySignBackend = Environment.GetEnvironmentVariable("WAILS_SIGN_BACKEND");
                if (!string.IsNullOrEmpty(drySignBackend))
                    context.Information($"[DRY-RUN] 将使用 {drySignBackend} 签名 *.exe");
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

            // 签名步骤（环境变量门控，签名失败非阻塞）
            var signBackend = Environment.GetEnvironmentVariable("WAILS_SIGN_BACKEND");
            if (!string.IsNullOrEmpty(signBackend))
            {
                context.Information($"----- 签名 {rid} 产物 -----");
                var exePath = System.IO.Directory.GetFiles(outputDir, "*.exe", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault();
                if (exePath != null)
                {
                    var tsUrl = Environment.GetEnvironmentVariable("WAILS_SIGN_TIMESTAMP_URL") ?? "http://timestamp.digicert.com";
                    if (signBackend.Equals("signtool", StringComparison.OrdinalIgnoreCase))
                    {
                        var certPath = Environment.GetEnvironmentVariable("WAILS_SIGN_CERT_PATH");
                        var certPwd = Environment.GetEnvironmentVariable("WAILS_SIGN_CERT_PASSWORD") ?? string.Empty;
                        if (!string.IsNullOrEmpty(certPath))
                        {
                            var args = $"sign /f \"{certPath}\" /p \"{certPwd}\" /tr \"{tsUrl}\" /td sha256 /fd sha256 \"{exePath}\"";
                            var exitCode = context.StartProcess("signtool", new ProcessSettings { Arguments = args });
                            if (exitCode != 0) context.Warning($"signtool 退出码 {exitCode}，签名失败但继续");
                            else context.Information($"已签名: {exePath}");
                        }
                        else
                        {
                            context.Warning("WAILS_SIGN_BACKEND=signtool 但 WAILS_SIGN_CERT_PATH 未设置，跳过签名");
                        }
                    }
                    else if (signBackend.Equals("azuresigntool", StringComparison.OrdinalIgnoreCase))
                    {
                        var kvu = Environment.GetEnvironmentVariable("WAILS_SIGN_AKV_URL");
                        var kvc = Environment.GetEnvironmentVariable("WAILS_SIGN_AKV_CERT");
                        var kvi = Environment.GetEnvironmentVariable("WAILS_SIGN_AZURE_CLIENT_ID");
                        var kvs = Environment.GetEnvironmentVariable("WAILS_SIGN_AZURE_CLIENT_SECRET");
                        var kvt = Environment.GetEnvironmentVariable("WAILS_SIGN_AZURE_TENANT_ID");
                        if (!string.IsNullOrEmpty(kvu) && !string.IsNullOrEmpty(kvc))
                        {
                            var args = $"sign -kvu \"{kvu}\" -kvc \"{kvc}\" -kvi \"{kvi}\" -kvs \"{kvs}\" -kvt \"{kvt}\" -tr \"{tsUrl}\" -td sha256 -fd sha256 \"{exePath}\"";
                            var exitCode = context.StartProcess("AzureSignTool", new ProcessSettings { Arguments = args });
                            if (exitCode != 0) context.Warning($"AzureSignTool 退出码 {exitCode}，签名失败但继续");
                            else context.Information($"已签名: {exePath}");
                        }
                        else
                        {
                            context.Warning("WAILS_SIGN_BACKEND=azuresigntool 但 AKV 参数未完整设置，跳过签名");
                        }
                    }
                    else
                    {
                        context.Warning($"不支持的签名后端: {signBackend}（支持: signtool, azuresigntool）");
                    }
                }
                else
                {
                    context.Warning($"未在 {outputDir} 找到 .exe 文件，跳过签名");
                }
            }
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
        var formats = context.ResolveLinuxFormats();
        context.Information($"========== Linux 自包含构建 v{version}（格式: {string.Join(", ", formats)}）==========");

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
            var debPath = $"{context.OutputRoot}/linux/{version}/Wails.Net.Demo-{version}-{rid}.deb";
            var rpmPath = $"{context.OutputRoot}/linux/{version}/Wails.Net.Demo-{version}-{rid}.rpm";

            if (context.DryRun)
            {
                context.Information($"[DRY-RUN] 将输出到: {outputDir}");
                if (formats.Contains("targz")) context.Information($"[DRY-RUN] 将创建 tar.gz: {tarGzPath}");
                if (formats.Contains("deb")) context.Information($"[DRY-RUN] 将创建 deb: {debPath}");
                if (formats.Contains("rpm")) context.Information($"[DRY-RUN] 将创建 rpm: {rpmPath}");
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

            if (formats.Contains("targz"))
            {
                context.CreateTarGz(outputDir, tarGzPath);
            }

            if (formats.Contains("deb"))
            {
                CreateDebPackage(context, outputDir, debPath, version, rid);
            }

            if (formats.Contains("rpm"))
            {
                CreateRpmPackage(context, outputDir, rpmPath, version, rid);
            }
        }

        context.Information("========== Linux 构建完成 ==========");
    }

    /// <summary>
    /// 内联创建 Debian 包（.deb），调用 dpkg-deb --build。
    /// </summary>
    private static void CreateDebPackage(BuildContext context, string outputDir, string debPath, string version, string rid)
    {
        var appName = "Wails.Net.Demo";
        var exeName = FindLinuxExe(outputDir, appName);
        if (exeName is null)
        {
            context.Warning($"未在 {outputDir} 找到 Linux 可执行文件，跳过 .deb 生成");
            return;
        }

        var pkgDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wailsnet-deb-{Guid.NewGuid():N}");
        try
        {
            // Debian 标准目录结构
            var debianDir = System.IO.Path.Combine(pkgDir, "DEBIAN");
            var usrBin = System.IO.Path.Combine(pkgDir, "usr", "bin");
            var appsDir = System.IO.Path.Combine(pkgDir, "usr", "share", "applications");
            var iconDir = System.IO.Path.Combine(pkgDir, "usr", "share", "icons", "hicolor", "256x256", "apps");
            System.IO.Directory.CreateDirectory(debianDir);
            System.IO.Directory.CreateDirectory(usrBin);
            System.IO.Directory.CreateDirectory(appsDir);
            System.IO.Directory.CreateDirectory(iconDir);

            // 复制发布产物到 usr/bin
            CopyAllFiles(outputDir, usrBin);
            // 设置主程序可执行权限
            context.StartProcess("chmod", new ProcessSettings { Arguments = $"+x \"{System.IO.Path.Combine(usrBin, exeName)}\"" });

            // control 文件
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

            // postinst
            System.IO.File.WriteAllText(System.IO.Path.Combine(debianDir, "postinst"),
                $"#!/bin/sh\nchmod +x /usr/bin/{exeName}\n");
            context.StartProcess("chmod", new ProcessSettings { Arguments = $"+x \"{System.IO.Path.Combine(debianDir, "postinst")}\"" });

            // .desktop 文件
            System.IO.File.WriteAllText(System.IO.Path.Combine(appsDir, $"{appName}.desktop"),
                $"[Desktop Entry]\nType=Application\nName={appName}\nExec={appName}\nIcon={appName}\nCategories=Utility;\nTerminal=false\n");

            // 调用 dpkg-deb --build
            context.EnsureDirectoryExists(System.IO.Path.GetDirectoryName(debPath)!);
            var exitCode = context.StartProcess("dpkg-deb",
                new ProcessSettings { Arguments = $"--build \"{pkgDir}\" \"{System.IO.Path.GetFullPath(debPath)}\"" });
            if (exitCode != 0)
            {
                context.Warning($"dpkg-deb 退出码 {exitCode}，.deb 生成失败");
            }
            else
            {
                context.Information($"已创建 deb: {debPath}");
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

    /// <summary>
    /// 内联创建 RPM 包（.rpm），调用 rpmbuild -bb。
    /// </summary>
    private static void CreateRpmPackage(BuildContext context, string outputDir, string rpmPath, string version, string rid)
    {
        var appName = "Wails.Net.Demo";
        var exeName = FindLinuxExe(outputDir, appName);
        if (exeName is null)
        {
            context.Warning($"未在 {outputDir} 找到 Linux 可执行文件，跳过 .rpm 生成");
            return;
        }

        var topDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wailsnet-rpm-{Guid.NewGuid():N}");
        try
        {
            foreach (var sub in new[] { "BUILD", "RPMS", "SOURCES", "SPECS", "SRPMS" })
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(topDir, sub));
            }

            // 主可执行文件 + .desktop 放入 SOURCES
            var sourcesDir = System.IO.Path.Combine(topDir, "SOURCES");
            System.IO.File.Copy(System.IO.Path.Combine(outputDir, exeName),
                System.IO.Path.Combine(sourcesDir, exeName), overwrite: true);
            System.IO.File.WriteAllText(System.IO.Path.Combine(sourcesDir, $"{appName}.desktop"),
                $"[Desktop Entry]\nType=Application\nName={appName}\nExec={appName}\nIcon={appName}\nCategories=Utility;\nTerminal=false\n");

            // .spec 文件
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

            // 调用 rpmbuild -bb
            context.EnsureDirectoryExists(System.IO.Path.GetDirectoryName(rpmPath)!);
            var exitCode = context.StartProcess("rpmbuild",
                new ProcessSettings { Arguments = $"--define \"_topdir {topDir}\" -bb \"{specPath}\"" });
            if (exitCode != 0)
            {
                context.Warning($"rpmbuild 退出码 {exitCode}，.rpm 生成失败");
            }
            else
            {
                var rpmFiles = System.IO.Directory.GetFiles(System.IO.Path.Combine(topDir, "RPMS"), "*.rpm", System.IO.SearchOption.AllDirectories);
                if (rpmFiles.Length > 0)
                {
                    System.IO.File.Copy(rpmFiles[0], System.IO.Path.GetFullPath(rpmPath), overwrite: true);
                    context.Information($"已创建 rpm: {rpmPath}");
                }
                else
                {
                    context.Warning("rpmbuild 执行完成但未找到生成的 .rpm 文件");
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

    /// <summary>
    /// 在目录中查找 Linux 可执行文件（无扩展名的文件）。
    /// </summary>
    private static string? FindLinuxExe(string dir, string appName)
    {
        var match = System.IO.Path.Combine(dir, appName);
        if (System.IO.File.Exists(match)) return appName;
        foreach (var f in System.IO.Directory.GetFiles(dir, "*", System.IO.SearchOption.TopDirectoryOnly))
        {
            if (string.IsNullOrEmpty(System.IO.Path.GetExtension(f))) return System.IO.Path.GetFileName(f);
        }
        return null;
    }

    /// <summary>
    /// 复制目录下所有文件到目标目录。
    /// </summary>
    private static void CopyAllFiles(string sourceDir, string destDir)
    {
        foreach (var file in System.IO.Directory.GetFiles(sourceDir, "*", System.IO.SearchOption.AllDirectories))
        {
            var rel = System.IO.Path.GetRelativePath(sourceDir, file);
            var dest = System.IO.Path.Combine(destDir, rel);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest)!);
            System.IO.File.Copy(file, dest, overwrite: true);
        }
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
