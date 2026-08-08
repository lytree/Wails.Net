using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Wails.Net.Cli.Build;

/// <summary>
/// 插件双包布局信息：后端 NuGet 包 + 前端 npm 包。
/// 对应 docs/development/plugin-packaging.md 的「一插件 = 双包」模型。
/// </summary>
public sealed record PluginLayout
{
    /// <summary>
    /// 插件短名（小写 kebab-case），如 <c>updater</c>、<c>file-system</c>。
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 后端 NuGet 包标识，如 <c>Wails.Net.Plugins.Updater</c>。
    /// </summary>
    public required string BackendPackageId { get; init; }

    /// <summary>
    /// 后端 .csproj 绝对路径；纯前端插件时为 null。
    /// </summary>
    public string? BackendProjectPath { get; init; }

    /// <summary>
    /// 前端 npm 包目录绝对路径；纯后端插件时为 null。
    /// </summary>
    public string? FrontendDir { get; init; }

    /// <summary>
    /// 前端 npm 包名（读取 package.json 的 name 字段），如 <c>@wails-net/plugin-updater</c>。
    /// </summary>
    public string? FrontendPackageName { get; init; }
}

/// <summary>
/// 插件双包构建器：定位仓库中的 Wails.Net 插件并执行构建。
/// <para>
/// 后端插件位于 <c>src/Wails.Net.Plugins.{Name}/</c>（NuGet 包），前端插件位于
/// <c>packages/wails-net-plugin-{name}/</c>（npm 包）。后端构建执行
/// <c>dotnet pack</c>，前端构建执行 <c>pnpm build</c>（pnpm 缺失时回退 npm）。
/// </para>
/// </summary>
public sealed class PluginBuilder
{
    /// <summary>后端插件目录前缀（含点号），如 <c>Wails.Net.Plugins.</c>。</summary>
    public const string BackendPrefix = "Wails.Net.Plugins.";

    /// <summary>前端插件目录前缀，如 <c>wails-net-plugin-</c>。</summary>
    public const string FrontendDirPrefix = "wails-net-plugin-";

    /// <summary>默认 NuGet 包输出目录（相对仓库根，与 Directory.Build.props 的 PackageOutputPath 对齐）。</summary>
    public const string DefaultOutputDir = "artifacts/nupkg";

    /// <summary>
    /// 发现仓库中的插件布局。
    /// <para>
    /// 从 <paramref name="startDir"/> 向上查找仓库根（含 Directory.Build.props 的目录），
    /// 扫描 <c>src/Wails.Net.Plugins.*/</c> 与 <c>packages/wails-net-plugin-*/</c> 目录；
    /// 未指定 <paramref name="pluginName"/> 时返回全部插件（按短名排序）。
    /// </para>
    /// </summary>
    /// <param name="pluginName">插件短名过滤（可选），大小写与 kebab/Pascal 风格不敏感。</param>
    /// <param name="startDir">搜索起点目录（默认当前目录）。</param>
    /// <returns>匹配的插件布局列表；未找到仓库时返回空列表。</returns>
    public static List<PluginLayout> DiscoverPlugins(string? pluginName = null, string? startDir = null)
    {
        var start = Path.GetFullPath(startDir ?? Directory.GetCurrentDirectory());
        var root = FindRepoRoot(start);

        // 起点目录本身即插件仓库（独立仓库布局）时直接使用
        if (root is null && Directory.Exists(Path.Combine(start, "src")))
        {
            root = start;
        }

        if (root is null)
        {
            return [];
        }

        var layouts = new List<PluginLayout>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ---- 后端插件：src/Wails.Net.Plugins.* ----
        var srcDir = Path.Combine(root, "src");
        if (Directory.Exists(srcDir))
        {
            foreach (var dir in Directory.GetDirectories(srcDir, $"{BackendPrefix}*"))
            {
                var packageId = Path.GetFileName(dir);
                var shortName = packageId[BackendPrefix.Length..];
                var csproj = Path.Combine(dir, $"{packageId}.csproj");

                var frontendDir = FindFrontendDir(root, shortName);
                var layout = new PluginLayout
                {
                    Name = ToKebabCase(shortName),
                    BackendPackageId = packageId,
                    BackendProjectPath = File.Exists(csproj) ? csproj : null,
                    FrontendDir = frontendDir,
                    FrontendPackageName = frontendDir is null ? null : ReadPackageJsonField(frontendDir, "name"),
                };
                layouts.Add(layout);
                seenNames.Add(Normalize(layout.Name));
            }
        }

        // ---- 前端插件补充：packages/wails-net-plugin-*（可能没有后端） ----
        var packagesDir = Path.Combine(root, "packages");
        if (Directory.Exists(packagesDir))
        {
            foreach (var dir in Directory.GetDirectories(packagesDir, $"{FrontendDirPrefix}*"))
            {
                var dirName = Path.GetFileName(dir);
                var kebab = dirName[FrontendDirPrefix.Length..];
                if (!seenNames.Add(Normalize(kebab)))
                {
                    continue;
                }

                layouts.Add(new PluginLayout
                {
                    Name = kebab,
                    BackendPackageId = $"{BackendPrefix}{ToPascalCase(kebab)}",
                    BackendProjectPath = null,
                    FrontendDir = dir,
                    FrontendPackageName = ReadPackageJsonField(dir, "name"),
                });
            }
        }

        // ---- 按 --plugin 过滤（规范化比较：忽略大小写与分隔符） ----
        if (!string.IsNullOrWhiteSpace(pluginName))
        {
            var target = Normalize(pluginName);
            layouts = layouts
                .Where(l => Normalize(l.Name) == target ||
                            Normalize(l.BackendPackageId) == target ||
                            Normalize(l.FrontendPackageName ?? string.Empty) == target)
                .ToList();
        }

        return layouts.OrderBy(l => l.Name).ToList();
    }

    /// <summary>
    /// 规范化名称用于匹配：仅保留字母数字并转为小写。
    /// 使 <c>e2e</c> / <c>E2E</c> / <c>e2-e</c>（ToKebabCase 的数字边界产物）等价。
    /// </summary>
    /// <param name="name">源名称。</param>
    /// <returns>规范化字符串。</returns>
    internal static string Normalize(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 构建插件双包。
    /// </summary>
    /// <param name="plugin">插件布局。</param>
    /// <param name="configuration">dotnet pack 构建配置（默认 Release）。</param>
    /// <param name="outputDir">NuGet 包输出目录（null 时使用仓库根 <see cref="DefaultOutputDir"/>）。</param>
    /// <param name="backendOnly">仅构建后端（前端目录存在时）。</param>
    /// <param name="frontendOnly">仅构建前端（后端 csproj 存在时）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>退出码：0 成功；非零失败。</returns>
    public async Task<int> BuildAsync(
        PluginLayout plugin,
        string configuration,
        string? outputDir,
        bool backendOnly,
        bool frontendOnly,
        CancellationToken cancellationToken = default)
    {
        var backendCode = 0;
        var frontendCode = 0;

        if (!frontendOnly && plugin.BackendProjectPath is not null)
        {
            backendCode = await BuildBackendAsync(plugin, configuration, outputDir, cancellationToken);
            if (backendCode != 0)
            {
                return backendCode;
            }
        }
        else if (plugin.BackendProjectPath is null)
        {
            Console.WriteLine($"[插件 {plugin.Name}] 未找到后端项目 {plugin.BackendPackageId}.csproj，跳过后端构建");
        }

        if (!backendOnly && plugin.FrontendDir is not null)
        {
            frontendCode = await BuildFrontendAsync(plugin, cancellationToken);
            if (frontendCode != 0)
            {
                return frontendCode;
            }
        }
        else if (plugin.FrontendDir is null)
        {
            Console.WriteLine($"[插件 {plugin.Name}] 未找到前端包目录 wails-net-plugin-{plugin.Name}/，跳过前端构建");
        }

        return Math.Max(backendCode, frontendCode);
    }

    /// <summary>
    /// 构建插件后端 NuGet 包（dotnet pack）。
    /// </summary>
    /// <param name="plugin">插件布局。</param>
    /// <param name="configuration">构建配置。</param>
    /// <param name="outputDir">输出目录（null 时使用仓库根 artifacts/nupkg）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>退出码。</returns>
    public async Task<int> BuildBackendAsync(
        PluginLayout plugin,
        string configuration,
        string? outputDir,
        CancellationToken cancellationToken = default)
    {
        if (plugin.BackendProjectPath is null)
        {
            return 1;
        }

        var resolvedOutput = ResolveOutputDir(plugin, outputDir);

        var args = new List<string>
        {
            "pack",
            plugin.BackendProjectPath,
            "-c",
            configuration,
        };

        if (!string.IsNullOrEmpty(resolvedOutput))
        {
            args.Add("-o");
            args.Add(resolvedOutput);
        }

        Console.WriteLine($"[插件 {plugin.Name}] 打包后端：dotnet pack {Path.GetFileName(plugin.BackendProjectPath)} -> {resolvedOutput}");
        var (exitCode, output) = await RunDotnetAsync(args, cancellationToken);
        if (exitCode != 0)
        {
            Console.Error.WriteLine($"[错误] dotnet pack 退出码 {exitCode}");
            if (!string.IsNullOrEmpty(output))
            {
                Console.WriteLine(output);
            }
            return 2;
        }

        return 0;
    }

    /// <summary>
    /// 构建插件前端 npm 包（pnpm build，pnpm 缺失时回退 npm）。
    /// </summary>
    /// <param name="plugin">插件布局。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>退出码。</returns>
    public async Task<int> BuildFrontendAsync(PluginLayout plugin, CancellationToken cancellationToken = default)
    {
        if (plugin.FrontendDir is null)
        {
            return 1;
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

        Console.WriteLine($"[插件 {plugin.Name}] 构建前端（{toolchain.PackageManager} build，{plugin.FrontendDir}）");
        return await toolchain.BuildAsync(plugin.FrontendDir, cancellationToken);
    }

    /// <summary>
    /// 解析 NuGet 输出目录：显式指定优先，否则回退仓库根 <see cref="DefaultOutputDir"/>。
    /// </summary>
    /// <param name="plugin">插件布局（用于推导仓库根）。</param>
    /// <param name="outputDir">用户显式指定的输出目录（可空）。</param>
    /// <returns>解析后的输出目录绝对路径。</returns>
    internal static string ResolveOutputDir(PluginLayout plugin, string? outputDir)
    {
        if (!string.IsNullOrEmpty(outputDir))
        {
            return Path.GetFullPath(outputDir);
        }

        var root = plugin.BackendProjectPath is not null
            ? FindRepoRoot(Path.GetDirectoryName(plugin.BackendProjectPath)!)
            : null;
        var baseDir = root ?? Directory.GetCurrentDirectory();
        return Path.Combine(baseDir, DefaultOutputDir);
    }

    /// <summary>
    /// 向上查找仓库根：目录包含 <c>Directory.Build.props</c> 即视为仓库根。
    /// </summary>
    /// <param name="startDir">搜索起点。</param>
    /// <returns>仓库根绝对路径；未找到返回 null。</returns>
    public static string? FindRepoRoot(string startDir)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(startDir));
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    /// <summary>
    /// PascalCase → kebab-case（FileSystem → file-system；FsWatch → fs-watch；WebSocket → web-socket）。
    /// 大小写转换边界处插入连字符，兼容连续大写缩写。
    /// </summary>
    /// <param name="name">源名称。</param>
    /// <returns>kebab-case 名称。</returns>
    public static string ToKebabCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0)
            {
                var prev = name[i - 1];
                var next = i + 1 < name.Length ? name[i + 1] : '\0';
                var boundary = char.IsLower(prev) || char.IsDigit(prev) ||
                               (char.IsUpper(prev) && char.IsLower(next));
                if (boundary)
                {
                    sb.Append('-');
                }
            }

            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }

    /// <summary>
    /// kebab-case / PascalCase → PascalCase（updater → Updater；file-system → FileSystem；fs-watch → FsWatch）。
    /// 用于由插件短名推导后端类型名与目录名。
    /// </summary>
    /// <param name="name">源名称。</param>
    /// <returns>PascalCase 名称。</returns>
    public static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(name.Length);
        var upperNext = true;
        foreach (var c in name)
        {
            if (c is '-' or '_' or ' ')
            {
                upperNext = true;
                continue;
            }

            sb.Append(upperNext ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
            upperNext = false;
        }

        return sb.ToString();
    }

    /// <summary>
    /// 读取 package.json 中的指定字符串字段。
    /// </summary>
    /// <param name="frontendDir">前端包目录。</param>
    /// <param name="field">字段名（name / version）。</param>
    /// <returns>字段值；文件缺失或解析失败返回 null。</returns>
    public static string? ReadPackageJsonField(string frontendDir, string field)
    {
        var pkg = Path.Combine(frontendDir, "package.json");
        if (!File.Exists(pkg))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(pkg));
            return doc.RootElement.TryGetProperty(field, out var el) && el.ValueKind == JsonValueKind.String
                ? el.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 在前端包目录中定位指定后端插件对应的 npm 包目录。
    /// 优先精确匹配 <c>packages/wails-net-plugin-{kebab}</c>，其次匹配 package.json name 为
    /// <c>@wails-net/plugin-{kebab}</c> 的目录。
    /// </summary>
    /// <param name="root">仓库根目录。</param>
    /// <param name="shortName">后端插件短名（PascalCase，如 FileSystem）。</param>
    /// <returns>前端包目录绝对路径；未找到返回 null。</returns>
    internal static string? FindFrontendDir(string root, string shortName)
    {
        var packagesDir = Path.Combine(root, "packages");
        if (!Directory.Exists(packagesDir))
        {
            return null;
        }

        var kebab = ToKebabCase(shortName);
        var direct = Path.Combine(packagesDir, $"{FrontendDirPrefix}{kebab}");
        if (Directory.Exists(direct))
        {
            return direct;
        }

        var expectedName = $"@wails-net/plugin-{kebab}";
        foreach (var dir in Directory.GetDirectories(packagesDir, $"{FrontendDirPrefix}*"))
        {
            // 规范化比较：容忍 ToKebabCase 的数字边界（如 E2E → e2-e）与 package.json 实际命名差异
            if (Normalize(ReadPackageJsonField(dir, "name") ?? string.Empty) == Normalize(expectedName))
            {
                return dir;
            }
        }

        return null;
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
