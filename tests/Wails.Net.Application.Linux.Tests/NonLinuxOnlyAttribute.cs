using TUnit.Core;

namespace Wails.Net.Application.Linux.Tests;

/// <summary>
/// 跳过在 Linux 平台运行的测试。
/// <para>
/// 被标记的测试验证 <see cref="LinuxPlatformApp"/> 在<b>非 Linux</b>平台上的降级行为
/// （如 SaveFileDialog 返回 null、DispatchOnMainThread 直接同步执行等）。
/// 这些断言依赖 <c>OperatingSystem.IsLinux()</c> 为 <c>false</c> 时的方法前置守卫。
/// </para>
/// <para>
/// 在 Linux CI runner（<c>ubuntu-latest</c>）上 <c>OperatingSystem.IsLinux()</c> 为 <c>true</c>，
/// 守卫不会生效，代码会进入真实的 GTK/GLib 原生调用路径；而 headless 环境无显示器且原生库
/// 可能未正确解析，会抛出 <see cref="DllNotFoundException"/> 或与断言语义冲突。因此此类测试
/// 仅在非 Linux 平台执行，在 Linux 上跳过。
/// </para>
/// </summary>
public sealed class NonLinuxOnlyAttribute : SkipAttribute
{
    /// <summary>
    /// 初始化跳过原因说明。
    /// </summary>
    public NonLinuxOnlyAttribute()
        : base("仅在非 Linux 平台运行：验证 LinuxPlatformApp 的跨平台降级行为（Linux 上由真实 GTK/GLib 路径覆盖）")
    {
    }

    /// <inheritdoc />
    public override Task<bool> ShouldSkip(TestRegisteredContext context)
    {
        return Task.FromResult(OperatingSystem.IsLinux());
    }
}
