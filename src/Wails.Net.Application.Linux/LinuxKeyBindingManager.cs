using System.Collections.Concurrent;
using Wails.Net.Application.Managers;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Platform;

/// <summary>
/// Linux 平台快捷键绑定管理器实现。
/// 对应 Wails v3 Go 版本 pkg/services/keys/ 模块。
/// 通过 GTK4 的 Gtk.ShortcutController + Gtk.Shortcut 实现应用级快捷键。
/// 注意：GTK4 不支持系统级全局热键（需要 X11 XGrabKey 或 Wayland portal 协议），
/// 此实现为应用级快捷键，仅在应用窗口获得焦点时生效。
/// </summary>
public sealed class LinuxKeyBindingManager : IKeyBindingManager, IDisposable
{
    /// <summary>
    /// accelerator 字符串到回调的映射表。
    /// </summary>
    private readonly ConcurrentDictionary<string, Action> _bindings = new();

    /// <summary>
    /// accelerator 字符串到 ShortcutController 的映射表。
    /// </summary>
    private readonly ConcurrentDictionary<string, Gtk.ShortcutController> _controllers = new();

    /// <summary>
    /// 标记是否已释放。
    /// </summary>
    private bool _disposed;

    /// <inheritdoc />
    public void RegisterKeyBinding(string accelerator, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        if (string.IsNullOrWhiteSpace(accelerator))
        {
            throw new ArgumentException("快捷键描述不能为空。", nameof(accelerator));
        }

        ObjectDisposedException.ThrowIf(_disposed, this);

        // 若已注册同名快捷键，先移除旧绑定。
        UnregisterKeyBinding(accelerator);

        _bindings[accelerator] = callback;

        // 查找当前应用的第一个窗口，将快捷键控制器附加到其上。
        var app = Application.Get();
        if (app is null)
        {
            return;
        }

        // 遍历所有窗口，为每个窗口附加快捷键控制器。
        foreach (var window in app.WindowManager?.GetAllWindows() ?? Array.Empty<WebviewWindow>())
        {
            AttachShortcutToWindow(window, accelerator, callback);
        }
    }

    /// <inheritdoc />
    public void UnregisterKeyBinding(string accelerator)
    {
        if (string.IsNullOrWhiteSpace(accelerator))
        {
            return;
        }

        ObjectDisposedException.ThrowIf(_disposed, this);

        _bindings.TryRemove(accelerator, out _);

        if (_controllers.TryRemove(accelerator, out var controller))
        {
            try
            {
                controller.Dispose();
            }
            catch
            {
                // 忽略销毁异常
            }
        }
    }

    /// <inheritdoc />
    public void HandleHotKey(int hotkeyId)
    {
        // Linux 的快捷键通过 GTK 信号直接触发回调，不需要 HandleHotKey。
        // 此方法为接口兼容保留，不做任何操作。
    }

    /// <summary>
    /// 将快捷键控制器附加到指定窗口。
    /// </summary>
    /// <param name="window">目标窗口。</param>
    /// <param name="accelerator">快捷键描述。</param>
    /// <param name="callback">触发回调。</param>
    private void AttachShortcutToWindow(WebviewWindow window, string accelerator, Action callback)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        try
        {
            // 将 Wails 风格的 accelerator（Ctrl+C）转换为 GTK 格式（<Control>c）。
            var gtkAccelerator = ConvertToGtkAccelerator(accelerator);

            // 创建快捷键动作。
            var action = Gtk.CallbackAction.New((_, _) =>
            {
                callback();
                return true;
            });

            // 创建快捷键触发器。
            var trigger = Gtk.ShortcutTrigger.ParseString(gtkAccelerator);

            // 创建快捷键。
            var shortcut = Gtk.Shortcut.New(trigger, action);

            // 创建快捷键控制器。
            var controller = Gtk.ShortcutController.New();
            controller.SetScope(Gtk.ShortcutScope.Global);
            controller.AddShortcut(shortcut);

            // 附加到窗口的 widget。
            if (window.Impl is LinuxWebviewWindow linuxImpl)
            {
                linuxImpl.AttachShortcutController(controller);
            }

            _controllers[accelerator] = controller;
        }
        catch
        {
            // GTK 快捷键创建失败时忽略，回调仍保留在 _bindings 中。
        }
    }

    /// <summary>
    /// 将 Wails 风格的 accelerator 字符串转换为 GTK 格式。
    /// 例如 "Ctrl+C" → "&lt;Control&gt;c"，"Alt+F4" → "&lt;Alt&gt;F4"。
    /// </summary>
    private static string ConvertToGtkAccelerator(string accelerator)
    {
        var parts = accelerator.Split('+');
        if (parts.Length == 0)
        {
            return accelerator;
        }

        var modifiers = new List<string>();
        var key = "";

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            var upper = trimmed.ToUpperInvariant();
            if (upper is "CTRL" or "CONTROL")
            {
                modifiers.Add("<Control>");
            }
            else if (upper is "ALT" or "OPTION" or "OPT")
            {
                modifiers.Add("<Alt>");
            }
            else if (upper is "SHIFT")
            {
                modifiers.Add("<Shift>");
            }
            else if (upper is "WIN" or "SUPER" or "META")
            {
                modifiers.Add("<Super>");
            }
            else
            {
                key = trimmed;
            }
        }

        return $"{string.Join("", modifiers)}{key}";
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var controller in _controllers.Values)
        {
            try
            {
                controller.Dispose();
            }
            catch
            {
                // 忽略销毁异常
            }
        }

        _controllers.Clear();
        _bindings.Clear();
        _disposed = true;
    }
}
