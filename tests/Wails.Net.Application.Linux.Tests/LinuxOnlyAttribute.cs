using TUnit.Core;

namespace Wails.Net.Application.Linux.Tests;

/// <summary>
/// 跳过在非 Linux 平台运行的测试。
/// <para>
/// 被标记的测试验证 <see cref="LinuxPlatformApp"/> 由 <see cref="PlatformFactory"/> 真实创建
/// （平台注册依赖目标平台程序集的 <c>[ModuleInitializer]</c>）。
/// 在 Windows/macOS 主机上 <see cref="PlatformFactory"/> 会解析为宿主平台并尝试加载对应程序集，
/// 而本测试项目仅引用 Linux 平台程序集，必然失败；这些断言只应在 Linux 上执行。
/// </para>
/// <para>
/// 在 Linux 上保留真实断言（强类型检查具体实现），不改为恒通过。
/// </para>
/// </summary>
public sealed class LinuxOnlyAttribute : SkipAttribute
{
    /// <summary>
    /// 初始化跳过原因说明。
    /// </summary>
    public LinuxOnlyAttribute()
        : base("仅在 Linux 平台运行：依赖 Linux 平台程序集注册的实际实现")
    {
    }

    /// <inheritdoc />
    public override Task<bool> ShouldSkip(TestRegisteredContext context)
    {
        return Task.FromResult(!OperatingSystem.IsLinux());
    }
}
