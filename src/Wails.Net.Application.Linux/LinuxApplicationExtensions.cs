using Wails.Net.Application.Platform;

namespace Wails.Net.Application;

/// <summary>
/// Linux 平台扩展方法，提供 UseLinux() 入口点以配置 Linux 平台应用。
/// </summary>
public static class LinuxApplicationExtensions
{
    /// <summary>
    /// 为应用配置 Linux 平台实现。
    /// </summary>
    /// <param name="app">应用实例。</param>
    /// <returns>传入的应用实例，以支持链式调用。</returns>
    public static Application UseLinux(this Application app)
    {
        app.SetPlatformApp(new LinuxPlatformApp(app.Options));
        return app;
    }
}
