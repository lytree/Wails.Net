using System.Reflection;
using System.Runtime.InteropServices;
using Wails.Net.Application.Clipboard;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform.ServerMode;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Platform;

/// <summary>
/// 平台工厂，根据运行时操作系统创建平台特定的实现。
/// </summary>
public static class PlatformFactory
{
    /// <summary>
    /// 用于启用 Server（无界面）模式的环境变量名称。
    /// </summary>
    private const string ServerModeEnvVar = "WAILS_SERVER_MODE";

    /// <summary>
    /// 创建平台特定的应用实例。
    /// </summary>
    /// <param name="options">应用配置选项。</param>
    /// <returns>平台应用实例。</returns>
    /// <exception cref="PlatformNotSupportedException">当平台尚未实现或不支持时抛出。</exception>
    public static IPlatformApp CreatePlatformApp(ApplicationOptions options)
    {
        if (IsServerMode())
        {
            return new ServerPlatformApp(options);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // 通过反射加载 Windows 平台实现，避免核心项目直接依赖 Windows 平台程序集
            var assembly = Assembly.Load("Wails.Net.Application.Windows");
            var type = assembly.GetType("Wails.Net.Application.Platform.WindowsPlatformApp")
                ?? throw new PlatformNotSupportedException("无法找到 WindowsPlatformApp 类型");
            return (IPlatformApp)Activator.CreateInstance(type, options)!;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // 通过反射加载 Linux 平台实现，避免核心项目直接依赖 Linux 平台程序集
            var assembly = Assembly.Load("Wails.Net.Application.Linux");
            var type = assembly.GetType("Wails.Net.Application.Platform.LinuxPlatformApp")
                ?? throw new PlatformNotSupportedException("无法找到 LinuxPlatformApp 类型");
            return (IPlatformApp)Activator.CreateInstance(type, options)!;
        }

        throw new PlatformNotSupportedException($"不支持的平台: {RuntimeInformation.OSDescription}");
    }

    /// <summary>
    /// 创建平台特定的 Webview 窗口实现。
    /// </summary>
    /// <param name="id">窗口 ID。</param>
    /// <param name="options">窗口配置选项。</param>
    /// <returns>Webview 窗口实现实例。</returns>
    /// <exception cref="PlatformNotSupportedException">当平台尚未实现或不支持时抛出。</exception>
    public static IWebviewWindowImpl CreateWebviewWindowImpl(uint id, WebviewWindowOptions options)
    {
        if (IsServerMode())
        {
            return new ServerWebviewWindow();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // TODO: 将在后续实现完整的 WebView2 集成
            throw new NotImplementedException("WebView2 窗口创建将在后续实现");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // TODO: 将在后续实现完整的 WebKitGTK 集成
            throw new NotImplementedException("WebKitGTK 窗口创建将在后续实现");
        }

        throw new PlatformNotSupportedException($"不支持的平台: {RuntimeInformation.OSDescription}");
    }

    /// <summary>
    /// 创建平台特定的剪贴板实现。
    /// </summary>
    /// <returns>剪贴板实现实例。</returns>
    /// <exception cref="PlatformNotSupportedException">当平台尚未实现或不支持时抛出。</exception>
    public static IClipboardImpl CreateClipboard()
    {
        if (IsServerMode())
        {
            return new ServerClipboard();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // 通过反射加载 Windows 平台剪贴板实现，避免核心项目直接依赖 Windows 平台程序集
            var assembly = Assembly.Load("Wails.Net.Application.Windows");
            var type = assembly.GetType("Wails.Net.Application.Clipboard.WindowsClipboard")
                ?? throw new PlatformNotSupportedException("无法找到 WindowsClipboard 类型");
            return (IClipboardImpl)Activator.CreateInstance(type)!;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // 通过反射加载 Linux 平台剪贴板实现，避免核心项目直接依赖 Linux 平台程序集
            var assembly = Assembly.Load("Wails.Net.Application.Linux");
            var type = assembly.GetType("Wails.Net.Application.Clipboard.LinuxClipboard")
                ?? throw new PlatformNotSupportedException("无法找到 LinuxClipboard 类型");
            return (IClipboardImpl)Activator.CreateInstance(type)!;
        }

        throw new PlatformNotSupportedException($"不支持的平台: {RuntimeInformation.OSDescription}");
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
}
