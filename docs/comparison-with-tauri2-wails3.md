# Wails.Net vs Tauri 2 vs Wails 3 功能对比详情

> 本文档对比 **Wails.Net**（当前项目，Wails v3 的 .NET 10 移植实现）、**Tauri 2**（Rust 桌面/移动框架）和 **Wails 3 v3.0.0-alpha.102**（Go 原版）三者的功能实现项与差异项。
>
> - **更新日期**：2026-07-19（基于实际仓库代码核对，新增 MenuRole 系统 + 4 个 Android 移动端插件平台实现 + AndroidRuntimePlugin + AndroidPlatformEvents + 3 个公共事件）
> - **对比基线**：基于本仓库当前 `src/` 实际代码状态（提交 `f867b9c`）
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
6. [菜单系统与 MenuRole](#6-菜单系统与-menurole)
7. [权限与安全模型](#7-权限与安全模型)
8. [移动端支持](#8-移动端支持)
9. [插件系统](#9-插件系统)
10. [AssetServer](#10-assetserver)
11. [Updater](#11-updater)
12. [打包与签名](#12-打包与签名)
13. [CLI 工具](#13-cli-工具)
14. [Server 模式（Wails.Net 独有）](#14-server-模式wailsnet-独有)
15. [测试框架](#15-测试框架)
16. [三方插件对照表](#16-三方插件对照表)
17. [Wails.Net 独有能力](#17-wailsnet-独有能力)
18. [Wails.Net 差距与路线图](#18-wailsnet-差距与路线图)
19. [总结](#19-总结)

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

- **Wails.Net** 是三者中**唯一**采用 ASP.NET Core 全栈（Host/DI/Config/Logging/Options）的方案，对 .NET 开发者最友好；同时通过 `ServerPlatformApp`/`ServerWebviewWindow` 提供容器化降级路径（见 §14）。
- **Tauri 2** 是三者中**唯一**支持全部 5 个平台（Win/Linux/macOS/iOS/Android）的方案。
- **Wails 3** 与 Wails.Net 共享对象模型（`WebviewWindowOptions`、`Application`、Manager 模式），但 Wails.Net 在 IPC/权限/插件/MenuRole 上分别借鉴了 Tauri 2 的设计。

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

- **Wails.Net** 通过 [BindingManager](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Bindings/Bindings.cs) 在编译期生成 `TryGetInvoker` 委托（由 [BindingSourceGenerator](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.SourceGenerators/BindingSourceGenerator.cs) 通过 `IIncrementalGenerator` 生成 `WailsGeneratedBindings.g.cs`，使用 `[ModuleInitializer]` 自动注册），运行时优先走源生成器路径，反射仅作为兜底，兼顾性能与开发体验。
- **Tauri 2** 完全无反射，性能最优，但需要 Rust 宏学习成本。
- **Wails 3** 与 Wails.Net 共享 FNV-1a 哈希算法，前端绑定 ID 可互通。

---

## 3. 事件系统

| 维度 | Wails.Net | Tauri 2 | Wails 3 |
|------|-----------|---------|---------|
| **核心类型** | `EventProcessor` | `EventBus` / `Emitter` | `Events` struct |
| **公共事件类型数** | **30 个**（ApplicationEventType 枚举，0~29） | ~20 个 | ~28 个 |
| **监听器存储** | `ConcurrentDictionary<string, List<EventListener>>` | `Vec<EventListener>` | `map[string][]EventListener` |
| **Pre-emit 钩子** | ✅ `List<Func<CustomEvent, bool>>`（返回 false 取消） | ❌ | ✅ Event Hooks |
| **多传输层广播** | ✅ `IWailsEventListener` 列表并行广播 | 单一 IPC | 单一 in-memory bridge |
| **事件命名空间** | `wails:window:*` / `wails:app:*` / `wails:updater:*` / `wails:low:memory` / `wails:screen:*` 等 | `tauri://*` | `wails:*` |
| **JS 端 API** | `wails.events.on/off/emit` | `listen/unlisten/emit` | `wails.events.on/off/emit` |
| **跨窗口广播** | ✅ 通过多传输层自动广播 | ✅ | ✅ |
| **线程安全** | ✅ `ConcurrentDictionary` + 锁 | ✅ `Mutex` | ✅ `sync.RWMutex` |
| **senderWindowId 传播** | ✅ EventProcessor/Transport 全链路携带（P1-2） | ❌ | ❌ |
| **PostShutdown 钩子** | ✅ `ApplicationOptions.PostShutdown`（P1-7） | ❌ | ✅ `Options.PostShutdown` |
| **ShouldQuit 回调** | ✅ `ApplicationOptions.ShouldQuit`（P1-7） | ❌ | ✅ `Options.ShouldQuit` |
| **maxCalls 限制** | ✅ `OnMultiple` + `Once` | ✅ `once` | ✅ `Once` |
| **LowMemory 事件** | ✅ `ApplicationEventType.LowMemory=27`（P2 新增） | ❌ | ✅ `Common.LowMemory=1290` |
| **ScreenLocked/Unlocked** | ✅ `ApplicationEventType.ScreenLocked=28` / `ScreenUnlocked=29`（P2 新增） | ❌ | ✅ `Common.ScreenLocked=1288` / `ScreenUnlocked=1289` |
| **平台事件映射** | ✅ `AndroidPlatformEvents.MapToCommonEvent`（P2 新增） | ❌ | ✅ `commonApplicationEventMap` |

### 关键差异

- **Wails.Net** 的 [EventProcessor](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Events/EventProcessor.cs) 同时支持 Pre-emit 钩子（学 Wails 3）和**多传输层并行广播**（独有）：一次 `Emit` 会同时通过 HttpTransport + WebSocketBroadcaster + EventIPCTransport + AssetServerTransport + NativeIpcTransport 广播，确保所有连接的前端都能收到。
- **Wails.Net** 自 P1-2 起在 EventProcessor 与 Transport 层全链路传播 `senderWindowId`，前端可按 `windowId` 过滤事件来源（[EventSenderWindowPropagationTests](file:///f:/Code/Dotnet/Wails.Net/tests/Wails.Net.Application.Tests/Transport/EventSenderWindowPropagationTests.cs) 验证），这是三者中**唯一**支持事件来源窗口标识的方案。
- **Wails.Net** 自 P1-7 起补齐 [PostShutdown / ShouldQuit](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Application.cs) 事件钩子，对齐 Wails v3 的 `cleanup()` 末尾调用与平台信号处理器 `shouldQuit()` 拦截机制。
- **Wails.Net** 自 P2 起新增 3 个公共事件（[ApplicationEventType](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Events/ApplicationEventType.cs) 扩展至 30 个枚举值），对齐 Wails v3 `Common.LowMemory/ScreenLocked/ScreenUnlocked`：
  - `LowMemory=27` → `wails:low:memory`
  - `ScreenLocked=28` → `wails:screen:locked`
  - `ScreenUnlocked=29` → `wails:screen:unlocked`
  - 通过 [AndroidPlatformEvents](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Android/AndroidPlatformEvents.cs) 将 Android 平台事件（`ApplicationLowMemory=1273` / `ScreenLocked=1284` / `ScreenUnlocked=1285`）映射到上述公共事件，对应 Wails v3 `events_common_android.go` 的 `commonApplicationEventMap`。
- **Tauri 2** 事件总线单一 IPC，但提供 `emit_to` / `emit_filter` 精细控制。
- **Wails 3** 通过 Event Hooks 实现服务端拦截，与 Wails.Net 的 Pre-emit 钩子等价。

---

## 4. IPC 与传输层

| 维度 | Wails.Net | Tauri 2 | Wails 3 |
|------|-----------|---------|---------|
| **传输抽象** | `ITransport` 接口 | `Invoke` 机制（编译期绑定） | `ITransport` 接口 |
| **默认传输** | NativeIpcTransport（默认）+ HttpTransport + WebSocketTransport | 平台原生 IPC（WebView2 host object / WKWebView message handler / WebKitGTK） | in-memory bridge（前端 postMessage） |
| **HTTP 端点** | `/wails/message` (POST) + `/wails/ws` (WS) | N/A | N/A |
| **默认端口** | 34115（HTTP） / 34116（WebSocket）起递增 | N/A | N/A |
| **WebSocket 传输** | ✅ `WebSocketTransport`（独立 34116 端口） | ❌（使用原生 IPC） | ❌ |
| **原生 IPC** | ✅ `NativeIpcTransport`（P0-C2，默认启用，`UseNativeIpc=true`） | ✅ 默认 | ✅ 默认 |
| **Event IPC 回退** | ✅ `EventIPCTransport`（P0-C3，始终追加为兜底监听器） | ❌ | ❌ |
| **AssetServer 传输** | ✅ `AssetServerTransport` | ❌ | ❌ |
| **分块上传** | ✅ `x-wails-chunk-id/index/total` 协议（单 chunk ≤1MB，总 ≤64MB，会话 TTL 30s） | ❌ | ❌ |
| **消息类型常量** | `MessageTypes`：Call/Event/Window/Query/Response/Error/Drag/ContextMenu/System/**Cancel** | `Invoke` / `Event` | Call/Event/Window/Query/Response/Error/Drag/ContextMenu/System |
| **异步队列** | ✅ `Channel`-based 异步处理 | ✅ Tokio task | ✅ goroutine |
| **CancellablePromise** | ✅ `_runningCalls` 表 + 前端 `cancel` 消息 | ✅ `Cancellation` | ✅ `CancelCall` |
| **运行中调用表** | `ConcurrentDictionary<string, CancellationTokenSource>` | N/A | `map[string]*CancelCall` |
| **CORS 配置** | ✅ `CorsOptions`（白名单回显，默认 `*`） | ✅ | N/A |
| **IPC Origin 校验** | ✅ `IpcOriginValidator` + `OriginValidator.Validate` | ✅ | N/A |
| **消息路由** | `MessageProcessor` 三路径：Call→BindingManager / Window→WindowPlugin / 其他→CommandDispatcher 兜底 | `Invoke` 直接路由到命令 | `MessageProcessor` + `DispatchWindowAction` |

### 关键差异

- **Wails.Net** 是三者中**唯一**支持多传输层并行广播的方案（见 [HttpTransport.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/HttpTransport.cs)），适用于容器化部署和调试场景。
- **Wails.Net** 的 [NativeIpcTransport](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/NativeIpcTransport.cs) 采用**混合策略**：小消息 (<512KB) 走原生 postMessage 通道，大消息自动回退 HTTP 分块，平衡延迟与容量。
- **Wails.Net** 的 [MessageProcessor](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/MessageProcessor.cs) 新增 `MessageTypes.Cancel` 消息类型，与 Wails 3 的 `CancelCall` 对齐；并采用 Tauri v2 "核心即插件" 哲学，未识别命名空间通过 `ProcessCommandFallbackAsync` 作为命令名派发到 CommandDispatcher。
- **Tauri 2** 和 **Wails 3** 默认走原生 IPC，性能更高但调试不便。

---

## 5. 窗口管理

| 维度 | Wails.Net | Tauri 2 | Wails 3 |
|------|-----------|---------|---------|
| **窗口抽象** | `IWebviewWindowImpl` + `WebviewWindow`（100+ 方法） | `WebviewWindow` / `WebviewWindowBuilder` | `WebviewWindow` + `IWebviewWindowImpl` |
| **窗口管理器** | `WindowManager`（`Interlocked.Increment` 线程安全 ID） | `WebviewWindowBuilder` | `WindowManager` |
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
| **热键** | ✅ `WM_HOTKEY` / `Win32KeyBindingManager` | ✅（plugin） | ✅ |
| **Badge** | ✅ `SetBadgeCount/SetBadgeLabel` | ✅（plugin） | ✅ |
| **多工作区** | ✅ `SetVisibleOnAllWorkspaces` | ✅ | ✅ |
| **边框颜色** | ✅ `SetBorderColor` | ✅ | ✅ |
| **任务栏进度** | ✅ `SetTaskbarProgress` / `SetOverlayIcon` | ✅（plugin） | ✅ |
| **窗口效果** | ✅ `SetEffects`（Mica/Acrylic 等） | ✅ | ✅ |
| **内容保护** | ✅ `SetContentProtection` | ✅ | ✅ |
| **窗口级 CSP** | ✅ `SetCspHeaderForWindow`（P0-4，per-window） | ✅ | ❌ |
| **命令路径** | `window.*` 命令通过 `WindowPlugin` 暴露 | `window.*` plugin 命令 | `wails.window.*` 前端 API |
| **权限校验** | ✅ `window:default` / `window:allow-readonly` / `window:allow-dangerous` | ✅ `window:default` / `window:allow-*` | ❌ |
| **上下文菜单数据** | ✅ `ContextMenuData` + `MenuManager`（P1-4） | ✅ Menu plugin | ✅ ContextMenu |
| **Frameless 拖拽 CSS 变量** | ✅ `--wails-drag-region` 统一变量（P1-5） | ✅ `data-tauri-drag-region` | ✅ `--wails-drag-region` |

### 关键差异

- **Wails.Net** 的 [WindowPlugin](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/BuiltIn/WindowPlugin.cs) 借鉴 Tauri v2 的"核心即插件"哲学，将所有窗口操作以插件命令形式暴露，同时声明三层权限集（`window:default` / `window:allow-readonly` / `window:allow-dangerous`）。命令分为：标题与尺寸、显示/隐藏/状态、全屏与置顶、DevTools、缩放、导航、打印与导出、执行 JS 与注入 CSS、透明度、可调整大小、自定义协议、任务栏、查询类操作（有返回值）。
- **Wails.Net** 自 P1-4 起新增 [MenuManager](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Managers/MenuManager.cs) 与 [ContextMenuData](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Menus/ContextMenuData.cs)，并让 [RuntimeGenerator](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/RuntimeGenerator.cs) 注入 ContextMenu 钩子，对齐 Wails v3 的右键菜单行为（[MessageProcessorContextMenuTests](file:///f:/Code/Dotnet/Wails.Net/tests/Wails.Net.Application.Tests/Transport/MessageProcessorContextMenuTests.cs) 验证）。
- **Wails.Net** 自 P1-5 起统一三平台 Frameless 拖拽实现：[DragRegionHelper](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Windows/DragRegionHelper.cs) 统一使用 `--wails-drag-region` CSS 变量，Windows / Linux / Android 三平台 WebviewWindow 行为一致。
- **Wails 3** 窗口 API 直接通过 `wails.window.*` 暴露，无权限校验。
- **Wails.Net** Win32 实现 [Win32WebviewWindow](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Windows/Win32WebviewWindow.cs) 完整处理 WM_DESTROY/WM_CLOSE/WM_SIZE/WM_COMMAND/WM_SYSCOMMAND/WM_GETMINMAXINFO/WM_DPICHANGED/WM_HOTKEY/WM_DROPFILES/WM_SETTINGCHANGE/WM_MOVE/WM_NCLBUTTONDOWN/WM_SETICON/WM_ACTIVATE/WM_DISPLAYCHANGE/WM_CLIPBOARDUPDATE/WM_KEYDOWN/WM_CONTEXTMENU 等 18+ 消息。

---

## 6. 菜单系统与 MenuRole

| 维度 | Wails.Net | Tauri 2 | Wails 3 |
|------|-----------|---------|---------|
| **菜单抽象** | `Menu` / `MenuItem` / `IMenuImpl` | `Menu` / `MenuItem` / `PredefinedMenuItem` | `Menu` / `MenuItem` / `MenuRole` |
| **预定义角色枚举** | ✅ `MenuRole`（21 个值，P2 新增） | ✅ `PredefinedMenuItem`（约 14 个） | ✅ `Role` 常量 |
| **角色工厂方法** | ✅ 13 个（CreateCopy/CreateCut/CreatePaste/CreateSelectAll/CreateUndo/CreateRedo/CreateSeparator/CreateMinimize/CreateMaximize/CreateFullscreen/CreateCloseWindow/CreateQuit/CreateAbout） | ✅ `PredefinedMenuItem::new_*` | ✅ 隐式 |
| **跨平台辅助工具** | ✅ `MenuRoleHelper`（默认 Label / 默认 Accelerator / macOS 专属判定 / PrepareRoleItem） | ✅ | ✅ |
| **macOS 专属角色降级** | ✅ Windows/Linux 静默 no-op（不抛异常） | ✅ | ✅ |
| **关于对话框元数据** | ✅ `AboutMetadata`（对应 Tauri v2 AboutMetadata） | ✅ `AboutMetadata` | ✅ |
| **标准菜单组合** | ✅ `AddStandardEditMenu` / `AddStandardWindowMenu` / `AddStandardHelpMenu` | ❌（需手动组合） | ❌ |
| **Win32 实现** | ✅ [Win32Menu.ApplyRole](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Windows/Win32Menu.cs)（Copy/Cut/Paste/SelectAll/Undo/Redo 通过 `document.execCommand`；Minimize/Maximize/Fullscreen/CloseWindow/Quit/About） | N/A | ✅ |
| **Linux 实现** | ✅ [LinuxMenu.ApplyRole](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Linux/LinuxMenu.cs)（GTK4 GMenu 模型 + KeyBindingManager 注册全局热键） | N/A | ✅ |
| **Android 实现** | ❌（Android 无标准应用菜单栏概念） | N/A | ❌ |
| **前端 API** | ✅ `wails.MenuRole` 常量 + `menu.addRoleItem` / `menu.addStandardEditMenu` / `menu.addStandardWindowMenu` / `menu.addStandardHelpMenu` | ✅ `MenuItem.predefined_*` | ✅ `Role` |
| **插件命令总数** | 10 个（menu.setApplicationMenu / menu.getApplicationMenu / menu.setContextMenu / menu.popup / menu.updateMenuItem / menu.addRoleItem / menu.addStandardEditMenu / menu.addStandardWindowMenu / menu.addStandardHelpMenu + 1 个内部分发） | 8 个 | N/A |

### MenuRole 21 个角色枚举值

[MenuRole](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Menus/MenuRole.cs) 定义如下 21 个枚举值，对齐 Wails v3 Go 版本 `menu.go` 中的 `Role` 常量并参考 Tauri v2 `PredefinedMenuItem` 平台支持矩阵：

| 类别 | 角色值 | 说明 | macOS 专属 |
|------|--------|------|-----------|
| 普通项 | `None` | 默认值，走自定义 Callback | 否 |
| 分隔符 | `Separator` | 等价于 `IsSeparator=true` | 否 |
| **Edit 角色** | `Copy` / `Cut` / `Paste` / `SelectAll` / `Undo` / `Redo` | 调用 `document.execCommand('copy/cut/paste/selectAll/undo/redo')` | 否 |
| **Window 角色** | `Minimize` / `Maximize` / `Fullscreen` / `CloseWindow` | 窗口操作 | 否 |
| | `Zoom` | macOS 专属语义，其他平台等价于 Maximize | 是 |
| **Application 角色** | `About` | 弹出关于对话框（需配合 `AboutMetadata`） | 否 |
| | `Quit` | 退出应用 | 否 |
| | `Hide` / `HideOthers` / `ShowAll` / `Services` / `BringAllToFront` | macOS 专属 | 是 |
| | `ToggleFullScreen` | macOS 专属别名，等价于 `Fullscreen` | 是 |

### 关键差异

- **Wails.Net** 的 MenuRole 系统（P2 新增）是三者中**最完整**的角色菜单实现，结合 Wails v3 的 Role 常量模型与 Tauri v2 的 PredefinedMenuItem 工厂模式：
  - **21 个角色枚举值**（[MenuRole.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Menus/MenuRole.cs)）：覆盖跨平台编辑/窗口/应用角色 + macOS 专属角色。
  - **13 个工厂方法**（[MenuItem.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Menus/MenuItem.cs)）：参考 Tauri `PredefinedMenuItem::new_*` 工厂 API，便于编程式构建。
  - **跨平台共享逻辑**（[MenuRoleHelper.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Menus/MenuRoleHelper.cs)）：默认中文 Label、默认 Accelerator（Ctrl+C 等）、macOS 专属判定、PrepareRoleItem 统一填充 Callback。
  - **平台实现分离**：[Win32Menu.ApplyRole](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Windows/Win32Menu.cs) / [LinuxMenu.ApplyRole](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Linux/LinuxMenu.cs) 各自实现 ExecuteRole，编辑命令通过 WebView2/WebKitGTK `document.execCommand` 调用，窗口命令直接调用 `IWebviewWindowImpl` 方法。
  - **全局热键注册**：角色带默认 Accelerator 时自动注册到 `KeyBindingManager`，修复了现有 Accelerator 仅在菜单栏内生效、不全局响应的 bug。
  - **关于对话框**（[AboutMetadata](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Menus/AboutMetadata.cs)）：对应 Tauri v2 `AboutMetadata`，包含 Name/Version/ShortVersion/Authors/Copyright/License/Website/WebsiteLabel/Comments 9 个字段。
  - **标准菜单组合**：`Menu.AddStandardEditMenu()`（Undo/Redo/Sep/Cut/Copy/Paste/SelectAll）+ `AddStandardWindowMenu()`（Minimize/Maximize/Sep/CloseWindow）+ `AddStandardHelpMenu(metadata)`（About），一键构建跨平台标准菜单。
  - **前端 API 注入**：[RuntimeGenerator](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/RuntimeGenerator.cs) 注入 `wails.MenuRole` 常量枚举与 4 个 menu.* 命令，前端可直接构造角色菜单。
  - **测试覆盖**：[MenuRoleTests](file:///f:/Code/Dotnet/Wails.Net/tests/Wails.Net.Application.Tests/MenuRoleTests.cs) 30+ 项测试覆盖 MenuRole 枚举、AboutMetadata、MenuItem 工厂方法、MenuRoleHelper。
- **Tauri 2** 通过 `PredefinedMenuItem` 提供约 14 个预定义菜单项，工厂 API 与 Wails.Net 类似，但无标准菜单组合方法。
- **Wails 3** 通过 `Role` 常量支持菜单角色，但缺少工厂方法与跨平台辅助工具的统一封装。

---

## 7. 权限与安全模型

| 维度 | Wails.Net | Tauri 2 | Wails 3 |
|------|-----------|---------|---------|
| **三层 ACL** | ✅ Capability + PermissionSet + Scope | ✅ Capability + Permission + Scope | ❌ |
| **配置文件** | `appsettings.json` (Wails:Capabilities) + `capabilities/*.json` | `capabilities/*.json` | N/A |
| **权限声明** | `DeclarePermission` / `RegisterPermissionSet` | `default.toml` / `permissions.toml` | N/A |
| **作用域类型** | `FileSystemScope` / `UrlScope` | `fs scope` / `http scope` / `asset protocol scope` | N/A |
| **Deny 优先** | ✅ `_deniedPermissions` 优先于 `_grantedPermissions` | ✅ `deny-default` | N/A |
| **Remote URL 限制** | ✅ `_remoteUrlScopes` + `IsGranted(perm, window, origin)` | ✅ `remote.urls` | N/A |
| **窗口级隔离** | ✅ `PermissionKey (Permission, Window)` 复合键 | ✅ `windows: ["main"]` | N/A |
| **命令校验** | ✅ `ValidateCommand` + `RequireCapabilityAttribute` | ✅ `capabilities` 引用命令权限 | ❌ |
| **运行时开关** | ✅ `PermissionManager.Enabled`（默认 false 保持向后兼容） | ✅ 默认启用 | N/A |
| **本地源判定** | `wails://` / `localhost` / `127.0.0.1` / null | `tauri://localhost` / `http://localhost` | N/A |
| **URL 白名单** | `UrlWhitelist` 通配符匹配 | `allowed-origins` | N/A |
| **自动加载** | ✅ `LoadCapabilities` 从目录加载 capabilities/*.json | ✅ 启动时加载 | N/A |

### 关键差异

- **Wails.Net** 的 [PermissionManager](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Security/PermissionManager.cs) 是对 Tauri v2 ACL 模型的完整移植，使用 6 个 `ConcurrentDictionary`（`_grantedPermissions` / `_declaredCapabilities` / `_permissionSets` / `_permissionScopes` / `_remoteUrlScopes` / `_deniedPermissions`）实现线程安全的权限管理。
- **Wails.Net** 默认 `Enabled=false` 保持向后兼容，迁移成本低于 Tauri 2（Tauri 2 默认强制启用）。
- **Wails 3** 完全无 ACL 模型，依赖操作系统进程隔离。
- **Wails.Net** 的 [Scopes](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Security/Scopes.cs) 支持：
  - `FileSystemScope`：`Path.GetFullPath` 规范化 + 目录前缀匹配
  - `UrlScope`：通配符 URL 模式（委托 `UrlWhitelist`）

---

## 8. 移动端支持

| 维度 | Wails.Net | Tauri 2 | Wails 3 |
|------|-----------|---------|---------|
| **Android** | ✅ `net10.0-android36.0`（API 24+） | ✅ | ✅ 稳定支持 |
| **iOS** | ❌ 暂不实现 | ✅ | ✅ 稳定支持 |
| **Android Webview** | `Android.Webkit.WebView`（.NET Android 互操作） | Android WebView | Android WebView |
| **iOS Webview** | N/A | WKWebView | WKWebView |
| **移动端插件** | 4 个跨平台插件 + **4 个 Android 平台实现** + 1 个 Android 运行时插件 | 20+ 移动端插件 | 有限 |
| **AndroidRuntimePlugin** | ✅ `device.info` / `toast.show`（P2 新增） | ❌ | ✅ `messageprocessor_android.go` |
| **AndroidPlatformEvents** | ✅ 12 个事件 ID + `MapToCommonEvent` 映射（P2 新增） | ❌ | ✅ `events_common_android.go` |
| **桌面专属 API** | ✅ 桌面方法在移动端 no-op（见 [AndroidWebviewWindow](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Android/AndroidWebviewWindow.cs)） | ✅ | ✅ |
| **构建工具** | `cake build --target=Dist-Android` | `tauri android build` | `wails3 task build` |
| **TFM** | `net10.0-android36.0`（`SupportedOSPlatformVersion=24`） | Kotlin/Java | Go mobile |
| **MAUI Controls** | ❌ 禁止引入（AGENTS.md §1.1） | N/A | N/A |
| **Android Asset Server** | ✅ `AndroidAssetServer`（适配 Android Assets） | ✅ | ✅ |
| **Android Clipboard** | ✅ `AndroidClipboard` | ✅ | ✅ |
| **Android Browser Manager** | ✅ `AndroidBrowserManager` | ✅ | ✅ |
| **Android 平台 App** | ✅ `AndroidPlatformApp`（353 行扩展，集成移动端插件） | ✅ | ✅ |
| **自定义 ChromeClient** | ✅ `WailsWebChromeClient` | N/A | N/A |
| **自定义 WebViewClient** | ✅ `WailsWebViewClient` | N/A | N/A |
| **Message Listener** | ✅ `WailsWebMessageListener` | N/A | N/A |

### 移动端插件平台实现矩阵

| 移动端插件 | 跨平台接口 | Android 实现（P2 补齐） | 对应 Tauri 2 | 对应 Wails 3 |
|-----------|-----------|----------------------|-------------|-------------|
| BarcodeScanner | `IPlatformBarcodeScanner` | ✅ [AndroidBarcodeScanner](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Android/Mobile/AndroidBarcodeScanner.cs) | `@tauri-apps/plugin-barcode-scanner` | ❌ |
| Biometric | `IPlatformBiometric` | ✅ [AndroidBiometric](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Android/Mobile/AndroidBiometric.cs) | `@tauri-apps/plugin-biometric` | ❌ |
| Haptics | `IPlatformHaptics` | ✅ [AndroidHaptics](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Android/Mobile/AndroidHaptics.cs) | `@tauri-apps/plugin-haptics` | `Haptics` |
| Nfc | `IPlatformNfc` | ✅ [AndroidNfc](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Android/Mobile/AndroidNfc.cs) | `@tauri-apps/plugin-nfc` | ❌ |

### Android 平台事件 ID 矩阵（P2 新增）

[AndroidPlatformEvents](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Android/AndroidPlatformEvents.cs) 定义 Android 平台事件 ID 常量，对应 Wails v3 `go-events/events.go` Android 常量（1267~1285）：

| 事件常量 | ID | 公共事件映射 | 说明 |
|---------|-----|------------|------|
| `ActivityCreated` | 1267 | `ApplicationEventType.Started` | Activity 已创建 |
| `ActivityStarted` | 1268 | （无） | Activity 已启动 |
| `ActivityResumed` | 1269 | （无） | Activity 已恢复 |
| `ActivityPaused` | 1270 | （无） | Activity 已暂停 |
| `ActivityStopped` | 1271 | （无） | Activity 已停止 |
| `ActivityDestroyed` | 1272 | （无） | Activity 已销毁 |
| `ApplicationLowMemory` | 1273 | `ApplicationEventType.LowMemory` | 系统内存不足 |
| `BatteryChanged` | 1281 | `ApplicationEventType.BatteryChanged` | 电池状态已更改 |
| `NetworkChanged` | 1282 | `ApplicationEventType.NetworkChanged` | 网络状态已更改 |
| `ThemeChanged` | 1283 | `ApplicationEventType.ThemeChanged` | 系统主题已更改 |
| `ScreenLocked` | 1284 | `ApplicationEventType.ScreenLocked` | 屏幕已锁定 |
| `ScreenUnlocked` | 1285 | `ApplicationEventType.ScreenUnlocked` | 屏幕已解锁 |

通过 `MapToCommonEvent(androidEventId)` 将 7 个 Android 事件映射到公共事件，对应 Wails v3 `events_common_android.go` 的 `commonApplicationEventMap`，由 `AndroidPlatformApp.HandlePlatformEvent(uint)` 调用 `Application.HandlePlatformEvent` 分发。

### 关键差异

- **Wails.Net** 仅支持 Android（API 24+），iOS 暂不实现；通过 .NET Android 工作负载直接调用 `Android.Webkit.WebView`，**不引入 MAUI Controls**。
- **Tauri 2** 和 **Wails 3** 均已稳定支持 iOS + Android。
- **Wails.Net** 通过自定义 `WailsWebChromeClient` / `WailsWebViewClient` / `WailsWebMessageListener` 三件套精细控制 Android WebView 行为。
- **Wails.Net** 自 P2 起补齐 **4 个 Android 移动端插件平台实现**（BarcodeScanner / Biometric / Haptics / Nfc），原本仅有跨平台接口，现 Android 端已具备实际能力。
- **Wails.Net** 自 P2 起新增 [AndroidRuntimePlugin](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Android/Mobile/AndroidRuntimePlugin.cs)，提供 `device.info` / `toast.show` 两个命令，对应 Wails v3 `messageprocessor_android.go` 中的 `androidDeviceInfo()` / `androidShowToast()`。
- **Wails.Net** 自 P2 起新增 [AndroidPlatformEvents](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Android/AndroidPlatformEvents.cs)，定义 12 个 Android 平台事件 ID 常量（1267~1285）与 7 个公共事件映射，对应 Wails v3 `events_common_android.go` 的 `commonApplicationEventMap`，由 `AndroidPlatformApp` 在 Activity 生命周期回调与系统广播接收器中触发。

---

## 9. 插件系统

| 维度 | Wails.Net | Tauri 2 | Wails 3 |
|------|-----------|---------|---------|
| **插件接口** | `IPlugin`（4 阶段生命周期） | `Plugin` trait (`init` / `setup`) | `Service` 接口（`ServiceStartup` / `ServiceShutdown`） |
| **生命周期** | `ConfigureServices` → `Configure` → `StartupAsync` → `ShutdownAsync` | `Builder::setup` | `ServiceStartup` → `ServiceShutdown` |
| **插件管理器** | `PluginManager`（`Interlocked` 幂等保护） | `tauri::Builder` 自动注册 | `ServiceManager` |
| **插件上下文** | `IPluginContext`（Commands / Permissions / Services） | `AppHandle` | `Manager` |
| **命令注册** | `commands.MapCommand` | `invoke_handler` | `Application.Bind` |
| **权限声明** | `context.Permissions.DeclarePermission` | `permissions/default.toml` | N/A |
| **内置插件数** | **37 个桌面** + **4 个移动端** + **1 个 Android 运行时** = **42 个** | 30+ 官方插件 | 有限 |
| **第三方加载** | ✅ 通过 `ConfigureServices` 注册 | ✅ `tauri-plugin-*` crate | ✅ Go import |
| **IHostedService 适配** | ✅ 双重触发防护（`_started` / `_stopped` Interlocked） | N/A | N/A |

### Wails.Net 内置插件清单（37 个桌面 + 4 个移动端 + 1 个 Android 运行时）

**桌面插件**（37 个，位于 `src/Wails.Net.Application/Plugins/BuiltIn/`，按插件名分组）：

| 类别 | 插件（插件名 → 类名） |
|------|---------------------|
| **窗口/对话框/菜单/托盘** | window → WindowPlugin / dialog → DialogPlugin / menu → MenuPlugin（10 个 menu.* 命令，含 5 个 MenuRole 命令）/ tray → TrayPlugin |
| **剪贴板/屏幕/DPI** | clipboard → ClipboardPlugin / screen → ScreenPlugin / dpi-scale → DpiScalePlugin |
| **文件系统/IO** | filesystem → FileSystemPlugin / fs-watch → FsWatchPlugin / path → PathPlugin / file-association → FileAssociationPlugin |
| **存储/数据库** | store → StorePlugin / sqlite → SqlPlugin / stronghold → StrongholdPlugin / persisted-scope → PersistedScopePlugin |
| **网络** | http → HttpPlugin / websocket → WebSocketPlugin / cookie → CookiePlugin / localhost → LocalhostPlugin / upload → UploadPlugin |
| **系统/进程/电源** | os → OsInfoPlugin / app → AppInfoPlugin / application → ApplicationPlugin / process → ProcessPlugin / power-management → PowerManagementPlugin / windows → WindowsPlugin |
| **日志/通知** | log → LogPlugin / notification → NotificationPlugin |
| **Shell/浏览器** | shell → ShellPlugin / opener → OpenerPlugin |
| **自启动/快捷键** | autostart → AutostartPlugin / globalshortcut → GlobalShortcutPlugin |
| **窗口状态/定位** | window-state → WindowStatePlugin / positioner → PositionerPlugin |
| **本地化/深链接** | localization → LocalizationPlugin / deep-link → DeepLinkPlugin |
| **更新器** | updater → UpdaterPlugin |

**移动端插件**（4 个，位于 `src/Wails.Net.Application/Plugins/Mobile/`，P2 已补齐 Android 平台实现）：
- barcode-scanner → BarcodeScannerPlugin（[Android 实现](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Android/Mobile/AndroidBarcodeScanner.cs)）
- biometric → BiometricPlugin（[Android 实现](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Android/Mobile/AndroidBiometric.cs)）
- haptics → HapticsPlugin（[Android 实现](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Android/Mobile/AndroidHaptics.cs)）
- nfc → NfcPlugin（[Android 实现](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Android/Mobile/AndroidNfc.cs)）

**Android 运行时插件**（1 个，P2 新增，位于 `src/Wails.Net.Application.Android/Mobile/`）：
- android-runtime → [AndroidRuntimePlugin](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Android/Mobile/AndroidRuntimePlugin.cs)（`device.info` / `toast.show` 命令，对应 Wails v3 `messageprocessor_android.go`）

**平台接口**（4 个，与移动端插件配套）：
- IPlatformBarcodeScanner / IPlatformBiometric / IPlatformHaptics / IPlatformNfc

### 关键差异

- **Wails.Net** 的 [IPlugin](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/IPlugin.cs) 4 阶段生命周期是最完整的：`ConfigureServices`（DI 注册）→ `Configure`（命令注册）→ `StartupAsync`（运行时初始化）→ `ShutdownAsync`（资源释放），并默认实现 `StartupAsync`/`ShutdownAsync` 返回 `Task.CompletedTask`。
- **Wails.Net** 的 [PluginManager](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/PluginManager.cs) 通过 `Interlocked.CompareExchange` 防止 `IHostedService` 适配器与 `Application.Run` 重复触发。
- **Tauri 2** 插件生态最丰富（30+ 官方 + 大量社区），通过 Cargo 集成。
- **Wails 3** 插件以 Service 模式为主，无独立权限声明。

---

## 10. AssetServer

| 维度 | Wails.Net | Tauri 2 | Wails 3 |
|------|-----------|---------|---------|
| **内置 AssetServer** | ✅ `Wails.Net.AssetServer` 独立项目 | ✅ `tauri::Asset` | ✅ `assetserver` |
| **中间件管道** | ✅ `IMiddleware`（基于路径） + `IHttpMiddleware`（基于 HTTP 上下文） + `MiddlewareChain` | ❌ | ❌ |
| **CSP Nonce 注入** | ✅ `NonceInjector` | ✅ | ✅ |
| **隔离注入** | ✅ `IsolationInjector` | ✅ | ❌ |
| **Range 请求** | ✅ `Content-Range` / `Accept-Ranges` | ✅ | ✅ |
| **ETag** | ✅ `ETag` / `If-None-Match` | ✅ | ✅ |
| **Last-Modified** | ✅ `Last-Modified` / `If-Modified-Since` | ✅ | ✅ |
| **自定义 Header** | ✅ `Headers` 静态类（含 `x-wails-window-id` / `x-wails-window-name`） | ✅ | ✅ |
| **MIME 类型** | ✅ 内置映射 | ✅ | ✅ |
| **Service Route 挂载** | ✅ `IHttpServiceHandler` 自定义路由（P1-6） | ❌ | ❌ |
| **per-window CSP** | ✅ `SetCspHeaderForWindow`（P0-4） | ✅ | ❌ |
| **AssetProvider 抽象** | ✅ `IAssetProvider` + `BundledAssetProvider` + `FileAssetProvider` | ✅ | ✅ |
| **Android AssetServer** | ✅ `AndroidAssetServer`（适配 Android Assets） | ✅ | ✅ |

### 关键差异

- **Wails.Net** 的 [AssetServer](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.AssetServer/AssetServer.cs) 是三者中**唯一**提供双中间件管道（`IMiddleware` 路径式 + `IHttpMiddleware` HTTP 上下文式）的方案，支持灵活扩展（如自定义鉴权、日志、压缩等中间件）。
- **Wails.Net** 自 P1-6 起新增 [IHttpServiceHandler](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.AssetServer/IHttpServiceHandler.cs) 抽象，允许业务代码挂载自定义 HTTP 路由到 AssetServer（如 `/api/health`、`/api/data` 等），无需独立启动 ASP.NET Core 管道。服务路由匹配规则：精确匹配 + 前缀匹配（`route + "/"`）+ 最长匹配优先。
- **Wails.Net** 自 P0-4 起支持 per-window CSP（`SetCspHeaderForWindow`），不同窗口可配置不同 CSP 策略，`ResolveCspHeader` 优先返回窗口级 CSP。
- **Tauri 2** 和 **Wails 3** 的 AssetServer 功能完整但无中间件抽象，也无业务路由挂载能力。

---

## 11. Updater

| 维度 | Wails.Net | Tauri 2 | Wails 3 |
|------|-----------|---------|---------|
| **Updater 服务** | ✅ `UpdaterService`（`IServiceStartup` / `IServiceShutdown`） | ✅ `tauri-plugin-updater` | ✅ `Updater` |
| **UpdateInfo 结构** | ✅ `Version` / `DownloadUrl` / `UpdateAvailable` / `ReleaseNotes` | ✅ | ✅ |
| **多 Provider** | ✅ `IUpdateProvider` 接口 + 链式尝试（P1-8） | ❌（单一） | ✅ 多 provider |
| **Provider 列表** | ✅ HttpUpdateProvider / GitHubUpdateProvider / GitLabUpdateProvider（P1-8） | N/A | 内置 |
| **ProviderName 注入** | ✅ `UpdateManifest.ProviderName`（P1-8） | ❌ | ✅ |
| **错误事件 payload** | ✅ `UpdateErrorInfo { stage, message, provider }`（P1-8） | ❌ | ✅ `ErrorInfo` |
| **下载器** | ✅ `UpdateDownloader`（断点续传 + SHA256 校验） | ✅ | ✅ |
| **解压器** | ✅ `UpdateExtractor`（.zip / .tar.gz / .tgz / .tar + 路径遍历保护） | ✅ | ✅ |
| **Helper Process** | ✅ `HelperProcess`（等待父进程退出 + 重试替换 + 重启应用） | ❌ | ✅ |
| **事件广播** | ✅ 通过 `EventProcessor` 广播 10 个更新事件 | ✅ | ✅ |
| **签名校验** | ✅ Minisign（BLAKE2b-512 + Ed25519）+ Authenticode + GPG | ✅ Minisign | ✅ Minisign |
| **Windows 自动签名** | ✅ Authenticode | ❌ | ❌ |
| **向后兼容** | ✅ 未注册 Provider 时回退到 `UpdateURL`（P1-8） | N/A | N/A |
| **平台特定安装** | ✅ Windows .msi(msiexec) / .exe(/SILENT)；Linux .deb(dpkg) / .rpm(rpm) / .AppImage(chmod+x) | ✅ | ✅ |

### 关键差异

- **Wails.Net** 自 P1-8 起实现完整的多 Provider Updater 系统（[UpdaterService](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Services/UpdaterService.cs)）：
  - **IUpdateProvider 接口**（[IUpdateProvider.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Services/Updater/IUpdateProvider.cs)）：抽象"检查更新"的来源，支持链式尝试（首个返回非 null 清单的胜出）。
  - **HttpUpdateProvider**（[HttpUpdateProvider.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Services/Updater/HttpUpdateProvider.cs)）：向后兼容默认 Provider，封装原有 `UpdateURL` 检查逻辑。
  - **GitHubUpdateProvider**（[GitHubUpdateProvider.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Services/Updater/GitHubUpdateProvider.cs)）：通过 GitHub REST API `repos/{owner}/{repo}/releases/latest` 获取，从 `tag_name` 提取版本号（去除 'v' 前缀），支持 token 认证、企业版 API base、资产名称模式匹配。
  - **GitLabUpdateProvider**（[GitLabUpdateProvider.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Services/Updater/GitLabUpdateProvider.cs)）：通过 GitLab REST API `projects/{projectId}/releases/permalink/latest` 获取，支持自托管 GitLab、URL 编码 projectId（如 `mygroup%2Fmyproject`）。
  - **ProviderName 注入**：`CheckForUpdatesAsync` 解析清单后注入 `ProviderName` 到 `UpdateManifest`，便于前端展示和日志追踪。
  - **UpdateErrorInfo**：对应 Wails v3 `ErrorInfo { stage, message, provider }` 的 payload 结构，用于错误事件。
- **Wails.Net** 集成 [HelperProcess](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Services/Updater/HelperProcess.cs)（对应 Wails v3 Go helper.go）：环境变量 `WAILS_UPDATER_HELPER/ARCHIVE/TARGET/PID` 触发 helper 模式，等待父进程退出（30s 超时）→ 备份 → 重试替换（20 次，500ms 间隔）→ Linux chmod +x → 清除环境变量 → 重启应用 → 清理暂存目录。
- **Wails.Net** 的 [SignatureVerifier](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Services/Updater/SignatureVerifier.cs) 支持 minisign 路径（`VerifyMinisignAsync`，BLAKE2b-512 + Ed25519）和旧路径（[Obsolete] Windows Authenticode via PowerShell + Linux GPG）。
- **Wails.Net** 集成 Minisign 签名校验和 Authenticode 自动签名（见 §12），是三者中**唯一**支持 Windows Authenticode 自动签名的方案。
- **测试覆盖**：[UpdateProviderTests](file:///f:/Code/Dotnet/Wails.Net/tests/Wails.Net.Application.Tests/UpdateProviderTests.cs) 51 项测试覆盖三个 Provider 与链式检查行为。

---

## 12. 打包与签名

| 维度 | Wails.Net | Tauri 2 | Wails 3 |
|------|-----------|---------|---------|
| **构建工具** | Cake Frosting（C#） | `tauri build` (Rust) | `wails3 task build` (Taskfile) |
| **脚本语言** | F# (.fsx)（AGENTS.md §7.5 禁止 Python） | Shell / PowerShell | Shell / PowerShell |
| **构建任务数** | 11 个 Cake Frosting 任务 | N/A | N/A |
| **Windows 安装包** | ✅ NSIS / MSI | ✅ NSIS / MSI | ✅ NSIS |
| **Linux 安装包** | ✅ .deb / .rpm / .tar.gz（dpkg-deb / rpmbuild 内联） | ✅ .deb / .rpm / AppImage | ✅ .deb / .rpm |
| **macOS 安装包** | ❌（暂不支持 macOS） | ✅ .dmg / .app | ✅ .dmg / .app |
| **Android APK/AAB** | ✅（.NET Android workload，APK 默认） | ✅ | ✅ |
| **iOS IPA** | ❌ | ✅ | ✅ |
| **代码签名（Windows）** | ✅ Authenticode 自动签名（signtool / azuresigntool，环境变量门控） | ✅（手动配置） | ❌ |
| **代码签名（Linux）** | ✅ GPG | ✅ GPG | ✅ GPG |
| **代码签名（macOS）** | N/A | ✅ notarization | ✅ notarization |
| **代码签名（Android）** | ✅ 正式/debug 签名（环境变量门控） | ✅ | ✅ |
| **Minisign 签名** | ✅ 5 个 Minisign 相关文件 | ✅ | ✅ |
| **CI runner** | Windows + Linux runner（Linux 包在 Linux runner 构建） | 跨平台 | 跨平台 |
| **自包含构建** | ✅ 三平台自包含（`PublishSingleFile=true`，无 .NET 运行时依赖） | ✅（Rust 编译） | ✅（Go 编译） |
| **前端构建集成** | ✅ `FrontendTask`（自动检测 pnpm > yarn > npm） | ✅ | ✅ |

### 关键差异

- **Wails.Net** 使用 **Cake Frosting**（C# 编写构建脚本）+ **F# .fsx** 脚本，符合 AGENTS.md §7.5 禁止 Python 的约束。
- **Wails.Net** 是三者中**唯一**支持 Windows Authenticode **自动签名**的方案（通过 `signtool` / `azuresigntool`，环境变量门控，签名失败非阻塞）。
- **Wails.Net** CI 已将 `dist-linux` 任务迁移到 Linux runner，提升构建效率；Linux 包构建内联实现 `dpkg-deb --build` 和 `rpmbuild -bb`。

---

## 13. CLI 工具

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
| **部署** | ✅ `Deploy` 子命令 | ❌ | ❌ |

### 关键差异

- **Wails.Net** 的 [Program.cs](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Cli/Program.cs) 提供 16 个子命令，覆盖生成、诊断、构建、发布、打包、插件、版本、清理、信息、图标、签名、平台、自更新、部署，是三者中**唯一**内置 `Deploy` 子命令的方案。
- **AGENTS.md §1.1 约束**：禁止使用 `McMaster.Extensions.CommandLineUtils`，必须使用 `System.CommandLine` 2.0.9。

---

## 14. Server 模式（Wails.Net 独有）

| 维度 | Wails.Net | Tauri 2 | Wails 3 |
|------|-----------|---------|---------|
| **无 GUI 模式** | ✅ `ServerPlatformApp` + `ServerWebviewWindow` + `ServerClipboard` + `ServerBrowserManager`（P1-1） | ❌ | ✅ Headless 模式 |
| **阻塞模型** | ✅ `ManualResetEventSlim` 阻塞直到 `SignalShutdown` | N/A | ✅ |
| **GUI 操作** | 全部 no-op | N/A | 全部 no-op |
| **单实例锁** | ✅ 始终返回 true（视作首实例） | N/A | ✅ |
| **对话框** | 返回默认值（首个按钮 / null） | N/A | ✅ |
| **屏幕查询** | 返回 null / 空数组 | N/A | ✅ |
| **主线程分发** | 同步执行 `action()` | N/A | ✅ |
| **浏览器管理** | ✅ `ServerBrowserManager` no-op（P1-1） | N/A | ✅ |
| **Destroy 可重写** | ✅ `virtual` 修饰符支持测试 override（P1-7） | N/A | N/A |
| **应用场景** | 容器化部署 / 自动化测试 / 服务端渲染 | N/A | 自动化测试 |

### 关键差异

- **Wails.Net** 的 [ServerPlatformApp](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Platform/ServerMode/ServerPlatformApp.cs) 是三者中**最完整**的无 GUI 降级方案，提供与桌面模式完全一致的 API 表面，所有 GUI 操作 no-op 但不抛异常，适用于：
  - 容器化部署（Docker / Kubernetes）
  - 自动化 UI 测试（无窗口环境）
  - 服务端预渲染
- **Wails.Net** 自 P1-1 起补齐 [ServerBrowserManager](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Platform/ServerMode/ServerBrowserManager.cs)，使 IBrowserManager 接口在 Server 模式下 no-op，保证 API 表面一致性。
- **Wails.Net** 自 P1-7 起将 `ServerPlatformApp.Destroy()` 改为 `virtual`，支持单元测试中 override 验证 Shutdown 流程（[ApplicationEventHooksTests](file:///f:/Code/Dotnet/Wails.Net/tests/Wails.Net.Application.Tests/ApplicationEventHooksTests.cs)）。
- **Wails 3** 的 Headless 模式功能类似，但 API 表面不如 Wails.Net 完整。
- **Tauri 2** 无内置 Server 模式。

---

## 15. 测试框架

| 维度 | Wails.Net | Tauri 2 | Wails 3 |
|------|-----------|---------|---------|
| **测试框架** | **TUnit** 1.58.0 | `cargo test` (Rust) | `testify` (Go) |
| **断言库** | `TUnit.Assertions`（必须 `await`） | `assert_eq!` | `testify/assert` |
| **运行命令** | `dotnet run --project tests/...`（非 `dotnet test`） | `cargo test` | `go test` |
| **禁止项** | MSTest / xUnit / NUnit | N/A | N/A |
| **测试项目数** | 6 个（Application / Windows / Linux / Android / AssetServer / Cli） | N/A | N/A |
| **平台特定测试** | Windows / Linux / Android / CLI 各自独立测试项目 | 跨平台 | 跨平台 |
| **并发控制** | `[NotInParallel]` 属性 | `#[serial]` | `t.Parallel()` |
| **测试命名** | `Method_Scenario_ExpectedBehavior` | `test_*` | `Test*` |
| **覆盖要求** | 公共 API 100% / 错误路径全覆盖 / 边界条件 / 并发场景 | 无强制 | 无强制 |

### 关键差异

- **Wails.Net** 是三者中**唯一**禁止使用主流测试框架（MSTest/xUnit/NUnit）的项目，强制使用 TUnit 1.58.0（AGENTS.md §1.1）。
- **Wails.Net** .NET 10 SDK 不再支持 `dotnet test`（VSTest 模式），必须使用 `dotnet run --project`（MTP 模式）。
- **Wails.Net** 测试覆盖要求最严格：公共 API 100% 方法覆盖、所有 `catch` 分支必须测试、边界条件、并发场景。
- **Wails.Net** 拥有 6 个独立测试项目：Application.Tests（70+ 测试文件）/ Windows.Tests / Linux.Tests / Android.Tests / AssetServer.Tests / Cli.Tests。
- **Wails.Net** 自 P2 起新增 Android 平台测试：[MenuRoleTests](file:///f:/Code/Dotnet/Wails.Net/tests/Wails.Net.Application.Tests/MenuRoleTests.cs)（442 行，30+ 项）/ [AndroidPlatformEventsTests](file:///f:/Code/Dotnet/Wails.Net/tests/Wails.Net.Application.Android.Tests/AndroidPlatformEventsTests.cs)（214 行）/ [AndroidRuntimePluginTests](file:///f:/Code/Dotnet/Wails.Net/tests/Wails.Net.Application.Android.Tests/Mobile/AndroidRuntimePluginTests.cs)（432 行）/ [AndroidBarcodeScannerTests](file:///f:/Code/Dotnet/Wails.Net/tests/Wails.Net.Application.Android.Tests/Mobile/AndroidBarcodeScannerTests.cs) / [AndroidBiometricTests](file:///f:/Code/Dotnet/Wails.Net/tests/Wails.Net.Application.Android.Tests/Mobile/AndroidBiometricTests.cs) / [AndroidHapticsTests](file:///f:/Code/Dotnet/Wails.Net/tests/Wails.Net.Application.Android.Tests/Mobile/AndroidHapticsTests.cs) / [AndroidNfcTests](file:///f:/Code/Dotnet/Wails.Net/tests/Wails.Net.Application.Android.Tests/Mobile/AndroidNfcTests.cs) / [AndroidPlatformAppTests](file:///f:/Code/Dotnet/Wails.Net/tests/Wails.Net.Application.Android.Tests/AndroidPlatformAppTests.cs)。

---

## 16. 三方插件对照表

| 功能领域 | Wails.Net | Tauri 2 | Wails 3 |
|---------|-----------|---------|---------|
| **窗口** | WindowPlugin ✅ | window | WebviewWindow API |
| **对话框** | DialogPlugin ✅ | dialog | dialog |
| **菜单** | MenuPlugin ✅（含 5 个 MenuRole 命令） | menu | menu |
| **托盘** | TrayPlugin ✅ | tray | tray |
| **剪贴板** | ClipboardPlugin ✅ | clipboard-manager | clipboard |
| **屏幕** | ScreenPlugin ✅ | (内置) | screen |
| **DPI 缩放** | DpiScalePlugin ✅ | (内置) | (内置) |
| **文件系统** | FileSystemPlugin ✅ | fs | fs |
| **文件监听** | FsWatchPlugin ✅ | fs (watch) | (无) |
| **路径** | PathPlugin ✅ | path | (内置) |
| **文件关联** | FileAssociationPlugin ✅ | (无) | (无) |
| **HTTP** | HttpPlugin ✅ | http | http |
| **WebSocket** | WebSocketPlugin ✅ | (社区) | (无) |
| **Cookie** | CookiePlugin ✅ | (无) | (无) |
| **Localhost** | LocalhostPlugin ✅ | localhost | (无) |
| **分块上传** | UploadPlugin ✅ | (无) | (无) |
| **存储** | StorePlugin ✅ | store | store |
| **SQL** | SqlPlugin ✅ | sql | (无) |
| **Stronghold** | StrongholdPlugin ✅ | stronghold | (无) |
| **持久化作用域** | PersistedScopePlugin ✅ | (无) | (无) |
| **日志** | LogPlugin ✅ | log | log |
| **通知** | NotificationPlugin ✅ | notification | notification |
| **Shell** | ShellPlugin ✅ | shell | shell |
| **Opener** | OpenerPlugin ✅ | opener | (无) |
| **OS 信息** | OsInfoPlugin ✅ | os | (内置) |
| **App 信息** | AppInfoPlugin ✅ | app | (内置) |
| **Application** | ApplicationPlugin ✅ | app | (内置) |
| **进程** | ProcessPlugin ✅ | process | (无) |
| **电源管理** | PowerManagementPlugin ✅ | (社区) | (无) |
| **Windows 专属** | WindowsPlugin ✅ | (无) | (无) |
| **自启动** | AutostartPlugin ✅ | autostart | (无) |
| **全局快捷键** | GlobalShortcutPlugin ✅ | global-shortcut | (内置) |
| **窗口状态** | WindowStatePlugin ✅ | window-state | (无) |
| **定位器** | PositionerPlugin ✅ | positioner | (无) |
| **本地化** | LocalizationPlugin ✅ | (无) | (无) |
| **深链接** | DeepLinkPlugin ✅ | deep-link | (无) |
| **更新器** | UpdaterPlugin ✅ | updater | updater |
| **更新器签名** | ✅ Minisign + Authenticode + GPG | ✅ Minisign | ✅ Minisign |
| **条码扫描（移动）** | BarcodeScannerPlugin ✅（含 Android 实现） | barcode-scanner | (无) |
| **生物识别（移动）** | BiometricPlugin ✅（含 Android 实现） | biometric | (无) |
| **触觉反馈（移动）** | HapticsPlugin ✅（含 Android 实现） | haptics | haptics |
| **NFC（移动）** | NfcPlugin ✅（含 Android 实现） | nfc | (无) |
| **Android 运行时** | AndroidRuntimePlugin ✅（`device.info` / `toast.show`，P2 新增） | (无) | `messageprocessor_android.go` |
| **菜单角色** | MenuRole（21 角色 + 13 工厂 + 3 标准菜单组合，P2 新增） | PredefinedMenuItem | Role 常量 |
| **关于对话框** | AboutMetadata（P2 新增） | AboutMetadata | (内置) |
| **深色模式** | (OsInfoPlugin 内置) | (内置) | (内置) |
| **OAuth** | (DeepLinkPlugin 可承载) | deep-link / oauth | (无) |

### 关键差异

- **Wails.Net** 内置 **42 个插件**（37 桌面 + 4 移动端 + 1 Android 运行时），覆盖 Tauri 2 的 SQL / Stronghold / Biometric / Barcode Scanner / Localhost / Positioner / DeepLink 等能力（已全部实现）。
- **Wails.Net** 自 P2 起 4 个移动端插件均补齐 Android 平台实现，不再是仅有接口。
- **Wails.Net** 自 P2 起新增 [AndroidRuntimePlugin](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Android/Mobile/AndroidRuntimePlugin.cs)，对应 Wails v3 `messageprocessor_android.go` 的 `device.info` / `toast.show` 命令。
- **Tauri 2** 官方插件 30+ 个，部分功能 Wails.Net 也已对齐（如 SQL / Stronghold / Biometric / Barcode Scanner / Localhost / Positioner / DeepLink）。
- **Wails 3** 内置插件最少，依赖社区扩展。
- **Wails.Net 独有插件**：CookiePlugin / FileAssociationPlugin / FsWatchPlugin / PathPlugin / PersistedScopePlugin / PowerManagementPlugin / UploadPlugin / WindowStatePlugin / WindowsPlugin / NfcPlugin / AndroidRuntimePlugin（Tauri 2 / Wails 3 均无内置对应实现）。

---

## 17. Wails.Net 独有能力

以下是 Wails.Net 相对于 Tauri 2 和 Wails 3 的独有能力：

### 17.1 Server 模式（见 §14）
完整的无 GUI 降级方案，支持容器化部署和自动化测试。

### 17.2 多传输层并行广播
[EventProcessor](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Events/EventProcessor.cs) 同时通过 HttpTransport + WebSocketBroadcaster + EventIPCTransport + AssetServerTransport + NativeIpcTransport 广播事件，确保所有连接的前端都能收到。

### 17.3 CommandDispatcher 中间件管道
[CommandDispatcher](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Commands/CommandDispatcher.cs) 支持 `ICommandMiddleware` 管道和命令超时，提供类似 ASP.NET Core 中间件的扩展点。

### 17.4 AssetServer 双中间件管道
[AssetServer](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.AssetServer/AssetServer.cs) 通过 `IMiddleware`（路径式）+ `IHttpMiddleware`（HTTP 上下文式）+ `MiddlewareChain` 提供灵活的 HTTP 中间件扩展。

### 17.5 Windows Authenticode 自动签名
三方中唯一支持 Windows Authenticode 自动签名（`signtool` / `azuresigntool`，环境变量门控）。

### 17.6 IPlugin 4 阶段生命周期
最完整的插件生命周期：`ConfigureServices` → `Configure` → `StartupAsync` → `ShutdownAsync`。

### 17.7 Channel API
[IChannel](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Channels/Channel.cs) + `ChannelManager` + `Interlocked` 线程安全，提供双向流式通信（对应 Tauri v2 Channel<T>）。

### 17.8 Microsoft.Extensions.* 全栈集成
对 .NET 开发者最友好：Host / DI / Config / Options / Logging 全栈集成。

### 17.9 源生成器绑定 + 反射回退（双路径）
[BindingManager](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Bindings/Bindings.cs) 优先走源生成器路径（性能优，由 [BindingSourceGenerator](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.SourceGenerators/BindingSourceGenerator.cs) 通过 `IIncrementalGenerator` + `[ModuleInitializer]` 自动注册），反射仅作为兜底（开发体验优）。

### 17.10 内置 42 个插件
内置插件数最多（37 桌面 + 4 移动端 + 1 Android 运行时），覆盖 Tauri 2 / Wails 3 全部对应能力（SQL / Stronghold / Biometric / Barcode Scanner / Localhost / Positioner / DeepLink 等），4 个移动端插件均补齐 Android 平台实现。

### 17.11 Logger ↔ 前端 console 双向桥接（P1-3）
- **[BrowserConsoleLogReceiver](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Logging/BrowserConsoleLogReceiver.cs)** 接收前端 `console.log/info/warn/error` 调用，桥接到 `ILogger<T>`，使前端日志自动进入 .NET 日志管道。
- **[BrowserConsoleLogForwarder](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Logging/BrowserConsoleLogForwarder.cs)** 将 `ILogger` 输出反向转发到前端 DevTools console，便于前端开发者查看后端日志。
- **[LogServiceLoggerProvider](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Logging/LogServiceLoggerProvider.cs)** 集成到 `Microsoft.Extensions.Logging`，作为 `ILoggerProvider` 注册到 DI 容器。
- **防回环机制**：同进程 `AsyncLocal<bool>` 防递归 + 跨方向检查 `source=browser` 字段。
- 这是三者中**唯一**支持日志双向桥接的方案。

### 17.12 事件 senderWindowId 传播（P1-2）
[EventProcessor](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Events/EventProcessor.cs) 与 Transport 层全链路携带 `senderWindowId`，前端可按 `windowId` 过滤事件来源。这是三者中**唯一**支持事件来源窗口标识的方案，对多窗口调试与隔离场景特别有用。

### 17.13 AssetServer Service Route 挂载（P1-6）
[IHttpServiceHandler](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.AssetServer/IHttpServiceHandler.cs) 允许业务代码挂载自定义 HTTP 路由到 AssetServer，无需独立启动 ASP.NET Core 管道。这是三者中**唯一**的方案。

### 17.14 多 Provider Updater + ProviderName 注入（P1-8）
[IUpdateProvider](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Services/Updater/IUpdateProvider.cs) 接口支持链式尝试多个更新源（Http / GitHub / GitLab），并在 `UpdateManifest` 中注入 `ProviderName`，便于前端展示来源和日志追踪。

### 17.15 三平台 BrowserManager（P1-1）
IBrowserManager 接口在 Windows / Linux / Android 三平台均有实现，支持打开外部 URL、验证 URL scheme。Server 模式下提供 `ServerBrowserManager` no-op 实现，保证 API 表面一致性。

### 17.16 Helper Process 替换二进制
[HelperProcess](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Services/Updater/HelperProcess.cs) 对齐 Wails v3 Go helper.go：环境变量触发 helper 模式，等待父进程退出 → 备份 → 重试替换（20 次，500ms 间隔）→ 重启应用 → 清理暂存目录。

### 17.17 窗口级 CSP（P0-4）
[AssetServer.SetCspHeaderForWindow](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.AssetServer/AssetServer.cs) 支持 per-window CSP 配置，不同窗口可配置不同 CSP 策略。

### 17.18 分块上传协议
[HttpTransport](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/HttpTransport.cs) 支持 `x-wails-chunk-id/index/total` 协议，单 chunk ≤1MB、总 ≤64MB、会话 TTL 30s，绕过 HTTP 请求体大小限制。

### 17.19 NativeIpcTransport 混合策略
[NativeIpcTransport](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/NativeIpcTransport.cs) 小消息 (<512KB) 走原生 postMessage 通道，大消息自动回退 HTTP 分块，平衡延迟与容量。

### 17.20 Deploy CLI 子命令
[Wails.Net.Cli](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Cli/Program.cs) 提供 16 个子命令，包含三者中**唯一**的 `Deploy` 子命令。

### 17.21 MenuRole 系统完整实现（P2 新增）
[MenuRole](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Menus/MenuRole.cs) 系统结合 Wails v3 Role 常量模型与 Tauri v2 PredefinedMenuItem 工厂模式：
- **21 个角色枚举值**：覆盖跨平台编辑/窗口/应用角色 + macOS 专属角色（Hide/HideOthers/ShowAll/Services/BringAllToFront/Zoom/ToggleFullScreen）。
- **13 个工厂方法**：`MenuItem.CreateCopy/CreateCut/CreatePaste/CreateSelectAll/CreateUndo/CreateRedo/CreateSeparator/CreateMinimize/CreateMaximize/CreateFullscreen/CreateCloseWindow/CreateQuit/CreateAbout`。
- **跨平台共享辅助**：[MenuRoleHelper](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Menus/MenuRoleHelper.cs) 提供默认中文 Label、默认 Accelerator（Ctrl+C 等）、macOS 专属判定、PrepareRoleItem 统一填充 Callback。
- **平台实现分离**：[Win32Menu.ApplyRole](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Windows/Win32Menu.cs) / [LinuxMenu.ApplyRole](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Linux/LinuxMenu.cs) 各自实现 ExecuteRole。
- **全局热键注册**：角色带默认 Accelerator 时自动注册到 `KeyBindingManager`，修复了现有 Accelerator 仅在菜单栏内生效、不全局响应的 bug。
- **关于对话框元数据**：[AboutMetadata](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Menus/AboutMetadata.cs) 9 个字段（对应 Tauri v2 AboutMetadata）。
- **标准菜单组合**：`AddStandardEditMenu` / `AddStandardWindowMenu` / `AddStandardHelpMenu` 一键构建跨平台标准菜单。
- **前端 API 注入**：[RuntimeGenerator](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/RuntimeGenerator.cs) 注入 `wails.MenuRole` 常量枚举与 4 个 menu.* 命令。
- **macOS 专属降级**：通过运行时 `IsMacOSExclusive` 判定，Windows/Linux 静默 no-op，不抛异常；不使用 `[SupportedOSPlatform]` 特性以避免跨平台代码触发 CA1416。
- **测试覆盖**：[MenuRoleTests](file:///f:/Code/Dotnet/Wails.Net/tests/Wails.Net.Application.Tests/MenuRoleTests.cs) 30+ 项测试。

### 17.22 Android 平台事件映射（P2 新增）
[AndroidPlatformEvents](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Android/AndroidPlatformEvents.cs) 定义 12 个 Android 平台事件 ID 常量（1267~1285）与 7 个公共事件映射（对应 Wails v3 `events_common_android.go` 的 `commonApplicationEventMap`），由 `AndroidPlatformApp` 在 Activity 生命周期回调与系统广播接收器中触发，通过 `MapToCommonEvent` 转发到公共 `ApplicationEventType`，由 `Application.HandlePlatformEvent(uint)` 分发。

### 17.23 AndroidRuntimePlugin（P2 新增）
[AndroidRuntimePlugin](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Android/Mobile/AndroidRuntimePlugin.cs) 提供 Android 平台专属运行时命令：
- `device.info` — 对应 Wails v3 `androidDeviceInfo()`，返回设备制造商、品牌、型号、SDK 版本等信息。
- `toast.show` — 对应 Wails v3 `androidShowToast(message)`，通过 `Android.Widget.Toast.MakeText` 显示。
- 采用 Tauri v2 风格的插件命令名（`device.*` / `toast.*`），而非 Wails v3 的 object ID 路由。
- 自带权限集 `android-runtime:default`（`android-runtime:allow-device-info` / `android-runtime:allow-toast`）。

### 17.24 4 个 Android 移动端插件平台实现（P2 新增）
4 个移动端插件均补齐 Android 平台实现（原本仅有跨平台接口）：
- [AndroidBarcodeScanner](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Android/Mobile/AndroidBarcodeScanner.cs)：通过隐式 Intent 启动第三方扫描应用，由 `AndroidPlatformApp` 注入扫描委托解耦 Activity 生命周期。
- [AndroidBiometric](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Android/Mobile/AndroidBiometric.cs)：通过 `BiometricPrompt` 调用系统生物识别。
- [AndroidHaptics](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Android/Mobile/AndroidHaptics.cs)：通过 `Vibrator` / `VibratorManager` 调用系统触觉反馈。
- [AndroidNfc](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Android/Mobile/AndroidNfc.cs)：通过 `NfcAdapter` 调用 NFC 读取。

---

## 18. Wails.Net 差距与路线图

### 18.1 已知差距

| 差距项 | 优先级 | 对标 | 说明 |
|-------|--------|------|------|
| **macOS / iOS 支持** | P2 | Tauri 2 / Wails 3 | 暂不实现，未来考虑通过 .NET macOS 工作负载 + WKWebView 补齐 |
| **移动端插件生态深度** | P2 | Tauri 2 | 4 个移动端插件 + 1 个 Android 运行时插件，Tauri 2 有 20+（Camera / Vibration / Permissions 等可扩充） |
| **AppImage 打包** | P3 | Tauri 2 | Wails.Net 仅支持 .deb / .rpm / .tar.gz，无 AppImage |
| **MSI 打包** | P3 | Tauri 2 | Wails.Net 仅支持 NSIS，MSI 待补齐 |
| **社区插件生态** | P3 | Tauri 2 | Tauri 2 拥有大量社区 Cargo crate，Wails.Net 待建设 |
| **OAuth 独立插件** | P3 | Tauri 2 | 当前由 DeepLinkPlugin 承载，无独立 OAuth 插件 |

### 18.2 已完成对齐项（P0 + P1 + P2 部分）

#### P0 阶段（已完成）
- ✅ CancellablePromise + CancelCall 全链路测试（`_runningCalls` + 前端 `cancel` 消息）
- ✅ EventIPCTransport 回退机制完善（始终追加为兜底监听器）
- ✅ 原生 IPC（NativeIpcTransport）作为默认传输（混合策略：<512KB 原生 / 大消息 HTTP 分块）
- ✅ Server 模式事件 API 完善
- ✅ P0-4 窗口级 CSP（per-window）
- ✅ P0-C1 分块上传协议

#### P1 阶段（已完成）
- ✅ P1-1：BrowserManager 三平台实现（Windows / Linux / Android / Server）
- ✅ P1-2：事件 senderWindowId 传播（EventProcessor + Transport 全链路）
- ✅ P1-3：Logger ↔ 前端 console 双向桥接（BrowserConsoleLogReceiver + Forwarder + LoggerProvider）
- ✅ P1-4：ContextMenu 行为对齐（MenuManager + ContextMenuData + RuntimeGenerator 钩子）
- ✅ P1-5：Frameless 拖拽 CSS 变量统一（`--wails-drag-region`，三平台一致）
- ✅ P1-6：AssetServer Service Route 挂载能力（IHttpServiceHandler）
- ✅ P1-7：Event Hooks 补齐（PostShutdown / ShouldQuit）
- ✅ P1-8：多 Provider Updater（HttpUpdateProvider / GitHubUpdateProvider / GitLabUpdateProvider）

#### P2 阶段（已完成部分）
- ✅ MenuRole 系统完整实现（21 个角色枚举 + 13 个工厂方法 + 3 个标准菜单组合 + AboutMetadata + Win32/Linux 平台实现 + 前端 API 注入）
- ✅ 4 个 Android 移动端插件平台实现补齐（BarcodeScanner / Biometric / Haptics / Nfc）
- ✅ AndroidRuntimePlugin 新增（`device.info` / `toast.show` 命令）
- ✅ AndroidPlatformEvents 新增（12 个 Android 事件 ID + 7 个公共事件映射）
- ✅ 3 个新公共事件（LowMemory=27 / ScreenLocked=28 / ScreenUnlocked=29）

### 18.3 路线图建议

1. **中期（P2 剩余）**：
   - macOS / iOS 平台支持（.NET macOS + WKWebView）
   - 移动端插件扩充（Camera / Vibration / Permissions 等）
   - MSI 打包补齐
   - 独立 OAuth 插件

2. **长期（P3）**：
   - AppImage 打包
   - 社区插件生态建设

---

## 19. 总结

### 19.1 三方定位

| 项目 | 定位 | 优势 | 劣势 |
|------|------|------|------|
| **Wails.Net** | Wails v3 的 .NET 10 移植，融合 ASP.NET Core + Wails v3 + Tauri v2 三家之长 | .NET 全栈集成 / Server 模式 / 多传输层 / 42 个插件（含 SQL/Stronghold/DeepLink/Biometric/BarcodeScanner 等已实现，4 个移动端插件已有 Android 平台实现）/ 源生成器绑定 / 完整 ACL / Authenticode 自动签名 / Logger 双向桥接 / 多 Provider Updater / senderWindowId 传播 / AssetServer Service Route / Helper Process / 窗口级 CSP / 分块上传 / Deploy CLI / MenuRole 系统完整实现 / AndroidRuntimePlugin / AndroidPlatformEvents | 无 macOS/iOS / 移动端插件少于 Tauri 2 / 无 AppImage / 无 MSI |
| **Tauri 2** | Rust 桌面/移动框架，安全优先 | 全平台（5 个）/ 30+ 官方插件 / 完整 ACL / 编译期绑定（性能最优）/ 强大生态 | Rust 学习曲线 / 无 Server 模式 / 无中间件管道 / 无日志双向桥接 / 单一 Updater Provider / 无 Authenticode 自动签名 / 无 MenuRole 标准菜单组合 |
| **Wails 3** | Go 桌面/移动框架，简洁实用 | 全平台 / Go 语言易上手 / 多 Provider Updater / Helper Process / Event Hooks | 无 ACL / 无中间件管道 / 插件生态弱 / 无 Authenticode / 无日志双向桥接 / 无 senderWindowId / 无 MenuRole 工厂方法与跨平台辅助工具 |

### 19.2 Wails.Net 的核心价值

1. **对 .NET 开发者最友好**：完整集成 Microsoft.Extensions.* 全栈，复用 ASP.NET Core 经验。
2. **架构融合创新**：三家之长（ASP.NET Core 的 Host/DI + Wails v3 的 Runtime/IPC + Tauri v2 的 Plugin/Capability）。
3. **企业级特性**：Server 模式（容器化部署）+ Authenticode 自动签名 + 完整 ACL + 中间件管道。
4. **插件生态最完整（内置）**：37 桌面 + 4 移动端 + 1 Android 运行时 = 42 个插件，开箱即用，已对齐 Tauri 2 的 SQL / Stronghold / Biometric / Barcode Scanner / Localhost / Positioner / DeepLink 等能力，4 个移动端插件均补齐 Android 平台实现。
5. **调试与运维友好**（P1 阶段新增）：
   - Logger 双向桥接：前端 console ↔ 后端 ILogger 双向流转，便于联调。
   - senderWindowId 传播：多窗口场景下可按来源窗口过滤事件。
   - AssetServer Service Route：业务路由直接挂载到 AssetServer，无需独立 Kestrel。
6. **更新能力完整**（P1-8 完成）：多 Provider Updater 与 Wails 3 持平，支持 GitHub / GitLab / HTTP 三种来源 + Helper Process 替换二进制。
7. **菜单系统完整**（P2 新增）：MenuRole 21 个角色 + 13 个工厂方法 + 3 个标准菜单组合 + AboutMetadata + Win32/Linux 平台实现，结合 Wails v3 Role 模型与 Tauri v2 PredefinedMenuItem 工厂模式。
8. **Android 平台能力补齐**（P2 新增）：
   - 4 个移动端插件 Android 平台实现（BarcodeScanner / Biometric / Haptics / Nfc）
   - AndroidRuntimePlugin（device.info / toast.show）
   - AndroidPlatformEvents（12 个 Android 事件 ID + 7 个公共事件映射）
   - 3 个新公共事件（LowMemory / ScreenLocked / ScreenUnlocked）

### 19.3 选型建议

- **.NET 团队 / 企业级桌面应用**：选 Wails.Net（复用 .NET 技能栈 + Server 模式 + Authenticode + Logger 双向桥接 + 多 Provider Updater + 42 个内置插件 + 完整 MenuRole + Android 平台能力）
- **Rust 团队 / 安全敏感应用**：选 Tauri 2（编译期绑定 + 完整 ACL + 全平台 + 强大生态）
- **Go 团队 / 快速原型**：选 Wails 3（Go 语言简洁 + 多 Provider Updater + Helper Process）
- **需要 macOS/iOS**：暂选 Tauri 2 或 Wails 3（Wails.Net 暂不支持）
- **需要 AppImage / MSI**：暂选 Tauri 2（Wails.Net 在 P3 路线图中）

---

**文档结束**

> 本文档基于 2026-07-19 仓库代码状态生成（提交 `f867b9c`，P2 阶段部分完成：MenuRole 系统 + 4 个 Android 移动端插件平台实现 + AndroidRuntimePlugin + AndroidPlatformEvents + 3 个新公共事件）。如发现信息过时或错误，请提交 Issue 或 PR 更新。
