namespace Wails.Net.Application;

/// <summary>
/// 调试模式判定工具，统一 Debug / Release 运行模式的检测逻辑。
/// </summary>
/// <remarks>
/// <para>
/// 优先级（由高到低）：
/// <list type="number">
/// <item><description><c>WAILS_DEBUG</c> 环境变量（<c>wails dev</c> 命令自动设置为 <c>true</c>）；</description></item>
/// <item><description><c>--debug</c> / <c>-d</c> 命令行参数；</description></item>
/// <item><description><c>DOTNET_ENVIRONMENT</c> / <c>ASPNETCORE_ENVIRONMENT</c> 为 <c>Development</c>。</description></item>
/// </list>
/// </para>
/// <para>
/// Demo 与模板项目统一通过 <see cref="IsEnabled"/> 判定当前模式，
/// 避免每个项目各自复制一份模式检测代码。
/// </para>
/// </remarks>
public static class DebugMode
{
    /// <summary>
    /// 调试模式环境变量名称。
    /// 设置为 <c>true</c>（不区分大小写）时表示当前处于 Debug 模式。
    /// </summary>
    public const string DebugEnvVar = "WAILS_DEBUG";

    /// <summary>
    /// 综合判定当前是否处于 Debug 模式。
    /// </summary>
    /// <param name="args">命令行参数（可空）。</param>
    /// <returns>
    /// 判定优先级：<see cref="DebugEnvVar"/> 环境变量（可解析时优先）&gt;
    /// <c>--debug</c>/<c>-d</c> 命令行参数 &gt;
    /// <c>DOTNET_ENVIRONMENT</c>/<c>ASPNETCORE_ENVIRONMENT</c> 为 <c>Development</c>；
    /// 全部未命中时返回 <see langword="false"/>。
    /// </returns>
    public static bool IsEnabled(string[]? args = null)
    {
        // 1. WAILS_DEBUG 环境变量（wails dev 自动设置）
        var envDebug = Environment.GetEnvironmentVariable(DebugEnvVar);
        if (!string.IsNullOrEmpty(envDebug) &&
            bool.TryParse(envDebug, out var envResult))
        {
            return envResult;
        }

        // 2. --debug 命令行参数
        if (args?.Any(static a => a is "--debug" or "-d") == true)
        {
            return true;
        }

        // 3. ASP.NET Core / .NET 通用环境变量
        var dotnetEnv = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                        ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if (string.Equals(dotnetEnv, "Development", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 仅按 <see cref="DebugEnvVar"/> 环境变量判定当前是否为 Debug 模式。
    /// </summary>
    /// <returns>如果 <see cref="DebugEnvVar"/> 为 <c>true</c>（不区分大小写）则返回 <see langword="true"/>。</returns>
    /// <remarks>
    /// 与 <c>PlatformFactory.IsDebugEnabled</c> 的原始语义保持一致：
    /// 只认值为 <c>true</c>，不解析命令行参数与其他环境变量。
    /// </remarks>
    public static bool IsEnvironmentEnabled()
    {
        var value = Environment.GetEnvironmentVariable(DebugEnvVar);
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }
}
