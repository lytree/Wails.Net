# 平台抽象层

## 概述

Wails.Net 通过**接口驱动的平台抽象**策略支持 Windows、Linux 与 Server（无 GUI）三种运行环境。核心程序集 `Wails.Net.Application` 仅包含平台无关的接口与默认实现（如 [ServerPlatformApp.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Platform/ServerMode/ServerPlatformApp.cs)），而具体的原生调用代码则分别位于 `Wails.Net.Application.Windows` 与 `Wails.Net.Application.Linux` 两个独立项目中。核心项目通过反射在运行时按需加载平台程序集，避免了对平台包的硬依赖，从而保证同一份核心代码可以在三平台之间复用。

平台抽象层的设计要点：

- **接口位于核心项目**：`IPlatformApp`、`IWebviewWindowImpl`、`IClipboardImpl`、`IMenuImpl`、`ISystemTrayImpl` 等接口都定义在 `Wails.Net.Application` 中。
- **实现位于平台项目**：Windows 平台使用 CsWin32 + WebView2；Linux 平台使用 GirCore 0.8.0 + GTK4 + WebKitGTK；Server 模式提供 no-op 桩实现。
- **托管封装（Managed Wrappers）策略**：不使用 C++/CLI 混合模式，所有原生调用均通过源生成器（CsWin32）或托管绑定（GirCore）完成。
- **工厂模式加载**：[PlatformFactory](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Platform/PlatformFactory.cs) 通过反射 `Assembly.Load` 加载平台程序集，确保核心项目不直接引用平台包。

## 核心接口

### IPlatformApp — 平台应用接口

[IPlatformApp.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Platform/IPlatformApp.cs) 定义平台应用顶层契约，对应 Wails v3 Go 版本中的 `platformApp` 接口。它涵盖主循环、单实例锁、窗口创建、对话框、屏幕信息、菜单、系统事件分发等能力：

```csharp
public interface IPlatformApp
{
    string Name { get; }
    int Run();                                                     // 主循环，阻塞直到退出
    bool AcquireSingleInstanceLock(string uniqueId) => true;       // 单实例锁（默认实现）
    void NotifySingleInstance(string[] args);                      // 通知已运行实例
    void Destroy();                                                // 销毁应用
    void SetApplicationMenu(Menu? menu);                           // 应用菜单
    uint GetCurrentWindowId();                                     // 当前活动窗口
    void ShowAboutDialog(string name, string description, byte[]? icon);
    void SetIcon(byte[]? icon);
    void On(uint id);                                              // 平台事件处理
    void DispatchOnMainThread(uint id);                            // 主线程事件分发
    void DispatchOnMainThread(Action action);                      // 主线程 Action 分发
    void CreateWebviewWindow(uint id, WebviewWindowOptions options); // 创建窗口
    Task<int> ShowMessageDialog(...);                              // 三类异步对话框
    Task<string?> OpenFileDialog(OpenFileDialogOptions options);
    Task<string?> SaveFileDialog(SaveFileDialogOptions options);
    Task<string[]?> OpenMultipleFilesDialog(OpenFileDialogOptions options);

    // 系统信息
    Screen? GetPrimaryScreen();
    Screen[] GetScreens();
    Dictionary<string, object?> GetFlags(ApplicationOptions options);
    bool IsOnMainThread();
    bool IsDarkMode();
    string GetAccentColor();
    void Hide();
    void Show();
}
```

注意 `CreateWebviewWindow` 直接由 `IPlatformApp` 提供 —— 桌面平台的窗口创建是平台应用的职责，而非 `PlatformFactory` 的工作（见下文）。

### IWebviewWindowImpl — Webview 窗口实现接口

[IWebviewWindowImpl.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Windows/IWebviewWindowImpl.cs) 对应 Go 版的 `webviewWindowImpl`，是平台抽象层中最大的接口。它包含窗口尺寸/位置/状态、菜单、调试、JS 执行、URL/HTML 加载、打印、拖拽、特效（Mica/Acrylic）、任务栏进度等约 60 个方法。接口提供了大量**默认实现方法**（default interface methods），用于渐进式扩展：

```csharp
public interface IWebviewWindowImpl
{
    // 必须实现的核心方法
    void SetTitle(string title);
    void SetSize(int width, int height);
    void Show();
    void Hide();
    void Close();
    void ExecJS(string js);
    void LoadURL(string url);
    bool IsVisible();
    (int Width, int Height) GetSize();

    // 默认空实现的可选方法（平台按需重写）
    void SetOpacity(float opacity) { }                    // Tauri v2 兼容
    void SetTaskbarProgress(TaskbarProgressState state,
                            ulong completed, ulong total) { } // Windows ITaskbarList3
    void SetEffects(WindowEffects effects) { }            // Mica/Acrylic/BlurBehind
    void SetOverlayIcon(byte[]? iconBytes, string? description) { }
    void RegisterCustomScheme(string scheme) { }
    Task<byte[]?> CapturePreviewAsync() => Task.FromResult<byte[]?>(null);
    // ... 更多默认实现
}
```

此设计借鉴了 Tauri v2 的 `WebviewWindow` API 形状，使平台实现可以只关注自身支持的功能，未实现的能力退化为 no-op。

### 子接口列表

平台抽象不仅覆盖顶层应用与窗口，还细分为多个职责清晰的子接口。下表列出每个接口的职责与各平台实现：

| 接口 | 命名空间 | Windows 实现 | Linux 实现 | Android 实现 | Server 实现 |
|------|---------|-------------|-----------|-------------|------------|
| `IPlatformApp` | `Wails.Net.Application.Platform` | `WindowsPlatformApp` | `LinuxPlatformApp` | `AndroidPlatformApp` | `ServerPlatformApp` |
| `IWebviewWindowImpl` | `Wails.Net.Application.Windows` | `Win32WebviewWindow` | `LinuxWebviewWindow` | `AndroidWebviewWindow` | `ServerWebviewWindow` |
| `IClipboardImpl` | `Wails.Net.Application.Clipboard` | `WindowsClipboard` | `LinuxClipboard` | `AndroidClipboard` | `ServerClipboard` |
| `IMenuImpl` | `Wails.Net.Application.Menus` | `Win32Menu` | `LinuxMenu` | （未实现，MenuRole 不可用） | （未实现） |
| `ISystemTrayImpl` | `Wails.Net.Application.SystemTray` | `Win32SystemTray` | `LinuxSystemTray` | （未实现） | （未实现） |
| `IKeyBindingManager` | `Wails.Net.Application.Managers` | `Win32KeyBindingManager` | `LinuxKeyBindingManager` | （未实现） | （未实现） |
| `IWebView` | `Wails.Net.Application.WebViews` | （由 `Win32WebviewWindow` 内嵌 `CoreWebView2` 实现） | （由 `LinuxWebviewWindow` 内嵌 WebKitGTK `WebView` 实现） | （由 `AndroidWebviewWindow` 内嵌 `Android.Webkit.WebView` 实现） | （无） |
| `IPlatformBiometric` | `Wails.Net.Application.Plugins.Mobile` | （未实现） | （未实现） | `AndroidBiometric` | （未实现） |
| `IPlatformNfc` | `Wails.Net.Application.Plugins.Mobile` | （未实现） | （未实现） | `AndroidNfc` | （未实现） |
| `IPlatformBarcodeScanner` | `Wails.Net.Application.Plugins.Mobile` | （未实现） | （未实现） | `AndroidBarcodeScanner` | （未实现） |
| `IPlatformHaptics` | `Wails.Net.Application.Plugins.Mobile` | （未实现） | （未实现） | `AndroidHaptics` | （未实现） |

> **Android 移动端插件抽象**：5 个移动端接口（`IPlatformBiometric` / `IPlatformNfc` / `IPlatformBarcodeScanner` / `IPlatformHaptics` + `AndroidRuntimePlugin` 平台类）的 Android 实现位于 [Wails.Net.Application.Android/Mobile](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Android/Mobile)，通过 `AndroidPlatformApp` 注入委托解耦 Activity 生命周期。

[IManagers.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Managers/IManagers.cs) 还包含一组与平台无关的管理器接口（`IWindowManager`、`IDialogManager`、`IEventManager`、`IScreenManager`、`ISystemTrayManager`、`IBrowserManager`、`IAutostartManager`、`IEnvironmentManager` 等），它们位于核心层，委托到上述平台子接口完成实际工作。

## PlatformFactory — 工厂模式与反射加载

[PlatformFactory.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Platform/PlatformFactory.cs) 是平台抽象层的入口。它的核心思路是**通过反射加载平台程序集，让核心项目对平台包保持零引用**：

```csharp
public static class PlatformFactory
{
    private const string ServerModeEnvVar = "WAILS_SERVER_MODE";

    public static IPlatformApp CreatePlatformApp(ApplicationOptions options)
    {
        if (IsServerMode())
            return new ServerPlatformApp(options);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var assembly = Assembly.Load("Wails.Net.Application.Windows");
            var type = assembly.GetType("Wails.Net.Application.Platform.WindowsPlatformApp")
                ?? throw new PlatformNotSupportedException("无法找到 WindowsPlatformApp 类型");
            return (IPlatformApp)Activator.CreateInstance(type, options)!;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var assembly = Assembly.Load("Wails.Net.Application.Linux");
            var type = assembly.GetType("Wails.Net.Application.Platform.LinuxPlatformApp")
                ?? throw new PlatformNotSupportedException("无法找到 LinuxPlatformApp 类型");
            return (IPlatformApp)Activator.CreateInstance(type, options)!;
        }

        throw new PlatformNotSupportedException($"不支持的平台: {RuntimeInformation.OSDescription}");
    }
    // ...
}
```

三个工厂方法的关键差异：

| 方法 | 行为 |
|------|------|
| `CreatePlatformApp` | Server 模式 → `ServerPlatformApp`；Windows/Linux → 反射加载平台程序集中的 `WindowsPlatformApp` / `LinuxPlatformApp` |
| `CreateClipboard` | 同上模式，加载 `WindowsClipboard` / `LinuxClipboard` / `ServerClipboard` |
| `CreateWebviewWindowImpl` | **仅 Server 模式可用**，返回 `ServerWebviewWindow`；桌面平台调用时抛 `PlatformNotSupportedException`，提示通过 `Application.Get().NewWebviewWindow` 创建窗口 |

最后一个方法的设计值得注意：桌面平台的窗口创建由 `IPlatformApp.CreateWebviewWindow` 直接处理，因为窗口对象需要由平台应用统一持有（用于菜单/图标应用、Hide/Show 遍历等）。`PlatformFactory.CreateWebviewWindowImpl` 只保留 Server 模式入口，是为了让 Server 模式也能构造窗口占位实例。

Server 模式检测非常简单：

```csharp
public static bool IsServerMode()
{
    var value = Environment.GetEnvironmentVariable(ServerModeEnvVar);
    return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
```

只要环境变量 `WAILS_SERVER_MODE=true`（不区分大小写），即进入 Server 模式。

## Windows 平台实现

### WindowsPlatformApp — WebView2 + Win32 API

[WindowsPlatformApp.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Windows/WindowsPlatformApp.cs) 是 Windows 平台的 `IPlatformApp` 实现，整合了 Win32 API、WinForms 对话框与系统注册表查询。关键实现：

- **主循环**：标准 Win32 消息循环 `GetMessage → TranslateMessage → DispatchMessage`，并在循环中处理线程级热键（`hwnd == NULL` 的 `WM_HOTKEY`）。
- **DPI 感知**：构造时优先调用 `SetProcessDpiAwarenessContext(Per-Monitor V2)`，失败时回退到 `SetProcessDPIAware`。
- **暗色模式**：通过 `HKCU\...\Themes\Personalize` 读取 `AppsUseLightTheme`，并通过 `DwmSetWindowAttribute(DwmwaUseImmersiveDarkMode = 20)` 应用到窗口标题栏。
- **强调色**：通过 `HKCU\Software\Microsoft\Windows\DWM` 读取 `AccentColor`（0xAARRGGBB）转换为 `#RRGGBB`。
- **对话框**：复用 WinForms 的 `MessageBox`、`OpenFileDialog`、`SaveFileDialog`，将 `DialogResult` 映射回按钮索引。
- **单实例锁**：通过 `CreateMutex` 命名互斥体实现；新实例通过 `PostMessage(WM_APP+1)` 通知已运行实例，命令行参数序列化为 JSON 写入临时文件由已运行实例读取。
- **主线程分发**：`ConcurrentQueue<Action>` + `PostMessage(WM_APP+2)`，WndProc 收到消息后排空队列。

```csharp
// WindowsPlatformApp.cs 中的消息循环片段
while ((int)PInvoke.GetMessage(out var msg, default, 0, 0) > 0)
{
    if (msg.hwnd.IsNull && msg.message == WmHotkey)
    {
        Application.Get()?.KeyBindingManager?.HandleHotKey((int)msg.wParam.Value);
        continue;
    }
    PInvoke.TranslateMessage(in msg);
    PInvoke.DispatchMessage(in msg);
}
```

### Win32WebviewWindow — CoreWebView2 封装

[Win32WebviewWindow.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Windows/Win32WebviewWindow.cs) 是 Windows 平台的 `IWebviewWindowImpl` 实现。关键设计：

- **窗口创建**：通过 `CreateWindowEx` 注册窗口类 `"WailsNetWebviewWindow"`，使用 `WNDPROC` 委托（保存在静态字段以防 GC 回收）。
- **WebView2 异步初始化**：使用 `TaskCompletionSource<bool>` 等待 `CoreWebView2Controller` 初始化完成。
- **静态实例表**：`Dictionary<IntPtr, Win32WebviewWindow>` 按 HWND 索引，WndProc 通过 HWND 反查实例分发消息。
- **消息处理**：覆盖 30+ Win32 消息，包括 `WM_SIZE`、`WM_GETMINMAXINFO`（最小/最大尺寸约束）、`WM_DPICHANGED`、`WM_DROPFILES`（拖放）、`WM_SETTINGCHANGE`（主题变化）、`WM_CLIPBOARDUPDATE`、`WM_KEYDOWN`（F12 打开 DevTools）等。
- **拖拽与缩放**：通过 `HTCAPTION` / `HTBOTTOMRIGHT` 命中测试值实现无标题栏拖动与右下角缩放。

### CsWin32 源生成器

按项目规范（AGENTS.md §1.1），Windows 平台**禁止使用 `PInvoke.*` 包**，必须通过 `Microsoft.Windows.CsWin32` 源生成器生成 Win32 绑定。所有 Win32 调用都通过 `Windows.Win32.PInvoke.*` 静态方法访问，例如：

```csharp
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;

PInvoke.GetMessage(out var msg, default, 0, 0);
PInvoke.PostMessage(hwnd, WmAppPlatformEvent, (WPARAM)(nuint)id, default);
PInvoke.DwmSetWindowAttribute(hwnd, (DWMWINDOWATTRIBUTE)DwmwaUseImmersiveDarkMode, &value, ...);
```

CsWin32 在编译期根据 NativeMethods.txt 中的目标 API 名称生成强类型绑定，避免运行时反射与手写 `DllImport` 的维护成本。

## Linux 平台实现

### LinuxPlatformApp — GTK4 + WebKitGTK

[LinuxPlatformApp.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Linux/LinuxPlatformApp.cs) 使用 GirCore 0.8.0 提供的 GTK4 托管绑定。关键设计：

- **GTK 初始化**：`EnsureGtkInitialized()` 使用 `Interlocked.Exchange` 保证 `Gtk.Functions.Init()` 仅调用一次，必须在 UI 线程上执行。
- **主循环**：通过 `GLib.MainLoop` 驱动，`Run()` 阻塞直到 `Destroy()` 调用 `_mainLoop.Quit()`。
- **主题读取**：优先通过 `Gio.Settings`（gsettings C API）读取 `org.gnome.desktop.interface` schema 的 `color-scheme` 键，失败时回退到 `gsettings` 命令行子进程，再回退到 `GTK_THEME` / `COLOR_SCHEME` 环境变量。
- **强调色映射**：将 GNOME 的 `accent-color` 名称（如 `blue`、`teal`）映射为十六进制颜色字符串。
- **屏幕信息**：通过 `Gdk.Display.GetDefault().GetMonitors()` 枚举显示器，从 `Monitor.Geometry` 与 `Monitor.ScaleFactor` 构造 `Screen` 实例。
- **对话框**：使用 GTK4 `AlertDialog`（消息框）与 `FileDialog`（文件选择）。
- **主线程分发**：通过 `GLib.Functions.IdleAdd` 将回调投递到 GTK 主循环。
- **单实例锁**：`FileStream`（独占 + `DeleteOnClose`）+ UNIX domain socket 监听后续实例的 JSON 通知。

```csharp
// LinuxPlatformApp.cs 中的主题读取片段
var scheme = RunGsettingsGet(GnomeInterfaceSchema, "color-scheme");
if (scheme is not null && scheme.Contains("dark", StringComparison.OrdinalIgnoreCase))
    return true;
```

### LinuxWebviewWindow — WebKitGTK 封装

[LinuxWebviewWindow.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Linux/LinuxWebviewWindow.cs) 是 Linux 平台的 `IWebviewWindowImpl` 实现。关键设计：

- **窗口组合**：`Gtk.Window` 作为顶层容器，`Gtk.Box` 内嵌 `PopoverMenuBar`（应用菜单）与 `WebKit.WebView`。
- **WebKitGTK**：通过 `WebKit.WebView` 加载网页内容，支持 HTML/URL 加载、JS 执行、CSS 注入、前进后退、DevTools。
- **GirCore 缺口填补**：GirCore 0.8.0 未生成 `gdk_file_list_get_files` 等少量 API，`LinuxWebviewWindow` 通过手写 `DllImport` 补齐（仅在原生绑定缺失时使用，非通用做法）。

### wails:// 自定义协议注册

为避免 `file://` 协议带来的安全与跨域问题，Linux 实现仿照 Wails v3 / Tauri v2 注册了 `wails://` 自定义 URI scheme。由于所有窗口共享同一 `WebContext`，注册只执行一次：

```csharp
private static bool _wailsSchemeRegistered;

private void EnsureWailsSchemeRegistered()
{
    if (_wailsSchemeRegistered) return;
    _wailsSchemeRegistered = true;

    var context = _webView.GetContext();
    context.RegisterUriScheme("wails", OnWailsSchemeRequest);
}

// 加载静态资源时导航到 wails://localhost/
const string wailsUrl = "wails://localhost/";
_webView?.LoadUri(wailsUrl);
```

请求由 `OnWailsSchemeRequest` 处理，从 `Application.AssetServer` 读取静态资源并通过 `URISchemeResponse` 返回。

## Android 平台实现

### AndroidPlatformApp — .NET Android + WebView

[AndroidPlatformApp](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Android/AndroidPlatformApp.cs) 是 Android 平台的 `IPlatformApp` 实现，使用 .NET Android 工作负载（`net10.0-android36.0`，最低 API Level 24）直接调用 `Android.Webkit.WebView` 等原生 API。关键设计：

- **Activity 模型**：`MainActivity` 继承 `WailsActivity`，在 `OnCreate` 中初始化 `AndroidPlatformApp`，并通过 `AndroidApplicationExtensions` 提供扩展方法。
- **WebView**：通过 `Android.Webkit.WebView` 加载前端，使用 `WebViewClient.ShouldInterceptRequest` 拦截 `wails://` 资源请求并委托到 `AssetServer`；通过 `WebMessageListener` 实现 IPC 双向通信。
- **主线程分发**：通过 `Handler(Looper.MainLooper)` 将回调投递到 Android 主线程。
- **单实例锁**：Android 系统默认单 Activity 实例，无需额外锁。
- **平台事件**：[AndroidPlatformEvents](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Android/AndroidPlatformEvents.cs) 定义 12 个 Android 平台事件 ID（1267–1273, 1281–1285），对应 Activity 生命周期、电池、网络、主题、屏幕锁定等系统广播。`MapToCommonEvent(uint)` 将其中 7 个事件映射到公共 `ApplicationEventType`，由 `HandlePlatformEvent(uint)` 调用 `Application.Get()?.HandlePlatformEvent` 分发到事件系统。

### AndroidWebviewWindow — Android.Webkit.WebView 封装

`AndroidWebviewWindow` 是 Android 平台的 `IWebviewWindowImpl` 实现，将 `Android.Webkit.WebView` 包装为 Wails.Net 窗口模型。关键能力：
- HTML/URL 加载、JS 执行（`EvaluateJavascript`）、CSS 注入、前进后退、DevTools（`setWebContentsDebuggingEnabled`）。
- 通过 `WebMessageListener` 接收前端 `postMessage`，转发到 `MessageProcessor`。

### 移动端插件平台后端

5 个移动端插件的 Android 实现位于 [Wails.Net.Application.Android/Mobile](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Android/Mobile)：

| 平台类 | 底层 Android API | 关键设计 |
|------|------|------|
| `AndroidBiometric` | `BiometricManager`（API 29+）+ `BiometricPrompt`（API 28+） | 通过 `Func<CancellationToken, Task<bool>>` 委托注入解耦 `FragmentActivity` |
| `AndroidNfc` | `NfcAdapter` + `Activity.OnNewIntent` | 通过读写委托注入，非 Android 环境降级 no-op |
| `AndroidBarcodeScanner` | `Intent.ActionGetContent` + 第三方扫描应用 | 通过 `Func<CancellationToken, Task<string>>` 委托注入 |
| `AndroidHaptics` | `Vibrator` / `VibratorManager`（API 31+） + `VibrationEffect`（API 26+） | 平台守卫用 `OperatingSystem.IsAndroidVersionAtLeast(int)` |
| `AndroidRuntimePlugin` | `Android.OS.Build` + `Toast.MakeText` | 提供 `device.info` / `toast.show` 命令，对应 Wails v3 `androidDeviceInfo` / `androidShowToast` |

**委托注入模式**：Activity 生命周期相关 API（`BiometricPrompt`、`OnNewIntent`、`OnActivityResult`）无法在单元测试中直接触发，因此平台类通过构造函数接受 `Func<...>` / `Action` 委托参数。`AndroidPlatformApp` 在 Activity 可用时注入实际实现，单元测试环境使用默认 null 委托降级为 no-op。这使 `Wails.Net.Application.Android.Tests` 可在 Windows 上运行（无需模拟器），详见 AGENTS.md §4.4。

## Server 模式

Server 模式提供无 GUI 的 no-op 实现，用于容器化部署、CI/CD 测试与无头环境。三个核心桩实现均位于 `Wails.Net.Application.Platform.ServerMode` 命名空间：

### ServerPlatformApp

[ServerPlatformApp.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Platform/ServerMode/ServerPlatformApp.cs) 的核心是使用 `ManualResetEventSlim` 模拟阻塞主循环：

```csharp
private readonly ManualResetEventSlim _shutdownEvent = new(initialState: false);

public int Run()
{
    _shutdownEvent.Wait();   // 阻塞直到 SignalShutdown 被调用
    return 0;
}

public void SignalShutdown() => _shutdownEvent.Set();
```

所有 GUI 操作为空，`IsDarkMode()` 返回 `false`，`GetAccentColor()` 返回 `"#000000"`，对话框返回默认值（0 / null / 空数组）。

### ServerWebviewWindow

[ServerWebviewWindow.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Platform/ServerMode/ServerWebviewWindow.cs) 是最大的桩类 —— 为 `IWebviewWindowImpl` 的所有方法提供空实现，状态查询返回 `false` / `(0, 0)` / 空字符串。

### ServerClipboard

[ServerClipboard.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Platform/ServerMode/ServerClipboard.cs) 的 `GetText()` 返回 `""`，`GetImage()` 返回 `null`，`GetFiles()` 返回空数组。

**用途**：

- **容器化部署**：在 Docker 容器中运行无 GUI 后端服务，仍能复用绑定、事件、IPC 等核心能力。
- **CI/CD 测试**：单元测试和集成测试可在无头 CI 环境运行（参见 AGENTS.md §4.4 中跨平台测试策略）。
- **降级运行**：当原生依赖缺失时，应用可优雅降级为 Server 模式而非崩溃。

## WebView 抽象 — IWebView

[IWebView.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/WebView/IWebView.cs) 是平台无关的 WebView 抽象接口，统一 Windows WebView2 与 Linux WebKitGTK 的事件模型。它定义了所有 WebView 共有的能力：

```csharp
public interface IWebView : IDisposable
{
    uint WindowId { get; }
    string Url { get; }
    string Title { get; }
    bool CanGoBack { get; }
    bool CanGoForward { get; }
    WebViewLoadStatus LoadStatus { get; }

    Task NavigateAsync(string url);
    Task LoadHtmlAsync(string html);
    Task<string> ExecuteScriptAsync(string javascript);
    Task InjectCssAsync(string css);
    Task PostMessageAsync(string json);
    void SetZoom(double zoom);
    void GoBack();
    void GoForward();
    void Reload();
    void Stop();
    void OpenDevTools();
    void CloseDevTools();

    event EventHandler<WebViewMessageEventArgs>? MessageReceived;
    event EventHandler<WebViewNavigationEventArgs>? NavigationStarted;
    event EventHandler<WebViewNavigationEventArgs>? NavigationCompleted;
    event EventHandler<string>? TitleChanged;
}
```

`IWebView` 与 `IWebviewWindowImpl` 的关系：`IWebviewWindowImpl` 是面向窗口的完整契约（包含窗口管理、菜单、特效等），而 `IWebView` 仅抽象 WebView 本身的能力，便于在非窗口场景下复用（如未来嵌入到其他容器）。当前 Windows 与 Linux 的 `IWebviewWindowImpl` 实现分别内嵌 `CoreWebView2` 与 WebKitGTK `WebView`，并未直接实现 `IWebView`，但通过相同的语义模型（加载、执行、消息、导航事件）保持一致性。

## 跨平台设计决策 — Managed Wrappers 而非 C++/CLI

AGENTS.md §1.2 明确规定 Wails.Net 采用 **Managed Wrappers（托管封装）** 策略：

| 维度 | 选择 | 原因 |
|------|------|------|
| Windows 互操作 | CsWin32 源生成器 | 编译期生成强类型绑定，避免运行时反射；与 .NET 10 SDK 深度集成；社区活跃 |
| Linux 互操作 | GirCore 0.8.0 | 基于 GObject Introspection 自动生成 GTK4/WebKitGTK/GIO 绑定；维护成本低 |
| 混合模式 | **禁止 C++/CLI** | C++/CLI 仅 Windows 可用，破坏跨平台一致性；构建工具链复杂；与 .NET 10 兼容性差 |

### 为何不使用 C++/CLI？

1. **跨平台限制**：C++/CLI 仅在 Windows 上可用，与项目同时支持 Windows + Linux 的目标冲突。
2. **构建复杂度**：C++/CLI 需要混合原生 C++ 编译器与 .NET 编译器，引入 MSBuild 复杂性与调试困难。
3. **AOT 兼容性**：.NET 10 推进 Native AOT 时，C++/CLI 是已知的不兼容点；Managed Wrappers 与源生成器策略天然 AOT 友好。
4. **维护成本**：CsWin32 与 GirCore 都通过源生成器/绑定生成器在编译期产出强类型代码，原生 API 升级时只需更新生成器配置，无需手写 `DllImport`。

### 为何禁止 PInvoke.* 包？

AGENTS.md §7.4 明确禁止 `PInvoke.*` 包。CsWin32 是其官方继任者，由 Windows 团队维护，优势包括：

- **按需生成**：通过 `NativeMethods.txt` 声明所需 API，源生成器自动生成所有依赖类型。
- **签名正确性**：自动处理 `SAFEARRAY`、`COM` 接口、`SafeHandle` 等复杂场景。
- **AOT 与 trimming 友好**：生成静态代码，无运行时反射，符合 .NET 10 的 AOT 路线。

> 例外：`LinuxWebviewWindow` 中存在少量手写 `DllImport`（如 `gdk_file_list_get_files`），这是 GirCore 0.8.0 绑定缺口的临时补救，仅用于弥补生成器未覆盖的 API，并非通用策略。

## 小结

Wails.Net 平台抽象层通过**接口分层 + 反射工厂 + 托管封装**三层设计，实现了核心代码与平台代码的彻底解耦：

- **接口层**（`IPlatformApp`、`IWebviewWindowImpl`、子接口）位于核心项目，定义平台无关契约。
- **工厂层**（`PlatformFactory`）通过 `Assembly.Load` 反射加载平台程序集，避免核心项目对平台包的硬依赖。
- **实现层**：Windows（CsWin32 + WebView2）、Linux（GirCore + GTK4 + WebKitGTK）、Server（no-op 桩）三套实现各自独立，互不引用。

Server 模式作为降级路径，保证应用在无 GUI 环境下仍能运行核心能力，是 CI/CD 与容器化部署的关键支撑。`IWebView` 接口进一步抽象 WebView 本身，为未来扩展场景（如嵌入式 WebView、无窗口 WebView）预留了空间。
