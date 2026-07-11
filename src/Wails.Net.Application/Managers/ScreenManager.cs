using Wails.Net.Application.Platform;
using Wails.Net.Application.Screens;

namespace Wails.Net.Application.Managers;

/// <summary>
/// 屏幕管理器，负责获取屏幕信息。
/// 对应 Wails v3 Go 版本中的 screenManager。
/// 通过 IPlatformApp 委托给平台特定的屏幕实现。
/// </summary>
public class ScreenManager : IScreenManager
{
    /// <summary>
    /// 平台应用实例。
    /// </summary>
    private readonly IPlatformApp? _platformApp;

    /// <summary>
    /// 使用指定的平台应用构造 ScreenManager 实例。
    /// </summary>
    /// <param name="platformApp">平台应用实例，可为 null（Server 模式）。</param>
    public ScreenManager(IPlatformApp? platformApp)
    {
        _platformApp = platformApp;
    }

    /// <summary>
    /// 获取主屏幕。
    /// </summary>
    /// <returns>主屏幕实例，若平台应用未设置则返回 null。</returns>
    public Screen? GetPrimaryScreen()
    {
        return _platformApp?.GetPrimaryScreen();
    }

    /// <summary>
    /// 获取所有屏幕。
    /// </summary>
    /// <returns>屏幕数组，若平台应用未设置则返回空数组。</returns>
    public Screen[] GetAllScreens()
    {
        if (_platformApp is null)
        {
            return Array.Empty<Screen>();
        }

        return _platformApp.GetScreens();
    }
}
