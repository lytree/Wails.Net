using System.Reflection;
using TUnit.Core;

namespace Wails.Net.Application.Tests.Architecture;

/// <summary>
/// 架构边界守护测试（审计 P2-3 落地）。
/// <para>
/// 防止分层腐化：核心程序集 <c>Wails.Net.Application</c> 不得依赖任何具体插件包
/// （<c>Wails.Net.Plugins.*</c>）。插件通过 <see cref="Wails.Net.Application.Plugins.IPlugin"/>
/// 抽象反向依赖核心层，核心层只保留插件抽象、命令调度、安全模型与平台基础接口
/// （对应 audit 文档 §7 目标架构边界）。
/// </para>
/// <para>
/// 说明：<see cref="Assembly.GetReferencedAssemblies"/> 仅读取编译期元数据（程序集引用表），
/// 属于 AGENTS.md §3.4.4 允许的测试代码白盒校验，不构成生产代码反射。
/// </para>
/// </summary>
public sealed class ArchitectureBoundaryTests
{
    [Test]
    public async Task ApplicationCore_DoesNotReferencePluginPackages()
    {
        // 安排：核心程序集
        var assembly = typeof(Wails.Net.Application.Application).Assembly;

        // 操作：读取编译期引用的程序集列表
        var pluginReferences = assembly
            .GetReferencedAssemblies()
            .Where(a => a.Name?.StartsWith("Wails.Net.Plugins.", StringComparison.Ordinal) == true)
            .Select(a => a.Name!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        // 断言：不得引用任何插件包
        await Assert.That(pluginReferences).IsEmpty();
    }

    [Test]
    public async Task ApplicationCore_DoesNotReferencePlatformImplementations()
    {
        // 安排：核心程序集
        var assembly = typeof(Wails.Net.Application.Application).Assembly;

        // 操作：检查平台实现程序集引用
        var platformReferences = assembly
            .GetReferencedAssemblies()
            .Where(a => a.Name?.StartsWith("Wails.Net.Application.", StringComparison.Ordinal) == true)
            .Select(a => a.Name!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        // 断言：核心层不得反向引用 Wails.Net.Application.{Windows,Linux,Android,MacOS}
        // （自引用 Wails.Net.Application 本身会被过滤掉，因为当前程序集不会列出自己）
        await Assert.That(platformReferences).IsEmpty();
    }
}
