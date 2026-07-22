using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Wails.Net.Application.Clipboard;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform.ServerMode;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Platform;

/// <summary>
/// 平台工厂，根据运行时操作系统创建平台特定的实现。
/// 对应 Wails v3 Go 版本 platform.go 中的平台检测与初始化逻辑。
/// <para>
/// 平台检测采用 6 级回退链，保证任何环境都能识别平台或降级到 Server 模式，
/// 不再抛出 <see cref="PlatformNotSupportedException"/>。
/// </para>
/// <para>
/// 实例化策略：
/// <list type="bullet">
/// <item>使用通过 <see cref="RegisterPlatformApp"/> 和 <see cref="RegisterClipboard"/>
/// 手动注册的委托，运行时零反射，AOT 友好；</item>
/// <item>若未注册委托，抛出 <see cref="InvalidOperationException"/>，提示平台项目通过
/// <c>[ModuleInitializer]</c> 自动注册委托（遵循 AGENTS.md §3.4 禁止反射的约束）。</item>
/// </list>
/// </para>
/// </summary>
public static class PlatformFactory
{
    /// <summary>
    /// 用于启用 Server（无界面）模式的环境变量名称。
    /// </summary>
    private const string ServerModeEnvVar = "WAILS_SERVER_MODE";

    /// <summary>
    /// 用于强制指定平台的环境变量名称。
    /// 设置为 <c>windows</c>、<c>linux</c> 或 <c>android</c> 可覆盖自动检测。
    /// </summary>
    private const string PlatformEnvVar = "WAILS_PLATFORM";

    /// <summary>
    /// 用于启用调试日志的环境变量名称。
    /// 设置为 <c>true</c>（不区分大小写）时，平台工厂会输出诊断信息到控制台。
    /// </summary>
    private const string DebugEnvVar = "WAILS_DEBUG";

    /// <summary>
    /// 支持的平台名称常量。
    /// </summary>
    private const string PlatformWindows = "windows";
    private const string PlatformLinux = "linux";
    private const string PlatformAndroid = "android";

    /// <summary>
    /// FriendlyName 检测的关键字（Level 4）。
    /// </summary>
    private static readonly string[] FriendlyNameKeywords = ["wailsapp", "wails-server", "wails-android"];

    /// <summary>
    /// ProcessName 检测的关键字映射（Level 5）。
    /// 键为进程名关键字（小写），值为对应的平台标识。
    /// </summary>
    private static readonly Dictionary<string, string> ProcessNameMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["maui-app"] = PlatformAndroid,
        ["wails-server"] = PlatformWindows, // wails-server 进程通常运行在桌面平台，降级到 Windows
        ["wailsapp"] = PlatformWindows
    };

    /// <summary>
    /// 手动注册的平台应用创建委托（按平台名称索引）。
    /// 由平台项目通过 [ModuleInitializer] 自动注册，避免运行时反射。
    /// </summary>
    private static readonly Dictionary<string, Func<ApplicationOptions, IPlatformApp>> _platformAppFactories = new(StringComparer.Ordinal);

    /// <summary>
    /// 手动注册的剪贴板创建委托（按平台名称索引）。
    /// 由平台项目通过 [ModuleInitializer] 自动注册，避免运行时反射。
    /// </summary>
    private static readonly Dictionary<string, Func<IClipboardImpl>> _clipboardFactories = new(StringComparer.Ordinal);

    /// <summary>
    /// 注册平台应用创建委托。
    /// 通常由平台项目（如 Wails.Net.Application.Windows）通过 <c>[ModuleInitializer]</c> 自动调用，
    /// 避免运行时反射加载程序集。
    /// </summary>
    /// <param name="platformName">平台名称（windows/linux/android）。</param>
    /// <param name="factory">创建 <see cref="IPlatformApp"/> 实例的委托。</param>
    public static void RegisterPlatformApp(string platformName, Func<ApplicationOptions, IPlatformApp> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _platformAppFactories[platformName.ToLowerInvariant()] = factory;
    }

    /// <summary>
    /// 注册剪贴板创建委托。
    /// 通常由平台项目通过 <c>[ModuleInitializer]</c> 自动调用，避免运行时反射加载程序集。
    /// </summary>
    /// <param name="platformName">平台名称（windows/linux/android）。</param>
    /// <param name="factory">创建 <see cref="IClipboardImpl"/> 实例的委托。</param>
    public static void RegisterClipboard(string platformName, Func<IClipboardImpl> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _clipboardFactories[platformName.ToLowerInvariant()] = factory;
    }

    /// <summary>
    /// 清除所有已注册的委托（仅用于测试）。
    /// </summary>
    internal static void ClearRegistrations()
    {
        _platformAppFactories.Clear();
        _clipboardFactories.Clear();
    }

    /// <summary>
    /// 创建平台特定的应用实例。
    /// </summary>
    /// <param name="options">应用配置选项。</param>
    /// <returns>平台应用实例。若所有检测级别均未命中，降级到 <see cref="ServerPlatformApp"/>。</returns>
    /// <exception cref="InvalidOperationException">当检测到平台但未通过 <see cref="RegisterPlatformApp"/>
    /// 注册对应委托时抛出。平台项目应通过 <c>[ModuleInitializer]</c> 自动注册委托。</exception>
    public static IPlatformApp CreatePlatformApp(ApplicationOptions options)
    {
        // Level 1：Server 模式环境变量
        if (IsServerMode())
        {
            LogDebug("[Level 1] Server 模式已启用，使用 ServerPlatformApp（无 GUI 占位实现）");
            return new ServerPlatformApp(options);
        }

        var platform = DetectPlatformOrNull();

        // Level 6：所有检测级别均未命中，降级到 ServerPlatformApp
        if (platform is null)
        {
            LogDebug("[Level 6] 所有平台检测级别均未命中，降级到 ServerPlatformApp");
            return new ServerPlatformApp(options);
        }

        // 使用手动注册的委托（零反射路径，遵循 AGENTS.md §3.4）
        if (_platformAppFactories.TryGetValue(platform, out var factory))
        {
            LogDebug($"[Manual] 使用手动注册的委托创建 {platform} 平台应用");
            return factory(options);
        }

        // 委托未命中：尝试加载对应平台程序集以触发 [ModuleInitializer] 注册委托。
        // 此路径让 UseAutoPlatform() 在未显式调用 UseLinux()/UseWindows() 时也能正常工作，
        // 消除 Demo 项目必须重复条件块的负担。Assembly.Load 仅触发模块初始化器，不使用反射调用方法。
        TryLoadPlatformAssembly(platform);

        if (_platformAppFactories.TryGetValue(platform, out factory))
        {
            LogDebug($"[AutoLoad] 加载平台程序集后成功创建 {platform} 平台应用");
            return factory(options);
        }

        // 加载后仍未注册委托时抛出异常（遵循 AGENTS.md §3.4，禁止反射回退）
        throw new InvalidOperationException(
            $"平台 '{platform}' 未注册创建委托。请通过 PlatformFactory.RegisterPlatformApp(\"{platform}\", ...) 注册，" +
            $"通常在 Wails.Net.Application.{Capitalize(platform)} 项目中通过 [ModuleInitializer] 自动调用。" +
            $"已尝试自动加载程序集 Wails.Net.Application.{Capitalize(platform)} 但未找到委托，请确认项目已引用该平台包。");
    }

    /// <summary>
    /// 创建平台特定的 Webview 窗口实现。
    /// 仅 Server 模式下返回 <see cref="ServerWebviewWindow"/> 占位实现；
    /// 桌面平台的窗口创建由平台特定的 <see cref="IPlatformApp"/> 通过反射加载，
    /// 不由此工厂方法处理。
    /// </summary>
    /// <param name="id">窗口 ID。</param>
    /// <param name="options">窗口配置选项。</param>
    /// <returns>Server 模式下返回 <see cref="ServerWebviewWindow"/>。</returns>
    /// <exception cref="PlatformNotSupportedException">非 Server 模式时抛出，提示使用平台特定的 IPlatformApp。</exception>
    public static IWebviewWindowImpl CreateWebviewWindowImpl(uint id, WebviewWindowOptions options)
    {
        if (IsServerMode())
        {
            return new ServerWebviewWindow(id);
        }

        throw new PlatformNotSupportedException(
            "桌面平台的 Webview 窗口创建由平台特定的 IPlatformApp 实现，" +
            "请通过 Application.Get().NewWebviewWindow 创建窗口。");
    }

    /// <summary>
    /// 创建平台特定的剪贴板实现。
    /// </summary>
    /// <returns>剪贴板实现实例。若所有检测级别均未命中，降级到 <see cref="ServerClipboard"/>。</returns>
    /// <exception cref="InvalidOperationException">当检测到平台但未通过 <see cref="RegisterClipboard"/>
    /// 注册对应委托时抛出。平台项目应通过 <c>[ModuleInitializer]</c> 自动注册委托。</exception>
    public static IClipboardImpl CreateClipboard()
    {
        // Level 1：Server 模式
        if (IsServerMode())
        {
            LogDebug("[Level 1] Server 模式已启用，使用 ServerClipboard（无 GUI 占位实现）");
            return new ServerClipboard();
        }

        var platform = DetectPlatformOrNull();

        // Level 6：降级到 ServerClipboard
        if (platform is null)
        {
            LogDebug("[Level 6] 所有平台检测级别均未命中，降级到 ServerClipboard");
            return new ServerClipboard();
        }

        // 使用手动注册的委托（零反射路径，遵循 AGENTS.md §3.4）
        if (_clipboardFactories.TryGetValue(platform, out var factory))
        {
            LogDebug($"[Manual] 使用手动注册的委托创建 {platform} 剪贴板");
            return factory();
        }

        // 委托未命中：尝试加载对应平台程序集以触发 [ModuleInitializer] 注册委托。
        TryLoadPlatformAssembly(platform);

        if (_clipboardFactories.TryGetValue(platform, out factory))
        {
            LogDebug($"[AutoLoad] 加载平台程序集后成功创建 {platform} 剪贴板");
            return factory();
        }

        // 加载后仍未注册委托时抛出异常（遵循 AGENTS.md §3.4，禁止反射回退）
        throw new InvalidOperationException(
            $"平台 '{platform}' 未注册剪贴板创建委托。请通过 PlatformFactory.RegisterClipboard(\"{platform}\", ...) 注册，" +
            $"通常在 Wails.Net.Application.{Capitalize(platform)} 项目中通过 [ModuleInitializer] 自动调用。" +
            $"已尝试自动加载程序集 Wails.Net.Application.{Capitalize(platform)} 但未找到委托，请确认项目已引用该平台包。");
    }

    /// <summary>
    /// 检查是否处于 Server（无界面）模式。
    /// </summary>
    /// <returns>如果 WAILS_SERVER_MODE 环境变量为 "true"（不区分大小写）则返回 true。</returns>
    public static bool IsServerMode()
    {
        var value = Environment.GetEnvironmentVariable(ServerModeEnvVar);
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 检查是否启用了调试日志。
    /// </summary>
    /// <returns>如果 WAILS_DEBUG 环境变量为 "true"（不区分大小写）则返回 true。</returns>
    public static bool IsDebugEnabled()
    {
        var value = Environment.GetEnvironmentVariable(DebugEnvVar);
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 返回所有 6 级检测的快照字符串，便于诊断平台识别问题。
    /// 包含每一级的环境变量值、运行时信息、进程信息。
    /// </summary>
    /// <returns>多行诊断报告字符串。</returns>
    public static string GetDetectionReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[Level 1] {ServerModeEnvVar}={Environment.GetEnvironmentVariable(ServerModeEnvVar) ?? "(null)"}");
        sb.AppendLine($"[Level 2] {PlatformEnvVar}={Environment.GetEnvironmentVariable(PlatformEnvVar) ?? "(null)"}");
        sb.AppendLine($"[Level 3] OS={RuntimeInformation.OSDescription} Arch={RuntimeInformation.OSArchitecture}");
        sb.AppendLine($"[Level 4] FriendlyName={AppDomain.CurrentDomain.FriendlyName}");
        sb.AppendLine($"[Level 5] ProcessName={GetSafeProcessName()}");
        sb.AppendLine($"[Level 6] Fallback=ServerPlatformApp");
        sb.AppendLine($"[Manual] RegisteredPlatforms={string.Join(",", _platformAppFactories.Keys)}");
        return sb.ToString();
    }

    /// <summary>
    /// 检测当前运行时平台（6 级回退链）。
    /// 依次尝试：环境变量 → RuntimeInformation → FriendlyName → ProcessName。
    /// 所有级别均未命中时返回 null，由调用方降级到 Server 模式。
    /// </summary>
    /// <returns>平台名称（windows/linux/android）；若所有级别均未命中则返回 null。</returns>
    private static string? DetectPlatformOrNull()
    {
        // Level 2：环境变量强制指定
        var forced = Environment.GetEnvironmentVariable(PlatformEnvVar);
        if (!string.IsNullOrEmpty(forced))
        {
            var normalized = forced.ToLowerInvariant().Trim();
            if (normalized is PlatformWindows or PlatformLinux or PlatformAndroid)
            {
                LogDebug($"[Level 2] 平台由环境变量 {PlatformEnvVar}={normalized} 强制指定");
                return normalized;
            }

            LogDebug($"[Level 2] 环境变量 {PlatformEnvVar}={forced} 值无效（应为 windows/linux/android），继续下一级检测");
        }

        // Level 3：运行时自动检测
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            LogDebug("[Level 3] 自动检测到 Windows 平台");
            return PlatformWindows;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            LogDebug("[Level 3] 自动检测到 Linux 平台");
            return PlatformLinux;
        }

        if (OperatingSystem.IsAndroid())
        {
            LogDebug("[Level 3] 自动检测到 Android 平台");
            return PlatformAndroid;
        }

        // Level 4：AppDomain FriendlyName 关键字检测
        var friendlyName = AppDomain.CurrentDomain.FriendlyName;
        if (!string.IsNullOrEmpty(friendlyName))
        {
            var lowerName = friendlyName.ToLowerInvariant();
            foreach (var keyword in FriendlyNameKeywords)
            {
                if (lowerName.Contains(keyword))
                {
                    LogDebug($"[Level 4] FriendlyName '{friendlyName}' 包含关键字 '{keyword}'，推断为 Windows 平台");
                    return PlatformWindows;
                }
            }
        }

        // Level 5：ProcessName 关键字检测
        var processName = GetSafeProcessName();
        if (!string.IsNullOrEmpty(processName))
        {
            foreach (var (key, platform) in ProcessNameMap)
            {
                if (processName.Contains(key, StringComparison.OrdinalIgnoreCase))
                {
                    LogDebug($"[Level 5] ProcessName '{processName}' 匹配关键字 '{key}'，推断为 {platform} 平台");
                    return platform;
                }
            }
        }

        // Level 6：所有级别均未命中，返回 null 由调用方降级
        LogDebug("[Level 6] 所有平台检测级别均未命中，将降级到 ServerPlatformApp");
        return null;
    }

    /// <summary>
    /// 安全获取当前进程名称。
    /// 在某些受限环境（如容器化部署）中，<see cref="Process.GetCurrentProcess()"/> 可能抛出异常，
    /// 此方法捕获异常并返回空字符串。
    /// </summary>
    /// <returns>当前进程名称；获取失败时返回空字符串。</returns>
    private static string GetSafeProcessName()
    {
        try
        {
            return Process.GetCurrentProcess().ProcessName;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            LogDebug($"获取进程名称失败: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// 将字符串首字母大写（用于将平台名称转换为类型名前缀）。
    /// </summary>
    /// <param name="value">要转换的字符串。</param>
    /// <returns>首字母大写的字符串。</returns>
    private static string Capitalize(string value)
    {
        return string.IsNullOrEmpty(value)
            ? value
            : char.ToUpperInvariant(value[0]) + value[1..];
    }

    /// <summary>
    /// 尝试按平台名称加载对应的平台程序集，并显式触发其 <c>[ModuleInitializer]</c> 完成委托注册。
    /// <para>
    /// 此方法解决 <see cref="UseAutoPlatform"/> 路径下平台程序集按需加载导致
    /// <c>[ModuleInitializer]</c> 未触发的根因问题：当用户未显式调用
    /// <c>UseLinux()</c>/<c>UseWindows()</c> 时，<see cref="DetectPlatformOrNull"/>
    /// 检测到平台但 <c>_platformAppFactories</c> 未注册委托，此时按约定程序集名
    /// <c>Wails.Net.Application.{Platform}</c> 加载程序集即可让
    /// <c>LinuxPlatformRegistrar</c> / <c>WindowsPlatformRegistrar</c> /
    /// <c>AndroidPlatformRegistrar</c> 的模块初始化器执行注册。
    /// </para>
    /// <para>
    /// <b>关键实现细节</b>：.NET 运行时对 <c>[ModuleInitializer]</c> 采用 lazy 策略，
    /// 仅 <see cref="Assembly.Load"/> 不保证立即触发模块初始化器执行。
    /// 必须显式调用 <see cref="RuntimeHelpers.RunModuleConstructor"/>
    /// 强制运行模块初始化器（若尚未运行），否则 <c>_platformAppFactories</c>
    /// 仍可能未注册，导致 <see cref="InvalidOperationException"/>。
    /// </para>
    /// <para>
    /// 注意：此路径不属于 AGENTS.md §3.4 禁止的"反射获取对应方法"——
    /// <see cref="Assembly.Load"/> 与 <see cref="RuntimeHelpers.RunModuleConstructor"/>
    /// 仅触发编译器生成的模块初始化器，不通过反射发现或调用方法，
    /// 实际方法调用仍走源生成器生成的强类型调用链。
    /// </para>
    /// </summary>
    /// <param name="platform">平台名称（windows/linux/android）。</param>
    /// <remarks>
    /// 程序集缺失或加载失败时静默降级，由调用方决定是否抛出异常或回退到 Server 模式。
    /// 采用 try-catch 而非 <see cref="AppDomain.AssemblyResolve"/> 事件，避免全局副作用。
    /// </remarks>
    private static void TryLoadPlatformAssembly(string platform)
    {
        var assemblyName = platform switch
        {
            PlatformWindows => "Wails.Net.Application.Windows",
            PlatformLinux => "Wails.Net.Application.Linux",
            PlatformAndroid => "Wails.Net.Application.Android",
            _ => null
        };

        if (assemblyName is null)
        {
            return;
        }

        try
        {
            LogDebug($"[AutoLoad] 尝试加载平台程序集: {assemblyName}");
            var assembly = Assembly.Load(assemblyName);

            // .NET 的 [ModuleInitializer] 是 lazy 的：仅 Assembly.Load 不保证立即触发。
            // 显式调用 RuntimeHelpers.RunModuleConstructor 强制运行模块初始化器，
            // 确保平台注册器（LinuxPlatformRegistrar 等）完成委托注册。
            // 若模块初始化器已运行过，此调用为 no-op，安全无副作用。
            foreach (var module in assembly.GetModules())
            {
                LogDebug($"[AutoLoad] 触发模块初始化器: {module.Name}");
                RuntimeHelpers.RunModuleConstructor(module.ModuleHandle);
            }
        }
        catch (FileNotFoundException ex)
        {
            LogDebug($"[AutoLoad] 平台程序集未部署: {assemblyName}（{ex.Message}），将由 Server 模式降级处理");
        }
        catch (FileLoadException ex)
        {
            LogDebug($"[AutoLoad] 平台程序集加载失败: {assemblyName}（{ex.Message}），将由 Server 模式降级处理");
        }
        catch (BadImageFormatException ex)
        {
            LogDebug($"[AutoLoad] 平台程序集格式不兼容: {assemblyName}（{ex.Message}），将由 Server 模式降级处理");
        }
    }

    /// <summary>
    /// 当调试模式启用时，输出诊断信息到控制台。
    /// </summary>
    /// <param name="message">诊断信息。</param>
    private static void LogDebug(string message)
    {
        if (IsDebugEnabled())
        {
            Console.WriteLine($"[Wails.Net PlatformFactory] {message}");
        }
    }
}
