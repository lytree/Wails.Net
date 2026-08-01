using Wails.Net.Application.Platform;

namespace Wails.Net.Application;

/// <summary>
/// macOS 平台扩展方法，提供 <c>UseMacOS()</c> 入口点以配置 macOS 平台应用。
/// 对应 Wails v3 Go 版本中各平台的 <c>Init()</c> 函数。
/// </summary>
/// <remarks>
/// 当前为 G7 阶段骨架：<see cref="MacOSPlatformApp"/> 为占位实现，
/// 后续阶段将集成 WKWebView / NSWindow / AppKit 实现完整 macOS GUI 支持。
/// </remarks>
public static class MacOSApplicationExtensions
{
    /// <summary>
    /// 为应用配置 macOS 平台实现。
    /// 创建 <see cref="MacOSPlatformApp"/> 并注册到 <see cref="Application"/>。
    /// <para>
    /// 当前为骨架实现，平台应用行为降级到 Server 模式（no-op）。
    /// </para>
    /// </summary>
    /// <param name="app">应用实例。</param>
    /// <returns>传入的应用实例，以支持链式调用。</returns>
    public static Application UseMacOS(this Application app)
    {
        var platformApp = new MacOSPlatformApp(app.Options);
        app.SetPlatformApp(platformApp);
        return app;
    }
}
