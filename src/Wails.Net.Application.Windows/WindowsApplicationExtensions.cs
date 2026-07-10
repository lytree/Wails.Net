using Wails.Net.Application.Platform;

namespace Wails.Net.Application;

/// <summary>
/// Windows 平台扩展方法，提供 UseWindows() 入口点以配置 Windows 平台应用。
/// </summary>
public static class WindowsApplicationExtensions
{
    /// <summary>
    /// 为应用配置 Windows 平台实现。
    /// </summary>
    /// <param name="app">应用实例。</param>
    /// <returns>传入的应用实例，以支持链式调用。</returns>
    public static Application UseWindows(this Application app)
    {
        app.SetPlatformApp(new WindowsPlatformApp(app.Options));
        return app;
    }
}
