using Wails.Net.Application.Dialogs;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Menus.Context;
using Wails.Net.Application.Windows;
using Menu = Wails.Net.Application.Menus.Menu;
using WailsApplication = Wails.Net.Application.Application;

namespace Wails.Net.Application.Platform;

/// <summary>
/// macOS 平台菜单实现，基于 AppKit NSMenu。
/// 对应 Wails v3 Go 版本 menu_darwin.go + menuitem_darwin.go。
/// <para>
/// 与 LinuxMenu 一致采用"整树重建"策略：任何菜单变更（添加/更新/移除菜单项）
/// 都触发 <see cref="Rebuild"/> 重建整棵 NSMenu 树，避免增量同步的复杂状态管理。
/// 菜单项点击通过 NSMenuItem.Activated 事件分发到 <see cref="MenuItem.Callback"/>
/// 或 <see cref="MenuItem.CallbackWithContext"/>。
/// </para>
/// <para>
/// 非 macOS 目标（<c>#if !MACOS</c>）保留 no-op 骨架保证任意宿主编译。
/// </para>
/// </summary>
public sealed class MacOSMenu : IMenuImpl, IDisposable
{
    /// <summary>
    /// 关联的源菜单实例。
    /// </summary>
    private readonly Menu _menu;

#if MACOS
    /// <summary>
    /// 根 NSMenu 实例（AppKit 原生菜单）。
    /// </summary>
    private AppKit.NSMenu? _nsMenu;

    /// <summary>
    /// 原生菜单项字典（menuItemID → NSMenuItem），用于整树重建前释放旧项。
    /// </summary>
    private readonly Dictionary<uint, AppKit.NSMenuItem> _nativeItems = new();
#endif

    /// <summary>
    /// 构造 MacOSMenu 实例并立即重建原生菜单树。
    /// </summary>
    /// <param name="menu">源菜单实例。</param>
    public MacOSMenu(Menu menu)
    {
        _menu = menu;
        Rebuild();
    }

#if MACOS
    /// <summary>
    /// 获取根 NSMenu 实例（无则 null）。
    /// </summary>
    public AppKit.NSMenu? NativeMenu => _nsMenu;
#endif

    /// <inheritdoc />
    public void Show()
    {
        // macOS 应用菜单由 NSApp.MainMenu 管理，无需额外显示操作。
    }

    /// <inheritdoc />
    public void Hide()
    {
        // macOS 应用菜单由 NSApp.MainMenu 管理，无需额外隐藏操作。
    }

    /// <inheritdoc />
    public void AddMenuItem(MenuItem item, int position)
    {
        Rebuild();
    }

    /// <inheritdoc />
    public void RemoveMenuItem(MenuItem item)
    {
        Rebuild();
    }

    /// <inheritdoc />
    public void UpdateMenuItem(MenuItem item)
    {
        Rebuild();
    }

    /// <inheritdoc />
    public void AddSubmenu(Menu submenu, int position)
    {
        Rebuild();
    }

    /// <inheritdoc />
    public void Destroy()
    {
#if MACOS
        _nsMenu?.Dispose();
        _nsMenu = null;
        _nativeItems.Clear();
#endif
    }

    /// <inheritdoc />
    public void SetLabel(string label)
    {
        _menu.Label = label;
        Rebuild();
    }

    /// <inheritdoc />
    public void SetEnabled(bool enabled)
    {
        Rebuild();
    }

    /// <inheritdoc />
    public void SetChecked(bool @checked)
    {
        Rebuild();
    }

    /// <inheritdoc />
    public void SetAccelerator(string accelerator)
    {
        Rebuild();
    }

    /// <inheritdoc />
    public void SetBitmap(byte[]? bitmap)
    {
        _menu.Bitmap = bitmap;
        Rebuild();
    }

    /// <inheritdoc />
    public void ApplyRole(MenuItem item, IWebviewWindowImpl? window)
    {
        if (item is null || item.Role == MenuRole.None)
        {
            return;
        }

        // 准备菜单项：填充默认 Label、Accelerator，设置 Callback（含 macOS 专属角色）。
        MenuRoleHelper.PrepareRoleItem(item, window, ExecuteRole);

        // 若角色带默认加速键，注册到 KeyBindingManager（Carbon RegisterEventHotKey）。
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
                // 已注册过或注册失败时忽略，不阻断菜单构建。
            }
        }
    }

    /// <summary>
    /// 执行角色对应的系统命令（含 macOS 专属角色）。
    /// </summary>
    /// <param name="role">菜单角色。</param>
    /// <param name="window">目标窗口。</param>
    /// <param name="aboutMetadata">关于对话框元数据（仅 About 角色使用）。</param>
    private static void ExecuteRole(MenuRole role, IWebviewWindowImpl? window, AboutMetadata? aboutMetadata)
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
                case MenuRole.Zoom:
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
#if MACOS
                case MenuRole.Hide:
                    AppKit.NSApplication.SharedApplication.Hide(null);
                    break;
                case MenuRole.HideOthers:
                    AppKit.NSApplication.SharedApplication.HideOtherApplications(null);
                    break;
                case MenuRole.ShowAll:
                    AppKit.NSApplication.SharedApplication.UnhideAllApplications(null);
                    break;
                case MenuRole.BringAllToFront:
                    AppKit.NSApplication.SharedApplication.ArrangeInFront(null);
                    break;
                case MenuRole.Services:
                    // Services 菜单在菜单树构建时通过 SetSubmenu 挂到 NSApp.ServicesMenu。
                    break;
#endif
            }
        }
        catch
        {
            // 角色命令执行失败不应中断菜单回调。
        }
    }

    /// <summary>
    /// 通过 WebView ExecJS 调用 document.execCommand。
    /// </summary>
    /// <param name="window">目标窗口。</param>
    /// <param name="command">编辑命令名（copy/cut/paste/selectAll/undo/redo）。</param>
    private static void SendEditorCommand(IWebviewWindowImpl? window, string command)
    {
        window?.ExecJS($"document.execCommand('{command}')");
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
        if (!string.IsNullOrEmpty(copyright))
        {
            lines.Add(copyright);
        }

        if (!string.IsNullOrEmpty(comments))
        {
            lines.Add(comments);
        }

        if (!string.IsNullOrEmpty(website))
        {
            lines.Add(website);
        }

        var message = string.Join(Environment.NewLine, lines);
        // 异步触发，不等待（菜单回调为同步）。
        _ = dialog.ShowMessageDialog("关于", message, DialogStyle.Info, new[] { "确定" });
    }

    /// <summary>
    /// 重建整棵 NSMenu 树。
    /// </summary>
    private void Rebuild()
    {
#if MACOS
        // 释放旧树：先释放所有原生菜单项，再移除根菜单全部项。
        _nativeItems.Clear();

        if (_nsMenu is null)
        {
            _nsMenu = new AppKit.NSMenu
            {
                AutoenablesItems = false,
            };
        }
        else
        {
            _nsMenu.RemoveAllItems();
        }

        if (!string.IsNullOrEmpty(_menu.Label))
        {
            _nsMenu.Title = _menu.Label;
        }

        foreach (var item in _menu.Items)
        {
            AppendItem(_nsMenu, item, null);
        }
#else
        // 非 macOS 目标：no-op 骨架。
#endif
    }

#if MACOS
    /// <summary>
    /// 将菜单项追加到指定原生菜单。
    /// </summary>
    /// <param name="parent">目标 NSMenu。</param>
    /// <param name="item">源菜单项。</param>
    /// <param name="window">关联窗口（用于角色命令），可为 null。</param>
    private void AppendItem(AppKit.NSMenu parent, MenuItem item, IWebviewWindowImpl? window)
    {
        if (item.IsSeparator || item.Role == MenuRole.Separator)
        {
            parent.AddItem(AppKit.NSMenuItem.SeparatorItem);
            return;
        }

        // 应用角色（如有）：填充默认 Label、绑定 Callback、注册全局热键。
        ApplyRole(item, window);

        var label = item.Label ?? string.Empty;

        // 子菜单：构建子 NSMenu 并挂到菜单项。
        if (item.IsSubMenu || item.Items.Count > 0)
        {
            var nsItem = new AppKit.NSMenuItem(label);
            var submenu = new AppKit.NSMenu { AutoenablesItems = false };
            foreach (var child in item.Items)
            {
                AppendItem(submenu, child, window);
            }

            nsItem.Submenu = submenu;

            // Services 角色：将子菜单挂到 NSApp.ServicesMenu。
            if (item.Role == MenuRole.Services)
            {
                AppKit.NSApplication.SharedApplication.ServicesMenu = submenu;
            }

            parent.AddItem(nsItem);
            _nativeItems[item.ID] = nsItem;
            return;
        }

        // 叶子菜单项。
        var leaf = new AppKit.NSMenuItem(label);

        // 复选框/单选状态。
        if (item.IsCheckbox || item.IsRadio)
        {
            leaf.State = item.Checked ? AppKit.NSControlStateValue.On : AppKit.NSControlStateValue.Off;
        }

        leaf.Enabled = !item.IsDisabled;

        // 快捷键（accelerator → keyEquivalent + modifier mask）。
        if (!string.IsNullOrEmpty(item.Accelerator))
        {
            var (key, mask) = ParseKeyEquivalent(item.Accelerator);
            leaf.KeyEquivalent = key;
            leaf.KeyEquivalentModifierMask = mask;
        }

        // 图标。
        if (item.Bitmap is { Length: > 0 } bitmap)
        {
            using var data = Foundation.NSData.FromArray(bitmap);
            var image = AppKit.NSImage.FromData(data);
            if (image is not null)
            {
                leaf.Image = image;
            }
        }

        // 点击回调（优先 CallbackWithContext，否则 Callback）。
        if (item.CallbackWithContext is not null || item.Callback is not null)
        {
            leaf.Activated += (_, _) => HandleMenuItemClick(item);
        }

        parent.AddItem(leaf);
        _nativeItems[item.ID] = leaf;
    }
#endif

#if MACOS
    /// <summary>
    /// 处理菜单项点击：复选框切换状态，构造 MenuContext 分发回调。
    /// </summary>
    /// <param name="item">被点击的菜单项。</param>
    private static void HandleMenuItemClick(MenuItem item)
    {
        if (item.IsCheckbox || item.IsRadio)
        {
            item.Checked = !item.Checked;
        }

        if (item.CallbackWithContext is not null)
        {
            var context = new MenuContext()
                .WithClickedMenuItem(item)
                .WithChecked(item.Checked);
            item.CallbackWithContext(context);
            return;
        }

        item.Callback?.Invoke();
    }

    /// <summary>
    /// 将 Wails 风格 accelerator（如 "CmdOrCtrl+Shift+K"）解析为 keyEquivalent 与修饰掩码。
    /// 对应 Wails v3 Go 版本 menuitem_darwin.go 的 translateKey + toMacModifier。
    /// </summary>
    /// <param name="accelerator">accelerator 字符串。</param>
    /// <returns>键字符串与 NSEventModifierMask 组合。</returns>
    private static (string Key, AppKit.NSEventModifierMask Mask) ParseKeyEquivalent(string accelerator)
    {
        AppKit.NSEventModifierMask mask = 0;
        string? key = null;

        foreach (var part in accelerator.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToUpperInvariant())
            {
                case "CMD":
                case "COMMAND":
                case "CMDORCTRL":
                case "SUPER":
                case "META":
                case "WIN":
                    mask |= AppKit.NSEventModifierMask.Command;
                    break;
                case "CTRL":
                case "CONTROL":
                    mask |= AppKit.NSEventModifierMask.Control;
                    break;
                case "ALT":
                case "OPTION":
                case "OPT":
                    mask |= AppKit.NSEventModifierMask.Alternate;
                    break;
                case "SHIFT":
                    mask |= AppKit.NSEventModifierMask.Shift;
                    break;
                default:
                    key = TranslateKey(part);
                    break;
            }
        }

        return (key ?? string.Empty, mask);
    }

    /// <summary>
    /// 将 accelerator 键名转换为 AppKit keyEquivalent 字符。
    /// 对应 Wails v3 Go 版本 menuitem_darwin.go 的 translateKey 表。
    /// </summary>
    /// <param name="key">键名（小写）。</param>
    /// <returns>keyEquivalent 字符。</returns>
    private static string TranslateKey(string key)
    {
        return key.ToLowerInvariant() switch
        {
            "backspace" => "\u0008",
            "tab" => "\u0009",
            "return" => "\u000d",
            "enter" => "\u000d",
            "escape" => "\u001b",
            "esc" => "\u001b",
            "left" => "\uf702",
            "right" => "\uf703",
            "up" => "\uf700",
            "down" => "\uf701",
            "space" => " ",
            "delete" => "\u007f",
            "home" => "\u2196",
            "end" => "\u2198",
            "pageup" => "\u21de",
            "pagedown" => "\u21df",
            "f1" => "\uf704",
            "f2" => "\uf705",
            "f3" => "\uf706",
            "f4" => "\uf707",
            "f5" => "\uf708",
            "f6" => "\uf709",
            "f7" => "\uf70a",
            "f8" => "\uf70b",
            "f9" => "\uf70c",
            "f10" => "\uf70d",
            "f11" => "\uf70e",
            "f12" => "\uf70f",
            _ => key,
        };
    }
#endif

    /// <inheritdoc />
    public void Dispose()
    {
        Destroy();
    }
}
