using Cake.Common.Diagnostics;
using Cake.Core;
using Cake.Frosting;
using System.IO.Compression;
using System.Xml.Linq;

namespace Wails.Net.Build;

/// <summary>
/// 构建上下文，承载所有命令行参数与辅助方法。
/// 对应 build.cake 的 Argument() + 辅助方法。
/// </summary>
public sealed class BuildContext : FrostingContext
{
    // === 命令行参数 ===
    // 注意：Configuration 使用 new 关键字，因为基类 CakeContextAdapter 已有同名属性（IConfiguration 类型）
    public string Target { get; }
    public new string Configuration { get; }
    public string Platform { get; }
    public string RidArg { get; }
    public string CustomVersion { get; }
    public string OutputRoot { get; }
    public bool SkipFrontend { get; }
    public bool DryRun { get; }
    public bool Rebuild { get; }
    /// <summary>Linux 打包格式（targz|deb|rpm|all|逗号分隔组合），默认 targz。</summary>
    public string LinuxFormats { get; }

    // === 常量 ===
    public const string SolutionFile = "Wails.Net.slnx";
    public const string DemoProject = "examples/Wails.Net.Demo/Wails.Net.Demo.csproj";
    public const string DemoAndroidProject = "examples/Wails.Net.Demo.Android/Wails.Net.Demo.Android.csproj";

    // === 平台 RID 映射 ===
    public static readonly string[] AllPlatforms = { "windows", "linux", "android" };

    public static readonly Dictionary<string, string[]> PlatformRIDs = new()
    {
        ["windows"] = new[] { "win-x64", "win-x86", "win-arm64" },
        ["linux"] = new[] { "linux-x64", "linux-arm64" },
        ["android"] = new[] { "android-arm64", "android-x64", "android-arm" },
    };

    public static readonly Dictionary<string, string> DefaultRID = new()
    {
        ["windows"] = "win-x64",
        ["linux"] = "linux-x64",
        ["android"] = "android-arm64",
    };

    public BuildContext(ICakeContext context) : base(context)
    {
        // ICakeArguments.GetArgument(string) 只接收 1 个参数，返回 null 表示参数不存在
        Target = context.Arguments.GetArgument("target") ?? "Default";
        Configuration = context.Arguments.GetArgument("configuration") ?? "Release";
        Platform = context.Arguments.GetArgument("platform") ?? "all";
        RidArg = context.Arguments.GetArgument("rid") ?? "";
        CustomVersion = context.Arguments.GetArgument("version") ?? "";
        OutputRoot = context.Arguments.GetArgument("output") ?? "artifacts/dist";
        SkipFrontend = context.Arguments.HasArgument("skip-frontend");
        DryRun = context.Arguments.HasArgument("dry-run");
        Rebuild = context.Arguments.HasArgument("rebuild");
        LinuxFormats = context.Arguments.GetArgument("linux-formats") ?? "targz";
    }

    // === 辅助方法 ===

    /// <summary>
    /// 读取 Directory.Build.props 中的 WailsNetVersion。
    /// </summary>
    public string GetVersion()
    {
        if (!string.IsNullOrEmpty(CustomVersion)) return CustomVersion;

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

    /// <summary>
    /// 解析 RID 参数，返回指定平台要构建的 RID 列表。
    /// </summary>
    public List<string> ResolveRIDs(string plat, string ridArgument)
    {
        var supported = PlatformRIDs[plat];
        var defaultRid = DefaultRID[plat];

        if (string.IsNullOrEmpty(ridArgument)) return new List<string> { defaultRid };
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
                this.Warning($"平台 {plat} 不支持 RID: {rid}（支持的: {string.Join(", ", supported)}）");
            }
        }
        return ridList.Count == 0 ? new List<string> { defaultRid } : ridList;
    }

    /// <summary>
    /// 解析 --linux-formats 参数，返回要生成的 Linux 包格式列表。
    /// 支持值：targz、deb、rpm、all，或逗号分隔组合（如 "targz,deb"）。
    /// </summary>
    public List<string> ResolveLinuxFormats()
    {
        var supported = new[] { "targz", "deb", "rpm" };
        if (string.IsNullOrEmpty(LinuxFormats) || LinuxFormats.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return new List<string>(supported);
        }

        var result = new List<string>();
        var parts = LinuxFormats.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var f in parts)
        {
            var lower = f.ToLowerInvariant();
            if (Array.IndexOf(supported, lower) >= 0)
            {
                if (!result.Contains(lower)) result.Add(lower);
            }
            else
            {
                this.Warning($"不支持的 Linux 格式: {f}（支持: {string.Join(", ", supported)}）");
            }
        }
        return result.Count == 0 ? new List<string> { "targz" } : result;
    }

    /// <summary>
    /// 检查指定平台是否应构建（基于 --platform 参数）。
    /// </summary>
    public bool ShouldBuildPlatform(string plat) => Platform == "all" || Platform == plat;

    /// <summary>
    /// 创建 tar.gz 压缩文件。
    /// </summary>
    public void CreateTarGz(string sourceDir, string tarGzPath)
    {
        var parent = System.IO.Path.GetDirectoryName(tarGzPath);
        if (!string.IsNullOrEmpty(parent)) System.IO.Directory.CreateDirectory(parent);
        if (System.IO.File.Exists(tarGzPath)) System.IO.File.Delete(tarGzPath);

        var tempTar = System.IO.Path.GetTempFileName();
        try
        {
            System.IO.File.Delete(tempTar);
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
        this.Information($"已创建 tar.gz: {tarGzPath}");
    }
}

/// <summary>
/// 程序入口。
/// </summary>
public static class Program
{
    /// <summary>
    /// 主入口，启动 Cake Frosting 主机。
    /// </summary>
    public static int Main(string[] args)
    {
        return new CakeHost()
            .UseContext<BuildContext>()
            .Run(args);
    }
}
