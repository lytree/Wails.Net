using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;

namespace Wails.Net.Application.Plugins.BuiltIn;

/// <summary>
/// 操作系统信息插件，提供获取系统平台、架构、版本等信息的命令。
/// 对应 Tauri v2 的 <c>@tauri-apps/plugin-os</c>。
/// <para>
/// 同时注册 <c>os.*</c> 和 <c>system.*</c> 两套命令名：
/// <list type="bullet">
/// <item><c>os.*</c> — 历史命令名，保留向后兼容</item>
/// <item><c>system.*</c> — 与前端 <c>wails.system.*</c> API 一致的命令名</item>
/// </list>
/// </para>
/// </summary>
public class OsInfoPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "os";

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。此插件无需注册额外服务。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        // 无需注册额外服务
    }

    /// <summary>
    /// 配置插件，注册操作系统信息相关命令。
    /// 同时注册 <c>os.*</c>（历史名）和 <c>system.*</c>（前端 API 名）两套命令。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        // 声明权限集
        context.Permissions.RegisterPermissionSet("os:default", "操作系统信息默认权限集",
            "os:allow-info");
        context.Permissions.DeclarePermission("os:allow-info", "允许获取操作系统信息");

        var commands = context.Commands;

        // === os.* 命令（历史名，保留向后兼容） ===
        commands.MapCommand("os.platform", (Func<ICommandContext, string>)(_ => GetPlatform()));
        commands.MapCommand("os.hostname", (Func<ICommandContext, string>)(_ => GetHostname()));
        commands.MapCommand("os.arch", (Func<ICommandContext, string>)(_ => GetArch()));
        commands.MapCommand("os.locale", (Func<ICommandContext, string>)(_ => GetLocale()));
        commands.MapCommand("os.version", (Func<ICommandContext, string>)(_ => GetVersion()));
        commands.MapCommand("os.type", (Func<ICommandContext, string>)(_ => GetSystemType()));

        // === system.* 命令（与前端 wails.system.* API 一致） ===
        commands.MapCommand("system.platform", (Func<ICommandContext, string>)(_ => GetPlatform()));
        commands.MapCommand("system.hostname", (Func<ICommandContext, string>)(_ => GetHostname()));
        commands.MapCommand("system.arch", (Func<ICommandContext, string>)(_ => GetArch()));
        commands.MapCommand("system.locale", (Func<ICommandContext, string>)(_ => GetLocale()));
        commands.MapCommand("system.version", (Func<ICommandContext, string>)(_ => GetVersion()));
        commands.MapCommand("system.type", (Func<ICommandContext, string>)(_ => GetSystemType()));
        commands.MapCommand("system.timezone", (Func<ICommandContext, string>)(_ => GetTimezone()));
    }

    private static string GetPlatform() =>
        OperatingSystem.IsWindows() ? "windows" : "linux";

    private static string GetHostname() =>
        Environment.MachineName;

    private static string GetArch() =>
        RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();

    private static string GetLocale() =>
        CultureInfo.CurrentCulture.Name;

    private static string GetVersion() =>
        Environment.OSVersion.Version.ToString();

    private static string GetSystemType() =>
        OperatingSystem.IsWindows() ? "Windows" : "Linux";

    private static string GetTimezone() =>
        TimeZoneInfo.Local.Id;
}
