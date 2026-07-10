using TUnit.Core;
using Wails.Net.Application.Managers;

namespace Wails.Net.Application.Windows.Tests;

/// <summary>
/// WindowsAutostartManager 的单元测试（TUnit）。
/// 通过注册表 HKCU\Software\Microsoft\Windows\CurrentVersion\Run 测试自启动管理。
/// 注意：测试共享注册表，因此不并行执行。每个测试使用唯一的 Guid 名称以避免冲突。
/// </summary>
[NotInParallel]
public sealed class WindowsAutostartManagerTests
{
    [Test]
    public async Task IsEnabled_ReturnsFalse_WhenNotConfigured()
    {
        // 安排：使用唯一名称创建管理器
        var name = "Wails.Net.Test.Autostart." + Guid.NewGuid();
        var manager = new WindowsAutostartManager(name);

        try
        {
            // 操作与断言：未配置时应返回 false
            await Assert.That(manager.IsEnabled()).IsFalse();
        }
        finally
        {
            // 清理注册表
            manager.Disable();
        }
    }

    [Test]
    public async Task Enable_SetsRegistryValue()
    {
        // 安排
        var name = "Wails.Net.Test.Autostart." + Guid.NewGuid();
        var manager = new WindowsAutostartManager(name);

        try
        {
            // 操作：启用自启动
            manager.Enable();

            // 断言：注册表值已设置
            await Assert.That(manager.IsEnabled()).IsTrue();
        }
        finally
        {
            // 清理注册表
            manager.Disable();
        }
    }

    [Test]
    public async Task Disable_RemovesRegistryValue()
    {
        // 安排
        var name = "Wails.Net.Test.Autostart." + Guid.NewGuid();
        var manager = new WindowsAutostartManager(name);

        try
        {
            // 操作：先启用再禁用
            manager.Enable();
            manager.Disable();

            // 断言：注册表值已移除
            await Assert.That(manager.IsEnabled()).IsFalse();
        }
        finally
        {
            // 确保清理
            manager.Disable();
        }
    }

    [Test]
    public async Task Disable_DoesNotThrow_WhenNotConfigured()
    {
        // 安排：使用唯一名称创建管理器，不先启用
        var name = "Wails.Net.Test.Autostart." + Guid.NewGuid();
        var manager = new WindowsAutostartManager(name);

        try
        {
            // 操作与断言：未配置时禁用不应抛出异常
            await Assert.That(() => manager.Disable()).ThrowsNothing();
        }
        finally
        {
            // 确保清理
            manager.Disable();
        }
    }
}
