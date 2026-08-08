using System.Diagnostics;
using System.Xml.Linq;

namespace Wails.Net.Cli.Build;

/// <summary>
/// 插件双包发布器：发布后端 NuGet 包（dotnet nuget push）与前端 npm 包（pnpm publish）。
/// <para>
/// 遵循 docs/development/plugin-packaging.md 的发布约束：
/// <list type="bullet">
/// <item><b>版本单一来源</b>：后端 <c>WailsNetVersion</c>（Directory.Build.props）必须与前端
/// package.json 的 <c>version</c> 一致，发布前强制校验，不一致即失败；</item>
/// <item><b>先构建后发布</b>：默认先执行构建，可用 --skip-build 跳过（产物需已存在）；</item>
/// <item><b>双端同发</b>：禁止只发布一端（后端或前端单独发布时报错）。</item>
/// </list>
/// 认证方式：NuGet 用 --api-key 或环境变量 NUGET_API_KEY；npm 依赖 pnpm 既有登录态
/// （NPM_TOKEN 写入 ~/.npmrc 或已执行 npm/pnpm login）。
/// </para>
/// </summary>
public sealed class PluginPublisher
{
    /// <summary>NuGet 官方源地址。</summary>
    public const string DefaultNuGetSource = "https://api.nuget.org/v3/index.json";

    /// <summary>NuGet API Key 环境变量名。</summary>
    public const string NuGetApiKeyEnvVar = "NUGET_API_KEY";

    /// <summary>
    /// 发布插件双包。
    /// </summary>
    /// <param name="plugin">插件布局。</param>
    /// <param name="configuration">构建配置（发布前构建时使用）。</param>
    /// <param name="outputDir">NuGet 包输出目录（null 时使用默认 artifacts/nupkg）。</param>
    /// <param name="source">NuGet 源地址。</param>
    /// <param name="apiKey">NuGet API Key（null 时读取环境变量 NUGET_API_KEY）。</param>
    /// <param name="skipBuild">是否跳过发布前构建。</param>
    /// <param name="dryRun">演练模式：打印将要执行的命令但不真正执行。</param>
    /// <param name="backendOnly">仅发布后端。</param>
    /// <param name="frontendOnly">仅发布前端。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>退出码：0 成功；1 前置条件/校验失败；2 执行失败。</returns>
    public async Task<int> PublishAsync(
        PluginLayout plugin,
        string configuration,
        string? outputDir,
        string? source,
        string? apiKey,
        bool skipBuild,
        bool dryRun,
        bool backendOnly,
        bool frontendOnly,
        CancellationToken cancellationToken = default)
    {
        // ---- 双端同发约束：仅当两端都存在时，禁止只发一端 ----
        var hasBackend = plugin.BackendProjectPath is not null;
        var hasFrontend = plugin.FrontendDir is not null;
        if (hasBackend && hasFrontend && backendOnly != frontendOnly)
        {
            Console.Error.WriteLine(
                "[错误] 插件采用前后端一体双包模型，禁止单独发布一端。请去掉 --backend-only / --frontend-only，双包同时发布。");
            return 1;
        }

        // ---- 版本一致性硬校验（发布前强制） ----
        var versionError = ValidateVersionConsistency(plugin, out var backendVersion, out var frontendVersion);
        if (versionError is not null)
        {
            Console.Error.WriteLine($"[错误] {versionError}");
            return 1;
        }

        if (backendVersion is not null && frontendVersion is not null)
        {
            Console.WriteLine($"[插件 {plugin.Name}] 版本一致：{backendVersion}（NuGet = npm）");
        }
        else if (backendVersion is not null)
        {
            Console.WriteLine($"[插件 {plugin.Name}] 后端版本：{backendVersion}（无前端包）");
        }
        else
        {
            Console.WriteLine($"[插件 {plugin.Name}] 前端版本：{frontendVersion}（无后端包）");
        }

        // ---- 发布前构建 ----
        if (!skipBuild)
        {
            var builder = new PluginBuilder();
            var buildCode = await builder.BuildAsync(
                plugin, configuration, outputDir, backendOnly: false, frontendOnly: false, cancellationToken);
            if (buildCode != 0)
            {
                Console.Error.WriteLine("[错误] 发布前构建失败，已中止发布（可用 --skip-build 跳过构建，直接发布已有产物）");
                return 2;
            }
        }

        var backendCode = 0;
        var frontendCode = 0;

        if (hasBackend)
        {
            backendCode = await PublishBackendAsync(plugin, outputDir, source, apiKey, dryRun, cancellationToken);
            if (backendCode != 0)
            {
                return backendCode;
            }
        }
        else
        {
            Console.WriteLine($"[插件 {plugin.Name}] 无后端项目，跳过 NuGet 发布");
        }

        if (hasFrontend)
        {
            frontendCode = await PublishFrontendAsync(plugin, dryRun, cancellationToken);
            if (frontendCode != 0)
            {
                return frontendCode;
            }
        }
        else
        {
            Console.WriteLine($"[插件 {plugin.Name}] 无前端包目录，跳过 npm 发布");
        }

        return Math.Max(backendCode, frontendCode);
    }

    /// <summary>
    /// 发布插件后端 NuGet 包。
    /// </summary>
    /// <param name="plugin">插件布局。</param>
    /// <param name="outputDir">包输出目录。</param>
    /// <param name="source">NuGet 源。</param>
    /// <param name="apiKey">API Key。</param>
    /// <param name="dryRun">演练模式。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>退出码。</returns>
    public async Task<int> PublishBackendAsync(
        PluginLayout plugin,
        string? outputDir,
        string? source,
        string? apiKey,
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
        var resolvedOutput = PluginBuilder.ResolveOutputDir(plugin, outputDir);
        var package = FindNuGetPackage(plugin, resolvedOutput);
        if (package is null)
        {
            Console.Error.WriteLine(
                $"[错误] 在 {resolvedOutput} 中未找到 {plugin.BackendPackageId} 的 .nupkg。请先执行 wails plugin build 或用 --skip-build 前先构建。");
            return 2;
        }

        var resolvedSource = string.IsNullOrWhiteSpace(source) ? DefaultNuGetSource : source;
        var resolvedApiKey = string.IsNullOrWhiteSpace(apiKey)
            ? Environment.GetEnvironmentVariable(NuGetApiKeyEnvVar)
            : apiKey;

        Console.WriteLine($"[插件 {plugin.Name}] 发布后端：{Path.GetFileName(package)}");
        Console.WriteLine($"  -> 源：{resolvedSource}");
        Console.WriteLine($"  -> API Key：{(string.IsNullOrEmpty(resolvedApiKey) ? "(未提供，需交互输入或设置 NUGET_API_KEY)" : "已提供")}");

        if (dryRun)
        {
            Console.WriteLine("[演练] 跳过实际发布（--dry-run）");
            return 0;
        }

        var args = new List<string>
        {
            "nuget",
            "push",
            package,
            "--source",
            resolvedSource,
        };

        if (!string.IsNullOrEmpty(resolvedApiKey))
        {
            args.Add("--api-key");
            args.Add(resolvedApiKey);
        }

        var (exitCode, output) = await RunDotnetAsync(args, cancellationToken);
        if (exitCode != 0)
        {
            Console.Error.WriteLine($"[错误] dotnet nuget push 退出码 {exitCode}");
            if (!string.IsNullOrEmpty(output))
            {
                Console.WriteLine(output);
            }
            return 2;
        }

        return 0;
    }

    /// <summary>
    /// 发布插件前端 npm 包（pnpm publish，pnpm 缺失时回退 npm）。
    /// </summary>
    /// <param name="plugin">插件布局。</param>
    /// <param name="dryRun">演练模式（pnpm publish --dry-run）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>退出码。</returns>
    public async Task<int> PublishFrontendAsync(
        PluginLayout plugin,
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
        if (plugin.FrontendDir is null)
        {
            return 1;
        }

        // 演练模式：不检测工具链、不执行任何命令，仅打印发布计划
        if (dryRun)
        {
            var packageName = string.IsNullOrEmpty(plugin.FrontendPackageName)
                ? $"@wails-net/plugin-{plugin.Name}"
                : plugin.FrontendPackageName;
            Console.WriteLine($"[插件 {plugin.Name}] 演练：pnpm publish（npm 包 {packageName}，目录 {plugin.FrontendDir}）");
            return 0;
        }

        FrontendToolchain? toolchain;
        try
        {
            toolchain = await FrontendToolchain.DetectAsync(cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine($"[错误] 未检测到 pnpm/npm：{ex.Message}");
            return 2;
        }

        Console.WriteLine(
            $"[插件 {plugin.Name}] 发布前端（{toolchain.PackageManager} publish{(dryRun ? " --dry-run" : string.Empty)}，{plugin.FrontendDir}）");

        // 复用 FrontendToolchain 的进程封装，但需要追加参数，故直接走 BuildHooks.ExecuteAsync
        var command = dryRun ? $"{toolchain.PackageManager} publish --dry-run" : $"{toolchain.PackageManager} publish";
        var result = await BuildHooks.ExecuteAsync(command, plugin.FrontendDir, cancellationToken, streamOutput: true);
        if (!result.Success)
        {
            Console.Error.WriteLine($"[错误] {toolchain.PackageManager} publish 失败：{result.ErrorMessage}");
            return 2;
        }

        return 0;
    }

    /// <summary>
    /// 校验后端 WailsNetVersion 与前端 package.json version 的一致性。
    /// 仅当两端都存在时校验；一方缺失视为通过（无对应包可发）。
    /// </summary>
    /// <param name="plugin">插件布局。</param>
    /// <param name="backendVersion">输出的后端版本（可能为 null）。</param>
    /// <param name="frontendVersion">输出的前端版本（可能为 null）。</param>
    /// <returns>错误消息；校验通过返回 null。</returns>
    public static string? ValidateVersionConsistency(
        PluginLayout plugin,
        out string? backendVersion,
        out string? frontendVersion)
    {
        backendVersion = null;
        frontendVersion = null;

        if (plugin.BackendProjectPath is not null)
        {
            backendVersion = ReadWailsNetVersion(Path.GetDirectoryName(plugin.BackendProjectPath)!);
            if (backendVersion is null)
            {
                return $"无法从插件 {plugin.Name} 解析 WailsNetVersion（Directory.Build.props 缺失或未配置）";
            }
        }

        if (plugin.FrontendDir is not null)
        {
            frontendVersion = PluginBuilder.ReadPackageJsonField(plugin.FrontendDir, "version");
            if (frontendVersion is null)
            {
                return $"无法从插件 {plugin.Name} 的前端包解析 version（package.json 缺失或未配置）";
            }
        }

        if (backendVersion is not null && frontendVersion is not null &&
            !string.Equals(backendVersion.Trim(), frontendVersion.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return
                $"插件 {plugin.Name} 前后端版本不一致（发布硬约束）：NuGet {backendVersion} != npm {frontendVersion}。请同步 Directory.Build.props 的 WailsNetVersion 与 package.json 的 version。";
        }

        return null;
    }

    /// <summary>
    /// 从 Directory.Build.props 解析 WailsNetVersion（向上查找仓库根）。
    /// </summary>
    /// <param name="startDir">搜索起点（插件后端目录）。</param>
    /// <returns>WailsNetVersion 值；未找到返回 null。</returns>
    public static string? ReadWailsNetVersion(string startDir)
    {
        var root = PluginBuilder.FindRepoRoot(startDir);
        if (root is null)
        {
            return null;
        }

        var propsPath = Path.Combine(root, "Directory.Build.props");
        try
        {
            var doc = XDocument.Load(propsPath);
            var el = doc.Descendants("WailsNetVersion").FirstOrDefault();
            var value = el?.Value?.Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 在输出目录中查找插件对应的 .nupkg 文件（精确匹配包 ID 前缀，排除 .snupkg）。
    /// </summary>
    /// <param name="plugin">插件布局。</param>
    /// <param name="outputDir">输出目录。</param>
    /// <returns>nupkg 文件路径；未找到返回 null。</returns>
    internal static string? FindNuGetPackage(PluginLayout plugin, string outputDir)
    {
        if (!Directory.Exists(outputDir))
        {
            return null;
        }

        var prefix = $"{plugin.BackendPackageId}.";
        return Directory.GetFiles(outputDir, "*.nupkg", SearchOption.TopDirectoryOnly)
            .Where(f => !f.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase))
            .Where(f => Path.GetFileName(f).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => Path.GetFileName(f))
            .FirstOrDefault();
    }

    /// <summary>
    /// 运行 dotnet 命令并捕获输出。
    /// </summary>
    /// <param name="args">参数列表。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>(退出码, 标准输出+错误输出)。</returns>
    private static async Task<(int ExitCode, string Output)> RunDotnetAsync(
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
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
            using var proc = new Process { StartInfo = psi };
            proc.Start();

            var stdoutTask = proc.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = proc.StandardError.ReadToEndAsync(cancellationToken);
            await proc.WaitForExitAsync(cancellationToken);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var combined = string.IsNullOrEmpty(stderr) ? stdout : $"{stdout}\n{stderr}";
            return (proc.ExitCode, combined);
        }
        catch (OperationCanceledException)
        {
            return (-1, "dotnet 命令执行被取消");
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
