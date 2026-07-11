using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Security;

namespace Wails.Net.Application.Plugins.BuiltIn;

/// <summary>
/// Opener 安全打开插件，提供受控的 URL/文件/程序打开能力。
/// 对应 Tauri v2 的 <c>@tauri-apps/plugin-opener</c>。
/// 通过协议白名单和 URL 模式白名单防止 <c>file://</c>、<c>javascript:</c> 等危险协议。
/// 复用 <see cref="UrlWhitelist"/> 进行通配符模式匹配。
/// </summary>
public class OpenerPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "opener";

    /// <summary>
    /// 默认允许的协议白名单：http、https、mailto。
    /// </summary>
    private static readonly HashSet<string> DefaultAllowedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http",
        "https",
        "mailto",
    };

    /// <summary>
    /// 协议白名单，默认包含 http/https/mailto。
    /// </summary>
    private readonly HashSet<string> _allowedSchemes;

    /// <summary>
    /// URL 模式白名单，空集合表示允许所有匹配协议白名单的 URL。
    /// </summary>
    private readonly UrlWhitelist _urlWhitelist = new();

    /// <summary>
    /// 允许打开的程序白名单（用于 target 参数），空集合表示不限制。
    /// </summary>
    private readonly HashSet<string> _allowedPrograms = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 初始化 Opener 插件实例，使用默认协议白名单。
    /// </summary>
    public OpenerPlugin()
    {
        _allowedSchemes = new HashSet<string>(DefaultAllowedSchemes, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 初始化 Opener 插件实例，指定自定义协议白名单。
    /// </summary>
    /// <param name="allowedSchemes">允许的协议列表（如 http、https、mailto、custom）。</param>
    public OpenerPlugin(params string[] allowedSchemes)
    {
        _allowedSchemes = allowedSchemes.Length > 0
            ? new HashSet<string>(allowedSchemes, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(DefaultAllowedSchemes, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 添加允许的 URL 模式（支持通配符，如 https://*.example.com）。
    /// </summary>
    /// <param name="pattern">URL 模式。</param>
    public void AddAllowedUrlPattern(string pattern)
    {
        _urlWhitelist.Add(pattern);
    }

    /// <summary>
    /// 添加允许的程序（用于 target 参数）。
    /// </summary>
    /// <param name="program">程序名称或路径。</param>
    public void AddAllowedProgram(string program)
    {
        ArgumentException.ThrowIfNullOrEmpty(program);
        _allowedPrograms.Add(program);
    }

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。此插件无需注册额外服务。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        // 无需注册额外服务
    }

    /// <summary>
    /// 配置插件，注册 Opener 相关命令。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        context.Commands.MapCommand("opener.openUrl", (Func<string, string?, bool>)((url, target) =>
            OpenUrl(url, target)));

        context.Commands.MapCommand("opener.openPath", (Func<string, string?, bool>)((path, target) =>
            OpenPath(path, target)));

        context.Commands.MapCommand("opener.revealInFolder", (Action<string>)(path =>
            RevealInFolder(path)));

        context.Commands.MapCommand("opener.isUrlAllowed", (Func<string, bool>)(url =>
            IsUrlAllowed(url)));

        context.Commands.MapCommand("opener.verifyUrl", (Func<string, string?>)(url =>
            VerifyUrl(url)));
    }

    /// <summary>
    /// 使用系统默认浏览器打开 URL，支持指定程序打开。
    /// </summary>
    /// <param name="url">要打开的 URL。</param>
    /// <param name="target">可选，指定使用的程序（如 chrome、firefox）。</param>
    /// <returns>是否成功打开。</returns>
    public bool OpenUrl(string url, string? target = null)
    {
        if (!IsUrlAllowed(url))
        {
            return false;
        }

        return OpenInternal(url, target);
    }

    /// <summary>
    /// 使用系统默认程序打开文件路径，支持指定程序打开。
    /// </summary>
    /// <param name="path">文件路径。</param>
    /// <param name="target">可选，指定使用的程序。</param>
    /// <returns>是否成功打开。</returns>
    public bool OpenPath(string path, string? target = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return OpenInternal(path, target);
    }

    /// <summary>
    /// 在文件管理器中显示指定文件。
    /// Windows 使用 explorer.exe /select，Linux 使用 xdg-open。
    /// </summary>
    /// <param name="path">文件路径。</param>
    public void RevealInFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);

            if (OperatingSystem.IsWindows())
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{fullPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                Process.Start(psi);
            }
            else
            {
                var dir = Path.GetDirectoryName(fullPath) ?? fullPath;
                OpenInternal(dir, null);
            }
        }
        catch
        {
            // 忽略打开失败
        }
    }

    /// <summary>
    /// 检查 URL 是否被允许打开（协议白名单 + URL 模式白名单）。
    /// </summary>
    /// <param name="url">要检查的 URL。</param>
    /// <returns>允许返回 true。</returns>
    public bool IsUrlAllowed(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!_allowedSchemes.Contains(uri.Scheme))
        {
            return false;
        }

        if (_urlWhitelist.Patterns.Count > 0)
        {
            return _urlWhitelist.IsAllowed(url);
        }

        return true;
    }

    /// <summary>
    /// 验证 URL 并返回可读的错误信息，无错误时返回 null。
    /// </summary>
    /// <param name="url">要验证的 URL。</param>
    /// <returns>错误信息，无错误返回 null。</returns>
    public string? VerifyUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "URL 不能为空";
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return "URL 格式无效";
        }

        if (!_allowedSchemes.Contains(uri.Scheme))
        {
            return $"协议 '{uri.Scheme}' 不在允许列表中";
        }

        if (_urlWhitelist.Patterns.Count > 0 && !_urlWhitelist.IsAllowed(url))
        {
            return $"URL 不匹配任何允许的模式";
        }

        return null;
    }

    /// <summary>
    /// 内部打开实现，处理 target 参数和异常。
    /// </summary>
    /// <param name="path">文件路径或 URL。</param>
    /// <param name="target">指定程序，可为 null。</param>
    /// <returns>是否成功打开。</returns>
    private bool OpenInternal(string path, string? target)
    {
        if (target is not null)
        {
            if (_allowedPrograms.Count > 0 && !_allowedPrograms.Contains(target))
            {
                return false;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = target,
                    Arguments = $"\"{path}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                Process.Start(psi);
                return true;
            }
            catch
            {
                return false;
            }
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            };
            Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
