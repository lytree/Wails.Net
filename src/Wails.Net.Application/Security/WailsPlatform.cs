namespace Wails.Net.Application.Security;

/// <summary>
/// 平台标识工具（OPT-SEC-002）。
/// <para>
/// 为 <see cref="Capability.Platforms"/> 提供平台标识常量、已知平台集合与当前平台推断。
/// 平台名大小写不敏感（统一小写比较）。
/// </para>
/// </summary>
public static class WailsPlatform
{
    /// <summary>Windows 平台标识。</summary>
    public const string Windows = "windows";

    /// <summary>Linux 平台标识。</summary>
    public const string Linux = "linux";

    /// <summary>Android 平台标识。</summary>
    public const string Android = "android";

    /// <summary>macOS 平台标识。</summary>
    public const string MacOS = "macos";

    /// <summary>Server 模式平台标识（无 GUI 降级运行时）。</summary>
    public const string Server = "server";

    /// <summary>
    /// 已知平台标识集合（大小写不敏感）。
    /// 加载 capability 文件时用于校验未知平台名。
    /// </summary>
    public static IReadOnlySet<string> KnownPlatforms { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Windows, Linux, Android, MacOS, Server,
        };

    /// <summary>
    /// 判断平台标识是否为已知平台。
    /// </summary>
    /// <param name="platform">平台标识，可为 null。</param>
    /// <returns>已知平台返回 true；未知或空返回 false。</returns>
    public static bool IsKnown(string? platform)
        => !string.IsNullOrWhiteSpace(platform) && KnownPlatforms.Contains(platform);

    /// <summary>
    /// 推断当前宿主平台标识。
    /// 基于 <see cref="OperatingSystem"/> 推断：
    /// Android 优先（.NET Android 运行时同时满足 IsWindows()=false / IsLinux()=true，
    /// 必须先判断 Android），随后 Windows、Linux、macOS。
    /// </summary>
    /// <returns>当前平台标识。</returns>
    public static string CurrentPlatform()
    {
        if (OperatingSystem.IsAndroid())
        {
            return Android;
        }

        if (OperatingSystem.IsWindows())
        {
            return Windows;
        }

        if (OperatingSystem.IsLinux())
        {
            return Linux;
        }

        if (OperatingSystem.IsMacOS())
        {
            return MacOS;
        }

        return Server;
    }

    /// <summary>
    /// 规范化平台标识：null/空白返回 null，否则转小写。
    /// </summary>
    /// <param name="platform">原始平台标识。</param>
    /// <returns>规范化后的平台标识。</returns>
    public static string? Normalize(string? platform)
        => string.IsNullOrWhiteSpace(platform) ? null : platform.Trim().ToLowerInvariant();
}
