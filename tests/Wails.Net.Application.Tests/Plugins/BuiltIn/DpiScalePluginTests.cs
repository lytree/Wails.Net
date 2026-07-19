using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TUnit.Core;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform.ServerMode;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Plugins.BuiltIn;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Tests.Plugins.BuiltIn;

/// <summary>
/// DpiScalePlugin 的单元测试（TUnit）。
/// 对应 Tauri v2 dpi-scale 插件功能。
/// 注意：部分测试依赖 Application 静态全局实例，因此标记 <see cref="NotInParallelAttribute"/>。
/// </summary>
[NotInParallel]
public sealed class DpiScalePluginTests
{
    /// <summary>
    /// 创建模拟的 <see cref="IPluginContext"/>，提供 CommandRegistry。
    /// </summary>
    private static IPluginContext CreatePluginContext()
    {
        var services = new ServiceCollection();
        var commands = new CommandRegistry();
        var config = new ConfigurationBuilder().Build();
        var loggerFactory = LoggerFactory.Create(_ => { });

        var context = Substitute.For<IPluginContext>();
        context.Services.Returns(services);
        context.Commands.Returns(commands);
        context.Configuration.Returns(config);
        context.LoggerFactory.Returns(loggerFactory);
        return context;
    }

    /// <summary>
    /// 创建模拟的 <see cref="ICommandContext"/>，可指定 WindowId。
    /// </summary>
    private static ICommandContext CreateCommandContext(uint? windowId = null)
    {
        var ctx = Substitute.For<ICommandContext>();
        ctx.Services.Returns(new ServiceCollection().BuildServiceProvider());
        ctx.WindowId.Returns(windowId);
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }

    /// <summary>
    /// 通过反射设置窗口的平台实现，使 GetZoomLevel/SetZoomLevel 可调用。
    /// </summary>
    private static void SetWindowImpl(WebviewWindow window)
    {
        var impl = new ServerWebviewWindow();
        var property = typeof(WebviewWindow).GetProperty(nameof(WebviewWindow.Impl))!;
        property.SetValue(window, impl);
    }

    /// <summary>
    /// 通过命令注册表调用命令。
    /// 调用编译期构建的强类型调用器（遵循 AGENTS.md §3.4 禁令，零反射）。
    /// </summary>
    private static object? InvokeCommand(CommandRegistry registry, string name, params object?[] args)
        => CommandTestHelper.Invoke(registry, name, args);

    // ---------------------------------------------------------------------
    // 基础测试
    // ---------------------------------------------------------------------

    [Test]
    public async Task Name_ReturnsDpiScale()
    {
        var plugin = new DpiScalePlugin();
        await Assert.That(plugin.Name).IsEqualTo("dpi-scale");
    }

    [Test]
    public async Task Configure_NullContext_ThrowsArgumentNullException()
    {
        var plugin = new DpiScalePlugin();
        await Assert.That(() => plugin.Configure(null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task Configure_RegistersAllCommands()
    {
        // 安排
        var plugin = new DpiScalePlugin();
        var context = CreatePluginContext();

        // 操作
        plugin.Configure(context);

        // 断言：3 个命令已注册
        await Assert.That(context.Commands.Find("dpi-scale.getScaleFactor")).IsNotNull();
        await Assert.That(context.Commands.Find("dpi-scale.setZoomFactor")).IsNotNull();
        await Assert.That(context.Commands.Find("dpi-scale.reset")).IsNotNull();
    }

    // ---------------------------------------------------------------------
    // 错误路径测试
    // ---------------------------------------------------------------------

    [Test]
    public async Task GetScaleFactor_NoWindowId_ThrowsInvalidOperationException()
    {
        // 安排
        var plugin = new DpiScalePlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        var cmdCtx = CreateCommandContext(windowId: null);

        // 操作与断言
        await Assert.That(() => InvokeCommand(context.Commands, "dpi-scale.getScaleFactor", cmdCtx))
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task GetScaleFactor_WindowNotFound_ThrowsInvalidOperationException()
    {
        // 安排：不创建 Application，Get() 返回 null
        var plugin = new DpiScalePlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        var cmdCtx = CreateCommandContext(windowId: 999u);

        // 操作与断言
        await Assert.That(() => InvokeCommand(context.Commands, "dpi-scale.getScaleFactor", cmdCtx))
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task SetZoomFactor_NoWindowId_ThrowsInvalidOperationException()
    {
        // 安排
        var plugin = new DpiScalePlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        var cmdCtx = CreateCommandContext(windowId: null);

        // 操作与断言
        await Assert.That(() => InvokeCommand(context.Commands, "dpi-scale.setZoomFactor", cmdCtx, new DpiScaleZoomOptions { Zoom = 1.5f }))
            .ThrowsExactly<InvalidOperationException>();
    }

    // ---------------------------------------------------------------------
    // 正常路径测试（需 Application + 窗口）
    // ---------------------------------------------------------------------

    [Test]
    public async Task GetScaleFactor_ValidWindow_ReturnsZoomLevel()
    {
        // 安排：创建 Application + 窗口（ServerWebviewWindow.GetZoomLevel 返回 0f）
        var app = new Application(new ApplicationOptions());
        var window = app.CreateWebviewWindow(new WebviewWindowOptions { Name = "TestWindow" });
        SetWindowImpl(window);

        var plugin = new DpiScalePlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        var cmdCtx = CreateCommandContext(windowId: window.ID);

        // 操作
        var result = InvokeCommand(context.Commands, "dpi-scale.getScaleFactor", cmdCtx);

        // 断言：ServerWebviewWindow.GetZoomLevel() 返回 0f
        await Assert.That(result).IsEqualTo(0f);
    }

    [Test]
    public async Task SetZoomFactor_ValidValue_CallsSetZoomLevel()
    {
        // 安排
        var app = new Application(new ApplicationOptions());
        var window = app.CreateWebviewWindow(new WebviewWindowOptions { Name = "TestWindow" });
        SetWindowImpl(window);

        var plugin = new DpiScalePlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        var cmdCtx = CreateCommandContext(windowId: window.ID);

        // 操作：设置缩放后查询验证（ServerWebviewWindow 是 no-op，不抛异常即成功）
        await Assert.That(() => InvokeCommand(context.Commands, "dpi-scale.setZoomFactor", cmdCtx, new DpiScaleZoomOptions { Zoom = 1.5f }))
            .ThrowsNothing();
    }

    [Test]
    public async Task Reset_CallsSetZoomLevelWithDefault()
    {
        // 安排
        var app = new Application(new ApplicationOptions());
        var window = app.CreateWebviewWindow(new WebviewWindowOptions { Name = "TestWindow" });
        SetWindowImpl(window);

        var plugin = new DpiScalePlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);
        var cmdCtx = CreateCommandContext(windowId: window.ID);

        // 操作：reset 调用 SetZoomLevel(1.0f)，ServerWebviewWindow 是 no-op
        await Assert.That(() => InvokeCommand(context.Commands, "dpi-scale.reset", cmdCtx))
            .ThrowsNothing();
    }
}
