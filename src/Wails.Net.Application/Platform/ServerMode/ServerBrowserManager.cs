using Wails.Net.Application.Browser;
using Wails.Net.Application.Managers;

namespace Wails.Net.Application.Platform.ServerMode;

/// <summary>
/// Server（无界面）模式下的浏览器管理器桩实现。
/// 所有打开 URL 操作均为空操作，适用于无头运行/容器化部署场景。
/// 对应 <see cref="IBrowserManager"/> 接口的最小可用实现。
/// </summary>
public sealed class ServerBrowserManager : IBrowserManager
{
    /// <inheritdoc />
    public void OpenURL(string url)
    {
        // Server 模式无 GUI，不打开浏览器；URL 验证仍执行以保持调用语义一致
        _ = BrowserUrlValidator.TryValidate(url, out _);
    }

    /// <inheritdoc />
    public void OpenURLInDefaultBrowser(string url)
    {
        _ = BrowserUrlValidator.TryValidate(url, out _);
    }
}
