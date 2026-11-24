using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using Wails.Net.Application.Dialogs;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Menus.Context;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using Menu = Wails.Net.Application.Menus.Menu;
using MenuItem = Wails.Net.Application.Menus.MenuItem;
using WailsApplication = Wails.Net.Application.Application;

namespace Wails.Net.Application.Platform;

/// <summary>
/// Win32 菜单实现，对应 Go 版 menu_windows.go。
/// 使用 CreateMenu/CreatePopupMenu 创建原生菜单，AppendMenuW 添加菜单项，
/// 通过静态命令 ID 映射表分发 WM_COMMAND 菜单点击回调。
/// </summary>
public sealed class Win32Menu : IMenuImpl
{
    /// <summary>
    /// 命令 ID → MenuItem 映射，用于 WM_COMMAND 分发时查找回调。
    /// </summary>
    private static readonly ConcurrentDictionary<int, MenuItem> s_commandToItem = new();

    /// <summary>
    /// MenuItem → 命令 ID 映射，确保同一菜单项复用同一命令 ID。
    /// </summary>
    private static readonly ConcurrentDictionary<MenuItem, int> s_itemToCommand = new();

    /// <summary>
    /// 命令 ID → 所属原生菜单句柄映射。
    /// 用于 <see cref="TryDispatchCommand"/> 中同步复选框勾选状态（<c>CheckMenuItem</c>）——
    /// 只知道命令 ID 无法定位菜单句柄，必须在此记录。
    /// </summary>
    private static readonly ConcurrentDictionary<int, HMENU> s_commandToMenu = new();

    /// <summary>
    /// 下一个命令 ID（线程安全递增）。命令 ID 必须 ≤ 0xFFFF 以适配 WM_COMMAND 的低字。
    /// </summary>
    private static int s_nextCommandId = 1;

    /// <summary>
    /// 关联的 Menu 实例。
    /// </summary>
    private readonly Menu _menu;

    /// <summary>
    /// 是否为弹出式菜单（用于上下文菜单和子菜单）。
    /// </summary>
    private readonly bool _isPopup;

    /// <summary>
    /// 原生菜单句柄。
    /// </summary>
    private HMENU _hmenu;

    /// <summary>
    /// 是否已销毁。
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// 父菜单句柄，当此菜单为子菜单（弹出式）时由父菜单设置，
    /// 用于 ModifyMenuW 实时更新菜单项。为 default 表示此菜单为顶级菜单栏。
    /// </summary>
    private HMENU _parentHmenu;

    /// <summary>
    /// 此菜单在父菜单中的位置索引（从 0 开始）。
    /// 用于 ModifyMenuW 的 MF_BYPOSITION 模式定位菜单项。
    /// </summary>
    private int _position;

    /// <summary>
    /// 子菜单项 → 子菜单实现映射。
    /// 弹出项没有命令 ID，无法通过 <see cref="s_itemToCommand"/> 定位；
    /// 更新子菜单标签/启用状态时必须靠此映射找回对应的 popup。
    /// </summary>
    private readonly Dictionary<MenuItem, Win32Menu> _childMenus = new();

    /// <summary>
    /// 构造 Win32Menu 实例。
    /// </summary>
    /// <param name="menu">关联的 Menu 实例。</param>
    public Win32Menu(Menu menu)
        : this(menu, isPopup: false)
    {
    }

    /// <summary>
    /// 构造 Win32Menu 实例，指定是否为弹出式菜单。
    /// </summary>
    /// <param name="menu">关联的 Menu 实例。</param>
    /// <param name="isPopup">是否为弹出式菜单。</param>
    internal Win32Menu(Menu menu, bool isPopup)
    {
        _menu = menu;
        _isPopup = isPopup;
    }

    /// <summary>
    /// 获取原生菜单句柄。
    /// </summary>
    internal HMENU Hmenu => _hmenu;

    /// <summary>
    /// 原生菜单结构变更后的回调。
    /// <para>
    /// 菜单栏是可运行时增删改的，但 <c>DrawMenuBar</c> 需要窗口句柄，
    /// 本类不持有窗口，故由 <c>WindowsPlatformApp</c> 注册回调，
    /// 在变更后把菜单句柄重新应用到各窗口以触发菜单栏重绘。
    /// </para>
    /// </summary>
    internal Action? NativeChanged { get; set; }

    /// <summary>
    /// 构建（或重建）原生菜单。遍历 _menu.Items 并添加到原生菜单。
    /// </summary>
    public void Build()
    {
        if (_disposed)
        {
            return;
        }

        // 销毁旧句柄。
        if (!_hmenu.IsNull)
        {
            // 旧子菜单句柄会随父句柄一起被回收，只把子实现标记为失效，
            // 避免它们残留非空 _hmenu 继续操作已释放的句柄。
            InvalidateChildMenus();
            PInvoke.DestroyMenu(_hmenu);
            _hmenu = default;
        }

        _hmenu = _isPopup ? PInvoke.CreatePopupMenu() : PInvoke.CreateMenu();
        foreach (var item in _menu.Items)
        {
            AppendItem(_hmenu, item);
        }

        // 挂载平台实现：此后 Menu.Append / Menu.Remove / Menu.Update 会直接落到原生菜单。
        // Menu.Impl 的 setter 为 internal，Application 程序集已对各平台程序集开放
        // InternalsVisibleTo，此处可直接挂载。
        _menu.AttachImpl(this);
    }

    /// <inheritdoc />
    public void Show()
    {
        // 菜单栏的显示由 SetMenu + DrawMenuBar 控制，此处无窗口句柄，留空。
    }

    /// <inheritdoc />
    public void Hide()
    {
        // 菜单栏的隐藏由 SetMenu(null) 控制，此处无窗口句柄，留空。
    }

    /// <inheritdoc />
    public void AddMenuItem(MenuItem item, int position)
    {
        if (_disposed || _hmenu.IsNull || item is null)
        {
            return;
        }

        // 应用角色（如有）：填充默认 Label、绑定 Callback、注册全局热键
        if (item.Role != MenuRole.None)
        {
            ApplyRole(item, window: null);
        }

        item.Parent = _menu;

        // position 为模型中的目标下标；越界或等于末尾时直接追加（AppendMenuW 更廉价）。
        var count = (int)PInvoke.GetMenuItemCount(_hmenu);
        if (position < 0 || position >= count)
        {
            AppendItem(_hmenu, item);
        }
        else
        {
            InsertItem(_hmenu, item, position);
        }

        NativeChanged?.Invoke();
    }

    /// <inheritdoc />
    public void RemoveMenuItem(MenuItem item)
    {
        if (_disposed || _hmenu.IsNull || item is null)
        {
            return;
        }

        // 子菜单（弹出项）没有命令 ID，必须按位置移除，并销毁其原生子菜单。
        if (_childMenus.TryGetValue(item, out var child))
        {
            child.RefreshPosition();
            PInvoke.RemoveMenu(_hmenu, (uint)child._position, MENU_ITEM_FLAGS.MF_BYPOSITION);
            child.Destroy();
            _childMenus.Remove(item);
        }
        else if (s_itemToCommand.TryRemove(item, out var commandId))
        {
            PInvoke.RemoveMenu(_hmenu, (uint)commandId, MENU_ITEM_FLAGS.MF_BYCOMMAND);
            s_commandToItem.TryRemove(commandId, out _);
            s_commandToMenu.TryRemove(commandId, out _);
        }

        item.Parent = null;
        NativeChanged?.Invoke();
    }

    /// <inheritdoc />
    public void UpdateMenuItem(MenuItem item)
    {
        if (_disposed || _hmenu.IsNull || item is null)
        {
            return;
        }

        // 子菜单：按位置改写 popup 标签（弹出项没有命令 ID）。
        if (_childMenus.TryGetValue(item, out var child))
        {
            child.RefreshPosition();
            ModifyMenuW(
                (IntPtr)_hmenu,
                (uint)child._position,
                MENU_ITEM_FLAGS.MF_BYPOSITION | MENU_ITEM_FLAGS.MF_STRING | MENU_ITEM_FLAGS.MF_POPUP,
                (UIntPtr)(IntPtr)child._hmenu,
                FormatItemText(item.Label, item.Accelerator));

            NativeChanged?.Invoke();
            return;
        }

        // 叶子项：按命令 ID 原地改写文本与状态位，保持项位置不变。
        if (!s_itemToCommand.TryGetValue(item, out var commandId) ||
            !s_commandToMenu.TryGetValue(commandId, out var parent) ||
            parent.IsNull)
        {
            return;
        }

        var flags = MENU_ITEM_FLAGS.MF_BYCOMMAND | MENU_ITEM_FLAGS.MF_STRING;
        if (item.Checked)
        {
            flags |= MENU_ITEM_FLAGS.MF_CHECKED;
        }

        if (item.IsDisabled)
        {
            flags |= MENU_ITEM_FLAGS.MF_DISABLED;
        }

        ModifyMenuW((IntPtr)parent, (uint)commandId, flags, (UIntPtr)(uint)commandId, FormatItemText(item.Label, item.Accelerator));

        NativeChanged?.Invoke();
    }

    /// <inheritdoc />
    public void AddSubmenu(Menu submenu, int position)
    {
        if (_disposed || _hmenu.IsNull || submenu is null)
        {
            return;
        }

        // 为子菜单创建独立的 Win32Menu（弹出式）并构建。
        var subImpl = new Win32Menu(submenu, isPopup: true);
        subImpl.Build();
        // 记录父菜单句柄和位置索引，供 SetBitmap/SetAccelerator 实时更新使用。
        subImpl._parentHmenu = _hmenu;
        subImpl._position = (int)PInvoke.GetMenuItemCount(_hmenu);
        AppendPopup(_hmenu, submenu.Label ?? string.Empty, subImpl.Hmenu);

        if (submenu is MenuItem subItem)
        {
            _childMenus[subItem] = subImpl;
        }

        NativeChanged?.Invoke();
    }

    /// <inheritdoc />
    public void Destroy()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // 释放本菜单树登记的命令映射，避免静态字典持有已销毁的 HMENU
        // （句柄值可能被系统复用，残留映射会让 CheckMenuItem 作用到错误的菜单上）。
        ReleaseCommandMappings(_menu);

        // 子实现先失效：其句柄会随父句柄的 DestroyMenu 一并回收，
        // 不再单独 DestroyMenu，避免重复释放。
        InvalidateChildMenus();

        if (!_hmenu.IsNull)
        {
            PInvoke.DestroyMenu(_hmenu);
            _hmenu = default;
        }

        _menu.DetachImpl(this);
        NativeChanged = null;
    }

    /// <summary>
    /// 递归标记子菜单实现为失效，并解除其与菜单模型的挂载。
    /// <para>
    /// 说明：子菜单句柄由父句柄的 <c>DestroyMenu</c> 一并回收，此处不重复调用。
    /// </para>
    /// </summary>
    private void InvalidateChildMenus()
    {
        foreach (var child in _childMenus.Values)
        {
            child.Invalidate();
        }

        _childMenus.Clear();
    }

    /// <summary>
    /// 将当前实现标记为失效（句柄已被父级回收）。
    /// </summary>
    private void Invalidate()
    {
        _disposed = true;
        _hmenu = default;
        _parentHmenu = default;
        InvalidateChildMenus();
        _menu.DetachImpl(this);
    }

    /// <summary>
    /// 递归移除菜单树中各菜单项登记的命令映射。
    /// </summary>
    /// <param name="menu">菜单节点。</param>
    private static void ReleaseCommandMappings(Menu menu)
    {
        foreach (var item in menu.Items)
        {
            if (s_itemToCommand.TryRemove(item, out var commandId))
            {
                s_commandToItem.TryRemove(commandId, out _);
                s_commandToMenu.TryRemove(commandId, out _);
            }

            if (item.Items.Count > 0)
            {
                ReleaseCommandMappings(item);
            }
        }
    }

    /// <inheritdoc />
    public void SetLabel(string label)
    {
        _menu.Label = label;

        // 该实现代表一个弹出菜单（子菜单），标签实际显示在父菜单的 popup 项上。
        if (_disposed || _parentHmenu.IsNull || _hmenu.IsNull)
        {
            return;
        }

        RefreshPosition();

        try
        {
            ModifyMenuW(
                (IntPtr)_parentHmenu,
                (uint)_position,
                MENU_ITEM_FLAGS.MF_BYPOSITION | MENU_ITEM_FLAGS.MF_STRING | MENU_ITEM_FLAGS.MF_POPUP,
                (UIntPtr)(IntPtr)_hmenu,
                FormatItemText(label, _menu.Accelerator));
        }
        catch
        {
            // 实时更新失败时忽略，状态已存储在 _menu.Label，下次 Build 时生效。
        }
    }

    /// <inheritdoc />
    public void SetEnabled(bool enabled)
    {
        // 简化实现：存储到 _menu.IsDisabled，下次 Build 时生效。
        _menu.IsDisabled = !enabled;
    }

    /// <inheritdoc />
    public void SetChecked(bool @checked)
    {
        // 简化实现：存储到 _menu.Checked，下次 Build 时生效。
        _menu.Checked = @checked;
    }

    /// <inheritdoc />
    public void SetAccelerator(string accelerator)
    {
        _menu.Accelerator = accelerator;

        // 若未加入父菜单（顶级菜单栏或尚未 Build），仅存储状态，下次 Build 时生效。
        if (_disposed || _parentHmenu.IsNull || _hmenu.IsNull)
        {
            return;
        }

        // 插入/删除兄弟项后旧索引会失效，按原生句柄重新定位。
        RefreshPosition();

        try
        {
            // 构造包含快捷键的标签文本（标签\t快捷键）。
            var text = FormatItemText(_menu.Label, accelerator);

            // 通过 ModifyMenuW 实时更新菜单项文本，保留弹出式（MF_POPUP）属性。
            ModifyMenuW(
                (IntPtr)_parentHmenu,
                (uint)_position,
                MENU_ITEM_FLAGS.MF_BYPOSITION | MENU_ITEM_FLAGS.MF_STRING | MENU_ITEM_FLAGS.MF_POPUP,
                (UIntPtr)(IntPtr)_hmenu,
                text);
        }
        catch
        {
            // 实时更新失败时忽略，状态已存储在 _menu.Accelerator，下次 Build 时生效
        }
    }

    /// <inheritdoc />
    public void SetBitmap(byte[]? bitmap)
    {
        _menu.Bitmap = bitmap;

        // 若未加入父菜单（顶级菜单栏或尚未 Build），或位图为空，仅存储状态。
        if (_disposed || _parentHmenu.IsNull || _hmenu.IsNull || bitmap is null || bitmap.Length == 0)
        {
            return;
        }

        // 插入/删除兄弟项后旧索引会失效，按原生句柄重新定位。
        RefreshPosition();

        try
        {
            // 从字节数据创建 GDI 位图句柄。
            using var ms = new MemoryStream(bitmap);
            using var bmp = new Bitmap(ms);
            var hbitmap = bmp.GetHbitmap();

            // 通过 ModifyMenuW 实时更新菜单项位图，保留弹出式（MF_POPUP）属性。
            ModifyMenuW(
                (IntPtr)_parentHmenu,
                (uint)_position,
                MENU_ITEM_FLAGS.MF_BYPOSITION | MENU_ITEM_FLAGS.MF_BITMAP | MENU_ITEM_FLAGS.MF_POPUP,
                (UIntPtr)(IntPtr)_hmenu,
                hbitmap);
        }
        catch
        {
            // 实时更新失败时忽略，状态已存储在 _menu.Bitmap，下次 Build 时生效
        }
    }

    /// <inheritdoc />
    public void ApplyRole(MenuItem item, Windows.IWebviewWindowImpl? window)
    {
        if (item is null || item.Role == MenuRole.None)
        {
            return;
        }

        // macOS 专属角色在 Windows 上静默 no-op
        if (MenuRoleHelper.IsMacOSExclusive(item.Role))
        {
            return;
        }

        // 准备菜单项：填充默认 Label、Accelerator，设置 Callback
        MenuRoleHelper.PrepareRoleItem(item, window, ExecuteRole);

        // 若角色带默认加速键，注册到 KeyBindingManager（修复现有 Accelerator 不全局注册的 bug）
        var accelerator = item.Accelerator;
        if (!string.IsNullOrEmpty(accelerator) && item.Callback is not null)
        {
            try
            {
                var keyBindingManager = WailsApplication.Get()?.KeyBindingManager;
                if (keyBindingManager is not null)
                {
                    keyBindingManager.RegisterKeyBinding(accelerator, item.Callback);
                }
            }
            catch
            {
                // 已注册过或注册失败时忽略，不阻断菜单构建
            }
        }
    }

    /// <summary>
    /// 执行角色对应的系统命令。
    /// </summary>
    /// <param name="role">菜单角色。</param>
    /// <param name="window">目标窗口。</param>
    /// <param name="aboutMetadata">关于对话框元数据（仅 About 角色使用）。</param>
    private static void ExecuteRole(MenuRole role, Windows.IWebviewWindowImpl? window, AboutMetadata? aboutMetadata)
    {
        try
        {
            switch (role)
            {
                case MenuRole.Copy:
                    SendEditorCommand(window, "copy");
                    break;
                case MenuRole.Cut:
                    SendEditorCommand(window, "cut");
                    break;
                case MenuRole.Paste:
                    SendEditorCommand(window, "paste");
                    break;
                case MenuRole.SelectAll:
                    SendEditorCommand(window, "selectAll");
                    break;
                case MenuRole.Undo:
                    SendEditorCommand(window, "undo");
                    break;
                case MenuRole.Redo:
                    SendEditorCommand(window, "redo");
                    break;
                case MenuRole.Minimize:
                    window?.Minimise();
                    break;
                case MenuRole.Maximize:
                    if (window?.IsMaximised() == true)
                    {
                        window?.UnMaximise();
                    }
                    else
                    {
                        window?.Maximise();
                    }
                    break;
                case MenuRole.Fullscreen:
                case MenuRole.ToggleFullScreen:
                    if (window?.IsFullscreen() == true)
                    {
                        window?.UnFullscreen();
                    }
                    else
                    {
                        window?.Fullscreen();
                    }
                    break;
                case MenuRole.CloseWindow:
                    window?.Close();
                    break;
                case MenuRole.Quit:
                    WailsApplication.Get()?.Quit();
                    break;
                case MenuRole.About:
                    ShowAboutDialog(aboutMetadata);
                    break;
            }
        }
        catch
        {
            // 角色命令执行失败不应中断菜单回调
        }
    }

    /// <summary>
    /// 通过 WebView2 ExecuteScriptAsync 调用 document.execCommand。
    /// 这是对 Win32 SendMessage(WM_COPY) 的更优替代——直接作用于 WebView 焦点元素。
    /// </summary>
    /// <param name="window">目标窗口。</param>
    /// <param name="command">编辑命令名（copy/cut/paste/selectAll/undo/redo）。</param>
    private static void SendEditorCommand(Windows.IWebviewWindowImpl? window, string command)
    {
        if (window is Win32WebviewWindow win)
        {
            win.ExecJS($"document.execCommand('{command}')");
        }
    }

    /// <summary>
    /// 显示关于对话框。使用应用级 Dialog API 弹出信息提示框。
    /// </summary>
    /// <param name="about">关于对话框元数据。</param>
    private static void ShowAboutDialog(AboutMetadata? about)
    {
        var app = WailsApplication.Get();
        var dialog = app?.DialogManager;
        if (dialog is null)
        {
            return;
        }

        var name = about?.Name ?? "Application";
        var version = about?.Version ?? "1.0.0";
        var copyright = about?.Copyright ?? string.Empty;
        var comments = about?.Comments ?? string.Empty;
        var website = about?.Website ?? string.Empty;

        var lines = new List<string> { name, $"版本 {version}" };
        if (!string.IsNullOrEmpty(copyright)) lines.Add(copyright);
        if (!string.IsNullOrEmpty(comments)) lines.Add(comments);
        if (!string.IsNullOrEmpty(website)) lines.Add(website);

        var message = string.Join(Environment.NewLine, lines);
        // 异步触发，不等待（菜单回调为同步）
        _ = dialog.ShowMessageDialog("关于", message, DialogStyle.Info, new[] { "确定" });
    }

    /// <summary>
    /// 将菜单项追加到父菜单（递归处理分隔符、子菜单、普通命令项）。
    /// </summary>
    /// <param name="parent">父菜单句柄。</param>
    /// <param name="item">要追加的菜单项。</param>
    private void AppendItem(HMENU parent, MenuItem item)
    {
        if (item is null)
        {
            return;
        }

        if (item.IsSeparator)
        {
            AppendSeparator(parent);
            return;
        }

        // 应用角色（如有）。Build 路径不经过 AddMenuItem，必须在此处补齐：
        // 否则标准 Edit 菜单等角色项在原生菜单中只剩空标签且没有回调，点击无任何效果。
        if (item.Role != MenuRole.None)
        {
            ApplyRole(item, window: null);
        }

        if (item.IsSubMenu || item.Items.Count > 0)
        {
            // 子菜单：递归构建子菜单的 Win32Menu。
            var subImpl = new Win32Menu(item, isPopup: true);
            subImpl.Build();
            // 记录父菜单句柄和位置索引，供 SetBitmap/SetAccelerator 实时更新使用。
            subImpl._parentHmenu = parent;
            subImpl._position = (int)PInvoke.GetMenuItemCount(parent);
            AppendPopup(parent, FormatItemText(item.Label, item.Accelerator), subImpl.Hmenu);
            _childMenus[item] = subImpl;
            return;
        }

        // 普通命令项。
        var commandId = GetOrCreateCommandId(item);
        AppendCommandItem(parent, item, commandId);
        // 记录命令 ID 所属的菜单句柄，供点击时刷新复选框原生状态使用。
        s_commandToMenu[commandId] = parent;
    }

    /// <summary>
    /// 在指定位置插入菜单项（供 <see cref="AddMenuItem"/> 处理非末尾插入）。
    /// </summary>
    /// <param name="parent">父菜单句柄。</param>
    /// <param name="item">要插入的菜单项。</param>
    /// <param name="position">插入位置（从 0 开始）。</param>
    private void InsertItem(HMENU parent, MenuItem item, int position)
    {
        if (item.IsSeparator)
        {
            InsertMenuW(
                (IntPtr)parent,
                (uint)position,
                MENU_ITEM_FLAGS.MF_BYPOSITION | MENU_ITEM_FLAGS.MF_SEPARATOR,
                UIntPtr.Zero,
                null);
            return;
        }

        if (item.IsSubMenu || item.Items.Count > 0)
        {
            var subImpl = new Win32Menu(item, isPopup: true);
            subImpl.Build();
            subImpl._parentHmenu = parent;
            subImpl._position = position;

            InsertMenuW(
                (IntPtr)parent,
                (uint)position,
                MENU_ITEM_FLAGS.MF_BYPOSITION | MENU_ITEM_FLAGS.MF_STRING | MENU_ITEM_FLAGS.MF_POPUP,
                (UIntPtr)(IntPtr)subImpl.Hmenu,
                FormatItemText(item.Label, item.Accelerator));

            _childMenus[item] = subImpl;
            return;
        }

        var commandId = GetOrCreateCommandId(item);
        var flags = MENU_ITEM_FLAGS.MF_BYPOSITION | MENU_ITEM_FLAGS.MF_STRING;
        if (item.Checked)
        {
            flags |= MENU_ITEM_FLAGS.MF_CHECKED;
        }

        if (item.IsDisabled)
        {
            flags |= MENU_ITEM_FLAGS.MF_DISABLED;
        }

        InsertMenuW((IntPtr)parent, (uint)position, flags, (UIntPtr)(uint)commandId, FormatItemText(item.Label, item.Accelerator));
        s_commandToMenu[commandId] = parent;
    }

    /// <summary>
    /// 拼接菜单项显示文本：<c>标签\t快捷键</c>。
    /// <para>
    /// Win32 菜单以制表符分隔标签与快捷键文本，右侧对齐显示。
    /// 此前只在子菜单的 <see cref="SetAccelerator"/> 路径上拼过，
    /// 叶子命令项（如"复制"）在菜单里看不到 Ctrl+C 之类的提示。
    /// </para>
    /// </summary>
    /// <param name="label">标签文本，可为 null。</param>
    /// <param name="accelerator">快捷键文本，可为 null。</param>
    /// <returns>用于 AppendMenuW/ModifyMenuW 的显示文本。</returns>
    internal static string FormatItemText(string? label, string? accelerator)
    {
        var text = label ?? string.Empty;
        return string.IsNullOrEmpty(accelerator) ? text : $"{text}\t{accelerator}";
    }

    /// <summary>
    /// 在父菜单中重新定位当前弹出菜单的位置。
    /// 菜单项被插入或删除后，构建期记录的 <see cref="_position"/> 会失效。
    /// </summary>
    private void RefreshPosition()
    {
        if (_parentHmenu.IsNull || _hmenu.IsNull)
        {
            return;
        }

        var count = (int)PInvoke.GetMenuItemCount(_parentHmenu);
        for (var i = 0; i < count; i++)
        {
            if ((IntPtr)PInvoke.GetSubMenu(_parentHmenu, i) == (IntPtr)_hmenu)
            {
                _position = i;
                return;
            }
        }
    }

    /// <summary>
    /// 追加分隔符。
    /// </summary>
    private static void AppendSeparator(HMENU parent)
    {
        AppendMenuW((IntPtr)parent, MENU_ITEM_FLAGS.MF_SEPARATOR, UIntPtr.Zero, null);
    }

    /// <summary>
    /// 追加普通命令菜单项。
    /// </summary>
    /// <param name="parent">父菜单句柄。</param>
    /// <param name="item">菜单项（提供标签、快捷键与状态位）。</param>
    /// <param name="commandId">命令 ID。</param>
    private static void AppendCommandItem(HMENU parent, MenuItem item, int commandId)
    {
        var flags = MENU_ITEM_FLAGS.MF_STRING;
        if (item.Checked)
        {
            flags |= MENU_ITEM_FLAGS.MF_CHECKED;
        }

        if (item.IsDisabled)
        {
            flags |= MENU_ITEM_FLAGS.MF_DISABLED;
        }

        AppendMenuW(
            (IntPtr)parent,
            flags,
            (UIntPtr)(uint)commandId,
            FormatItemText(item.Label, item.Accelerator));
    }

    /// <summary>
    /// 追加弹出式子菜单。
    /// </summary>
    private static void AppendPopup(HMENU parent, string label, HMENU subMenu)
    {
        AppendMenuW((IntPtr)parent, MENU_ITEM_FLAGS.MF_STRING | MENU_ITEM_FLAGS.MF_POPUP, (UIntPtr)(IntPtr)subMenu, label);
    }

    /// <summary>
    /// 为菜单项获取或创建命令 ID（复用已分配的 ID）。
    /// </summary>
    private static int GetOrCreateCommandId(MenuItem item)
    {
        if (s_itemToCommand.TryGetValue(item, out var existing))
        {
            return existing;
        }

        var newId = Interlocked.Increment(ref s_nextCommandId) - 1;
        // 约束在 16 位以内以适配 WM_COMMAND 低字。
        newId &= 0xFFFF;
        if (newId == 0)
        {
            newId = Interlocked.Increment(ref s_nextCommandId) - 1;
            newId &= 0xFFFF;
        }

        s_itemToCommand[item] = newId;
        s_commandToItem[newId] = item;
        return newId;
    }

    /// <summary>
    /// 尝试分发 WM_COMMAND 命令。根据命令 ID 查找 MenuItem 并触发其回调。
    /// 由窗口过程在收到 WM_COMMAND 时调用。
    /// <para>
    /// 对应 Wails v3 Go 版本 <c>MenuItem.handleClick</c>：构建 <see cref="MenuContext"/>，
    /// 对复选框菜单项切换选中状态，优先调用 <see cref="MenuItem.CallbackWithContext"/>，
    /// 为 null 时回退到 <see cref="MenuItem.Callback"/>。
    /// </para>
    /// </summary>
    /// <param name="commandId">命令 ID（WM_COMMAND wParam 低字）。</param>
    /// <returns>若找到并触发了回调返回 true，否则 false。</returns>
    internal static bool TryDispatchCommand(uint commandId)
    {
        if (s_commandToItem.TryGetValue((int)commandId, out var item))
        {
            try
            {
                // 构建菜单点击上下文，对应 Wails v3 Go 版本 handleClick 中的 newContext()。
                var context = new MenuContext()
                    .WithClickedMenuItem(item)
                    .WithContextMenuData(item.ContextMenuData);

                // 复选框菜单项：切换选中状态并填充 IsChecked。
                if (item.IsCheckbox)
                {
                    item.Checked = !item.Checked;
                    context.WithChecked(item.Checked);
                    // 同步原生勾选标记，否则用户点击后菜单上看不到任何变化。
                    ApplyCheckState(commandId, item.Checked);
                }

                // 优先调用带上下文的回调，否则回退到无参回调。
                if (item.CallbackWithContext is not null)
                {
                    item.CallbackWithContext(context);
                }
                else
                {
                    item.Callback?.Invoke();
                }
            }
            catch
            {
                // 回调异常不应中断消息处理
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// 将复选框的选中状态同步到原生菜单项（<c>CheckMenuItem</c>）。
    /// <para>
    /// 此前点击只在托管模型上翻转 <see cref="MenuItem.Checked"/>，原生菜单的勾选标记不会变化，
    /// 用户看不到任何反馈；菜单重建后才会按模型重新渲染。
    /// </para>
    /// </summary>
    /// <param name="commandId">菜单项命令 ID。</param>
    /// <param name="isChecked">是否选中。</param>
    private static void ApplyCheckState(uint commandId, bool isChecked)
    {
        if (!s_commandToMenu.TryGetValue((int)commandId, out var parent) || parent.IsNull)
        {
            return;
        }

        // MF_UNCHECKED 取值为 0，未选中时无需附加标志位。
        var flags = MENU_ITEM_FLAGS.MF_BYCOMMAND;
        if (isChecked)
        {
            flags |= MENU_ITEM_FLAGS.MF_CHECKED;
        }

        CheckMenuItemNative((IntPtr)parent, commandId, flags);
    }

    /// <summary>
    /// AppendMenuW P/Invoke 声明。
    /// CsWin32 生成的 AppendMenu 重载仅接受 SafeHandle，无法直接传入 HMENU，
    /// 此处手动声明以支持 HMENU 句柄（通过 IntPtr 传递）。
    /// </summary>
    /// <param name="hMenu">父菜单句柄（IntPtr 形式）。</param>
    /// <param name="uFlags">菜单项标志（MF_* 组合）。</param>
    /// <param name="uIDNewItem">命令 ID 或子菜单句柄（MF_POPUP 时为菜单句柄）。</param>
    /// <param name="lpNewItem">菜单项文本，分隔符时为 null。</param>
    /// <returns>成功返回 true，否则 false。</returns>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(IntPtr hMenu, MENU_ITEM_FLAGS uFlags, UIntPtr uIDNewItem, string? lpNewItem);

    /// <summary>
    /// InsertMenuW P/Invoke 声明。
    /// 与 AppendMenuW 同理，CsWin32 生成的 InsertMenu 仅接受 SafeHandle，
    /// 此处手动声明以支持 HMENU 句柄与位置插入（<c>MF_BYPOSITION</c>）。
    /// </summary>
    /// <param name="hMenu">父菜单句柄（IntPtr 形式）。</param>
    /// <param name="uPosition">插入位置（MF_BYPOSITION 时为索引）。</param>
    /// <param name="uFlags">菜单项标志（MF_* 组合）。</param>
    /// <param name="uIDNewItem">命令 ID 或子菜单句柄（MF_POPUP 时为菜单句柄）。</param>
    /// <param name="lpNewItem">菜单项文本，分隔符时为 null。</param>
    /// <returns>成功返回 true，否则 false。</returns>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InsertMenuW(IntPtr hMenu, uint uPosition, MENU_ITEM_FLAGS uFlags, UIntPtr uIDNewItem, string? lpNewItem);

    /// <summary>
    /// ModifyMenuW P/Invoke 声明（字符串重载）。
    /// 与 AppendMenuW 同理，手动声明以支持 HMENU 句柄直接传递。
    /// 用于 SetAccelerator 等需要更新菜单项文本的场景。
    /// </summary>
    /// <param name="hMnu">包含待修改项的菜单句柄（IntPtr 形式）。</param>
    /// <param name="uPosition">待修改项的位置（MF_BYPOSITION）或命令 ID（MF_BYCOMMAND）。</param>
    /// <param name="uFlags">菜单项标志（MF_* 组合）。</param>
    /// <param name="uIDNewItem">新的命令 ID 或子菜单句柄。</param>
    /// <param name="lpNewItem">新的菜单项文本（MF_STRING 时使用）。</param>
    /// <returns>成功返回 true，否则 false。</returns>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "ModifyMenuW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ModifyMenuW(IntPtr hMnu, uint uPosition, MENU_ITEM_FLAGS uFlags, UIntPtr uIDNewItem, string? lpNewItem);

    /// <summary>
    /// ModifyMenuW P/Invoke 声明（位图重载）。
    /// MF_BITMAP 时 lpNewItem 为 HBITMAP 句柄，使用 IntPtr 重载传递。
    /// 用于 SetBitmap 等需要更新菜单项位图的场景。
    /// </summary>
    /// <param name="hMnu">包含待修改项的菜单句柄（IntPtr 形式）。</param>
    /// <param name="uPosition">待修改项的位置（MF_BYPOSITION）或命令 ID（MF_BYCOMMAND）。</param>
    /// <param name="uFlags">菜单项标志（MF_* 组合，含 MF_BITMAP）。</param>
    /// <param name="uIDNewItem">新的命令 ID 或子菜单句柄。</param>
    /// <param name="lpNewItem">新的位图句柄（HBITMAP，MF_BITMAP 时使用）。</param>
    /// <returns>成功返回 true，否则 false。</returns>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "ModifyMenuW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ModifyMenuW(IntPtr hMnu, uint uPosition, MENU_ITEM_FLAGS uFlags, UIntPtr uIDNewItem, IntPtr lpNewItem);

    /// <summary>
    /// CheckMenuItem P/Invoke 声明。
    /// 与 AppendMenuW 同理，CsWin32 生成的重载仅接受 SafeHandle 且标志位参数为 uint，
    /// 此处手动声明以支持 HMENU 句柄与强类型标志位。
    /// </summary>
    /// <param name="hMenu">包含待修改项的菜单句柄。</param>
    /// <param name="uIDCheckItem">菜单项命令 ID（MF_BYCOMMAND）或位置（MF_BYPOSITION）。</param>
    /// <param name="uCheck">标志位（MF_BYCOMMAND 与 MF_CHECKED 的组合）。</param>
    /// <returns>成功返回 true，否则 false。</returns>
    [DllImport("user32.dll", SetLastError = true, EntryPoint = "CheckMenuItem")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CheckMenuItemNative(IntPtr hMenu, uint uIDCheckItem, MENU_ITEM_FLAGS uCheck);
}
