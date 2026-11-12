using Microsoft.Win32;
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
    /// <summary>
    /// 自启动注册表键路径，与 <see cref="WindowsAutostartManager"/> 内部使用的一致。
    /// </summary>
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

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

        // Enable() 按设计静默吞掉注册表写入异常（无权限时不影响应用启动），
        // 因此在"注册表只读"的环境中本用例既无法证明也无法证伪写入语义。
        // 先用同一条注册表路径（CreateSubKey，与实现一致）探测可写性，不可写则跳过，
        // 避免把环境限制报成实现缺陷。
        Skip.Unless(
            CanWriteRunKey(name),
            @"当前环境禁止写 HKCU\Software\Microsoft\Windows\CurrentVersion\Run，无法验证自启动写入语义");

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

    /// <summary>
    /// 探测当前环境能否向自启动注册表键写入并读回一个临时值（探测后立即清理）。
    /// 用 <see cref="RegistryKey.CreateSubKey(string)"/> 探测，与实现路径保持一致的语义
    /// （键不存在时可创建，而不是直接放弃写入）。
    /// </summary>
    /// <param name="probeName">探测用的值名称。</param>
    /// <returns>可写入并读回时为 true。</returns>
    private static bool CanWriteRunKey(string probeName)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key is null)
            {
                return false;
            }

            key.SetValue(probeName, Environment.ProcessPath ?? string.Empty);
            var written = key.GetValue(probeName) is not null;
            key.DeleteValue(probeName, throwOnMissingValue: false);
            return written;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException
            or UnauthorizedAccessException
            or IOException)
        {
            return false;
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
