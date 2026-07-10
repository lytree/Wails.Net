using TUnit.Core;
using Wails.Net.Application.Managers;

namespace Wails.Net.Application.Linux.Tests;

/// <summary>
/// LinuxAutostartManager 的单元测试（TUnit）。
/// 通过 XDG autostart .desktop 文件测试自启动管理。
/// 注意：测试共享文件系统和环境变量，因此不并行执行。
/// 每个测试使用唯一的 Guid 名称以避免冲突，并使用 XDG_CONFIG_HOME 隔离测试目录。
/// </summary>
[NotInParallel]
public sealed class LinuxAutostartManagerTests
{
    /// <summary>
    /// 保存环境变量的原始值，用于测试结束后恢复。
    /// </summary>
    private string? _originalXdgConfigHome;

    /// <summary>
    /// 测试使用的临时配置目录路径。
    /// </summary>
    private string? _tempConfigHome;

    [Before(Test)]
    public void Setup()
    {
        // 保存原始 XDG_CONFIG_HOME 并设置到临时目录，以隔离测试
        _originalXdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        _tempConfigHome = $"/tmp/wails-test-config-{Guid.NewGuid()}";
        Directory.CreateDirectory(_tempConfigHome);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _tempConfigHome);
    }

    [After(Test)]
    public void Teardown()
    {
        // 恢复环境变量
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _originalXdgConfigHome);

        // 清理临时目录
        if (_tempConfigHome is not null && Directory.Exists(_tempConfigHome))
        {
            try
            {
                Directory.Delete(_tempConfigHome, recursive: true);
            }
            catch (IOException)
            {
                // 忽略清理失败
            }
            catch (UnauthorizedAccessException)
            {
                // 忽略清理失败
            }
        }
    }

    [Test]
    public async Task IsEnabled_ReturnsFalse_WhenNotConfigured()
    {
        // 安排：使用唯一名称创建管理器
        var name = "wails-test-autostart-" + Guid.NewGuid();
        var manager = new LinuxAutostartManager(name);

        // 操作与断言：未配置时应返回 false
        await Assert.That(manager.IsEnabled()).IsFalse();
    }

    [Test]
    public async Task Enable_CreatesDesktopFile()
    {
        // 安排
        var name = "wails-test-autostart-" + Guid.NewGuid();
        var manager = new LinuxAutostartManager(name);

        // 操作：启用自启动
        manager.Enable();

        // 断言：.desktop 文件已创建
        await Assert.That(manager.IsEnabled()).IsTrue();
    }

    [Test]
    public async Task Enable_CreatesAutostartDirectory_WhenNotExists()
    {
        // 安排：删除 autostart 目录（Setup 已创建配置目录，但 autostart 子目录不应存在）
        var name = "wails-test-autostart-" + Guid.NewGuid();
        var manager = new LinuxAutostartManager(name);
        var autostartDir = Path.Combine(_tempConfigHome!, "autostart");
        if (Directory.Exists(autostartDir))
        {
            Directory.Delete(autostartDir, recursive: true);
        }

        // 操作：启用自启动应创建 autostart 目录
        manager.Enable();

        // 断言：autostart 目录和 .desktop 文件已创建
        await Assert.That(Directory.Exists(autostartDir)).IsTrue();
        await Assert.That(manager.IsEnabled()).IsTrue();
    }

    [Test]
    public async Task Disable_RemovesDesktopFile()
    {
        // 安排
        var name = "wails-test-autostart-" + Guid.NewGuid();
        var manager = new LinuxAutostartManager(name);

        // 操作：先启用再禁用
        manager.Enable();
        manager.Disable();

        // 断言：.desktop 文件已移除
        await Assert.That(manager.IsEnabled()).IsFalse();
    }

    [Test]
    public async Task Disable_DoesNotThrow_WhenNotConfigured()
    {
        // 安排：使用唯一名称创建管理器，不先启用
        var name = "wails-test-autostart-" + Guid.NewGuid();
        var manager = new LinuxAutostartManager(name);

        // 操作与断言：未配置时禁用不应抛出异常
        await Assert.That(() => manager.Disable()).ThrowsNothing();
    }

    [Test]
    public async Task Enable_Disable_Enable_CycleWorks()
    {
        // 安排
        var name = "wails-test-autostart-" + Guid.NewGuid();
        var manager = new LinuxAutostartManager(name);

        // 操作：启用 -> 禁用 -> 再启用
        manager.Enable();
        await Assert.That(manager.IsEnabled()).IsTrue();

        manager.Disable();
        await Assert.That(manager.IsEnabled()).IsFalse();

        manager.Enable();

        // 断言：再次启用后状态为已启用
        await Assert.That(manager.IsEnabled()).IsTrue();
    }
}
