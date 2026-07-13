# 窗口管理实现

> 本文描述 Wails.Net 窗口管理子系统的设计与实现，对应 Wails v3 的 `webview_window.go` 及平台相关文件。

## 1. 概述

Wails.Net 的窗口管理采用三层架构，遵循"接口驱动的平台抽象"原则：

```
┌──────────────────────────────────────────────┐
│  WebviewWindow（公共 API，平台无关）          │
│  - 提供统一的窗口操作方法                       │
│  - 转发到 IWebviewWindowImpl 实现              │
└──────────────────┬───────────────────────────┘
                   │ 委托
┌──────────────────▼───────────────────────────┐
│  IWebviewWindowImpl（平台抽象接口）           │
│  - 定义平台无关的窗口操作契约                   │
│  - 提供默认空实现，平台按需重写                 │
└──────┬──────────────────────────┬────────────┘
       │                          │
┌──────▼──────────────┐  ┌────────▼────────────┐
│ Win32WebviewWindow  │  │ LinuxWebviewWindow  │
│ - WebView2 + Win32  │  │ - WebKitGTK + GTK4   │
└─────────────────────┘  └─────────────────────┘
```

- **公共层**：[WebviewWindow.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Windows/WebviewWindow.cs) 暴露给应用开发者，承载所有窗口操作方法。
- **抽象层**：[IWebviewWindowImpl.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Windows/IWebviewWindowImpl.cs) 定义平台契约，并对可选项提供默认空实现。
- **实现层**：[Win32WebviewWindow.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Windows/Win32WebviewWindow.cs) 与 [LinuxWebviewWindow.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Linux/LinuxWebviewWindow.cs)。

窗口创建通过 [WindowManager.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Managers/WindowManager.cs) 集中管理，前端通过 [WindowPlugin.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/BuiltIn/WindowPlugin.cs) 与 [WindowsPlugin.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/BuiltIn/WindowsPlugin.cs) 暴露的命令访问窗口能力。

## 2. WebviewWindow 公共类

### 2.1 创建窗口流程

窗口创建由 `Application.NewWebviewWindow` 触发，经过 `WindowManager` 分配 ID，再委托到平台应用：

```
Application.CreateWebviewWindow(options)
    │
    ▼
WindowManager.CreateWebviewWindow(options)
    │ 1. Interlocked.Increment(ref _nextWindowId) 生成 ID
    │ 2. new WebviewWindow(id, name, options)     构造公共实例
    │ 3. 注册 WindowClosed 事件监听器以自动清理
    ▼
IPlatformApp.CreateWebviewWindow(id, options)
    │
    ├── Windows: new Win32WebviewWindow(id, options) → Application.Windows 平台项目
    └── Linux:  new LinuxWebviewWindow(id, options) → Application.Linux 平台项目
    │
    ▼
window.Impl = platformWindow  // 平台实现注入回公共实例
```

公共构造函数仅记录 ID、名称与选项，平台实现由 `Application` 在创建后通过 `Impl` 属性注入：

```csharp
public WebviewWindow(uint id, string name, WebviewWindowOptions options)
{
    ID = id;
    Name = name;
    Options = options;
}

public IWebviewWindowImpl? Impl { get; internal set; }
```

调用方法时通过 `ImplRequired` 访问器获取平台实例，若未设置则抛出 `InvalidOperationException`：

```csharp
private IWebviewWindowImpl ImplRequired =>
    Impl ?? throw new InvalidOperationException("窗口平台实现尚未设置。");

public void SetTitle(string title) => ImplRequired.SetTitle(title);
```

### 2.2 窗口属性与方法

`WebviewWindow` 提供约 80+ 个公共方法，覆盖：

| 分类 | 示例方法 |
|------|---------|
| 标题与尺寸 | `SetTitle`、`SetSize`、`SetMinSize`、`SetMaxSize`、`SetPosition` |
| 显示与状态 | `Show`、`Hide`、`Maximise`、`Minimise`、`Fullscreen`、`Restore`、`Centre` |
| 查询 | `GetSize`、`GetPosition`、`IsFullscreen`、`IsMaximised`、`IsVisible`、`IsFocused` |
| 导航 | `SetURL`、`SetHTML`、`LoadURL`、`LoadHTML`、`GoBack`、`GoForward`、`Reload` |
| DevTools | `OpenDevTools`、`CloseDevTools`、`SetDebuggingEnabled` |
| 缩放 | `SetZoom`、`SetZoomLevel`、`ZoomIn`、`ZoomOut`、`ZoomReset` |
| 平台扩展 | `SetOpacity`、`SetTaskbarProgress`、`SetOverlayIcon`、`SetSkipTaskbar`、`SetEffects`、`SetBorderColor` |
| 模态与拖拽 | `AttachAsModal`、`StartDrag`、`StartResize` |
| 输出 | `ExecJS`、`InjectCSS`、`Print`、`PrintToPDF`、`CapturePreviewAsync` |

返回元组的方法使用 C# 命名元组以便前端调用方直观解构：

```csharp
public (int Width, int Height) GetSize() => ImplRequired.GetSize();
public (int X, int Y) GetPosition() => ImplRequired.GetPosition();
```

### 2.3 事件系统

`WebviewWindow` 内部维护按事件类型分组的事件监听器字典，通过 `On`/`Off`/`Emit` 三方法实现窗口级事件订阅：

```csharp
private readonly Dictionary<uint, List<Action>> _eventListeners = new();
private readonly object _eventLock = new();

public void On(uint eventType, Action callback)
{
    lock (_eventLock)
    {
        if (!_eventListeners.TryGetValue(eventType, out var list))
        {
            list = new List<Action>();
            _eventListeners[eventType] = list;
        }
        list.Add(callback);
    }
}
```

`Emit` 方法在触发本地监听器之外，还做两件事：

1. 当事件类型为 `WindowEventType.WindowRuntimeReady` 时同步触发 `RuntimeReady` 事件；
2. 将事件传播到应用级 `Application.Events` 事件处理器，使窗口事件接入全局 `EventProcessor`：

```csharp
public void Emit(uint eventType, object? data = null)
{
    // 触发本地监听器（线程安全快照）
    Action[] callbacks;
    lock (_eventLock) { /* ... */ }
    foreach (var callback in callbacks) { callback(); }

    if (eventType == (uint)WindowEventType.WindowRuntimeReady)
    {
        RuntimeReady?.Invoke();
    }

    // 传播到应用级事件总线
    Application.Get()?.Events?.Emit(KnownEvents.GetEventName(eventType), data, ID);
}
```

窗口还暴露 `OnClose` 与 `RuntimeReady` 两个 C# 事件供应用代码使用：

```csharp
public event Action<uint>? OnClose;
public event Action? RuntimeReady;
```

## 3. WebviewWindowOptions

[WebviewWindowOptions.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Options/WebviewWindowOptions.cs) 是窗口创建时的配置载体，主要字段如下：

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Name` | string | `""` | 窗口名称（用于按名查找） |
| `Title` | string | `""` | 窗口标题栏文本 |
| `Width` / `Height` | int | 800 / 600 | 初始窗口尺寸 |
| `MinWidth` / `MinHeight` | int | 0 / 0 | 最小尺寸（0 表示不限制） |
| `MaxWidth` / `MaxHeight` | int | 0 / 0 | 最大尺寸 |
| `X` / `Y` | int | -1 / -1 | 初始位置（-1 表示系统默认/居中） |
| `Frameless` | bool | false | 是否无边框 |
| `AlwaysOnTop` | bool | false | 是否总置顶 |
| `Hidden` | bool | false | 是否隐藏启动 |
| `Resizable` / `Maximisable` / `Minimisable` / `Closable` | bool | true | 各按钮可用性 |
| `Fullscreen` | bool | false | 是否启动即全屏 |
| `URL` / `HTML` | string / string? | `""` / null | 初始加载内容 |
| `JS` / `CSS` | string? | null | 启动后注入的 JS/CSS |
| `Icon` | byte[]? | null | 窗口图标（ICO 格式） |
| `TitleBar` | TitleBarStyle | Default | 标题栏样式 |
| `Centered` | bool | false | 是否居中显示 |
| `BackgroundColour` | (byte,byte,byte,byte)? | null | RGBA 背景色 |
| `BackgroundType` | string? | null | "transparent" / "translucent" / "solid" |
| `Translucent` | bool | false | 是否半透明 |
| `Zoom` | double | 1.0 | 初始缩放比例 |
| `ZoomEnabled` | bool | true | 是否允许用户缩放 |
| `Minimised` / `Maximised` | bool | false | 启动时窗口状态 |
| `ShowDevmodeEnabled` | bool | false | 是否启用 DevTools |
| `DisableContextMenu` | bool | false | 是否禁用右键菜单 |
| `EnableDragAndDrop` | bool | false | 是否启用拖放 |

`TitleBarStyle` 枚举定义于 [IWebviewWindowImpl.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Windows/IWebviewWindowImpl.cs)：

```csharp
public enum TitleBarStyle
{
    Default = 0,
    Hidden = 1,
    HiddenInset = 2,
    Unified = 3
}
```

同文件还定义了 `TaskbarProgressState`（None/Indeterminate/Normal/Error/Paused，对应 Windows TBPF 标志）与 `WindowEffect` / `WindowEffects`（用于 Mica/Acrylic 等视觉特效）。

## 4. WindowManager

[WindowManager.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Managers/WindowManager.cs) 实现窗口注册表、ID 生成与查找：

```csharp
public class WindowManager : IWindowManager
{
    private readonly ConcurrentDictionary<uint, WebviewWindow> _windows = new();
    private readonly ConcurrentDictionary<string, uint> _windowNames = new();
    private uint _nextWindowId = 1;
    private readonly IPlatformApp? _platformApp;
}
```

关键设计要点：

1. **线程安全的 ID 生成**：使用 `Interlocked.Increment` 而非 `++`，符合 AGENTS.md 中"ID 生成器必须使用 Interlocked"规则：

   ```csharp
   public uint CreateWebviewWindow(WebviewWindowOptions options)
   {
       var id = Interlocked.Increment(ref _nextWindowId) - 1;
       var window = new WebviewWindow(id, options.Name, options);
       _windows[id] = window;
       if (!string.IsNullOrEmpty(options.Name))
       {
           _windowNames[options.Name] = id;
       }

       // 注册自动清理监听器
       window.On((uint)WindowEventType.WindowClosed, () =>
       {
           if (_windows.TryRemove(id, out _))
           {
               if (!string.IsNullOrEmpty(window.Name))
                   _windowNames.TryRemove(window.Name, out _);
           }
       });

       _platformApp?.CreateWebviewWindow(id, options);
       return id;
   }
   ```

2. **双索引查找**：支持按 ID 与按名称查找，名称字典使前端可使用 `windows.getByName` 而无需维护 ID 映射。

3. **窗口关闭自动清理**：通过订阅 `WindowClosed` 事件移除字典条目，避免泄漏。

4. **查询接口**：`AllWindows`、`Count`、`GetWindow(id)`、`GetWindowByName(name)`、`GetAllWindows()`、`GetActiveWindow()`、`DestroyWindow(id)`、`Clear()`。

## 5. Win32WebviewWindow 实现详解

[Win32WebviewWindow.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Windows/Win32WebviewWindow.cs) 是 Windows 平台的具体实现，约 3000 行，使用 CsWin32 源生成器调用 Win32 API。

### 5.1 WebView2 初始化

构造函数中通过 `InitializeWebViewAsync` 异步初始化 WebView2，使用 `TaskCompletionSource` 跟踪完成状态：

```csharp
public Win32WebviewWindow(uint id, WebviewWindowOptions options)
{
    _id = id;
    _options = options;

    EnsureWindowClassRegistered();
    CreateNativeWindow();

    lock (_instancesLock)
    {
        _instancesByHwnd[(IntPtr)_hwnd] = this;
    }

    ApplyInitialOptions();
    _ = InitializeWebViewAsync();  // 不阻塞构造函数
}

private async Task InitializeWebViewAsync()
{
    var hwndPtr = (IntPtr)_hwnd;
    var environment = await CoreWebView2Environment.CreateAsync();
    var controller = await environment.CreateCoreWebView2ControllerAsync(hwndPtr);
    _controller = controller;
    _webview = controller.CoreWebView2;

    UpdateBounds();
    _webview.Settings.AreDevToolsEnabled = _options.ShowDevmodeEnabled;
    // 注册 WebResource / WebMessage / Navigation 事件
    _webview.AddWebResourceRequestedFilter("http://wails.localhost/*", CoreWebView2WebResourceContext.All);
    _webview.WebResourceRequested += OnWebResourceRequested;
    _webview.WebMessageReceived += OnWebMessageReceived;
    _webview.NavigationCompleted += OnNavigationCompleted;

    InjectRuntimeJs();
    // 加载 URL / HTML / AssetServer 默认页
    _initTcs.TrySetResult(true);
}
```

### 5.2 窗口创建（CreateWindowEx + HWND）

通过 `PInvoke.CreateWindowEx` 创建原生窗口，注册全局窗口类 `WailsNetWebviewWindow`：

```csharp
internal const string WindowClassName = "WailsNetWebviewWindow";

private void CreateNativeWindow()
{
    var style = WINDOW_STYLE.WS_OVERLAPPEDWINDOW;
    var x = _options.X < 0 ? CW_USEDEFAULT : _options.X;
    // ...
    _hwnd = PInvoke.CreateWindowEx(
        dwExStyle: 0,
        lpClassName: WindowClassName,
        lpWindowName: _options.Title,
        dwStyle: style,
        X: x, Y: y, nWidth: width, nHeight: height,
        hWndParent: default, hMenu: null, hInstance: null, lpParam: null);

    PInvoke.DragAcceptFiles(_hwnd, true);
    PInvoke.AddClipboardFormatListener(_hwnd);
}
```

窗口类只注册一次（双重检查锁），并保留 `WNDPROC` 委托引用防止 GC 回收：

```csharp
private static WNDPROC? _wndProc;
private static readonly object _classLock = new();

internal static void EnsureWindowClassRegistered()
{
    if (_wndProc is not null) return;
    lock (_classLock)
    {
        if (_wndProc is not null) return;
        _wndProc = StaticWindowProc;
        // PInvoke.RegisterClassEx(...)
    }
}
```

### 5.3 消息循环（WndProc）

`StaticWindowProc` 通过 HWND 查找实例并转发消息，处理关键 Win32 消息：

| 消息 | 处理 |
|------|------|
| `WM_CREATE` / `WM_DESTROY` | 从实例表移除，触发 `WindowClosed` 事件，最后一个窗口关闭时 `PostQuitMessage(0)` 退出消息循环 |
| `WM_CLOSE` | 触发 `WindowClosing` 事件 |
| `WM_SIZE` | 同步 WebView2 边界、触发 `WindowResized`/`Minimised`/`Maximised`/`Unminimised`/`Unmaximised` 事件 |
| `WM_MOVE` | 触发 `WindowMoved` 事件 |
| `WM_GETMINMAXINFO` | 约束最小/最大窗口尺寸 |
| `WM_DPICHANGED` | 按建议矩形调整窗口并触发 `WindowDPIChanged` |
| `WM_ACTIVATE` | 根据激活状态触发 `WindowFocus` 或 `WindowFocusLost` |
| `WM_DROPFILES` | 通过 `DragQueryFileW` 解析文件路径，触发 `WindowFileDropped` 事件 |
| `WM_HOTKEY` | 转发到 `KeyBindingManager.HandleHotKey` |
| `WM_KEYDOWN` | F12 打开 DevTools |
| `WM_CONTEXTMENU` | 启用默认右键菜单（DevTools/Reload/Inspect） |
| `WM_SETTINGCHANGE` | 检测 `ImmersiveColorSet` 主题变化 |
| `WM_DISPLAYCHANGE` | 显示器配置变化 |
| `WM_CLIPBOARDUPDATE` | 剪贴板内容变化 |

### 5.4 运行时 JS 注入

Wails 运行时 JS 必须在页面脚本执行前注入，使用 `AddScriptToExecuteOnDocumentCreatedAsync` 而非 `ExecuteScriptAsync`：

```csharp
private void InjectRuntimeJs()
{
    if (_runtimeInjected || _webview is null) return;
    var app = Application.Get();
    if (app is null) return;

    var js = app.GenerateRuntimeJs(false);
    if (!string.IsNullOrEmpty(js))
    {
        // 每个新文档创建时、页面脚本执行前注入
        _ = _webview.AddScriptToExecuteOnDocumentCreatedAsync(js);
        _runtimeInjected = true;
    }
}
```

注释明确说明：若放在导航之后，页面脚本已执行完，`window.wails` 会是 `undefined`。

### 5.5 资源拦截与 IPC 端点

通过 `AddWebResourceRequestedFilter` 拦截 `http(s)://wails.localhost/*` 的所有请求，由 `OnWebResourceRequested` 处理：

- **IPC 端点**：拦截 `POST /wails/*` 请求，同步读取请求体后调用 `Application.HandleMessageFromFrontend(body, _id)`，返回 JSON 响应；
- **静态资源**：转发到 `AssetServer.ServeAsync(path)`，根据扩展名计算 MIME 类型；
- **SPA 路由回退**：当资源不存在且路径无扩展名时回退到 `index.html`。

> 注释强调必须使用同步方法（`GetAwaiter().GetResult()`），因为 Win32 消息循环无 `SynchronizationContext`，`await` 后的 continuation 会在线程池线程运行，而 WebView2 要求 `CoreWebView2` 成员只能在 UI 线程访问。

`WebMessageReceived` 处理前端通过 `chrome.webview.postMessage` 发送的消息：优先识别拖拽请求 `wails:drag` 直接调用 `StartDrag`，其他消息转发到标准 IPC 处理流程。

### 5.6 文件拖放

通过 `DragQueryFileW` 与 `DragFinish` 手动 P/Invoke（CsWin32 未生成）解析 `WM_DROPFILES` 的 `HDROP`：

```csharp
[DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
private static extern unsafe uint DragQueryFileW(IntPtr hDrop, uint iFile, char* lpszFile, uint cch);

private unsafe string[] ParseDropFiles(nint hDropPtr)
{
    var count = DragQueryFileW(hDropPtr, 0xFFFFFFFF, null, 0);
    var files = new string[count];
    for (uint i = 0; i < count; i++)
    {
        var length = DragQueryFileW(hDropPtr, i, null, 0);
        var buffer = new char[length + 1];
        fixed (char* ptr = buffer)
        {
            DragQueryFileW(hDropPtr, i, ptr, (uint)buffer.Length);
        }
        files[i] = new string(buffer, 0, (int)length);
    }
    DragFinish(hDropPtr);
    return files;
}
```

### 5.7 无边框窗口拖拽（DragRegionHelper）

[DragRegionHelper.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Windows/DragRegionHelper.cs) 提供 `-webkit-app-region: drag` CSS 属性支持，对应 Tauri v2 / Electron 的无边框窗口拖拽方案。

它由三部分组成：

1. **`GetDragRegionScript`**：监听 `mousedown` 事件，遍历目标元素祖先链查找 `-webkit-app-region` 计算样式：
   - 值为 `drag` 时调用 `window.__wails_startDrag__()` 触发后端拖拽；
   - 值为 `no-drag` 时允许默认交互（覆盖祖先的 `drag` 设置）。

2. **`GetDragRegionCss`**：为 `drag` 元素设置默认 `user-select: none` 与 `cursor: default`。

3. **`GetStartDragCallbackScript(windowId)`**：注册全局 `__wails_startDrag__` 回调，通过 `chrome.webview.postMessage` 发送 JSON 消息 `{ type: 'wails:drag', windowId: <id> }` 到后端。

注入顺序很重要：先注册回调，再注入监听脚本。`OnNavigationCompleted` 中执行：

```csharp
if (_options.Frameless)
{
    _ = _webview.ExecuteScriptAsync(DragRegionHelper.GetStartDragCallbackScript(_id));
    _ = _webview.ExecuteScriptAsync(DragRegionHelper.GetDragRegionScript());
}
```

`StartDrag` 通过释放鼠标捕获并发送 `WM_NCLBUTTONDOWN` 模拟标题栏拖动：

```csharp
public void StartDrag()
{
    PInvoke.ReleaseCapture();
    PInvoke.SendMessage(_hwnd, WmNclButtonDown, (WPARAM)(nuint)Htcaption, default);
}
```

### 5.8 DevTools 控制

- `OpenDevTools`：调用 `_webview.OpenDevToolsWindow()`；
- `CloseDevTools`：由于 WebView2 未提供直接关闭 API，通过 `AreDevToolsEnabled = false; true;` 间接关闭；
- F12 快捷键：在 `WM_KEYDOWN` 中检测 `VkF12` 调用 `OpenDevTools()`；
- 右键菜单：当 `Application.Options.EnableDefaultContextMenu == true` 时通过 `TrackPopupMenu` 显示内置菜单（开发者工具 / 重新加载 / 检查元素）。

## 6. LinuxWebviewWindow 实现要点

[LinuxWebviewWindow.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Linux/LinuxWebviewWindow.cs) 使用 GirCore 0.8.0 调用 GTK4 与 WebKitGTK。

### 6.1 GTK4 Window + WebKit.WebView

构造函数中创建 `Gtk.Window` 与 `WebKit.WebView`，使用垂直 `Box` 组合菜单栏与 WebView：

```csharp
private void CreateNativeWindow()
{
    _window = Window.New();
    _window.SetTitle(_options.Title);
    _window.SetDefaultSize(_options.Width, _options.Height);
    _window.SetResizable(_options.Resizable);
    if (_options.Fullscreen) _window.Fullscreen();
}

private void CreateWebView()
{
    _webView = WebView.New();
    _mainBox = Box.New(Orientation.Vertical, 0);
    _webView.SetHexpand(true);
    _webView.SetVexpand(true);
    _mainBox.Append(_webView);
    _window?.SetChild(_mainBox);

    var settings = _webView.GetSettings();
    settings?.SetEnableJavascript(true);
    settings?.SetEnableDeveloperExtras(true);
}
```

### 6.2 wails:// 自定义协议

通过 `WebContext.RegisterUriScheme` 注册 `wails` scheme（全局只注册一次）：

```csharp
private static bool _wailsSchemeRegistered;

private void EnsureWailsSchemeRegistered()
{
    if (_wailsSchemeRegistered || _webView is null) return;
    _wailsSchemeRegistered = true;
    var context = _webView.GetContext();
    context.RegisterUriScheme("wails", OnWailsSchemeRequest);
}
```

`OnWailsSchemeRequest` 处理流程与 Win32 的 `OnWebResourceRequested` 类似：从 `AssetServer` 读取资源，支持 SPA 路由回退到 `index.html`，使用 `URISchemeResponse` 构造响应。

### 6.3 信号连接与文件拖放

`ConnectWebViewSignals` 连接 WebKit 与 GTK 关键信号：`OnLoadChanged`、`OnLoadFailed`、`OnCreate`、`OnClose`、`OnContextMenu`、`OnDecidePolicy`、`OnNotify`、`OnReadyToShow` 与窗口的 `OnNotify`（检测 `is-active` 属性变化分发焦点事件）。

文件拖放使用 GTK4 的 `Gtk.DropTarget`（替代 GTK3 的 `drag-data-received`），通过 `Gdk.FileList.GetGType()` 创建接受文件列表的目标：

```csharp
private void SetupFileDropTarget()
{
    var dropTarget = Gtk.DropTarget.New(Gdk.FileList.GetGType(), Gdk.DragAction.Copy);
    dropTarget.OnDrop += OnFileDrop;
    _window.AddController(dropTarget);
}
```

`OnFileDrop` 中由于 GirCore 0.8.0 未公开 `Gdk.FileList.GetFiles`，通过手动 P/Invoke 调用原生 `gdk_file_list_get_files` 与 `g_file_get_path` 遍历 GSList 提取文件路径。

页面加载完成（`LoadEvent.Finished`）时，若为无边框模式，同样注入 `DragRegionHelper` 拖拽脚本。

## 7. WindowPlugin 命令实现

[WindowPlugin.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/BuiltIn/WindowPlugin.cs) 借鉴 Tauri v2 "核心即插件" 哲学，将 `WebviewWindow` 的原生操作以插件命令形式暴露给前端，对应前端 `wails.window.*` API。

### 7.1 命令分类

命令分三类：

1. **Action（带 Options 参数）**：`window.setTitle`、`window.setSize`、`window.setMinSize`、`window.setMaxSize`、`window.setPosition`、`window.setAlwaysOnTop`、`window.setFullscreen`、`window.setFrameless`、`window.setZoom`、`window.setURL`、`window.setHTML`、`window.printToPDF`、`window.execJS`、`window.injectCSS`、`window.setOpacity`、`window.setResizable`、`window.registerCustomScheme`、`window.setSkipTaskbar`、`window.setIgnoreCursorEvents`、`window.setBadgeCount`、`window.setBadgeLabel`、`window.setVisibleOnAllWorkspaces`、`window.setBorderColor`、`window.setFileDropEnabled`。

2. **Query（有返回值）**：`window.getSize` → `WindowSizeResult`、`window.getPosition` → `WindowPositionResult`、`window.getURL` → string、`window.getZoom` → float、`window.getOpacity` → float、`window.isFullscreen` / `window.isMaximised` / `window.isMinimised` / `window.isVisible` / `window.isFocused` → bool。

3. **无参数 Action**：`window.close`、`window.minimize`、`window.maximize`、`window.unminimize`、`window.unmaximize`、`window.show`、`window.hide`、`window.centre`、`window.restore`、`window.focus`、`window.unfullscreen`、`window.openDevTools`、`window.closeDevTools`、`window.zoomIn`、`window.zoomOut`、`window.zoomReset`、`window.goBack`、`window.goForward`、`window.reload`、`window.print`。

### 7.2 Options 对象模式

每个带参数命令都定义独立的 Options 类，便于序列化与扩展：

```csharp
public sealed class WindowSetTitleOptions { public string Title { get; set; } = string.Empty; }
public sealed class WindowSizeOptions { public int Width { get; set; } public int Height { get; set; } }
public sealed class WindowPositionOptions { public int X { get; set; } public int Y { get; set; } }
public sealed class WindowFullscreenOptions { public bool Fullscreen { get; set; } }
public sealed class WindowPrintToPdfOptions
{
    public string Path { get; set; } = string.Empty;
    public PrintToPdfOptions? Options { get; set; }
}
// ... 共 20+ Options 类
```

查询类命令定义对应的 Result 类：

```csharp
public sealed class WindowSizeResult { public int Width { get; set; } public int Height { get; set; } }
public sealed class WindowPositionResult { public int X { get; set; } public int Y { get; set; } }
```

### 7.3 命令上下文与窗口定位

所有命令通过 `ICommandContext.WindowId` 定位目标 `WebviewWindow`：

```csharp
private static WebviewWindow GetWindowOrThrow(ICommandContext ctx)
{
    if (ctx.WindowId is not uint windowId)
    {
        throw new InvalidOperationException("窗口消息未指定目标窗口 ID");
    }

    var app = Application.Get();
    var window = app?.GetWindow(windowId);
    if (window is null)
    {
        throw new InvalidOperationException($"找不到 ID 为 {windowId} 的窗口");
    }
    return window;
}
```

前端可通过两种路径调用命令：

- `wails.window.setTitle('标题')` → 走 `MessageProcessor.ProcessWindow` → `CommandDispatcher`；
- `wails.call('window.setTitle', [{ title: '标题' }])` → 走 `ProcessCallAsync` → `CommandDispatcher` 回退。

命令注册示例：

```csharp
commands.MapCommand("window.setTitle",
    (Action<ICommandContext, WindowSetTitleOptions>)((ctx, opts) =>
        GetWindowOrThrow(ctx).SetTitle(opts.Title)));

commands.MapCommand("window.getSize",
    (Func<ICommandContext, WindowSizeResult>)(ctx =>
    {
        var (width, height) = GetWindowOrThrow(ctx).GetSize();
        return new WindowSizeResult { Width = width, Height = height };
    }));
```

## 8. WindowsPlugin 命令

[WindowsPlugin.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/BuiltIn/WindowsPlugin.cs) 暴露窗口管理器级别的查询命令，对应前端 `wails.windows.*` API。与 `WindowPlugin` 区别：

- `WindowPlugin` 操作单个窗口，通过 `ICommandContext.WindowId` 定位；
- `WindowsPlugin` 查询窗口列表，不依赖 `WindowId`。

注册的命令：

| 命令 | 签名 | 说明 |
|------|------|------|
| `windows.getCurrent` | `Func<ICommandContext, WindowInfo?>` | 返回当前窗口信息（无 WindowId 时返回 null） |
| `windows.getAll` | `Func<ICommandContext, WindowInfo[]>` | 返回所有窗口列表 |
| `windows.getByName` | `Func<ICommandContext, WindowsByNameOptions, WindowInfo?>` | 按名称查找窗口 |
| `windows.getById` | `Func<ICommandContext, WindowsByIdOptions, WindowInfo?>` | 按 ID 查找窗口 |
| `windows.emit` | `Action<ICommandContext, WindowsEmitOptions>` | 向指定窗口或所有窗口广播事件 |

返回的 `WindowInfo` DTO 只包含序列化友好的最小字段：

```csharp
public sealed class WindowInfo
{
    public uint Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
```

`windows.emit` 通过 `Application.Events.Emit` 转发，事件名以 `wails:window:` 前缀命名：

```csharp
commands.MapCommand("windows.emit",
    (Action<ICommandContext, WindowsEmitOptions>)((ctx, opts) =>
    {
        var app = GetAppOrThrow(ctx);
        if (opts.TargetWindowId.HasValue)
        {
            var window = app.GetWindow((uint)opts.TargetWindowId.Value);
            if (window is not null)
            {
                app.Events.Emit($"wails:window:{opts.Name}", opts.Data);
            }
        }
        else
        {
            app.Events.Emit($"wails:window:{opts.Name}", opts.Data);
        }
    }));
```

`WindowsEmitOptions.TargetWindowId` 为 `long?`，`null` 时广播到所有窗口，非空时仅触发到指定窗口（但当前实现为通过事件总线广播，由订阅方根据 `windowId` 过滤）。

## 9. 关键设计要点小结

1. **三层架构**：公共 API、平台抽象接口、平台实现分离，便于扩展新平台。
2. **默认实现策略**：`IWebviewWindowImpl` 对可选项（如 `SetBackgroundType`、`SetOpacity`、`SetEffects`）提供默认空实现，平台按需重写，避免破坏性变更。
3. **线程安全**：`WindowManager` 使用 `ConcurrentDictionary` 与 `Interlocked.Increment`；`WebviewWindow` 事件字典使用 `lock`。
4. **事件传播**：窗口级 `Emit` 同步传播到应用级 `EventProcessor`，使全局订阅者能收到窗口事件。
5. **平台特性差异**：Windows 通过 `DwmSetWindowAttribute` 实现 Mica/Acrylic 与边框颜色，通过 `ITaskbarList3` COM 实现任务栏进度与叠加图标；Linux 通过 GTK CSS 实现透明背景。
6. **IPC 双通道**：Win32 使用 `WebResourceRequested` 拦截 HTTP 请求，同时使用 `WebMessageReceived` 处理 `postMessage`，后者专用于拖拽等需绕过 IPC 队列的实时操作。
7. **插件化窗口命令**：窗口管理是核心能力，但通过插件命令路径暴露给前端，符合 Tauri v2 的"核心即插件"哲学，使前端 API 与插件 API 保持一致。
