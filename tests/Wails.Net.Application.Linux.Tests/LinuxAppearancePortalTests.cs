using TUnit.Core;
using Wails.Net.Application.Platform;

namespace Wails.Net.Application.Linux.Tests;

/// <summary>
/// <see cref="LinuxAppearancePortal"/> 的单元测试（TUnit）。
/// 覆盖 portal color-scheme 数值映射；真实 D-Bus 读取需要 Linux 桌面会话，
/// 故此处仅验证映射逻辑与非 Linux 平台的安全回退。
/// </summary>
[NotInParallel]
public sealed class LinuxAppearancePortalTests
{
    [Test]
    public async Task MapColorScheme_PreferDark_ReturnsTrue()
    {
        await Assert.That(LinuxAppearancePortal.MapColorScheme(1)).IsTrue();
    }

    [Test]
    public async Task MapColorScheme_PreferLight_ReturnsFalse()
    {
        await Assert.That(LinuxAppearancePortal.MapColorScheme(2)).IsFalse();
    }

    [Test]
    [Arguments(0u)]
    [Arguments(3u)]
    [Arguments(uint.MaxValue)]
    public async Task MapColorScheme_NoPreferenceOrUnknown_ReturnsNull(uint value)
    {
        await Assert.That(LinuxAppearancePortal.MapColorScheme(value)).IsNull();
    }

    [Test]
    public async Task TryReadColorSchemeIsDark_OnNonLinux_ReturnsNull()
    {
        if (OperatingSystem.IsLinux())
        {
            // 真实 D-Bus 读取依赖桌面会话，单元测试不做断言（需手动验证）。
            return;
        }

        // 非 Linux 平台不做任何 D-Bus 调用，应静默返回 null 而非抛出异常。
        bool? result = LinuxAppearancePortal.TryReadColorSchemeIsDark();

        await Assert.That(result).IsNull();
    }
}
