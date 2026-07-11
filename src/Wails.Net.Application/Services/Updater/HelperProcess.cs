using System.Diagnostics;

namespace Wails.Net.Application.Services.Updater;

/// <summary>
/// Helper 进程管理器，负责在更新安装时执行文件替换和进程重启。
/// 对应 Wails v3 Go 版本 helper.go 中的 HandleHelperMode 及相关函数。
/// <para>
/// Helper 模式协议：为避免发布单独的 helper 二进制文件，Updater 通过设置哨兵环境变量
/// 重新执行当前应用程序。helper 进程等待父进程（发起更新的应用）退出后，
/// 用下载的文件替换磁盘上的二进制，然后重新启动应用。
/// </para>
/// </summary>
public static class HelperProcess
{
    /// <summary>
    /// Helper 模式哨兵环境变量名，设为 "1" 时进入 helper 模式。
    /// </summary>
    private const string EnvHelperMode = "WAILS_UPDATER_HELPER";

    /// <summary>
    /// 更新包路径环境变量名。
    /// </summary>
    private const string EnvHelperArchive = "WAILS_UPDATER_ARCHIVE";

    /// <summary>
    /// 目标文件路径环境变量名（要被替换的当前可执行文件）。
    /// </summary>
    private const string EnvHelperTarget = "WAILS_UPDATER_TARGET";

    /// <summary>
    /// 主进程 PID 环境变量名，helper 等待该进程退出后再执行替换。
    /// </summary>
    private const string EnvHelperPid = "WAILS_UPDATER_PID";

    /// <summary>
    /// 文件替换最大重试次数。
    /// </summary>
    private const int MaxRetries = 20;

    /// <summary>
    /// 每次重试之间的延迟（毫秒）。
    /// </summary>
    private const int RetryDelayMs = 500;

    /// <summary>
    /// 等待父进程退出的超时时间。
    /// </summary>
    private static readonly TimeSpan ParentExitTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 检查并处理 Helper 模式。
    /// 如果当前进程不是以 helper 模式启动，则立即返回。
    /// 如果是 helper 模式，则执行文件替换、重启应用并退出进程（永不返回）。
    /// 应在应用启动时（如 ServiceStartup）调用此方法。
    /// </summary>
    public static void HandleHelperMode()
    {
        if (Environment.GetEnvironmentVariable(EnvHelperMode) != "1")
        {
            return;
        }

        var archive = Environment.GetEnvironmentVariable(EnvHelperArchive) ?? string.Empty;
        var target = Environment.GetEnvironmentVariable(EnvHelperTarget) ?? string.Empty;
        var pidStr = Environment.GetEnvironmentVariable(EnvHelperPid) ?? string.Empty;

        if (archive.Length == 0 || target.Length == 0)
        {
            Environment.Exit(2);
        }

        int pid = 0;
        if (pidStr.Length > 0 && int.TryParse(pidStr, out var parsedPid))
        {
            pid = parsedPid;
        }

        // 等待父进程退出，确保目标文件不再被锁定
        if (pid > 0 && !WaitForParentExit(pid, ParentExitTimeout))
        {
            Environment.Exit(17);
        }

        // 备份目标文件
        var backup = target + ".bak";
        try
        {
            if (File.Exists(backup))
            {
                File.Delete(backup);
            }
        }
        catch (IOException) { /* 忽略备份清理错误 */ }

        if (File.Exists(target))
        {
            try
            {
                File.Copy(target, backup, overwrite: true);
            }
            catch (IOException)
            {
                Environment.Exit(12);
            }
        }

        // 重试替换目标文件（OS 可能短暂持有文件锁）
        var swapped = false;
        for (var i = 0; i < MaxRetries; i++)
        {
            try
            {
                File.Copy(archive, target, overwrite: true);
                swapped = true;
                break;
            }
            catch (IOException)
            {
                Thread.Sleep(RetryDelayMs);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(RetryDelayMs);
            }
        }

        if (!swapped)
        {
            // 替换失败，尝试从备份恢复
            RestoreFromBackup(backup, target);
            Environment.Exit(13);
        }

        // Linux 上恢复可执行权限
        SetExecutablePermissions(target);

        // 清除 helper 环境变量，防止重启的进程再次进入 helper 模式
        ClearHelperEnv();

        // 重启应用
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi);
        }
        catch (Exception)
        {
            // 启动失败，尝试从备份恢复
            RestoreFromBackup(backup, target);
            Environment.Exit(15);
        }

        // 清理备份文件
        try
        {
            if (File.Exists(backup))
            {
                File.Delete(backup);
            }
        }
        catch (IOException) { /* 非致命 */ }

        // 清理暂存目录
        CleanupStagingDirectory(archive);

        Environment.Exit(0);
    }

    /// <summary>
    /// 以 helper 模式重新启动当前进程。
    /// 设置环境变量后启动自身作为 helper 进程，然后退出当前进程。
    /// </summary>
    /// <param name="archivePath">更新包文件路径。</param>
    /// <param name="targetPath">要替换的目标文件路径（当前可执行文件）。</param>
    /// <exception cref="InvalidOperationException">无法确定当前进程路径。</exception>
    public static void RelaunchAsHelper(string archivePath, string targetPath)
    {
        Environment.SetEnvironmentVariable(EnvHelperMode, "1");
        Environment.SetEnvironmentVariable(EnvHelperArchive, archivePath);
        Environment.SetEnvironmentVariable(EnvHelperTarget, targetPath);
        Environment.SetEnvironmentVariable(EnvHelperPid, Environment.ProcessId.ToString());

        var exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("无法确定当前进程可执行文件路径。");

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process.Start(psi);
        Environment.Exit(0);
    }

    /// <summary>
    /// 重启应用程序。启动新的进程后退出当前进程。
    /// </summary>
    /// <param name="executablePath">要启动的可执行文件路径。</param>
    /// <param name="args">传递给新进程的命令行参数。</param>
    public static void RelaunchApplication(string executablePath, string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (args is not null)
        {
            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }
        }

        Process.Start(psi);
        Environment.Exit(0);
    }

    /// <summary>
    /// 等待指定 PID 的进程退出。
    /// </summary>
    /// <param name="pid">要等待的进程 ID。</param>
    /// <param name="timeout">等待超时时间。</param>
    /// <returns>进程在超时前退出返回 true，否则返回 false。</returns>
    private static bool WaitForParentExit(int pid, TimeSpan timeout)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.WaitForExit((int)timeout.TotalMilliseconds);
        }
        catch (ArgumentException)
        {
            // 进程已退出
            return true;
        }
        catch (InvalidOperationException)
        {
            // 进程不可访问，视为已退出
            return true;
        }
    }

    /// <summary>
    /// 从备份恢复目标文件。
    /// </summary>
    /// <param name="backup">备份文件路径。</param>
    /// <param name="target">目标文件路径。</param>
    private static void RestoreFromBackup(string backup, string target)
    {
        try
        {
            if (File.Exists(backup))
            {
                File.Copy(backup, target, overwrite: true);
            }
        }
        catch (IOException) { /* 恢复失败，忽略 */ }
    }

    /// <summary>
    /// 在 Linux 上设置文件可执行权限（chmod +x）。
    /// </summary>
    /// <param name="path">文件路径。</param>
    private static void SetExecutablePermissions(string path)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"+x \"{path}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var proc = Process.Start(psi);
            proc?.WaitForExit();
            proc?.Dispose();
        }
        catch (Exception) { /* chmod 失败非致命 */ }
    }

    /// <summary>
    /// 清除所有 helper 模式环境变量。
    /// </summary>
    private static void ClearHelperEnv()
    {
        Environment.SetEnvironmentVariable(EnvHelperMode, null);
        Environment.SetEnvironmentVariable(EnvHelperArchive, null);
        Environment.SetEnvironmentVariable(EnvHelperTarget, null);
        Environment.SetEnvironmentVariable(EnvHelperPid, null);
    }

    /// <summary>
    /// 清理更新包所在的暂存目录。
    /// 仅删除以 "wails-update-" 为前缀的目录，避免误删用户提供的路径。
    /// </summary>
    /// <param name="archivePath">更新包文件路径。</param>
    private static void CleanupStagingDirectory(string archivePath)
    {
        try
        {
            var stagingDir = Path.GetDirectoryName(archivePath);
            if (stagingDir is not null &&
                Path.GetFileName(stagingDir).StartsWith("wails-update-", StringComparison.Ordinal))
            {
                Directory.Delete(stagingDir, recursive: true);
            }
        }
        catch (IOException) { /* 清理失败非致命 */ }
    }
}
