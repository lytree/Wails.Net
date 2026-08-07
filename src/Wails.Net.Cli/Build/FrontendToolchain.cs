using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Wails.Net.Cli.Build;

/// <summary>
/// 前端工具链抽象：检测包管理器（pnpm 优先，npm 回退）与 monorepo 工作区根，
/// 并提供 install / build / dev 的进程执行封装（流式输出到控制台）。
/// <para>
/// 约定（遵循「前端使用 vite + pnpm 管理」与「使用 CLI 进行构建和打包」）：
/// - 默认使用 pnpm；仅当 pnpm 不可用时回退到 npm。
/// - 在 monorepo（仓库根存在 pnpm-workspace.yaml）中，install 在工作区根执行一次，
///   使 packages/* 与 examples/*/frontend 共享 node_modules；build/dev 仍在各前端目录执行。
/// </para>
/// </summary>
public sealed class FrontendToolchain
{
    /// <summary>检测到的包管理器：<c>pnpm</c> 或 <c>npm</c>。</summary>
    public string PackageManager { get; }

    private FrontendToolchain(string packageManager)
    {
        PackageManager = packageManager;
    }

    /// <summary>
    /// 检测系统可用的包管理器。优先 pnpm，缺失时回退 npm。两者皆不可用时抛出异常。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>封装检测结果的前端工具链实例。</returns>
    public static async Task<FrontendToolchain> DetectAsync(CancellationToken cancellationToken = default)
    {
        if (await IsAvailableAsync("pnpm", cancellationToken))
        {
            return new FrontendToolchain("pnpm");
        }

        if (await IsAvailableAsync("npm", cancellationToken))
        {
            return new FrontendToolchain("npm");
        }

        throw new InvalidOperationException(
            "未找到 pnpm 或 npm。请先安装 Node.js 与 pnpm（npm i -g pnpm），或在 wails.json 中显式配置 frontend.installCommand / frontend.buildCommand。");
    }

    /// <summary>
    /// 从指定前端目录向上查找 monorepo 工作区根。
    /// 命中条件：目录中存在 <c>pnpm-workspace.yaml</c>，或存在 <c>package.json</c> 且包含
    /// <c>workspaces</c> / <c>packageManager</c> 字段（pnpm 工作区特征）。
    /// 未命中时返回原前端目录（非 monorepo 项目）。
    /// </summary>
    /// <param name="frontendDir">前端项目目录。</param>
    /// <returns>工作区根目录（绝对路径）。</returns>
    public static string FindWorkspaceRoot(string frontendDir)
    {
        var dir = Path.GetFullPath(frontendDir);
        while (true)
        {
            if (File.Exists(Path.Combine(dir, "pnpm-workspace.yaml")))
            {
                return dir;
            }

            var pkgJson = Path.Combine(dir, "package.json");
            if (File.Exists(pkgJson) && IsWorkspacePackageJson(pkgJson))
            {
                return dir;
            }

            var parent = Path.GetDirectoryName(dir);
            if (parent is null || parent == dir)
            {
                return Path.GetFullPath(frontendDir);
            }

            dir = parent;
        }
    }

    /// <summary>
    /// 执行依赖安装。非 monorepo 直接在 <paramref name="frontendDir"/> 执行；
    /// monorepo 在工作区根执行一次。
    /// </summary>
    /// <param name="frontendDir">前端项目目录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>进程退出码（0 表示成功）。</returns>
    public async Task<int> InstallAsync(string frontendDir, CancellationToken cancellationToken = default)
    {
        var workspaceRoot = FindWorkspaceRoot(frontendDir);
        var installDir = workspaceRoot == Path.GetFullPath(frontendDir) ? frontendDir : workspaceRoot;
        return await RunAsync($"{PackageManager} install", installDir, cancellationToken);
    }

    /// <summary>
    /// 执行前端构建（在 <paramref name="frontendDir"/> 目录执行 <c>&lt;pm&gt; build</c>）。
    /// </summary>
    /// <param name="frontendDir">前端项目目录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>进程退出码（0 表示成功）。</returns>
    public async Task<int> BuildAsync(string frontendDir, CancellationToken cancellationToken = default)
    {
        return await RunAsync($"{PackageManager} build", frontendDir, cancellationToken);
    }

    /// <summary>
    /// 启动前端开发服务器（vite dev），用于 dev 命令与 dotnet watch 并行。
    /// </summary>
    /// <param name="frontendDir">前端项目目录。</param>
    /// <param name="cancellationToken">取消令牌（触发时终止开发服务器）。</param>
    /// <returns>进程退出码。</returns>
    public async Task<int> DevAsync(string frontendDir, CancellationToken cancellationToken = default)
    {
        return await RunAsync($"{PackageManager} dev", frontendDir, cancellationToken, forwardSignals: true);
    }

    /// <summary>
    /// 在指定目录执行 shell 命令，流式转发标准输出 / 错误到控制台。
    /// </summary>
    /// <param name="command">完整命令字符串。</param>
    /// <param name="workingDirectory">工作目录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="forwardSignals">是否向前台进程转发 Ctrl+C（用于 dev server）。</param>
    /// <returns>进程退出码。</returns>
    private async Task<int> RunAsync(
        string command,
        string workingDirectory,
        CancellationToken cancellationToken,
        bool forwardSignals = false)
    {
        var (fileName, args) = BuildShellCommand(command);
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
            EnableRaisingEvents = true,
        };

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) Console.WriteLine(e.Data);
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) Console.Error.WriteLine(e.Data);
        };

        if (!proc.Start())
        {
            throw new InvalidOperationException($"无法启动前端命令：{command}");
        }

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        // 取消令牌触发时强制终止子进程，避免缺陷进程残留（dev server 路径关键）。
        using var cancelReg = cancellationToken.Register(() =>
        {
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                /* 进程可能已退出 */
            }
        });

        using var reg = forwardSignals ? ConsoleCancelHandler.Attach(proc) : null;

        try
        {
            await proc.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                /* 进程可能已退出 */
            }

            throw;
        }

        return proc.ExitCode;
    }

    private static (string FileName, string Arguments) BuildShellCommand(string command)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return ("cmd.exe", $"/c \"{command}\"");
        }

        return ("sh", $"-c \"{command}\"");
    }

    private static async Task<bool> IsAvailableAsync(string command, CancellationToken cancellationToken)
    {
        try
        {
            var (fileName, args) = BuildShellCommand($"{command} --version");
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };
            if (!proc.Start())
            {
                return false;
            }

            await proc.WaitForExitAsync(cancellationToken);
            return proc.ExitCode == 0;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsWorkspacePackageJson(string pkgJsonPath)
    {
        try
        {
            var text = File.ReadAllText(pkgJsonPath);
            return text.Contains("\"workspaces\"") || text.Contains("\"packageManager\"");
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 在 dev server 运行期间将 Ctrl+C 转发给子进程，避免孤立进程残留。
    /// </summary>
    private sealed class ConsoleCancelHandler : IDisposable
    {
        private readonly Process _child;
        private readonly ConsoleCancelEventHandler _handler;

        private ConsoleCancelHandler(Process child)
        {
            _child = child;
            _handler = (_, e) =>
            {
                e.Cancel = false;
                try
                {
                    if (!_child.HasExited)
                    {
                        _child.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    /* ignore */
                }
            };
            Console.CancelKeyPress += _handler;
        }

        public static ConsoleCancelHandler Attach(Process child) => new(child);

        public void Dispose()
        {
            Console.CancelKeyPress -= _handler;
        }
    }
}
