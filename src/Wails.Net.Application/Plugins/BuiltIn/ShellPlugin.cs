using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Security;

namespace Wails.Net.Application.Plugins.BuiltIn;

/// <summary>
/// Shell 插件，允许前端执行 shell 命令或用系统默认程序打开文件/URL。
/// 对应 Tauri v2 的 <c>@tauri-apps/api/shell</c>。
/// 内置允许列表机制，仅允许执行白名单中的命令。
/// </summary>
public class ShellPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "shell";

    private readonly HashSet<string> _allowedCommands = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 初始化 Shell 插件实例。
    /// 默认不限制可执行的命令。
    /// 警告：不限制时前端可执行任意命令，仅适用于受信任的应用。
    /// </summary>
    public ShellPlugin()
    {
    }

    /// <summary>
    /// 初始化带命令白名单的 Shell 插件实例。
    /// 仅允许执行白名单中列出的命令。
    /// </summary>
    /// <param name="allowedCommands">允许执行的命令名称列表。</param>
    public ShellPlugin(params string[] allowedCommands)
    {
        foreach (var cmd in allowedCommands)
        {
            _allowedCommands.Add(cmd);
        }
    }

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        // 无需注册额外服务
    }

    /// <summary>
    /// 配置插件，注册 Shell 相关命令。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        // 声明权限集
        context.Permissions.RegisterPermissionSet("shell:default", "Shell 默认权限集",
            "shell:allow-execute", "shell:allow-open");
        context.Permissions.DeclarePermission("shell:allow-execute", "允许执行命令行程序");
        context.Permissions.DeclarePermission("shell:allow-open", "允许使用系统默认程序打开文件或URL");

        // 使用带 [ScopeParameter] 特性的方法组注册
        // 对应 Tauri v2 的 shell scope：限定可执行的命令和可打开的路径
        context.Commands.MapCommand("shell.execute", (Func<string, string?, ShellResult>)ExecuteScoped);
        context.Commands.MapCommand("shell.executeAsync", (Func<string, string?, Task<ShellResult>>)ExecuteScopedAsync);
        context.Commands.MapCommand("shell.open", (Action<string>)OpenPath);
        context.Commands.MapCommand("shell.openUrl", (Action<string>)OpenUrl);
    }

    /// <summary>
    /// 同步执行 shell 命令（带 Scope 校验包装）。
    /// </summary>
    public ShellResult ExecuteScoped(
        [ScopeParameter("shell:allow-execute")] string command,
        string? args) => Execute(command, args);

    /// <summary>
    /// 异步执行 shell 命令（带 Scope 校验包装）。
    /// </summary>
    public Task<ShellResult> ExecuteScopedAsync(
        [ScopeParameter("shell:allow-execute")] string command,
        string? args) => ExecuteAsync(command, args);

    /// <summary>
    /// 使用系统默认程序打开文件（带 Scope 校验包装）。
    /// </summary>
    public void OpenPath([ScopeParameter("shell:allow-open")] string path) => Open(path);

    /// <summary>
    /// 使用系统默认程序打开 URL（带 Scope 校验包装）。
    /// </summary>
    public void OpenUrl([ScopeParameter("shell:allow-open")] string url) => Open(url);

    /// <summary>
    /// 同步执行 shell 命令。
    /// </summary>
    /// <param name="command">命令名称。</param>
    /// <param name="args">命令参数，可为 null。</param>
    /// <returns>执行结果。</returns>
    public ShellResult Execute(string command, string? args = null)
    {
        if (!IsAllowed(command))
        {
            return new ShellResult
            {
                ExitCode = -1,
                Stderr = $"命令 '{command}' 不在允许列表中"
            };
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                Arguments = args ?? string.Empty,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return new ShellResult { ExitCode = -1, Stderr = "无法启动进程" };
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            return new ShellResult
            {
                ExitCode = process.ExitCode,
                Stdout = stdout,
                Stderr = stderr
            };
        }
        catch (Exception ex)
        {
            return new ShellResult
            {
                ExitCode = -1,
                Stderr = ex.Message
            };
        }
    }

    /// <summary>
    /// 异步执行 shell 命令。
    /// </summary>
    /// <param name="command">命令名称。</param>
    /// <param name="args">命令参数，可为 null。</param>
    /// <returns>执行结果。</returns>
    public async Task<ShellResult> ExecuteAsync(string command, string? args = null)
    {
        if (!IsAllowed(command))
        {
            return new ShellResult
            {
                ExitCode = -1,
                Stderr = $"命令 '{command}' 不在允许列表中"
            };
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                Arguments = args ?? string.Empty,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return new ShellResult { ExitCode = -1, Stderr = "无法启动进程" };
            }

            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            return new ShellResult
            {
                ExitCode = process.ExitCode,
                Stdout = stdout,
                Stderr = stderr
            };
        }
        catch (Exception ex)
        {
            return new ShellResult
            {
                ExitCode = -1,
                Stderr = ex.Message
            };
        }
    }

    /// <summary>
    /// 使用系统默认程序打开文件或 URL。
    /// 对应 Tauri v2 的 <c>shell.open</c>。
    /// </summary>
    /// <param name="path">文件路径或 URL。</param>
    public void Open(string path)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    /// <summary>
    /// 检查命令是否在允许列表中。
    /// </summary>
    private bool IsAllowed(string command)
    {
        // 无白名单时允许所有命令。
        if (_allowedCommands.Count == 0)
        {
            return true;
        }

        return _allowedCommands.Contains(command);
    }
}

/// <summary>
/// Shell 命令执行结果。
/// </summary>
public sealed class ShellResult
{
    /// <summary>退出码，0 表示成功</summary>
    public int ExitCode { get; set; }

    /// <summary>标准输出</summary>
    public string Stdout { get; set; } = string.Empty;

    /// <summary>标准错误输出</summary>
    public string Stderr { get; set; } = string.Empty;
}
