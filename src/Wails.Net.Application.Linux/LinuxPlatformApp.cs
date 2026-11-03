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
using Wails.Net.Application.Windows;
using WebKit;
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
/// 单实例对齐 Go 语义：由应用自身 ID 派生并占用 D-Bus bus name（<c>uniqueID + ".SingleInstance"</c>），
/// 同时保留锁文件 + UNIX socket 作为参数通知通道与 D-Bus 不可用时的降级路径；
/// 应用菜单通过 GMenu 模型构建。
/// GTK application id 覆盖（beta.23 #5972）的推导/校验见 <see cref="LinuxApplicationId"/>。
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
    /// Linux 平台特定选项，全局生效于所有窗口共享的 WebKitGTK 环境。
    /// 在构造时从 <see cref="ApplicationOptions.Linux"/> 读取，传入 <see cref="LinuxWebviewWindow"/>。
    /// </summary>
    private readonly LinuxOptions? _linuxOptions;

    /// <summary>
    /// 主线程 ID，用于 IsOnMainThread 判断。
    /// 在 <see cref="Run"/> 方法启动时设置，因为 UI 线程可能不是构造时的线程。
    /// </summary>
    private int _mainThreadId;

    /// <summary>
    /// GTK 是否已初始化的标志（0=未初始化，1=已初始化）。
    /// 使用 Interlocked 确保线程安全的单次初始化。
    /// </summary>
    private static int _gtkInitialized;

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
    /// 单实例占用的 D-Bus bus name 所对应的连接（由 <c>org.freedesktop.DBus.RequestName</c> 获得）。
    /// 首实例持有期间保持存活；释放该连接即隐式释放所占用名字。
    /// 对应 Go single_instance_linux.go 中 linuxLock 持有的 D-Bus 连接。
    /// </summary>
    private DBusConnection? _singleInstanceDbusConnection;

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
        _linuxOptions = options.Linux;

        // 记录构造线程为主线程 ID，供 IsOnMainThread 判断使用。
        // 应用总是在主线程上构造，Run() 也复用同一线程启动 GTK 主循环，
        // 因此此处捕获即可，无需等待 Run() 执行（Run 在非 Linux 平台会直接抛异常，
        // 此前导致 _mainThreadId 始终为 0，IsOnMainThread 恒返回 false 的缺陷）。
        _mainThreadId = Environment.CurrentManagedThreadId;

        // 解析 GTK application id 与程序名（纯逻辑，对应 Go application_linux_appid.go 的
        // applicationID()/programName()，beta.23 #5972）。
        // 覆盖来源为 LinuxApplicationId 的静态属性：核心库 LinuxOptions 未提供
        // ApplicationID/ProgramName 字段（本任务约束下不修改核心库），详见该类注释。
        ApplicationId = LinuxApplicationId.ResolveApplicationId(
            _name, LinuxApplicationId.ApplicationIdOverride, out var applicationIdError);
        ApplicationIdError = applicationIdError;
        ProgramName = LinuxApplicationId.ResolveProgramName(
            LinuxApplicationId.ProgramNameOverride, LinuxApplicationId.ApplicationIdOverride, ApplicationId);
    }

    /// <summary>
    /// 解析后的 GTK application id（对应 Go application_linux_appid.go 的 <c>applicationID()</c>）。
    /// 未设置覆盖时为 <c>"org.wails." + SanitizeAppName(Name)</c>；设置了合法覆盖时为覆盖值；
    /// 覆盖值非法时回退为推导 id（原因见 <see cref="ApplicationIdError"/>）。
    /// </summary>
    internal string ApplicationId { get; }

    /// <summary>
    /// 交给 GTK <c>g_set_prgname()</c> 的程序名（对应 Go <c>programName()</c>）。
    /// 为 <c>null</c> 时不设置程序名，保留 GTK 从可执行文件名推断的默认值。
    /// </summary>
    internal string? ProgramName { get; }

    /// <summary>
    /// application id 覆盖值被 GTK 拒绝时的原因（对应 Go 的错误处理器上报）。
    /// 无错误时为 <c>null</c>；不影响启动，应用继续使用推导出的回退 id。
    /// </summary>
    internal string? ApplicationIdError { get; }

    /// <inheritdoc />
    public string Name => _name;

    /// <inheritdoc />
    /// <remarks>
    /// Linux 通过 GTK4（GirCore 0.8.0）+ WebKitGTK 提供原生支持：
    /// <list type="bullet">
    /// <item><see cref="PlatformCapabilities.HasNativeDrag"/>：<c>true</c>，GTK4 原生支持拖放。</item>
    /// <item><see cref="PlatformCapabilities.GtkVersion"/>：通过 <c>Gtk.Functions.GetMajorVersion()</c> 获取（通常为 4）。</item>
    /// <item><see cref="PlatformCapabilities.WebKitVersion"/>：通过 <c>WebKit.Functions.GetMajorVersion()</c>/<c>GetMinorVersion()</c>/<c>GetMicroVersion()</c> 拼接。</item>
    /// </list>
    /// 在非 Linux 平台调用时返回 <see cref="PlatformCapabilities.Default"/>。
    /// </remarks>
    public PlatformCapabilities Capabilities
    {
        get
        {
            if (!OperatingSystem.IsLinux())
            {
                return PlatformCapabilities.Default;
            }

            try
            {
                return new PlatformCapabilities
                {
                    HasNativeDrag = true,
                    GtkVersion = (int)Gtk.Functions.GetMajorVersion(),
                    WebKitVersion = $"{(int)WebKit.Functions.GetMajorVersion()}.{(int)WebKit.Functions.GetMinorVersion()}.{(int)WebKit.Functions.GetMicroVersion()}",
                };
            }
            catch
            {
                // GirCore 调用失败时回退到默认值（如 GTK 未初始化）
                return new PlatformCapabilities
                {
                    HasNativeDrag = true,
                    GtkVersion = 4,
                    WebKitVersion = string.Empty,
                };
            }
        }
    }

    /// <inheritdoc />
    public bool IsOnMainThread()
    {
        return Environment.CurrentManagedThreadId == _mainThreadId;
    }

    /// <inheritdoc />
    public bool IsDarkMode()
    {
        // Wails v3.1.0 #6072：优先读取 xdg-desktop-portal 的 color-scheme，
        // 兼容 KDE 等不提供 GNOME gsettings schema 的桌面。
        var portalDark = LinuxAppearancePortal.TryReadColorSchemeIsDark();
        if (portalDark is not null)
        {
            return portalDark.Value;
        }

        // 在 Linux 上其次通过 D-Bus（gsettings）读取 color-scheme。
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

        // 在 UI 线程启动时记录线程 ID。
        _mainThreadId = Environment.CurrentManagedThreadId;

        // 确保 GTK 已初始化（在 UI 线程上）。
        EnsureGtkInitialized();

        // 将程序名交给 GTK 的 g_set_prgname()（薄封装原生边界调用）。
        // 对应 Go application_linux.go 中 newPlatformApp 内的 setProgramName()。
        // Wayland 下窗口的 surface app_id 取自 g_get_prgname()，因此设置程序名能使窗口
        // 与 .desktop 文件匹配；ProgramName 为 null 时保持未设置，沿用可执行文件名推断值。
        // 注意：Wails.Net Linux 未使用 Gtk.Application（而是 GLib.MainLoop），故 application id
        // 无法挂到 gtk_application_new()，只能通过程序名间接体现（差异说明见 LinuxApplicationId）。
        var programName = ProgramName;
        if (!string.IsNullOrEmpty(programName))
        {
            try
            {
                GLib.Functions.SetPrgname(programName);
            }
            catch
            {
                // 原生调用失败时忽略：程序名仅影响窗口分组，不影响启动。
            }
        }

        // 启动系统级事件监听器（网络连通性变化）。
        _systemEventWatcher = new LinuxSystemEventWatcher();
        _systemEventWatcher.Start();

        _mainLoop = MainLoop.New(GLib.Functions.MainContextDefault(), false);
        _mainLoop.Run();
        return 0;
    }

    /// <summary>
    /// 确保 GTK 已初始化。使用 Interlocked 实现线程安全的单次初始化。
    /// 必须在创建任何 GTK 窗口或控件之前调用。
    /// </summary>
    internal static void EnsureGtkInitialized()
    {
        if (Interlocked.Exchange(ref _gtkInitialized, 1) == 0)
        {
            // 在 GTK 初始化前根据环境选择合适的显示后端。
            // WSLg 的 Wayland 实现存在 seat/shared_memory 不完整问题，
            // 默认回退到 X11 以避免 gdk_seat_get_keyboard 等错误。
            // 用户显式设置 GDK_BACKEND 时尊重用户选择。
            SelectDisplayBackend();

            // GirCore 0.8.0 要求先调用 Module.Initialize() 注册 DllImportResolver，
            // 否则 P/Invoke 无法将 "Gtk" 等 GirCore 内部库名映射到实际的 libgtk-4.so.0。
            // WebKit.Module.Initialize() 会级联初始化所有依赖模块：
            //   WebKit → Gtk → Gdk/Gsk → GdkPixbuf/Gio/Pango/PangoCairo/Cairo
            //   WebKit → JavaScriptCore
            //   WebKit → Soup
            // 内部会调用 gtk_init() 完成 GTK4 初始化。
            WebKit.Module.Initialize();
        }
    }

    /// <summary>
    /// 选择合适的 GDK 显示后端。
    /// 策略：
    /// 1. 用户已通过 <c>GDK_BACKEND</c> 显式指定时，尊重用户选择；
    /// 2. 检测到 WSLg 环境（<c>WSL_DISTRO_NAME</c> 存在或 <c>/mnt/wslg</c> 存在）时，
    ///    默认设置 <c>GDK_BACKEND=x11</c>，因为 WSLg 的 Wayland 实现存在 seat 问题；
    /// 3. 其他环境不设置，由 GTK 自动选择（Wayland 优先，X11 回退）。
    /// 对应 Wails v3 Go 版本在 WSL2 调试场景下的 X11 回退策略。
    /// </summary>
    private static void SelectDisplayBackend()
    {
        // 已显式指定时尊重用户选择。
        var current = Environment.GetEnvironmentVariable("GDK_BACKEND");
        if (!string.IsNullOrEmpty(current))
        {
            return;
        }

        // 检测 WSLg 环境：WSL_DISTRO_NAME 环境变量或 /mnt/wslg 目录存在。
        var wslDistro = Environment.GetEnvironmentVariable("WSL_DISTRO_NAME");
        var wslgExists = System.IO.Directory.Exists("/mnt/wslg");
        var isWslg = !string.IsNullOrEmpty(wslDistro) || wslgExists;

        if (isWslg)
        {
            // WSLg 的 Wayland 实现存在 seat/shared_memory 问题，
            // 默认使用 X11 后端以避免 gdk_seat_get_keyboard 等错误。
            // 用户可通过显式设置 GDK_BACKEND=wayland 强制使用 Wayland。
            Environment.SetEnvironmentVariable("GDK_BACKEND", "x11");
        }

        // 非 WSLg 环境不设置 GDK_BACKEND，由 GTK 自动选择（Wayland 优先，X11 回退）。
    }

    /// <inheritdoc />
    /// <remarks>
    /// Linux 单实例实现（对齐 Go 版 single_instance_linux.go，beta.23 #5973）：
    /// <list type="number">
    /// <item>由 <paramref name="uniqueId"/> 派生 D-Bus bus name <c>uniqueID + ".SingleInstance"</c>，
    /// 通过 <c>org.freedesktop.DBus.RequestName</c> 以应用自身 ID 占位（<c>DO_NOT_QUEUE</c>）；</item>
    /// <item>bus name 已被他人持有时返回 <c>false</c>：本进程为第二实例，由
    /// <see cref="Application"/> 通知首实例后按 <c>SingleInstanceExitCode</c> 退出，语义与 Go 的
    /// <c>alreadyRunningError</c> 一致；</item>
    /// <item>D-Bus 不可用（无 session bus、沙箱拒绝该名字等）时降级为既有「独占锁文件 + UNIX socket」
    /// 机制而不中断启动 —— 这是与 Go 的差异：Go 在该情形上报致命错误；</item>
    /// <item>占位成功（首实例）时仍保留锁文件 + socket，以维持既有的参数通知通道。</item>
    /// </list>
    /// </remarks>
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
            // 1) 以应用自身 ID 派生 D-Bus bus name 并尝试占位（对齐 Go single_instance_linux.go）。
            if (LinuxSingleInstanceNames.TryDerive(uniqueId, out var busName, out _, out _, out _))
            {
                switch (LinuxDbusSingleInstance.TryClaim(busName, out var dbusConnection))
                {
                    case LinuxDbusSingleInstance.BusNameClaimResult.OwnedByOther:
                        // bus name 已由首实例持有：本进程是第二实例。
                        return false;
                    case LinuxDbusSingleInstance.BusNameClaimResult.Owned:
                        _singleInstanceDbusConnection = dbusConnection;
                        break;
                    case LinuxDbusSingleInstance.BusNameClaimResult.Unavailable:
                        // D-Bus 不可用：未持有名字（TryClaim 已确保该情形下输出连接为 null），
                        // 降级为仅锁文件 + socket。
                        break;
                }
            }

            // 2) 保留既有「独占锁文件 + UNIX socket」语义。
            // 以独占方式创建锁文件，若已有实例持有则抛出 IOException 表示非首实例。
            _singleInstanceLockStream = new FileStream(
                lockPath, FileMode.Create, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            _singleInstanceLockPath = lockPath;

            // 启动 UNIX socket 监听，接收后续实例的实时通知。
            // 对应 Go 版 single_instance_linux.go 中的 D-Bus 导出对象通知通道。
            StartSingleInstanceSocket(safeName);
            return true;
        }
        catch
        {
            // 获取失败：释放已占用的 D-Bus bus name，按第二实例处理。
            ReleaseSingleInstanceDbusName();
            _singleInstanceLockStream = null;
            _singleInstanceLockPath = null;
            return false;
        }
    }

    /// <summary>
    /// 释放 single-instance 占用的 D-Bus bus name。
    /// 对应 Go linuxLock.release()（Go 依赖连接关闭隐式释放名字，本实现显式释放连接以达到同等效果）。
    /// </summary>
    private void ReleaseSingleInstanceDbusName()
    {
        _singleInstanceDbusConnection?.Dispose();
        _singleInstanceDbusConnection = null;
    }

    /// <inheritdoc />
    /// <remarks>
    /// 通过 UNIX socket 向首实例投递命令行参数（失败时回退为锁文件同目录下的 <c>.args</c> 文件）。
    /// 与 Go 的差异：Go 的 <c>linuxLock.notify</c> 通过 D-Bus 调用首实例导出的
    /// <c>SendMessage</c> 方法后按 <c>ExitCode</c> 退出；Wails.Net 保留既有 socket 通道，
    /// 退出码由 <see cref="Application"/> 依据 <c>SingleInstanceExitCode</c> 统一处理。
    /// </remarks>
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

                        // 通过 Application 集中入口分发 SecondInstanceLaunched 事件并触发用户回调。
                        // 对应主题 D：替换魔法字符串为 KnownEvents.SecondInstanceLaunched 常量。
                        Application.Get()?.RaiseSecondInstanceLaunched(args);
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

        // 释放 D-Bus bus name（关闭连接即释放所占用的名字，对应 Go linuxLock.release）。
        ReleaseSingleInstanceDbusName();

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
    public IWebviewWindowImpl? CreateWebviewWindow(uint id, WebviewWindowOptions options)
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        // 将应用级 LinuxOptions 传入窗口，用于配置 WebKitGTK 环境（用户数据目录、附加参数等）。
        var window = new LinuxWebviewWindow(id, options, _linuxOptions);
        _windows[id] = window;

        // 若已设置应用菜单，将其应用到新窗口（注入 GMenu 模型与 ActionGroup）。
        if (_appMenu is not null)
        {
            window.SetApplicationMenu(_appMenu.MenuModel, _appMenu.ActionGroup);
        }

        // 显示窗口。GTK 窗口创建后默认不可见，必须显式调用 Present() 才能显示。
        // 对应 Wails v3 Go 版本中窗口创建后立即显示的行为。
        window.Show();

        return window;
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
