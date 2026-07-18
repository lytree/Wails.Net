using System.Collections.Concurrent;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Platform;

namespace Wails.Net.Application.Managers;

/// <summary>
/// 菜单管理器默认实现（P1-4）。
/// <para>
/// 提供 <see cref="IMenuManager"/> 的跨平台默认实现：
/// <list type="bullet">
/// <item>应用菜单：缓存最近设置的 <see cref="Menu"/> 实例，并委托 <see cref="IPlatformApp.SetApplicationMenu"/>
/// 通知平台层同步应用菜单。GetApplicationMenu 返回缓存值（<see cref="IPlatformApp"/> 未提供查询接口）。</item>
/// <item>上下文菜单注册表：使用 <see cref="ConcurrentDictionary{TKey, TValue}"/> 按字符串 ID 维护，
/// 对应 Wails v3 Go 版本 <c>application.go</c> 中的 <c>contextMenus</c> 字典与 <c>contextMenusLock</c> 锁。
/// 此注册表为纯托管代码，与平台无关，跨 Windows/Linux/Android 共享。</item>
/// </list>
/// </para>
/// <para>
/// 平台层仍可继承此类或重新实现 <see cref="IMenuManager"/> 以覆盖应用菜单行为。
/// </para>
/// </summary>
public class MenuManager : IMenuManager
{
    /// <summary>
    /// 上下文菜单注册表，按字符串 ID 索引。
    /// <see cref="ConcurrentDictionary{TKey, TValue}"/> 自身线程安全，无需额外锁。
    /// </summary>
    private readonly ConcurrentDictionary<string, ContextMenu> _contextMenus = new();

    /// <summary>
    /// 应用菜单缓存。
    /// IPlatformApp 接口未提供 GetApplicationMenu 查询方法，此处缓存便于读取。
    /// 使用 volatile + 简单赋值，读取线程可能短暂读到旧值，但应用菜单设置通常在应用启动时一次性完成，可接受。
    /// </summary>
    private volatile Menu? _applicationMenu;

    /// <summary>
    /// 平台应用实例，用于委托应用菜单操作。
    /// 为 null 时应用菜单操作仅更新缓存。
    /// </summary>
    private readonly IPlatformApp? _platformApp;

    /// <summary>
    /// 构造 <see cref="MenuManager"/> 实例。
    /// </summary>
    /// <param name="platformApp">平台应用实例，可为 null（如 Server 模式下）。</param>
    public MenuManager(IPlatformApp? platformApp = null)
    {
        _platformApp = platformApp;
    }

    /// <inheritdoc />
    public void SetApplicationMenu(Menu? menu)
    {
        _applicationMenu = menu;
        _platformApp?.SetApplicationMenu(menu);
    }

    /// <inheritdoc />
    public Menu? GetApplicationMenu()
    {
        return _applicationMenu;
    }

    /// <inheritdoc />
    public void RegisterContextMenu(string id, ContextMenu menu)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(menu);
        _contextMenus[id] = menu;
    }

    /// <inheritdoc />
    public ContextMenu? GetContextMenu(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _contextMenus.TryGetValue(id, out var menu) ? menu : null;
    }

    /// <inheritdoc />
    public void RemoveContextMenu(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        _contextMenus.TryRemove(id, out _);
    }
}
