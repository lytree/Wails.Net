namespace Wails.Net.Application.Coordinators;

/// <summary>
/// 运行时协调器标记接口（审计 P1-1 落地）。
/// <para>
/// 用于收敛 <see cref="Application"/> 聚合根的运行时段职责，防止其继续膨胀为"上帝对象"：
/// 新增的运行时职责应优先进入独立的 coordinator，而不是直接堆到 <see cref="Application"/>。
/// 已落地的实现：
/// <list type="bullet">
/// <item><see cref="RuntimeServicesCoordinator"/>：注册服务生命周期顺序管理（IServiceStartup / IServiceShutdown）。</item>
/// </list>
/// 后续可扩展：
/// <list type="bullet">
/// <item>FrontendRuntimeCoordinator：runtime JS、AssetServer、IPC transport 绑定；</item>
/// <item>PlatformLifecycleCoordinator：IPlatformApp、窗口主循环、Server 模式生命周期。</item>
/// </list>
/// </para>
/// </summary>
public interface IRuntimeCoordinator
{
    /// <summary>协调器名称（用于日志与诊断）。</summary>
    string Name { get; }
}
