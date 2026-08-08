using System.Runtime.InteropServices;
using Wails.Net.Application.Managers;
using Wails.Net.Application.SystemEnvironment;

namespace Wails.Net.Application.Managers;

/// <summary>
/// macOS 环境信息管理器实现。
/// 对应 Wails v3 Go 版本 environment_manager_darwin.go，
/// 通过 NSUserDefaults / NSColor 查询系统主题与强调色。
/// </summary>
public sealed class MacOSEnvironmentManager : IEnvironmentManager
{
    /// <summary>
    /// 应用名称，用作数据目录名。
    /// </summary>
    private readonly string _appName;

    /// <summary>
    /// 构造 MacOSEnvironmentManager 实例。
    /// </summary>
    /// <param name="appName">应用名称。</param>
    public MacOSEnvironmentManager(string appName)
    {
        _appName = appName;
    }

    /// <inheritdoc />
    public string GetOS() => "macos";

    /// <inheritdoc />
    public string GetArch()
    {
        return RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "amd64",
            Architecture.Arm => "arm",
            Architecture.X86 => "386",
            _ => RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
        };
    }

    /// <inheritdoc />
    public string GetHomeDir()
        => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <inheritdoc />
    public string GetDataDir()
        => Path.Combine(GetHomeDir(), "Library", "Application Support", _appName);

    /// <inheritdoc />
    public EnvironmentInfo Info() => new() { OS = GetOS(), Arch = GetArch() };

    /// <inheritdoc />
    public bool IsDarkMode()
    {
#if MACOS
        try
        {
            // 参照 DevToys ThemeListener：EffectiveAppearance.FindBestMatch 判定明暗。
            var appearance = AppKit.NSApplication.SharedApplication.EffectiveAppearance;
            var bestMatch = appearance.FindBestMatch(new[] { AppKit.NSAppearance.NameAqua, AppKit.NSAppearance.NameDarkAqua });
            return bestMatch == AppKit.NSAppearance.NameDarkAqua;
        }
        catch
        {
            return false;
        }
#else
        return false;
#endif
    }

    /// <inheritdoc />
    public string GetAccentColor()
    {
#if MACOS
        try
        {
            var accent = AppKit.NSColor.ControlAccentColor;
            using var rgb = accent.ColorUsingColorSpace(AppKit.NSColorSpace.SRGBColorSpace) ?? accent;
            rgb.GetRgba(out var red, out var green, out var blue, out _);
            return $"rgb({(int)(red * 255)},{(int)(green * 255)},{(int)(blue * 255)})";
        }
        catch
        {
            return "rgb(0,122,255)"; // 系统蓝回退，对应 IEnvironmentManager 默认。
        }
#else
        return "rgb(0,122,255)";
#endif
    }

    /// <inheritdoc />
    public void OpenFileManager(string path, bool selectFile)
    {
#if MACOS
        try
        {
            using var url = Foundation.NSUrl.FromFilename(path);
            if (url is null)
            {
                return;
            }

            var workspace = AppKit.NSWorkspace.SharedWorkspace;
            if (selectFile && Directory.Exists(path))
            {
                // 目录：直接打开。
                workspace.OpenUrl(url);
            }
            else if (selectFile && File.Exists(path))
            {
                // 文件：在 Finder 中选中。
                workspace.ActivateFileViewerSelecting(new[] { url });
            }
            else
            {
                workspace.OpenUrl(url);
            }
        }
        catch
        {
            // 打开失败时静默忽略。
        }
#else
        // 非 macOS 目标：no-op 骨架。
#endif
    }

    /// <inheritdoc />
    public bool HasFocusFollowsMouse() => false;
}
