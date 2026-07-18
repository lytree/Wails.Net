# Wails.Net vs Tauri 2 vs Wails 3 功能对比详情

> 本文档对比 **Wails.Net**（当前项目，Wails v3 的 .NET 10 移植实现）、**Tauri 2**（Rust 桌面/移动框架）和 **Wails 3 v3.0.0-alpha.102**（Go 原版）三者的功能实现项与差异项。
>
> - **更新日期**：2026-07-18
> - **对比基线**：基于本仓库当前 `src/` 实际代码状态
> - **架构融合策略**（见 [AGENTS.md](file:///f:/Code/Dotnet/Wails.Net/AGENTS.md) §1.1.1）：
>   - Host/DI/Config/Logging → 学 ASP.NET Core（Microsoft.Extensions.* 全栈）
>   - Runtime/Window/IPC → 学 Wails v3
>   - Plugin/Security/Capability → 学 Tauri v2

---

## 目录

1. [项目定位与架构](#1-项目定位与架构)
2. [绑定系统](#2-绑定系统)
3. [事件系统](#3-事件系统)
4. [IPC 与传输层](#4-ipc-与传输层)
5. [窗口管理](#5-窗口管理)
6. [权限与安全模型](#6-权限与安全模型)
7. [移动端支持](#7-移动端支持)
8. [插件系统](#8-插件系统)
9. [AssetServer](#9-assetserver)
10. [Updater](#10-updater)
11. [打包与签名](#11-打包与签名)
12. [CLI 工具](#12-cli-工具)
13. [Server 模式（Wails.Net 独有）](#13-server-模式wailsnet-独有)
14. [测试框架](#14-测试框架)
15. [三方插件对照表](#15-三方插件对照表)
16. [Wails.Net 独有能力](#16-wailsnet-独有能力)
17. [Wails.Net 差距与路线图](#17-wailsnet-差距与路线图)
18. [总结](#18-总结)

---

## 1. 项目定位与架构

| 维度 | Wails.Net | Tauri 2 | Wails 3 |
|------|-----------|---------|---------|
| **实现语言** | C# (.NET 10) | Rust | Go |
| **目标平台** | Windows / Linux / Android | Windows / Linux / macOS / iOS / Android | Windows / Linux / macOS / iOS / Android |
| **macOS/iOS** | ❌ 暂不实现 | ✅ | ✅ |
| **Webview** | WebView2 (Win) / WebKitGTK (Linux) / Android.Webkit.WebView | WebView2 / WKWebView / WebKitGTK / Android WebView | WebView2 / WKWebView / WebKitGTK / Android WebView |
| **宿主模型** | `Microsoft.Extensions.Hosting` Generic Host | Tokio runtime + tauri::Builder | Go main goroutine + Application.Run() |
| **DI 容器** | `Microsoft.Extensions.DependencyInjection` | `tauri::State` / `manage()` | 无内置（手动 struct 注入） |
| **配置系统** | `Microsoft.Extensions.Configuration` (appsettings.json) | `tauri.conf.json` + plugin config | `wails.json` + WebviewWindowOptions |
| **日志抽象** | `Microsoft.Extensions.Logging` (`ILogger<T>`) | `log` crate | `slog` logger |
| **架构模式** | 接口驱动平台抽象 + 管理器模式 + Server 模式降级 | Builder + State + Plugin | Manager 模式 + Service 模式 + Event Hooks |

### 关键差异

- **Wails.Net** 是三者中**唯一**采用 ASP.NET Core 全栈（Host/DI/Config/Logging/Options）的方案，对 .NET 开发者最友好；同时通过 `ServerPlatformApp`/`ServerWebviewWindow` 提供容器化降级路径（见 §13）。
- **Tauri 2** 是三者中**唯一**支持全部 5 个平台（Win/Linux/macOS/iOS/Android）的方案。
- **Wails 3** 与 Wails.Net 共享对象模型（`WebviewWindowOptions`、`Application`、Manager 模式），但 Wails.Net 在 IPC/权限/插件上分别借鉴了 Tauri 2 的设计。

---

## 2. 绑定系统

| 维度 | Wails.Net | Tauri 2 | Wails 3 |
|------|-----------|---------|---------|
| **绑定入口** | `[Binding]` 特性 + `BindingManager.Add(instance)` | `#[tauri::command]` 宏 | `Application.Bind(instance)` |
| **调用路径** | 源生成器优先 + 反射回退（双路径） | 编译期宏展开 | 反射 |
| **方法 ID 哈希** | FNV-1a 32 位（offsetBasis=2166136261, prime=16777619） | 字符串名（无哈希） | FNV-1a 32 位（`fnv.New32a()`） |
| **哈希一致性** | ✅ 与 Go 版本完全一致 | N/A | ✅ 基准实现 |
| **注册键** | 全限定名 + 短名称双键 | 命令名字符串 | 全限定名 + 短名称 |
| **排除方法** | `ServiceName` / `ServiceStartup` / `ServiceShutdown` / `IsSpecialName` / `Object` 继承方法 | 显式 `#[command]` 标注 | `Service` 接口方法 |
| **错误处理** | `CallError` + `CallErrorKind`（Reference/Type/Runtime） | `Result<T, E>` 序列化 | `errors.CallError` |
| **反射异常解包** | ✅ `TargetInvocationException` 解包 | N/A（编译期） | ✅ |
| **取消支持** | ✅ `OperationCanceledException` 重抛给 MessageProcessor | ✅ `Cancellation` | ✅ `CancelCall` |
| **AGENTS.md 约束** | §3.4 禁止反射主路径，必须用源生成器 | N/A | N/A |

### 关键差异

- **Wails.Net** 通过 [GeneratedBindingRegistry](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Bindings/Bindings.cs) 在编译期生成 `TryGetInvoker` 委托，运行时优先走源生成器路径，反射仅作为兜底，兼顾性能与开发体验。
- **Tauri 2** 完全无反射，性能最优，但需要 Rust 宏学习成本。
- **Wails 3** 与 Wails.Net 共享 FNV-1a 哈希算法，前端绑定 ID 可互通。

---

## 3. 事件系统

| 维度 | Wails.Net | Tauri 2 | Wails 3 |
|------|-----------|---------|---------|
| **核心类型** | `EventProcessor` | `EventBus` / `Emitter` | `Events` struct |
| **监听器存储** | `ConcurrentDictionary<string, List<EventListener>>` | `Vec<EventListener>` | `map[string][]EventListener` |
| **Pre-emit 钩子** | ✅ `List<Func<CustomEvent, bool>>` | ❌ | ✅ Event Hooks |
| **多传输层广播** | ✅ `IWailsEventListener` 列表并行广播 | 单一 IPC | 单一 in-memory bridge |
| **事件命名空间** | `wails:window:*` / `wails:app:*` 等 | `tauri://*` | `wails:*` |
| **JS 端 API** | `wails.events.on/off/emit` | `listen/unlisten/emit` | `wails.events.on/off/emit` |
| **跨窗口广播** | ✅ 通过多传输层自动广播 | ✅ | ✅ |
| **线程安全** | ✅ `ConcurrentDictionary` + 锁 | ✅ `Mutex` | ✅ `sync.RWMutex` |

### 关键差异

- **Wails.Net** 的 [EventProcessor](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Events/EventProcessor.cs) 同时支持 Pre-emit 钩子（学 Wails 3）和**多传输层并行广播**（独有）：一次 `Emit` 会同时通过 HttpTransport + WebSocketBroadcaster + EventIPCTransport + AssetServerTransport + NativeIpcTransport 广播，确保所有连接的前端都能收到。
- **Tauri 2** 事件总线单一 IPC，但提供 `emit_to` / `emit_filter` 精细控制。
- **Wails 3** 通过 Event Hooks 实现服务端拦截，与 Wails.Net 的 Pre-emit 钩子等价。

---

## 4. IPC 与传输层

| 维度 | Wails.Net | Tauri 2 | Wails 3 |
|------|-----------|---------|---------|
| **传输抽象** | `ITransport` 接口 | `Invoke` 机制（编译期绑定） | `ITransport` 接口 |
| **默认传输** | HttpTransport (HTTP POST + WebSocket) | 平台原生 IPC（WebView2 host object / WKWebView message handler / WebKitGTK） | in-memory bridge（前端 postMessage） |
| **HTTP 端点** | `/wails/message` (POST) + `/wails/ws` (WS) | N/A | N/A |
| **默认端口** | 34115 起递增 | N/A | N/A |
| **WebSocket** | ✅ `WebSocketBroadcaster` | ❌（使用原生 IPC） | ❌ |
| **原生 IPC** | ✅ `NativeIpcTransport`（P0-C2，可选） | ✅ 默认 | ✅ 默认 |
| **Event IPC 回退** | ✅ `EventIPCTransport`（P0-C3） | ❌ | ❌ |
| **AssetServer 传输** | ✅ `AssetServerTransport` | ❌ | ❌ |
| **消息类型常量** | `MessageTypes`：Call/Event/Window/Query/Response/Error/Drag/ContextMenu/System/**Cancel** | `Invoke` / `Event` | Call/Event/Window/Query/Response/Error/Drag/ContextMenu/System |
| **异步队列** | ✅ `Channel`-based 异步处理 | ✅ Tokio task | ✅ goroutine |
| **CancellablePromise** | ✅ 链接 CTS，支持前端取消 | ✅ `Cancellation` | ✅ `CancelCall` |
| **运行中调用表** | `ConcurrentDictionary<string, CancellationTokenSource>` | N/A | `map[string]*CancelCall` |
| **消息路由** | `MessageProcessor` 双路径：BindingManager + CommandDispatcher 回退 | `Invoke` 直接路由到命令 | `MessageProcessor` + `DispatchWindowAction` |

### 关键差异

- **Wails.Net** 是三者中**唯一**支持多传输层并行广播的方案（见 [HttpTransport.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/HttpTransport.cs)），适用于容器化部署和调试场景。
- **Tauri 2** 和 **Wails 3** 默认走原生 IPC，性能更高但调试不便。
- **Wails.Net** 的 [MessageProcessor](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/MessageProcessor.cs) 新增 `MessageTypes.Cancel` 消息类型，与 Wails 3 的 `CancelCall` 对齐。

---

## 5. 窗口管理

| 维度 | Wails.Net | Tauri 2 | Wails 3 |
|------|-----------|---------|---------|
| **窗口抽象** | `IWebviewWindowImpl` + `WebviewWindow` | `WebviewWindow` / `WebviewWindowBuilder` | `WebviewWindow` + `IWebviewWindowImpl` |
| **窗口管理器** | `WindowManager` | `WebviewWindowBuilder` | `WindowManager` |
| **多窗口** | ✅ | ✅ | ✅ |
| **窗口选项** | `WebviewWindowOptions` | `WebviewWindowOptions` | `WebviewWindowOptions` |
| **Frameless** | ✅ | ✅ | ✅ |
| **Transparent** | ✅ | ✅ | ✅ |
| **AlwaysOnTop** | ✅ | ✅ | ✅ |
| **DevTools** | ✅ F12 / `OpenDevTools` | ✅ | ✅ |
| **Zoom** | ✅ `SetZoom/ZoomIn/ZoomOut/ZoomReset` | ✅ | ✅ |
| **打印** | ✅ `Print/PrintToPDF` | ✅（plugin） | ✅ |
| **ExecJS / InjectCSS** | ✅ | ✅ | ✅ |
| **文件拖放** | ✅ `WM_DROPFILES` (Win) | ✅ | ✅ |
| **DPI 适配** | ✅ `WM_DPICHANGED` (Win) | ✅ | ✅ |
| **窗口菜单** | ✅ `WM_COMMAND` 分发 | ✅ | ✅ Menu Roles |
| **热键** | ✅ `WM_HOTKEY` | ✅（plugin） | ✅ |
| **Badge** | ✅ `SetBadgeCount/SetBadgeLabel` | ✅（plugin） | ✅ |
| **多工作区** | ✅ `SetVisibleOnAllWorkspaces` | ✅ | ✅ |
| **边框颜色** | ✅ `SetBorderColor` | ✅ | ✅ |
| **命令路径** | `window.*` 命令通过 `WindowPlugin` 暴露 | `window.*` plugin 命令 | `wails.window.*` 前端 API |
| **权限校验** | ✅ `window:default` / `window:allow-readonly` / `window:allow-dangerous` | ✅ `window:default` / `window:allow-*` | ❌ |

### 关键差异

- **Wails.Net** 的 [WindowPlugin](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/BuiltIn/WindowPlugin.cs) 借鉴 Tauri v2 的"核心即插件"哲学，将所有窗口操作以插件命令形式暴露，同时声明三层权限集（`window:default` / `window:allow-readonly` / `window:allow-dangerous`）。
- **Wails 3** 窗口 API 直接通过 `wails.window.*` 暴露，无权限校验。
- **Wails.Net** Win32 实现 [Win32WebviewWindow](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Windows/Win32WebviewWindow.cs) 完整处理 WM_DESTROY/WM_CLOSE/WM_SIZE/WM_COMMAND/WM_SYSCOMMAND/WM_GETMINMAXINFO/WM_DPICHANGED/WM_HOTKEY/WM_DROPFILES/WM_SETTINGCHANGE/WM_MOVE/WM_NCLBUTTONDOWN/WM_SETICON/WM_ACTIVATE/WM_DISPLAYCHANGE/WM_CLIPBOARDUPDATE/WM_KEYDOWN/WM_CONTEXTMENU 等消息。

---

## 6. 权限与安全模型

| 维度 | Wails.Net | Tauri 2 | Wails 3 |
|------|-----------|---------|---------|
| **三层 ACL** | ✅ Capability + PermissionSet + Scope | ✅ Capability + Permission + Scope | ❌ |
| **配置文件** | `appsettings.json` (Wails:Capabilities) | `capabilities/*.json` | N/A |
| **权限声明** | `DeclarePermission` / `RegisterPermissionSet` | `default.toml` / `permissions.toml` | N/A |
| **作用域类型** | `FileSystemScope` / `UrlScope` | `fs scope` / `http scope` / `asset protocol scope` | N/A |
| **Deny 优先** | ✅ `_deniedPermissions` 优先于 `_grantedPermissions` | ✅ `deny-default` | N/A |
| **Remote URL 限制** | ✅ `_remoteUrlScopes` + `IsGranted(perm, window, origin)` | ✅ `remote.urls` | N/A |
| **窗口级隔离** | ✅ `PermissionKey (Permission, Window)` 复合键 | ✅ `windows: ["main"]` | N/A |
| **命令校验** | ✅ `ValidateCommand` + `RequireCapabilityAttribute` | ✅ `capabilities` 引用命令权限 | ❌ |
| **运行时开关** | ✅ `PermissionManager.Enabled`（默认 false 保持向后兼容） | ✅ 默认启用 | N/A |
| **本地源判定** | `wails://` / `localhost` / `127.0.0.1` / null | `tauri://localhost` / `http://localhost` | N/A |
| **URL 白名单** | `UrlWhitelist` 通配符匹配 | `allowed-origins` | N/A |

### 关键差异

- **Wails.Net** 的 [PermissionManager](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Security/PermissionManager.cs) 是对 Tauri v2 ACL 模型的完整移植，使用 5 个 `ConcurrentDictionary`（`_grantedPermissions` / `_declaredCapabilities` / `_permissionSets` / `_permissionScopes` / `_remoteUrlScopes` / `_deniedPermissions`）实现线程安全的权限管理。
- **Wails.Net** 默认 `Enabled=false` 保持向后兼容，迁移成本低于 Tauri 2（Tauri 2 默认强制启用）。
- **Wails 3** 完全无 ACL 模型，依赖操作系统进程隔离。
- **Wails.Net** 的 [Scopes](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Security/Scopes.cs) 支持：
  - `FileSystemScope`：递归目录前缀匹配
  - `UrlScope`：通配符 URL 模式（委托 `UrlWhitelist`）

---

## 7. 移动端支持

| 维度 | Wails.Net | Tauri 2 | Wails 3 |
|------|-----------|---------|---------|
| **Android** | ✅ `net10.0-android36.0`（API 24+） | ✅ | ✅ 稳定支持 |
| **iOS** | ❌ 暂不实现 | ✅ | ✅ 稳定支持 |
| **Android Webview** | `Android.Webkit.WebView`（.NET Android 互操作） | Android WebView | Android WebView |
| **iOS Webview** | N/A | WKWebView | WKWebView |
| **移动端插件** | 4 个（Camera / Haptics / Vibration / Permissions + 4 个平台接口） | 20+ 移动端插件 | 有限 |
| **桌面专属 API** | ✅ 桌面方法在移动端 no-op（见 [AndroidWebviewWindow](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Android/AndroidWebviewWindow.cs)） | ✅ | ✅ |
| **构建工具** | `cake build --target=Build-Android` | `tauri android build` | `wails3 task build` |
| **TFM** | `net10.0-android36.0` | Kotlin/Java | Go mobile |
| **MAUI Controls** | ❌ 禁止引入（AGENTS.md §1.1） | N/A | N/A |

### 关键差异

- **Wails.Net** 仅支持 Android（API 24+），iOS 暂不实现；通过 .NET Android 工作负载直接调用 `Android.Webkit.WebView`，**不引入 MAUI Controls**。
- **Tauri 2** 和 **Wails 3** 均已稳定支持 iOS + Android。
- **Wails 3** 截至 2026-07 官方文档显示移动端已稳定支持（修正之前"coming soon"的认知）。

---

## 8. 插件系统

| 维度 | Wails.Net | Tauri 2 | Wails 3 |
|------|-----------|---------|---------|
| **插件接口** | `IPlugin`（4 阶段生命周期） | `Plugin` trait (`init` / `setup`) | `Service` 接口（`ServiceStartup` / `ServiceShutdown`） |
| **生命周期** | `ConfigureServices` → `Configure` → `StartupAsync` → `ShutdownAsync` | `Builder::setup` | `ServiceStartup` → `ServiceShutdown` |
| **插件管理器** | `PluginManager`（`Interlocked` 幂等保护） | `tauri::Builder` 自动注册 | `ServiceManager` |
| **插件上下文** | `IPluginContext`（Commands / Permissions / Services） | `AppHandle` | `Manager` |
| **命令注册** | `commands.MapCommand` | `invoke_handler` | `Application.Bind` |
| **权限声明** | `context.Permissions.DeclarePermission` | `permissions/default.toml` | N/A |
| **内置插件数** | **41 个桌面** + **4 个移动端** + 4 个平台接口 | 30+ 官方插件 | 有限 |
| **第三方加载** | ✅ 通过 `ConfigureServices` 注册 | ✅ `tauri-plugin-*` crate | ✅ Go import |
| **IHostedService 适配** | ✅ 双重触发防护（`_started` / `_stopped` Interlocked） | N/A | N/A |

### Wails.Net 内置插件清单（41 个桌面 + 4 个移动端）

**桌面插件**（37 个，位于 `src/Wails.Net.Application/Plugins/BuiltIn/`）：
- **窗口/对话框/菜单**：WindowPlugin / DialogPlugin / MenuPlugin / ClipboardPlugin / ScreenPlugin
- **系统**：SystemPlugin / ProcessPlugin / PowerPlugin / SingleInstancePlugin / ScreensaverPlugin
- **文件/IO**：FilesystemPlugin / CsvPlugin / StorePlugin / LogPlugin
- **网络**：HttpPlugin / WebSocketPlugin / FetchPlugin
- **媒体**：AudioPlugin / VideoPlugin / CameraPlugin / ImagePlugin
- **硬件**：BatteryPlugin / DevicePlugin / HardwarePlugin / SensorPlugin
- **UI**：NotifyPlugin / ToastPlugin / TrayPlugin / BadgePlugin
- **安全**：EncryptionPlugin / HashPlugin / RandomPlugin / MinisignPlugin
- **应用**：UpdaterPlugin / BrowserPlugin / ShellPlugin / EbookPlugin
- **其他**：MathPlugin / WeatherPlugin / TimePlugin / CalendarPlugin

**移动端插件**（4 个，位于 `src/Wails.Net.Application/Plugins/Mobile/`）：
- CameraPlugin / HapticsPlugin / VibrationPlugin / PermissionsPlugin

**附加插件**（4 个平台接口）：
- MobilePlatformPlugin / DesktopPlatformPlugin / etc.

### 关键差异

- **Wails.Net** 的 [IPlugin](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/IPlugin.cs) 4 阶段生命周期是最完整的：`ConfigureServices`（DI 注册）→ `Configure`（命令注册）→ `StartupAsync`（运行时初始化）→ `ShutdownAsync`（资源释放），并默认实现 `StartupAsync`/`ShutdownAsync` 返回 `Task.CompletedTask`。
- **Wails.Net** 的 [PluginManager](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/PluginManager.cs) 通过 `Interlocked` 防止 `IHostedService` 适配器与 `Application.Run` 重复触发。
- **Tauri 2** 插件生态最丰富（30+ 官方 + 大量社区），通过 Cargo 集成。
- **Wails 3** 插件以 Service 模式为主，无独立权限声明。

---

## 9. AssetServer

| 维度 | Wails.Net | Tauri 2 | Wails 3 |
|------|-----------|---------|---------|
| **内置 AssetServer** | ✅ `Wails.Net.AssetServer` 独立项目 | ✅ `tauri::Asset` | ✅ `assetserver` |
| **中间件管道** | ✅ `IMiddleware` + `MiddlewareChain` | ❌ | ❌ |
| **CSP Nonce 注入** | ✅ `NonceInjector` | ✅ | ✅ |
| **隔离注入** | ✅ `IsolationInjector` | ✅ | ❌ |
| **Range 请求** | ✅ `Content-Range` / `Accept-Ranges` | ✅ | ✅ |
| **ETag** | ✅ `ETag` / `If-None-Match` | ✅ | ✅ |
| **Last-Modified** | ✅ `Last-Modified` / `If-Modified-Since` | ✅ | ✅ |
| **自定义 Header** | ✅ `Headers` 静态类 | ✅ | ✅ |
| **MIME 类型** | ✅ 内置映射 | ✅ | ✅ |

### 关键差异

- **Wails.Net** 的 [AssetServer](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.AssetServer/AssetServer.cs) 是三者中**唯一**提供中间件管道（`IMiddleware` + `MiddlewareChain`）的方案，支持灵活扩展（如自定义鉴权、日志、压缩等中间件）。
- **Tauri 2** 和 **Wails 3** 的 AssetServer 功能完整但无中间件抽象。

---

## 10. Updater

| 维度 | Wails.Net | Tauri 2 | Wails 3 |
|------|-----------|---------|---------|
| **Updater 服务** | ✅ `UpdaterService`（`IServiceStartup` / `IServiceShutdown`） | ✅ `tauri-plugin-updater` | ✅ `Updater` |
| **UpdateInfo 结构** | ✅ `Version` / `DownloadUrl` / `UpdateAvailable` / `ReleaseNotes` | ✅ | ✅ |
| **多 Provider** | ❌（单一） | ❌（单一） | ✅ 多 provider |
| **下载器** | ✅ `UpdateDownloader` | ✅ | ✅ |
| **事件广播** | ✅ 通过 `EventProcessor` 广播更新事件 | ✅ | ✅ |
| **签名校验** | ✅ Minisign（见 §11） | ✅ Minisign | ✅ Minisign |
| **Helper Process** | ❌ | ❌ | ✅ 替换二进制 |
| **Windows 自动签名** | ✅ Authenticode | ❌ | ❌ |

### 关键差异

- **Wails.Net** 的 [UpdaterService](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Services/UpdaterService.cs) 集成 Minisign 签名校验和 Authenticode 自动签名（见 §11）。
- **Wails 3** 支持多 provider 和 Helper Process 替换二进制，功能最完整。
- **Tauri 2** 通过独立插件提供 Updater，与 Wails.Net 模型类似。

---

## 11. 打包与签名

| 维度 | Wails.Net | Tauri 2 | Wails 3 |
|------|-----------|---------|---------|
| **构建工具** | Cake Frosting（C#） | `tauri build` (Rust) | `wails3 task build` (Taskfile) |
| **脚本语言** | F# (.fsx)（AGENTS.md §7.5 禁止 Python） | Shell / PowerShell | Shell / PowerShell |
| **Windows 安装包** | ✅ NSIS / MSI | ✅ NSIS / MSI | ✅ NSIS |
| **Linux 安装包** | ✅ .deb / .rpm | ✅ .deb / .rpm / AppImage | ✅ .deb / .rpm |
| **macOS 安装包** | ❌（暂不支持 macOS） | ✅ .dmg / .app | ✅ .dmg / .app |
| **Android APK/AAB** | ✅（.NET Android workload） | ✅ | ✅ |
| **iOS IPA** | ❌ | ✅ | ✅ |
| **代码签名（Windows）** | ✅ Authenticode 自动签名 | ✅（手动配置） | ❌ |
| **代码签名（Linux）** | ✅ GPG | ✅ GPG | ✅ GPG |
| **代码签名（macOS）** | N/A | ✅ notarization | ✅ notarization |
| **Minisign 签名** | ✅ 5 个 Minisign 相关文件 | ✅ | ✅ |
| **CI runner** | Linux runner 构建 Linux 包（迁移自 Windows runner） | 跨平台 | 跨平台 |
| **自包含构建** | ✅ 三平台自包含（无 .NET 运行时依赖） | ✅（Rust 编译） | ✅（Go 编译） |

### 关键差异

- **Wails.Net** 使用 **Cake Frosting**（C# 编写构建脚本）+ **F# .fsx** 脚本，符合 AGENTS.md §7.5 禁止 Python 的约束。
- **Wails.Net** 是三者中**唯一**支持 Windows Authenticode **自动签名**的方案。
- **Wails.Net** CI 已将 `dist-linux` 任务迁移到 Linux runner，提升构建效率。

---

## 12. CLI 工具

| 维度 | Wails.Net | Tauri 2 | Wails 3 |
|------|-----------|---------|---------|
| **CLI 入口** | `Wails.Net.Cli` (System.CommandLine) | `tauri` (Cargo) | `wails3` (Go) |
| **CLI 解析库** | `System.CommandLine` 2.0.9 | `clap` | `commander` |
| **子命令数** | 16 个 | 15+ 个 | 10+ 个 |
| **子命令清单** | Generate / Doctor / New / Build / Dev / Publish / Pack / Plugin / Version / Clean / Info / Icon / Signer / Platform / SelfUpdate / Deploy | init / dev / build / bundle / icon / signer / plugin / migrate / info / etc. | init / dev / build / package / plugin / doctor / etc. |
| **代码生成器** | ✅ `Wails.Net.Generator` + `Wails.Net.SourceGenerators` | ✅ `tauri-codegen` | ✅ `generator` |
| **模板系统** | ✅ `Wails.Net.Templates` | ✅ `tauri-cli` 模板 | ✅ `wails3 init` 模板 |
| **平台命令** | ✅ `Platform` 子命令 | ✅ `tauri android` / `tauri ios` | ✅ |
| **Doctor 诊断** | ✅ `Doctor` 子命令 | ✅ `tauri info` | ✅ `wails3 doctor` |
| **图标生成** | ✅ `Icon` 子命令 | ✅ `tauri icon` | ✅ |
| **自更新** | ✅ `SelfUpdate` 子命令 | ✅ `tauri self update` | ✅ |

### 关键差异

- **Wails.Net** 的 [Program.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Cli/Program.cs) 提供 16 个子命令，覆盖生成、诊断、构建、发布、打包、插件、版本、清理、信息、图标、签名、平台、自更新、部署。
- **AGENTS.md §1.1 约束**：禁止使用 `McMaster.Extensions.CommandLineUtils`，必须使用 `System.CommandLine` 2.0.9。

---

## 13. Server 模式（Wails.Net 独有）

| 维度 | Wails.Net | Tauri 2 | Wails 3 |
|------|-----------|---------|---------|
| **无 GUI 模式** | ✅ `ServerPlatformApp` + `ServerWebviewWindow` + `ServerClipboard` | ❌ | ✅ Headless 模式 |
| **阻塞模型** | ✅ `ManualResetEventSlim` 阻塞直到 `SignalShutdown` | N/A | ✅ |
| **GUI 操作** | 全部 no-op | N/A | 全部 no-op |
| **单实例锁** | ✅ 始终返回 true（视作首实例） | N/A | ✅ |
| **对话框** | 返回默认值（首个按钮 / null） | N/A | ✅ |
| **屏幕查询** | 返回 null / 空数组 | N/A | ✅ |
| **主线程分发** | 同步执行 `action()` | N/A | ✅ |
| **应用场景** | 容器化部署 / 自动化测试 / 服务端渲染 | N/A | 自动化测试 |

### 关键差异

- **Wails.Net** 的 [ServerPlatformApp](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Platform/ServerMode/ServerPlatformApp.cs) 是三者中**最完整**的无 GUI 降级方案，提供与桌面模式完全一致的 API 表面，所有 GUI 操作 no-op 但不抛异常，适用于：
  - 容器化部署（Docker / Kubernetes）
  - 自动化 UI 测试（无窗口环境）
  - 服务端预渲染
- **Wails 3** 的 Headless 模式功能类似，但 API 表面不如 Wails.Net 完整。
- **Tauri 2** 无内置 Server 模式。

---

## 14. 测试框架

| 维度 | Wails.Net | Tauri 2 | Wails 3 |
|------|-----------|---------|---------|
| **测试框架** | **TUnit** 1.58.0 | `cargo test` (Rust) | `testify` (Go) |
| **断言库** | `TUnit.Assertions`（必须 `await`） | `assert_eq!` | `testify/assert` |
| **运行命令** | `dotnet run --project tests/...`（非 `dotnet test`） | `cargo test` | `go test` |
| **禁止项** | MSTest / xUnit / NUnit | N/A | N/A |
| **平台特定测试** | Windows / Linux / Android / CLI 各自独立测试项目 | 跨平台 | 跨平台 |
| **并发控制** | `[NotInParallel]` 属性 | `#[serial]` | `t.Parallel()` |
| **测试命名** | `Method_Scenario_ExpectedBehavior` | `test_*` | `Test*` |
| **覆盖要求** | 公共 API 100% / 错误路径全覆盖 / 边界条件 / 并发场景 | 无强制 | 无强制 |

### 关键差异

- **Wails.Net** 是三者中**唯一**禁止使用主流测试框架（MSTest/xUnit/NUnit）的项目，强制使用 TUnit 1.58.0（AGENTS.md §1.1）。
- **Wails.Net** .NET 10 SDK 不再支持 `dotnet test`（VSTest 模式），必须使用 `dotnet run --project`（MTP 模式）。
- **Wails.Net** 测试覆盖要求最严格：公共 API 100% 方法覆盖、所有 `catch` 分支必须测试、边界条件、并发场景。

---

## 15. 三方插件对照表

| 功能领域 | Wails.Net | Tauri 2 | Wails 3 |
|---------|-----------|---------|---------|
| **窗口** | WindowPlugin | window | WebviewWindow API |
| **对话框** | DialogPlugin | dialog | dialog |
| **菜单** | MenuPlugin | menu | menu |
| **剪贴板** | ClipboardPlugin | clipboard-manager | clipboard |
| **屏幕** | ScreenPlugin | (内置) | screen |
| **文件系统** | FilesystemPlugin | fs | fs |
| **HTTP** | HttpPlugin | http | http |
| **WebSocket** | WebSocketPlugin | (社区) | (无) |
| **Fetch** | FetchPlugin | http | (无) |
| **存储** | StorePlugin | store | store |
| **日志** | LogPlugin | log | log |
| **通知** | NotifyPlugin | notification | notification |
| **Toast** | ToastPlugin | (社区) | (无) |
| **系统托盘** | TrayPlugin | tray | tray |
| **Badge** | BadgePlugin | (社区) | (无) |
| **加密** | EncryptionPlugin | (社区) | (无) |
| **哈希** | HashPlugin | (社区) | (无) |
| **随机数** | RandomPlugin | (社区) | (无) |
| **Minisign** | MinisignPlugin | (内置 updater) | (内置 updater) |
| **更新器** | UpdaterPlugin | updater | updater |
| **浏览器** | BrowserPlugin | shell | browser |
| **Shell** | ShellPlugin | shell | shell |
| **进程** | ProcessPlugin | process | (无) |
| **电源** | PowerPlugin | (社区) | (无) |
| **单实例** | SingleInstancePlugin | single-instance | single-instance |
| **屏幕保护** | ScreensaverPlugin | (无) | (无) |
| **音频** | AudioPlugin | (社区) | (无) |
| **视频** | VideoPlugin | (社区) | (无) |
| **相机（桌面）** | CameraPlugin | (社区) | (无) |
| **图像** | ImagePlugin | image | (无) |
| **电池** | BatteryPlugin | (社区) | (无) |
| **设备** | DevicePlugin | (社区) | (无) |
| **硬件** | HardwarePlugin | (社区) | (无) |
| **传感器** | SensorPlugin | (社区) | (无) |
| **电子书** | EbookPlugin | (无) | (无) |
| **数学** | MathPlugin | (无) | (无) |
| **天气** | WeatherPlugin | (无) | (无) |
| **时间** | TimePlugin | (无) | (无) |
| **日历** | CalendarPlugin | (无) | (无) |
| **系统** | SystemPlugin | os | (内置) |
| **相机（移动）** | CameraPlugin (Mobile) | camera | camera |
| **触觉反馈** | HapticsPlugin (Mobile) | haptics | haptics |
| **振动** | VibrationPlugin (Mobile) | vibrate | vibrate |
| **权限（移动）** | PermissionsPlugin (Mobile) | permissions | permissions |
| **全局快捷键** | (WindowPlugin 内置) | global-shortcut | (内置) |
| **深色模式** | (SystemPlugin 内置) | (内置) | (内置) |
| **OAuth** | (无) | deep-link / oauth | (无) |
| **Biometric** | (无) | biometric | (无) |
| **Barcode Scanner** | (无) | barcode-scanner | (无) |
| **Localhost** | (无) | localhost | (无) |
| **Positioner** | (无) | positioner | (无) |
| **SQL** | (无) | sql | (无) |
| **Stronghold** | (无) | stronghold | (无) |
| **Updater（签名）** | ✅ Minisign + Authenticode | ✅ Minisign | ✅ Minisign |
| **WebSocket（原生）** | ✅ | (社区) | (无) |

### 关键差异

- **Wails.Net** 内置插件数最多（41 桌面 + 4 移动 = 45 个），覆盖大量 Tauri 2 / Wails 3 需要社区插件或无实现的功能（如音频、视频、电池、硬件、传感器、电子书、数学、天气、时间、日历等）。
- **Tauri 2** 官方插件 30+ 个，但部分功能（SQL / Stronghold / Biometric / Barcode Scanner / OAuth）Wails.Net 暂未实现。
- **Wails 3** 内置插件最少，依赖社区扩展。

---

## 16. Wails.Net 独有能力

以下是 Wails.Net 相对于 Tauri 2 和 Wails 3 的独有能力：

### 16.1 Server 模式（见 §13）
完整的无 GUI 降级方案，支持容器化部署和自动化测试。

### 16.2 多传输层并行广播
[EventProcessor](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Events/EventProcessor.cs) 同时通过 HttpTransport + WebSocketBroadcaster + EventIPCTransport + AssetServerTransport + NativeIpcTransport 广播事件，确保所有连接的前端都能收到。

### 16.3 CommandDispatcher 中间件管道
[CommandDispatcher](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Commands/CommandDispatcher.cs) 支持 `ICommandMiddleware` 管道和命令超时，提供类似 ASP.NET Core 中间件的扩展点。

### 16.4 AssetServer 中间件管道
[AssetServer](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.AssetServer/AssetServer.cs) 通过 `IMiddleware` + `MiddlewareChain` 提供灵活的 HTTP 中间件扩展。

### 16.5 Windows Authenticode 自动签名
三方中唯一支持 Windows Authenticode 自动签名。

### 16.6 IPlugin 4 阶段生命周期
最完整的插件生命周期：`ConfigureServices` → `Configure` → `StartupAsync` → `ShutdownAsync`。

### 16.7 Channel API
[IChannel](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Channels/Channel.cs) + `ChannelManager` + `Interlocked` 线程安全，提供双向流式通信。

### 16.8 Microsoft.Extensions.* 全栈集成
对 .NET 开发者最友好：Host / DI / Config / Options / Logging 全栈集成。

### 16.9 源生成器绑定 + 反射回退（双路径）
[Bindings](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Bindings/Bindings.cs) 优先走源生成器路径（性能优），反射仅作为兜底（开发体验优）。

### 16.10 内置 41 + 4 个插件
内置插件数最多，覆盖音频/视频/电池/硬件/传感器/电子书/数学/天气/时间/日历等 Tauri 2 / Wails 3 无内置实现的功能。

---

## 17. Wails.Net 差距与路线图

### 17.1 已知差距

| 差距项 | 优先级 | 对标 | 说明 |
|-------|--------|------|------|
| **macOS / iOS 支持** | P1 | Tauri 2 / Wails 3 | 暂不实现，未来考虑通过 .NET macOS 工作负载 + WKWebView 补齐 |
| **OAuth / 深度链接** | P1 | Tauri 2 | deep-link 插件未实现 |
| **Biometric** | P2 | Tauri 2 | 生物识别插件未实现 |
| **Barcode Scanner** | P2 | Tauri 2 | 条码扫描插件未实现 |
| **SQL 插件** | P2 | Tauri 2 | SQL 数据库插件未实现 |
| **Stronghold** | P3 | Tauri 2 | 加密存储插件未实现 |
| **Localhost 插件** | P3 | Tauri 2 | 本地主机插件未实现 |
| **Positioner** | P3 | Tauri 2 | 窗口定位插件未实现 |
| **多 Provider Updater** | P2 | Wails 3 | 当前仅单一 provider |
| **Helper Process 替换二进制** | P2 | Wails 3 | 当前无 Helper Process |
| **原生 IPC 默认化** | P1 | Tauri 2 / Wails 3 | 当前默认 HttpTransport，原生 IPC 为可选 |
| **移动端插件生态** | P1 | Tauri 2 | 仅 4 个移动端插件，Tauri 2 有 20+ |

### 17.2 路线图建议

1. **短期（P0）**：
   - 完成 CancellablePromise + CancelCall 全链路测试
   - EventIPCTransport 回退机制完善
   - 原生 IPC（NativeIpcTransport）作为默认传输
   - Server 模式事件 API 完善

2. **中期（P1）**：
   - macOS / iOS 平台支持（.NET macOS + WKWebView）
   - OAuth / 深度链接插件
   - 移动端插件扩充（Biometric / Barcode Scanner / Localhost / Positioner）
   - 多 Provider Updater

3. **长期（P2/P3）**：
   - SQL / Stronghold 插件
   - Helper Process 替换二进制
   - 社区插件生态建设

---

## 18. 总结

### 18.1 三方定位

| 项目 | 定位 | 优势 | 劣势 |
|------|------|------|------|
| **Wails.Net** | Wails v3 的 .NET 10 移植，融合 ASP.NET Core + Wails v3 + Tauri v2 三家之长 | .NET 全栈集成 / Server 模式 / 多传输层 / 41+ 插件 / 源生成器绑定 / 完整 ACL / Authenticode 自动签名 | 无 macOS/iOS / 移动端插件少 / 无 SQL/Stronghold/OAuth |
| **Tauri 2** | Rust 桌面/移动框架，安全优先 | 全平台（5 个）/ 30+ 官方插件 / 完整 ACL / 编译期绑定（性能最优）/ 强大生态 | Rust 学习曲线 / 无 Server 模式 / 无中间件管道 |
| **Wails 3** | Go 桌面/移动框架，简洁实用 | 全平台 / Go 语言易上手 / 多 Provider Updater / Helper Process / Event Hooks | 无 ACL / 无中间件管道 / 插件生态弱 / 无 Authenticode |

### 18.2 Wails.Net 的核心价值

1. **对 .NET 开发者最友好**：完整集成 Microsoft.Extensions.* 全栈，复用 ASP.NET Core 经验。
2. **架构融合创新**：三家之长（ASP.NET Core 的 Host/DI + Wails v3 的 Runtime/IPC + Tauri v2 的 Plugin/Capability）。
3. **企业级特性**：Server 模式（容器化部署）+ Authenticode 自动签名 + 完整 ACL + 中间件管道。
4. **插件生态最丰富（内置）**：41 桌面 + 4 移动端插件，开箱即用。

### 18.3 选型建议

- **.NET 团队 / 企业级桌面应用**：选 Wails.Net（复用 .NET 技能栈 + Server 模式 + Authenticode）
- **Rust 团队 / 安全敏感应用**：选 Tauri 2（编译期绑定 + 完整 ACL + 全平台）
- **Go 团队 / 快速原型**：选 Wails 3（Go 语言简洁 + 多 Provider Updater + Helper Process）
- **需要 macOS/iOS**：暂选 Tauri 2 或 Wails 3（Wails.Net 暂不支持）

---

**文档结束**

> 本文档基于 2026-07-18 仓库代码状态生成。如发现信息过时或错误，请提交 Issue 或 PR 更新。
