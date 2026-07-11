using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using Gdk;
using Gio;
using GLib;
using Gtk;
using Wails.Net.Application.Dialogs;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Screens;
using Action = System.Action;
using Array = System.Array;
using File = Gio.File;
using Menu = Wails.Net.Application.Menus.Menu;
using Monitor = Gdk.Monitor;
using Window = Gtk.Window;

namespace Wails.Net.Application.Platform;

/// <summary>
/// Linux 平台应用实现，对应 Go 版 application_linux.go 中的 platformApp。
/// 通过 GTK4 提供主循环、对话框、屏幕信息和窗口管理。
/// 主题与强调色通过 D-Bus（gsettings）读取 org.gnome.desktop.interface；
/// 单实例通过文件锁实现；应用菜单通过 GMenu 模型构建。
/// 在非 Linux 平台上，GTK 相关方法返回默认值或抛出 PlatformNotSupportedException。
/// </summary>
public sealed class LinuxPlatformApp : IPlatformApp
{
    /// <summary>
    /// gsettings schema：org.gnome.desktop.interface，包含主题与强调色设置。
    /// </summary>
    private const string GnomeInterfaceSchema = "org.gnome.desktop.interface";

    /// <summary>
    /// 应用名称。
    /// </summary>
    private readonly string _name;

    /// <summary>
    /// 主线程 ID，用于 IsOnMainThread 判断。
    /// </summary>
    private readonly int _mainThreadId;

    /// <summary>
    /// GTK 主循环实例，Run() 时创建，Destroy() 时退出。
    /// </summary>
    private MainLoop? _mainLoop;

    /// <summary>
    /// 已创建的 Webview 窗口字典，按窗口 ID 索引。
    /// </summary>
    private readonly Dictionary<uint, LinuxWebviewWindow> _windows = new();

    /// <summary>
    /// 单实例文件锁流，持有期间阻止其他实例获取同名锁。
    /// </summary>
    private FileStream? _singleInstanceLockStream;

    /// <summary>
    /// 单实例锁文件路径，用于派生通知文件路径。
    /// </summary>
    private string? _singleInstanceLockPath;

    /// <summary>
    /// 单实例 UNIX socket 监听器，首实例启动后监听第二个实例的连接。
    /// 对应 Go 版 application_single_instance_linux.go 中的 D-Bus 信号通知机制。
    /// </summary>
    private System.Net.Sockets.Socket? _singleInstanceSocket;

    /// <summary>
    /// 单实例 UNIX socket 路径，用于清理。
    /// </summary>
    private string? _singleInstanceSocketPath;

    /// <summary>
    /// 应用菜单的 LinuxMenu 实例，由 SetApplicationMenu 构建。
    /// 包含 GMenu 模型与对应的 ActionGroup（含所有菜单项回调）。
    /// </summary>
    private LinuxMenu? _appMenu;

    /// <summary>
    /// 系统级事件监听器，监听网络连通性变化等系统事件。
    /// </summary>
    private LinuxSystemEventWatcher? _systemEventWatcher;

    /// <summary>
    /// 构造 LinuxPlatformApp 实例。
    /// </summary>
    /// <param name="options">应用配置选项。</param>
    public LinuxPlatformApp(ApplicationOptions options)
    {
        _name = options.Name;
        _mainThreadId = Environment.CurrentManagedThreadId;
    }

    /// <inheritdoc />
    public string Name => _name;

    /// <inheritdoc />
    public bool IsOnMainThread()
    {
        return Environment.CurrentManagedThreadId == _mainThreadId;
    }

    /// <inheritdoc />
    public bool IsDarkMode()
    {
        // 在 Linux 上优先通过 D-Bus（gsettings）读取 color-scheme。
        if (OperatingSystem.IsLinux())
        {
            var scheme = RunGsettingsGet(GnomeInterfaceSchema, "color-scheme");
            if (scheme is not null)
            {
                if (scheme.Contains("dark", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (scheme.Contains("light", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        // 回退：读取 GTK_THEME 环境变量，若包含 "dark" 则为暗色模式。
        var gtkTheme = Environment.GetEnvironmentVariable("GTK_THEME");
        if (gtkTheme is not null && gtkTheme.Contains("dark", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 回退：读取 COLOR_SCHEME 环境变量（freedesktop portal 约定），值为 "dark" 时为暗色模式。
        var colorScheme = Environment.GetEnvironmentVariable("COLOR_SCHEME");
        if (string.Equals(colorScheme, "dark", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 默认返回亮色模式。
        return false;
    }

    /// <inheritdoc />
    public string GetAccentColor()
    {
        // 在 Linux 上通过 D-Bus（gsettings）读取 accent-color 名称并映射为十六进制颜色。
        if (OperatingSystem.IsLinux())
        {
            var accentName = RunGsettingsGet(GnomeInterfaceSchema, "accent-color");
            if (accentName is not null)
            {
                var hex = MapAccentColorName(accentName);
                if (hex is not null)
                {
                    return hex;
                }
            }
        }

        // 回退到默认蓝色。
        return "#0078D4";
    }

    /// <inheritdoc />
    public Screen? GetPrimaryScreen()
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        var display = Display.GetDefault();
        if (display is null)
        {
            return null;
        }

        var monitors = display.GetMonitors();
        if (monitors.GetNItems() == 0)
        {
            return null;
        }

        var monitor = monitors.GetObject(0) as Monitor;
        if (monitor is null)
        {
            return null;
        }

        return CreateScreenFromMonitor(monitor, isPrimary: true);
    }

    /// <inheritdoc />
    public Screen[] GetScreens()
    {
        if (!OperatingSystem.IsLinux())
        {
            return Array.Empty<Screen>();
        }

        var display = Display.GetDefault();
        if (display is null)
        {
            return Array.Empty<Screen>();
        }

        var monitors = display.GetMonitors();
        var count = monitors.GetNItems();
        if (count == 0)
        {
            return Array.Empty<Screen>();
        }

        var screens = new List<Screen>();
        for (uint i = 0; i < count; i++)
        {
            var monitor = monitors.GetObject(i) as Monitor;
            if (monitor is null)
            {
                continue;
            }

            screens.Add(CreateScreenFromMonitor(monitor, isPrimary: i == 0));
        }

        return screens.ToArray();
    }

    /// <summary>
    /// 从 Gdk.Monitor 创建 Screen 实例。
    /// </summary>
    /// <param name="monitor">Gdk 显示器实例。</param>
    /// <param name="isPrimary">是否为主屏幕。</param>
    /// <returns>Screen 实例。</returns>
    private static Screen CreateScreenFromMonitor(Monitor monitor, bool isPrimary)
    {
        var geometry = monitor.Geometry;
        var scaleFactor = (float)monitor.ScaleFactor;
        var connector = monitor.Connector ?? "Monitor";
        return new Screen
        {
            Name = connector,
            X = geometry.X,
            Y = geometry.Y,
            Width = geometry.Width,
            Height = geometry.Height,
            WorkAreaX = geometry.X,
            WorkAreaY = geometry.Y,
            WorkAreaWidth = geometry.Width,
            WorkAreaHeight = geometry.Height,
            ScaleFactor = scaleFactor,
            IsPrimary = isPrimary,
        };
    }

    /// <inheritdoc />
    public Dictionary<string, object?> GetFlags(ApplicationOptions options)
    {
        return new Dictionary<string, object?>();
    }

    /// <inheritdoc />
    public int Run()
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("GTK 主循环仅在 Linux 上可用。");
        }

        // 启动系统级事件监听器（网络连通性变化）。
        _systemEventWatcher = new LinuxSystemEventWatcher();
        _systemEventWatcher.Start();

        _mainLoop = MainLoop.New(GLib.Functions.MainContextDefault(), false);
        _mainLoop.Run();
        return 0;
    }

    /// <inheritdoc />
    public bool AcquireSingleInstanceLock(string uniqueId)
    {
        if (string.IsNullOrEmpty(uniqueId))
        {
            return true;
        }

        var safeName = SanitizeFileName(uniqueId);
        var lockPath = Path.Combine(Path.GetTempPath(), $"wails-{safeName}.lock");
        try
        {
            // 以独占方式创建锁文件，若已有实例持有则抛出 IOException 表示非首实例。
            _singleInstanceLockStream = new FileStream(
                lockPath, FileMode.Create, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            _singleInstanceLockPath = lockPath;

            // 启动 UNIX socket 监听，接收后续实例的实时通知。
            // 对应 Go 版 application_single_instance_linux.go 中的 D-Bus 信号注册。
            StartSingleInstanceSocket(safeName);
            return true;
        }
        catch
        {
            _singleInstanceLockStream = null;
            _singleInstanceLockPath = null;
            return false;
        }
    }

    /// <inheritdoc />
    public void NotifySingleInstance(string[] args)
    {
        if (!OperatingSystem.IsLinux() || _singleInstanceLockPath is null)
        {
            return;
        }

        // 通过 UNIX socket 向首实例发送命令行参数，实现实时通知。
        // 对应 Go 版 application_single_instance_linux.go 中的 notifySingleInstance。
        var safeName = SanitizeFileName(_name);
        var socketPath = Path.Combine(Path.GetTempPath(), $"wails-{safeName}.sock");
        try
        {
            using var client = new System.Net.Sockets.Socket(
                AddressFamily.Unix, System.Net.Sockets.SocketType.Stream, ProtocolType.Unspecified);
            var endpoint = new UnixDomainSocketEndPoint(socketPath);
            client.Connect(endpoint);

            // 将 args 以 JSON 数组格式发送，避免参数边界歧义。
            var json = System.Text.Json.JsonSerializer.Serialize(args);
            var bytes = Encoding.UTF8.GetBytes(json);
            client.Send(bytes);
        }
        catch
        {
            // socket 连接失败时回退到文件通知方式。
            var notifyPath = Path.ChangeExtension(_singleInstanceLockPath, ".args");
            try
            {
                System.IO.File.WriteAllLines(notifyPath, args);
            }
            catch
            {
                // 忽略通知写入失败。
            }
        }
    }

    /// <summary>
    /// 启动单实例 UNIX socket 监听，接收后续实例的命令行参数。
    /// 在后台线程中 Accept 连接，读取 args 后触发 SecondInstanceLaunched 事件。
    /// </summary>
    /// <param name="safeName">安全的应用名称，用于 socket 路径。</param>
    private void StartSingleInstanceSocket(string safeName)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var socketPath = Path.Combine(Path.GetTempPath(), $"wails-{safeName}.sock");
        try
        {
            // 清理可能残留的旧 socket 文件。
            if (System.IO.File.Exists(socketPath))
            {
                System.IO.File.Delete(socketPath);
            }

            _singleInstanceSocket = new System.Net.Sockets.Socket(
                AddressFamily.Unix, System.Net.Sockets.SocketType.Stream, ProtocolType.Unspecified);
            _singleInstanceSocket.Bind(new UnixDomainSocketEndPoint(socketPath));
            _singleInstanceSocket.Listen(1);
            _singleInstanceSocketPath = socketPath;

            // 在后台线程中 Accept 连接，避免阻塞主循环。
            System.Threading.Tasks.Task.Run(() =>
            {
                while (_singleInstanceSocket is not null)
                {
                    try
                    {
                        var client = _singleInstanceSocket.Accept();
                        using var stream = new NetworkStream(client, ownsSocket: true);
                        using var reader = new StreamReader(stream, Encoding.UTF8);
                        var json = reader.ReadToEnd();
                        var args = System.Text.Json.JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();

                        // 通过事件系统分发 SecondInstanceLaunched 事件，携带命令行参数。
                        Application.Get()?.Events.Emit("SecondInstanceLaunched", args, null);
                    }
                    catch
                    {
                        // socket 关闭或异常时退出循环。
                        break;
                    }
                }
            });
        }
        catch
        {
            // socket 创建失败时忽略，回退到文件通知方式。
            _singleInstanceSocket?.Dispose();
            _singleInstanceSocket = null;
            _singleInstanceSocketPath = null;
        }
    }

    /// <inheritdoc />
    public void Destroy()
    {
        // 停止系统级事件监听器。
        _systemEventWatcher?.Dispose();
        _systemEventWatcher = null;

        _mainLoop?.Quit();
        _singleInstanceSocket?.Dispose();
        _singleInstanceSocket = null;
        if (_singleInstanceSocketPath is not null && System.IO.File.Exists(_singleInstanceSocketPath))
        {
            try { System.IO.File.Delete(_singleInstanceSocketPath); } catch { }
        }
        _singleInstanceSocketPath = null;
        _singleInstanceLockStream?.Dispose();
        _singleInstanceLockStream = null;
        _singleInstanceLockPath = null;
    }

    /// <inheritdoc />
    public void SetApplicationMenu(Menu? menu)
    {
        if (!OperatingSystem.IsLinux() || menu is null)
        {
            _appMenu = null;
            return;
        }

        // 通过 LinuxMenu 构建 GMenu 模型与 ActionGroup（含所有菜单项回调）。
        // 对应 Go 版 application_linux_gtk3.go 中的菜单构建逻辑。
        // LinuxMenu 同时创建 SimpleAction 并连接 OnActivate 回调，使菜单项点击可响应。
        _appMenu = new LinuxMenu(menu);
    }

    /// <inheritdoc />
    public uint GetCurrentWindowId()
    {
        // 返回第一个窗口的 ID（简化实现）。
        foreach (var id in _windows.Keys)
        {
            return id;
        }

        return 0;
    }

    /// <inheritdoc />
    public void ShowAboutDialog(string name, string description, byte[]? icon)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        // 使用 Gtk.AlertDialog 显示关于对话框。
        var dialog = AlertDialog.NewWithProperties([]);
        dialog.SetMessage(name);
        if (!string.IsNullOrEmpty(description))
        {
            dialog.SetDetail(description);
        }

        dialog.SetButtons(new[] { "确定" });
        _ = dialog.ChooseAsync(null!);
    }

    /// <inheritdoc />
    public void SetIcon(byte[]? icon)
    {
        if (!OperatingSystem.IsLinux() || icon is null || icon.Length == 0)
        {
            return;
        }

        // GTK4 移除了 SetDefaultIconFromFile API，简化实现：将图标字节写入临时文件，
        // 并通过 Gtk.IconTheme 将临时目录加入搜索路径，以 SetDefaultIconName 设置应用默认图标。
        try
        {
            var tempDir = Path.GetTempPath();
            var tempPath = Path.Combine(tempDir, "wails-app-icon.png");
            System.IO.File.WriteAllBytes(tempPath, icon);

            var display = Display.GetDefault();
            if (display is not null)
            {
                var theme = IconTheme.GetForDisplay(display);
                theme.AddSearchPath(tempDir);
            }

            Window.SetDefaultIconName("wails-app-icon");
        }
        catch
        {
            // 忽略图标设置失败。
        }
    }

    /// <inheritdoc />
    public void On(uint id)
    {
        // 将平台事件分发到主线程并交由 Application.HandlePlatformEvent 处理。
        DispatchPlatformEventOnMainThread(id);
    }

    /// <inheritdoc />
    public void DispatchOnMainThread(uint id)
    {
        // 将事件 ID 分发到主线程执行。
        DispatchPlatformEventOnMainThread(id);
    }

    /// <summary>
    /// 将平台事件分发到 GTK 主线程（GLib.IdleAdd），由 Application.HandlePlatformEvent 处理。
    /// 非 Linux 环境下直接同步处理。
    /// </summary>
    /// <param name="id">平台事件 ID。</param>
    private static void DispatchPlatformEventOnMainThread(uint id)
    {
        if (!OperatingSystem.IsLinux())
        {
            global::Wails.Net.Application.Application.Get()?.HandlePlatformEvent(id);
            return;
        }

        GLib.Functions.IdleAdd(0, () =>
        {
            global::Wails.Net.Application.Application.Get()?.HandlePlatformEvent(id);
            return false;
        });
    }

    /// <inheritdoc />
    public void Hide()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        foreach (var window in _windows.Values)
        {
            window.Hide();
        }
    }

    /// <inheritdoc />
    public void Show()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        foreach (var window in _windows.Values)
        {
            window.Show();
        }
    }

    /// <inheritdoc />
    public void DispatchOnMainThread(Action action)
    {
        if (!OperatingSystem.IsLinux())
        {
            // 非 Linux 环境直接执行（测试环境兼容）。
            action();
            return;
        }

        // 使用 GLib.IdleAdd 将操作投递到 GTK 主循环。
        GLib.Functions.IdleAdd(0, () =>
        {
            action();
            return false;
        });
    }

    /// <inheritdoc />
    public void CreateWebviewWindow(uint id, WebviewWindowOptions options)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var window = new LinuxWebviewWindow(id, options);
        _windows[id] = window;

        // 若已设置应用菜单，将其应用到新窗口（注入 GMenu 模型与 ActionGroup）。
        if (_appMenu is not null)
        {
            window.SetApplicationMenu(_appMenu.MenuModel, _appMenu.ActionGroup);
        }
    }

    /// <inheritdoc />
    public async Task<int> ShowMessageDialog(string title, string message, DialogStyle style, string[] buttons)
    {
        if (!OperatingSystem.IsLinux())
        {
            return 0;
        }

        var dialog = AlertDialog.NewWithProperties([]);
        dialog.SetMessage(title);
        if (!string.IsNullOrEmpty(message))
        {
            dialog.SetDetail(message);
        }

        dialog.SetButtons(buttons.Length > 0 ? buttons : new[] { "确定" });
        return await dialog.ChooseAsync(null!).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public async Task<string?> OpenFileDialog(OpenFileDialogOptions options)
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        var dialog = FileDialog.New();
        if (!string.IsNullOrEmpty(options.Title))
        {
            dialog.SetTitle(options.Title);
        }

        if (!string.IsNullOrEmpty(options.Directory))
        {
            var initialFolder = Gio.Functions.FileNewForPath(options.Directory);
            dialog.SetInitialFolder(initialFolder);
        }

        try
        {
            var file = await dialog.OpenAsync(null!).ConfigureAwait(true);
            return file?.GetPath();
        }
        catch (Exception)
        {
            // 用户取消或发生错误时返回 null。
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<string?> SaveFileDialog(SaveFileDialogOptions options)
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        var dialog = FileDialog.New();
        if (!string.IsNullOrEmpty(options.Title))
        {
            dialog.SetTitle(options.Title);
        }

        if (!string.IsNullOrEmpty(options.Directory))
        {
            var initialFolder = Gio.Functions.FileNewForPath(options.Directory);
            dialog.SetInitialFolder(initialFolder);
        }

        if (!string.IsNullOrEmpty(options.Filename))
        {
            dialog.SetInitialName(options.Filename);
        }

        try
        {
            var file = await dialog.SaveAsync(null!).ConfigureAwait(true);
            return file?.GetPath();
        }
        catch (Exception)
        {
            // 用户取消或发生错误时返回 null。
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<string[]?> OpenMultipleFilesDialog(OpenFileDialogOptions options)
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        var dialog = FileDialog.New();
        if (!string.IsNullOrEmpty(options.Title))
        {
            dialog.SetTitle(options.Title);
        }

        if (!string.IsNullOrEmpty(options.Directory))
        {
            var initialFolder = Gio.Functions.FileNewForPath(options.Directory);
            dialog.SetInitialFolder(initialFolder);
        }

        try
        {
            var listModel = await dialog.OpenMultipleAsync(null!).ConfigureAwait(true);
            if (listModel is null)
            {
                return null;
            }

            var count = listModel.GetNItems();
            if (count == 0)
            {
                return null;
            }

            var files = new List<string>();
            for (uint i = 0; i < count; i++)
            {
                var file = listModel.GetObject(i) as File;
                if (file is not null)
                {
                    var path = file.GetPath();
                    if (!string.IsNullOrEmpty(path))
                    {
                        files.Add(path);
                    }
                }
            }

            return files.Count > 0 ? files.ToArray() : null;
        }
        catch (Exception)
        {
            // 用户取消或发生错误时返回 null。
            return null;
        }
    }

    /// <summary>
    /// 通过 Gio.Settings 读取指定 schema 的键值。
    /// 对应 Go 版 application_linux_dbus.go 中通过 D-Bus 读取 portal 设置的逻辑。
    /// 优先使用 Gio.Settings（GSettings C API 的直接绑定，无需子进程），
    /// 失败时回退到 gsettings 命令行子进程。
    /// </summary>
    /// <param name="schema">gsettings schema 名称。</param>
    /// <param name="key">键名称。</param>
    /// <returns>读取到的值（已去除首尾引号与空白），失败返回 null。</returns>
    private static string? RunGsettingsGet(string schema, string key)
    {
        // 优先使用 Gio.Settings 直接读取（避免子进程开销）。
        try
        {
            var settings = Gio.Settings.New(schema);
            if (settings is not null)
            {
                var value = settings.GetString(key);
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }
        }
        catch
        {
            // Gio.Settings 不可用（schema 未安装等）时回退到 gsettings 命令行。
        }

        // 回退：通过 gsettings 命令行子进程读取。
        try
        {
            var psi = new ProcessStartInfo("gsettings", $"get {schema} {key}")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(2000);
            var trimmed = output.AsSpan().Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '\'' && trimmed[^1] == '\'')
            {
                trimmed = trimmed[1..^1];
            }

            return trimmed.Length > 0 ? trimmed.ToString() : null;
        }
        catch
        {
            // gsettings 不可用或命令失败时返回 null。
            return null;
        }
    }

    /// <summary>
    /// 将 GNOME accent-color 名称映射为十六进制颜色字符串。
    /// </summary>
    /// <param name="name">强调色名称（如 blue、teal）。</param>
    /// <returns>十六进制颜色字符串，未知名称返回 null。</returns>
    private static string? MapAccentColorName(string name)
    {
        return name.Trim().ToLowerInvariant() switch
        {
            "blue" => "#3584E4",
            "teal" => "#2190A4",
            "green" => "#3A944A",
            "yellow" => "#C88800",
            "orange" => "#E66100",
            "red" => "#E01B24",
            "pink" => "#C5638E",
            "purple" => "#9141AC",
            "slate" => "#5F5F5F",
            "default" => "#0078D4",
            _ => null,
        };
    }

    /// <summary>
    /// 将任意字符串转换为安全的文件名（移除非法字符）。
    /// </summary>
    /// <param name="name">原始字符串。</param>
    /// <returns>仅含字母、数字、短横线的安全文件名。</returns>
    private static string SanitizeFileName(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
            {
                sb.Append(c);
            }
            else
            {
                sb.Append('-');
            }
        }

        return sb.ToString();
    }
}
