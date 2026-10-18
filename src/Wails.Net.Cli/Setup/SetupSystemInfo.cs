using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Wails.Net.Cli.Commands;

namespace Wails.Net.Cli.Setup;

/// <summary>
/// setup 向导的系统详情快照。
/// 对应 Wails v3 Go 版本 <c>internal/setupwizard/wizard.go:220-232</c> 的
/// <c>SystemInfo</c> 结构（<c>os</c> / <c>arch</c> / <c>wailsVersion</c> / <c>goVersion</c> /
/// <c>homeDir</c> / <c>osName</c> / <c>osVersion</c> / <c>gitName</c> / <c>gitEmail</c> / <c>details</c>）。
/// <para>
/// .NET 侧以 .NET 运行时版本对应 <c>goVersion</c>；平台相关字段（WebView2 / GTK4 /
/// WebKitGTK、.NET SDK）归入 <see cref="SetupSystemDetails.Extras"/>，缺失值统一标记为
/// <c>unknown</c>。
/// </para>
/// </summary>
internal sealed record SetupSystemInfo
{
    /// <summary>操作系统标识（对应 Go <c>SystemInfo.OS</c>；取值 windows / linux / darwin / unknown）。</summary>
    [JsonPropertyName("os")]
    public string Os { get; init; } = "unknown";

    /// <summary>CPU 架构（对应 Go <c>SystemInfo.Arch</c>，采用 GOARCH 命名：amd64 / arm64 / 386 / arm）。</summary>
    [JsonPropertyName("arch")]
    public string Arch { get; init; } = "unknown";

    /// <summary>Wails.Net 版本（对应 Go <c>SystemInfo.WailsVersion</c>）。</summary>
    [JsonPropertyName("wailsVersion")]
    public string WailsVersion { get; init; } = "unknown";

    /// <summary>.NET 运行时版本（对应 Go <c>SystemInfo.GoVersion</c>）。</summary>
    [JsonPropertyName("runtimeVersion")]
    public string RuntimeVersion { get; init; } = "unknown";

    /// <summary>当前用户主目录（对应 Go <c>SystemInfo.HomeDir</c>）。</summary>
    [JsonPropertyName("homeDir")]
    public string HomeDir { get; init; } = "unknown";

    /// <summary>操作系统友好名称（对应 Go <c>SystemInfo.OSName</c>）。</summary>
    [JsonPropertyName("osName")]
    public string OsName { get; init; } = "unknown";

    /// <summary>操作系统版本（对应 Go <c>SystemInfo.OSVersion</c>）。</summary>
    [JsonPropertyName("osVersion")]
    public string OsVersion { get; init; } = "unknown";

    /// <summary>Git 用户名（对应 Go <c>SystemInfo.GitName</c>，用于预填签名表单）。</summary>
    [JsonPropertyName("gitName")]
    public string GitName { get; init; } = "unknown";

    /// <summary>Git 邮箱（对应 Go <c>SystemInfo.GitEmail</c>）。</summary>
    [JsonPropertyName("gitEmail")]
    public string GitEmail { get; init; } = "unknown";

    /// <summary>详细系统报告（对应 Go <c>SystemInfo.Details</c>，即 <c>doctor.SystemReport</c>）。</summary>
    [JsonPropertyName("details")]
    public SetupSystemDetails Details { get; init; } = new();
}

/// <summary>
/// 详细系统报告。
/// 对应 Wails v3 Go 版本 <c>internal/doctor/doctor.go:38-49</c> 的 <c>SystemReport</c>
/// （<c>name</c> / <c>version</c> / <c>id</c> / <c>branding</c> / <c>platform</c> /
/// <c>architecture</c> / <c>cpu</c> / <c>gpu</c> / <c>memory</c> / <c>extras</c>）。
/// <c>gpu</c> 在 .NET 侧无跨平台可靠来源，保留 <c>unknown</c>。
/// </summary>
internal sealed record SetupSystemDetails
{
    /// <summary>系统名称。</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "unknown";

    /// <summary>系统版本。</summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = "unknown";

    /// <summary>系统标识（Windows 为 <c>windows</c>，Linux 取 <c>ID</c>）。</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = "unknown";

    /// <summary>品牌信息（无可靠来源，保留 unknown）。</summary>
    [JsonPropertyName("branding")]
    public string Branding { get; init; } = "unknown";

    /// <summary>平台（与 <see cref="SetupSystemInfo.Os"/> 一致）。</summary>
    [JsonPropertyName("platform")]
    public string Platform { get; init; } = "unknown";

    /// <summary>架构（与 <see cref="SetupSystemInfo.Arch"/> 一致）。</summary>
    [JsonPropertyName("architecture")]
    public string Architecture { get; init; } = "unknown";

    /// <summary>CPU 摘要（.NET 侧以逻辑核数表示）。</summary>
    [JsonPropertyName("cpu")]
    public string Cpu { get; init; } = "unknown";

    /// <summary>物理内存估算（来自 GC 可用内存信息）。</summary>
    [JsonPropertyName("memory")]
    public string Memory { get; init; } = "unknown";

    /// <summary>平台附加信息（WebView2 / GTK4 / WebKitGTK / .NET SDK 等）。</summary>
    [JsonPropertyName("extras")]
    public Dictionary<string, string> Extras { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>
/// setup 系统信息的采集器。
/// 对应 Wails v3 Go 版本 <c>internal/setupwizard/wizard.go:433-451</c> 的 <c>initSystemInfo</c>
/// 与 <c>internal/doctor/doctor.go:151-207</c> 的 <c>CollectSystemInfo</c>。
/// <para>
/// 复用 <see cref="DoctorCommand"/> 已提取的探测辅助方法（<c>reg query</c> 注册表读取、
/// 子进程版本读取、shell 单行输出），避免重复实现且保证与 <c>doctor</c> 命令口径一致。
/// 缺失字段一律以 <c>unknown</c> 标注，不抛异常。
/// </para>
/// </summary>
internal static class SetupSystemInfoCollector
{
    /// <summary>
    /// 采集当前机器的系统详情快照。
    /// </summary>
    /// <returns>系统详情快照；探测失败字段以 <c>unknown</c> 填充。</returns>
    internal static SetupSystemInfo Collect()
    {
        var os = DetectOs();
        var arch = DetectArch();
        var (osName, osVersion, osId) = DetectOsIdentity(os);

        var sdkVersion = NonEmptyOrUnknown(DoctorCommand.ReadExecutableVersion("dotnet", "--version"));
        var extras = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["dotnetSdk"] = sdkVersion,
            ["webView2"] = os == "windows"
                ? NonEmptyOrUnknown(DoctorCommand.ReadWebView2RegistryVersion())
                : "unknown",
            ["gtk4"] = os == "linux"
                ? NonEmptyOrUnknown(DoctorCommand.ReadExecutableVersion("pkg-config", "--modversion", "gtk4"))
                : "unknown",
            ["webkitgtk-6.0"] = os == "linux"
                ? NonEmptyOrUnknown(DoctorCommand.ReadExecutableVersion("pkg-config", "--modversion", "webkitgtk-6.0"))
                : "unknown",
        };

        var (gitName, gitEmail) = DetectGitIdentity();

        return new SetupSystemInfo
        {
            Os = os,
            Arch = arch,
            WailsVersion = NonEmptyOrUnknown(VersionConstants.InformationalVersion),
            RuntimeVersion = NonEmptyOrUnknown(Environment.Version.ToString()),
            HomeDir = NonEmptyOrUnknown(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
            OsName = osName,
            OsVersion = osVersion,
            GitName = gitName,
            GitEmail = gitEmail,
            Details = new SetupSystemDetails
            {
                Name = osName,
                Version = osVersion,
                Id = osId,
                Platform = os,
                Architecture = arch,
                Cpu = DetectCpu(),
                Memory = DetectMemory(),
                Extras = extras,
            },
        };
    }

    /// <summary>
    /// 以 GOOS 命名探测当前操作系统（对应 Go <c>runtime.GOOS</c>）。
    /// </summary>
    private static string DetectOs()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "windows";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "linux";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "darwin";
        }

        return "unknown";
    }

    /// <summary>
    /// 以 GOARCH 命名探测 CPU 架构（对应 Go <c>runtime.GOARCH</c>）。
    /// </summary>
    private static string DetectArch() => RuntimeInformation.OSArchitecture switch
    {
        Architecture.X64 => "amd64",
        Architecture.X86 => "386",
        Architecture.Arm64 => "arm64",
        Architecture.Arm => "arm",
        _ => RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
    };

    /// <summary>
    /// 探测操作系统友好名称与版本（对应 <c>operatingsystem.Info()</c> 的 Name / Version / ID）。
    /// </summary>
    private static (string Name, string Version, string Id) DetectOsIdentity(string os)
    {
        if (os == "windows")
        {
            const string currentVersionKey = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
            var name = DoctorCommand.ReadRegistryValue(currentVersionKey, "ProductName");
            var version = DoctorCommand.ReadRegistryValue(currentVersionKey, "DisplayVersion");
            if (string.IsNullOrEmpty(version))
            {
                version = DoctorCommand.ReadRegistryValue(currentVersionKey, "ReleaseId");
            }

            var fallbackVersion = Environment.OSVersion.Version.ToString();
            return (
                NonEmptyOrUnknown(name),
                NonEmptyOrUnknown(string.IsNullOrEmpty(version) ? fallbackVersion : version),
                "windows");
        }

        if (os == "linux")
        {
            return ReadOsRelease();
        }

        if (os == "darwin")
        {
            return ("macOS", NonEmptyOrUnknown(Environment.OSVersion.Version.ToString()), "macos");
        }

        return ("unknown", NonEmptyOrUnknown(Environment.OSVersion.Version.ToString()), "unknown");
    }

    /// <summary>
    /// 解析 <c>/etc/os-release</c>（或 <c>/usr/lib/os-release</c>）中的 PRETTY_NAME / VERSION / ID。
    /// </summary>
    private static (string Name, string Version, string Id) ReadOsRelease()
    {
        try
        {
            var path = "/etc/os-release";
            if (!File.Exists(path))
            {
                path = "/usr/lib/os-release";
            }

            if (!File.Exists(path))
            {
                return ("unknown", "unknown", "unknown");
            }

            var name = string.Empty;
            var version = string.Empty;
            var id = string.Empty;
            foreach (var line in File.ReadAllLines(path))
            {
                var separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var key = line[..separator];
                var value = line[(separator + 1)..].Trim().Trim('"');
                switch (key)
                {
                    case "PRETTY_NAME":
                        name = value;
                        break;
                    case "VERSION":
                        version = value;
                        break;
                    case "ID":
                        id = value;
                        break;
                }
            }

            return (NonEmptyOrUnknown(name), NonEmptyOrUnknown(version), NonEmptyOrUnknown(id));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ("unknown", "unknown", "unknown");
        }
    }

    /// <summary>
    /// 读取 git 用户身份（对应 <c>initSystemInfo</c> 中的 <c>git config user.name/email</c>）。
    /// </summary>
    private static (string Name, string Email) DetectGitIdentity()
    {
        var name = DoctorCommand.ReadShellCommandOutput("git config user.name");
        var email = DoctorCommand.ReadShellCommandOutput("git config user.email");
        return (NonEmptyOrUnknown(name), NonEmptyOrUnknown(email));
    }

    /// <summary>
    /// 探测 CPU 摘要。.NET 无跨平台 CPU 型号来源，故以逻辑核数表示。
    /// </summary>
    private static string DetectCpu()
        => Environment.ProcessorCount > 0 ? $"{Environment.ProcessorCount} cores" : "unknown";

    /// <summary>
    /// 估算物理内存（来自 GC 可用内存信息），无法获取时返回 unknown。
    /// </summary>
    private static string DetectMemory()
    {
        var bytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        if (bytes <= 0)
        {
            return "unknown";
        }

        var gigabytes = bytes / 1024.0 / 1024 / 1024;
        return gigabytes.ToString("0.#", CultureInfo.InvariantCulture) + "GB";
    }

    /// <summary>
    /// 空值/空白统一归一为 <c>unknown</c>，否则返回去除首尾空白后的值。
    /// </summary>
    private static string NonEmptyOrUnknown(string? value)
        => string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
}

/// <summary>
/// setup 系统详情的 JSON 源生成序列化上下文（NativeAOT 兼容，遵循 AGENTS.md §3.4，
/// 与 <c>Serialization.CliJsonSerializerContext</c> 保持 camelCase + 不忽略 null 的口径）。
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(SetupSystemInfo))]
internal sealed partial class SetupSystemInfoJsonContext : JsonSerializerContext
{
}
