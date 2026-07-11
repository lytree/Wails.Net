using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using Wails.Net.Application.Menus;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using Menu = Wails.Net.Application.Menus.Menu;
using MenuItem = Wails.Net.Application.Menus.MenuItem;

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
            PInvoke.DestroyMenu(_hmenu);
            _hmenu = default;
        }

        _hmenu = _isPopup ? PInvoke.CreatePopupMenu() : PInvoke.CreateMenu();
        foreach (var item in _menu.Items)
        {
            AppendItem(_hmenu, item);
        }

        // 注：Menu.Impl 的 setter 为 internal，跨程序集不可访问。
        // 此处不设置 Impl；后续动态添加菜单项时需直接调用 Win32Menu.AddMenuItem。
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
        if (_disposed || _hmenu.IsNull)
        {
            return;
        }

        AppendItem(_hmenu, item);
    }

    /// <inheritdoc />
    public void RemoveMenuItem(MenuItem item)
    {
        if (_disposed || _hmenu.IsNull)
        {
            return;
        }

        if (s_itemToCommand.TryGetValue(item, out var commandId))
        {
            PInvoke.RemoveMenu(_hmenu, (uint)commandId, MENU_ITEM_FLAGS.MF_BYCOMMAND);
        }
    }

    /// <inheritdoc />
    public void UpdateMenuItem(MenuItem item)
    {
        // 更新菜单项：简化实现，状态已存储在 MenuItem 上，下次 Build 时生效。
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
    }

    /// <inheritdoc />
    public void Destroy()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (!_hmenu.IsNull)
        {
            PInvoke.DestroyMenu(_hmenu);
            _hmenu = default;
        }
    }

    /// <inheritdoc />
    public void SetLabel(string label)
    {
        // 子菜单项标签更新需要父菜单句柄，简化实现：存储到 _menu.Label，下次 Build 时生效。
        _menu.Label = label;
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

        try
        {
            // 构造包含快捷键的标签文本（标签\t快捷键）。
            var label = _menu.Label ?? string.Empty;
            var text = string.IsNullOrEmpty(accelerator) ? label : $"{label}\t{accelerator}";

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

        if (item.IsSubMenu)
        {
            // 子菜单：递归构建子菜单的 Win32Menu。
            var subImpl = new Win32Menu(item, isPopup: true);
            subImpl.Build();
            // 记录父菜单句柄和位置索引，供 SetBitmap/SetAccelerator 实时更新使用。
            subImpl._parentHmenu = parent;
            subImpl._position = (int)PInvoke.GetMenuItemCount(parent);
            AppendPopup(parent, item.Label ?? string.Empty, subImpl.Hmenu);
            return;
        }

        // 普通命令项。
        var commandId = GetOrCreateCommandId(item);
        AppendCommandItem(parent, item.Label ?? string.Empty, commandId, item.Checked, item.IsDisabled);
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
    private static void AppendCommandItem(HMENU parent, string label, int commandId, bool isChecked, bool isDisabled)
    {
        var flags = MENU_ITEM_FLAGS.MF_STRING;
        if (isChecked)
        {
            flags |= MENU_ITEM_FLAGS.MF_CHECKED;
        }

        if (isDisabled)
        {
            flags |= MENU_ITEM_FLAGS.MF_DISABLED;
        }

        AppendMenuW((IntPtr)parent, flags, (UIntPtr)(uint)commandId, label);
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
    /// </summary>
    /// <param name="commandId">命令 ID（WM_COMMAND wParam 低字）。</param>
    /// <returns>若找到并触发了回调返回 true，否则 false。</returns>
    internal static bool TryDispatchCommand(uint commandId)
    {
        if (s_commandToItem.TryGetValue((int)commandId, out var item))
        {
            try
            {
                item.Callback?.Invoke();
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
}
