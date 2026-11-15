using Microsoft.Extensions.Logging;

namespace Wails.Net.Application.Hosting;

/// <summary>
/// 默认信号处理器协调器（P2-21，RH-HOST-02 抽出）。
/// <para>
/// 对应 Wails v3 Go 版本 <c>App.setupSignalHandler</c>：在应用已就绪（服务/窗口已起）之后
/// 安装 SIGINT / SIGTERM 处理器；首个信号经 <c>ShouldQuit</c> 判定后退出，重复信号逐级提示，
/// 达到上限强制退出。
/// </para>
/// </summary>
public sealed class SignalHandlerCoordinator
{
    /// <summary>
    /// 当前安装的信号处理器；未安装或已释放时为 null。
    /// </summary>
    public DefaultSignalHandler? Current { get; private set; }

    /// <summary>
    /// 安装信号处理器。
    /// </summary>
    /// <param name="options">应用选项；<see cref="Options.ApplicationOptions.DisableDefaultSignalHandler"/> 为 true 时跳过。</param>
    /// <param name="shouldQuit">退出许可判定（通常为 <c>Application.ShouldQuit</c>）。</param>
    /// <param name="quit">实际退出动作（通常为 <c>Application.Quit</c>）。</param>
    /// <param name="logger">日志记录器，可为 null。</param>
    /// <returns>已安装的处理器；被禁用或已存在时为 null。</returns>
    public DefaultSignalHandler? Setup(
        Options.ApplicationOptions options,
        Func<bool> shouldQuit,
        Action quit,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(shouldQuit);
        ArgumentNullException.ThrowIfNull(quit);

        if (options.DisableDefaultSignalHandler)
        {
            return null;
        }

        Current = new DefaultSignalHandler(
            cleanup: () =>
            {
                if (shouldQuit())
                {
                    quit();
                }
                else
                {
                    logger?.LogInformation("收到退出信号，但 ShouldQuit 返回 false，应用继续运行。");
                }
            },
            logger: logger);
        Current.Start();
        return Current;
    }

    /// <summary>
    /// 释放信号处理器，恢复 SIGINT / SIGTERM 的操作系统默认处置。幂等。
    /// </summary>
    public void Dispose()
    {
        Current?.Dispose();
        Current = null;
    }
}
