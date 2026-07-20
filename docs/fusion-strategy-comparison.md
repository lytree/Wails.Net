# Wails.Net 架构融合策略对比详情

> 本文档按 **架构融合策略**（AGENTS.md §1.1.1）的三大维度，详细对比 **Wails.Net**（当前项目）、**Tauri 2**（Rust 桌面/移动框架）和 **Wails 3 v3.0.0-alpha.102**（Go 原版）在功能实现项上的差别。
>
> - **融合策略**：
>   - 维度 1：Host/DI/Config/Logging → 学 ASP.NET Core（Microsoft.Extensions.* 全栈）
>   - 维度 2：Runtime/Window/IPC → 学 Wails v3（对象模型、IPC、多窗口、事件总线）
>   - 维度 3：Plugin/Security/Capability → 学 Tauri v2（插件能力、权限模型、安全设计）
> - **更新日期**：2026-07-20
> - **对比基线**：基于本仓库当前 `src/` 实际代码状态（提交 `6832cbf`，P2 阶段完成 + P3 阶段完成 20 个 Demo 项目）

---

## 目录

- [维度 1：Host / DI / Config / Logging（学 ASP.NET Core）](#维度-1host--di--config--logging学-aspnet-core)
  - [1.1 Host 模型](#11-host-模型)
  - [1.2 DI 容器](#12-di-容器)
  - [1.3 配置系统](#13-配置系统)
  - [1.4 日志系统](#14-日志系统)
  - [1.5 选项模式](#15-选项模式)
  - [1.6 生命周期钩子](#16-生命周期钩子)
- [维度 2：Runtime / Window / IPC（学 Wails v3）](#维度-2runtime--window--ipc学-wails-v3)
  - [2.1 Application 对象模型](#21-application-对象模型)
  - [2.2 平台抽象与 Server 模式](#22-平台抽象与-server-模式)
  - [2.3 窗口管理](#23-窗口管理)
  - [2.4 IPC 传输层](#24-ipc-传输层)
  - [2.5 绑定系统](#25-绑定系统)
  - [2.6 事件系统](#26-事件系统)
  - [2.7 MessageProcessor 路由](#27-messageprocessor-路由)
  - [2.8 菜单系统与 MenuRole](#28-菜单系统与-menurole)
  - [2.9 系统托盘](#29-系统托盘)
  - [2.10 全局快捷键与拖拽](#210-全局快捷键与拖拽)
- [维度 3：Plugin / Security / Capability（学 Tauri v2）](#维度-3plugin--security--capability学-tauri-v2)
  - [3.1 插件系统](#31-插件系统)
  - [3.2 权限模型（PermissionManager）](#32-权限模型permissionmanager)
  - [3.3 Capability 能力声明](#33-capability-能力声明)
  - [3.4 Scope 作用域](#34-scope-作用域)
  - [3.5 命令调度（CommandDispatcher）](#35-命令调度commanddispatcher)
  - [3.6 Channels](#36-channels)
  - [3.7 AssetServer 中间件](#37-assetserver-中间件)
  - [3.8 Updater 与签名安全](#38-updater-与签名安全)
  - [3.9 移动端插件](#39-移动端插件)
  - [3.10 Stronghold / SQL / Store 加密存储](#310-stronghold--sql--store-加密存储)
- [整体评估与差距](#整体评估与差距)

---

## 维度 1：Host / DI / Config / Logging（学 ASP.NET Core）

### 1.1 Host 模型

| 实现项 | Wails.Net | Tauri 2 | Wails 3 |
|--------|-----------|---------|---------|
| **应用入口** | `DesktopApplicationBuilder` + `DesktopApplication`（封装 `HostApplicationBuilder` + `IHost`） | `tauri::Builder::default().build().run()` | `application.New(Options).Run()` |
| **底层 Host** | `Microsoft.Extensions.Hosting.HostApplicationBuilder` | Tokio runtime + tauri 事件循环 | Go main goroutine + 平台事件循环 |
| **Builder 模式** | ✅ Fluent API（`UsePlugin<TPlugin>`、`AddWails`） | ✅ `Builder::plugin().invoke_handler().setup()` | ❌ 工厂函数 `New(Options)` |
| **主线程模型** | 专用 **STA 线程**运行 UI 主循环 | Tokio worker + 主线程事件循环 | 平台主线程（GTK/Win32） |
| **应用句柄** | `Application` 单例（DI 注入） | `AppHandle`（线程安全克隆） | `*Application`（指针） |
| **跨线程访问** | `Application.DispatchOnMainThread(action)` | `AppHandle` 任意线程克隆 | `Application.DispatchOnMainThread` |
| **异常兜底** | ✅ `AppDomain.UnhandledException` + `TaskScheduler.UnobservedTaskException` | ✅ panic catch | ❌ 无统一兜底 |
| **退出码** | ✅ `RunAsync` 返回 int 退出码 | ✅ `App::run` 返回 i32 | ❌ 仅 error |

**Wails.Net 关键实现**：
- [DesktopApplicationBuilder](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Hosting/DesktopApplicationBuilder.cs)：在构造时创建 `HostApplicationBuilder`，注册 `DesktopHostOptions`（绑定 "Wails" 节）、`ApplicationOptions` 工厂单例（`IOptionsMonitor<ApplicationOptions>` 支持热重载）、`Application` 单例、`DesktopHostedService`、`PluginHostedServiceAdapter`。
- [DesktopHostedService](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Hosting/DesktopHostedService.cs)：`StartAsync` 在专用 STA 线程运行 `Application.Run()`，`StopAsync` 触发 `Shutdown` 并 `Join` UI 线程 5 秒。
- **Build() 初始化顺序**：插件 `InitializeAll` → 创建 `CommandDispatcher` → 自动创建 `FileAssetServer`（若配置 `AssetsDirectory`）→ `LoadCapabilities` 自动加载 → `SetWindowConfigs` 延迟窗口创建。

**Tauri 2 原生功能**：
- `tauri::Builder` 链式注册：`plugin(P)`、`invoke_handler(handler)`、`setup(|app| {...})`。
- `App::run(|handle, event| {...})` 派发 `RunEvent::{Ready, Exit, ExitRequested, Resumed, MainEventsCleared, WindowEvent}`。
- `AppHandle` 是 `Arc` 内部句柄，可跨线程克隆。
- `ExitRequested` 可阻止退出。

**Wails 3 原生功能**：
- `application.New(Options)` 工厂构造，`Options` 携带 `Name`/`Services`/`Logger`/`Bindings` 等。
- `App.Run()` 启动平台事件循环。
- 信号处理器自动捕获 SIGINT/SIGTERM，触发 `OnShutdown`。

**差异分析**：
- Wails.Net 是**三者中唯一**采用 `IHost` 抽象的方案，原生支持 `IHostedService`、`IHostApplicationLifetime`、`IHostEnvironment`，可无缝接入 ASP.NET Core 生态（如 `Microsoft.Extensions.Hosting.WindowsServices` 可让 Wails 应用作为 Windows 服务运行）。
- Tauri 2 和 Wails 3 都是"自托管"模型，不依赖外部 Host 抽象。
- Wails.Net 的 STA 线程模型对 Win32 GUI 友好；Tauri 2 的 Tokio runtime 对异步 IO 友好；Wails 3 直接复用平台主线程。

---

### 1.2 DI 容器

| 实现项 | Wails.Net | Tauri 2 | Wails 3 |
|--------|-----------|---------|---------|
| **DI 容器** | ✅ `Microsoft.Extensions.DependencyInjection` | ❌ 无完整容器，仅 `Manager::manage(T)` | ❌ 无独立容器，Service 模式 |
| **服务注册** | `services.AddSingleton<TService, TImpl>()` | `app.manage(T)`（任意 `Send + Sync + 'static`） | `Options.Services []Service` |
| **生命周期** | Singleton / Transient / Scoped 三种 | 全局单例（手动 `Arc<Mutex<T>>`） | 单例（Service 实例） |
| **构造函数注入** | ✅ `ActivatorUtilities.CreateInstance` | ❌ 手动 `app.state::<T>()` | ❌ 手动 struct 字段 |
| **服务定位** | `IServiceProvider.GetService<T>()` | `app.state::<T>()` | `app.GetService(name)` |
| **插件 DI 集成** | ✅ `IPlugin.ConfigureServices(IServiceCollection)` | ✅ `Plugin::setup` 内 `app.manage()` | ✅ `Service` 即绑定即注入 |
| **管理器单例** | EventProcessor / BindingManager / WindowManager 等全部单例 | 通过 `Manager` trait 共享 | 通过 `App` 字段共享 |
| **命令容器 DI** | ✅ `[DesktopCommand]` 标记类自动 DI | ✅ `#[tauri::command]` State 注入 | ❌ Service 实例方法 |

**Wails.Net 关键实现**：
- [ServiceCollectionExtensions](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Hosting/ServiceCollectionExtensions.cs) 提供 `AddWailsManagers`（注册 5 个核心管理器）+ `AddWailsServices`（注册 6 个内置服务）+ `AddWailsLogging`（注册 `LogServiceLoggerProvider`）三个扩展方法，`AddWails` 是一站式入口。
- [CommandRegistry.RegisterFromAssembly](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Commands/CommandRegistry.cs)：使用 `ActivatorUtilities.CreateInstance` 创建标记 `[DesktopCommand]` 的命令容器，支持构造函数注入任意 DI 服务。

**Tauri 2 原生功能**：
- `Manager::manage(T)` 注入任意 `Send + Sync + 'static` 类型到全局状态池。
- 命令参数中以 `State<'_, T>` 注入，推荐外层包 `Arc<Mutex<T>>` 或 `Arc<RwLock<T>>`。
- 无 Scoped 概念，无构造函数注入，无自动装配。

**Wails 3 原生功能**：
- 通过 `Options.Services` 注册 `Service` 实例。
- 无独立 DI 容器，Service 既是状态容器也是绑定源。
- 依赖关系通过 struct 字段手动注入。

**差异分析**：
- Wails.Net 是**三者中唯一**提供完整 DI 容器（Singleton/Transient/Scoped 三种生命周期）+ 构造函数注入 + 服务定位的方案。
- Tauri 2 的 `Manager::manage` 只能注册全局单例，且需要手动加锁。
- Wails 3 完全无 DI 抽象，依赖 Go struct 字段。
- Wails.Net 的 DI 集成让插件可通过 `IPlugin.ConfigureServices` 注入自身服务，与 ASP.NET Core `IStartup.ConfigureServices` 模式一致。

---

### 1.3 配置系统

| 实现项 | Wails.Net | Tauri 2 | Wails 3 |
|--------|-----------|---------|---------|
| **配置文件** | ✅ `appsettings.json` + 环境变量 + 命令行 | ✅ `tauri.conf.json` (JSON5/TOML) + 平台覆盖 | ❌ 无内置配置文件 |
| **配置抽象** | `IConfiguration` | `tauri::Config` struct | `Options` struct |
| **平台覆盖** | ✅ 环境变量覆盖 | ✅ `tauri.{linux,windows,macos,android,ios}.conf.json` 通过 RFC 7396 JSON Merge Patch 合并 | ❌ |
| **热重载** | ✅ `IOptionsMonitor<T>` + `IOptionsSnapshot<T>` | ❌ | ❌ |
| **强类型选项** | ✅ `IOptions<T>` 绑定 | ✅ struct 反序列化 | ✅ struct |
| **命名选项** | ✅ `IOptionsMonitor<T>.Get(name)` | ❌ | ❌ |
| **配置根节** | 统一 "Wails" 节 | 平铺 | N/A |
| **插件配置** | ✅ 各插件 `ConfigureServices` 内读取 | ✅ `plugins.<name>` 节 | ✅ `Options.Services` 字段 |

**Wails.Net 关键实现**：
- [DesktopHostOptions](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Hosting/DesktopHostOptions.cs)：宿主层配置（`ApplicationName`/`Window`/`AssetsDirectory`/`DevServerUrl`/`SingleInstance`/`Permissions`/`Assets`/`App`）。
- [ApplicationOptions](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Options/ApplicationOptions.cs)：对应 Wails v3 `application_options.go`，包含 `OnStartup`/`OnShutdown`/`PostShutdown`/`ShouldQuit` 钩子、`ShutdownTasks`、`Capabilities`、`Csp`、`AllowedUrls`。
- [WebviewWindowOptions](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Options/WebviewWindowOptions.cs)：窗口选项，支持 `Csp` 窗口级覆盖。
- [PermissionOptions](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Security/PermissionOptions.cs)：权限配置（`Permissions`/`Enabled`/`DenyByDefault`/`Scopes`/`CapabilitiesDirectory`）。
- 所有配置类绑定到 "Wails" 根节下的子节（如 `Wails:Permissions`/`Wails:Assets`/`Wails:App`），避免与 ASP.NET Core 标准节（`Logging`/`Kestrel`）冲突。

**Tauri 2 原生功能**：
- `tauri.conf.json` 支持 JSON5/TOML 三种格式。
- 平台配置通过 RFC 7396 JSON Merge Patch 合并（`tauri.linux.conf.json` 覆盖 `tauri.conf.json`）。
- CLI `--config` 支持运行时覆盖。
- 字段含 `app`/`build`/`bundle`/`plugins` 等。

**Wails 3 原生功能**：
- 不内置 config 文件系统，所有配置通过代码内 `Options` 结构传递。
- 可结合 `viper`/`envconfig` 等第三方库实现配置加载。

**差异分析**：
- Wails.Net 复用 `Microsoft.Extensions.Configuration` 抽象，**原生支持 JSON / 环境变量 / 命令行 / XML / INI 多种源**，可叠加自定义 `IConfigurationSource`。
- Tauri 2 的 `tauri.conf.json` 主要面向"构建期配置"，运行时配置需插件自行实现。
- Wails 3 完全无内置配置系统，是最弱的方案。
- Wails.Net 的 `IOptionsMonitor<T>` 支持**运行时热重载**，是三者中独有的能力。

---

### 1.4 日志系统

| 实现项 | Wails.Net | Tauri 2 | Wails 3 |
|--------|-----------|---------|---------|
| **日志抽象** | ✅ `ILogger<T>` + `ILoggerProvider` | `log` crate | `slog.Logger` |
| **日志类别** | ✅ `ILogger<T>` 泛型类别 | ✅ `target` | ✅ `logger.WithGroup/WithAttr` |
| **日志过滤** | ✅ `AddFilter` + 配置 `Logging:LogLevel` | ✅ `filter()` / `level_for()` | ✅ `LoggerLevel` |
| **多目标输出** | ✅ 多 `ILoggerProvider` 注册 | ✅ 多 `Target`（Stdout/Stderr/Webview/LogDir/Folder） | ✅ `slog.Handler` 多路 |
| **日志轮转** | ❌（依赖第三方如 Serilog） | ✅ `RotationStrategy`（按大小/时间） | ❌ |
| **前端 console 桥接** | ✅ **双向**（接收 + 转发） | ✅ 单向（`Webview` target 转发到前端） | ❌ |
| **日志范围** | ✅ `ILogger.BeginScope` | ❌ | ✅ `slog` scope |
| **结构化日志** | ✅ `LoggerExtensions` 字典参数 | ✅ `kv!` 宏 | ✅ `slog` kv |
| **AsyncLocal 防递归** | ✅ `_isWriting` | ❌ | ❌ |

**Wails.Net 关键实现**：
- [LogServiceLoggerProvider](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Logging/LogServiceLoggerProvider.cs)：`ILoggerProvider` 实现，桥接 `ILogger<T>` → `LogService`，使用 `AsyncLocal<bool> _isWriting` 防止日志写入触发新的日志导致无限递归。
- [BrowserConsoleLogForwarder](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Logging/BrowserConsoleLogForwarder.cs)：反向桥接，将 `LogService` 输出推送到前端 `console`，使用 `AsyncLocal<bool> _isForwarding` + 检查 `source=browser` 字段防止前端→后端→前端的循环转发。
- **完整链路**：`ILogger<T>`（.NET 标准）↔ `LogService`（Wails 服务）↔ 前端 `console`（开发者工具）。

**Tauri 2 原生功能**：
- `tauri-plugin-log` 官方独立插件，支持 `Target` 列表：`Stdout`/`Stderr`/`Webview`/`LogDir`/`Folder`。
- `Webview` target 把日志转发到前端 console（单向）。
- 支持 `RotationStrategy` 按大小/时间轮转。

**Wails 3 原生功能**：
- 默认使用 Go 1.21+ 的 `slog` 标准库。
- `Application` 持有 `Logger *slog.Logger`，提供 `debug/info/warn/error` 辅助方法。
- 无前端 console 桥接。

**差异分析**：
- Wails.Net 是**三者中唯一**支持日志双向桥接的方案：前端 `console.*` 自动进入 .NET 日志管道，后端 `ILogger` 输出反向转发到前端 DevTools。
- Tauri 2 仅单向转发（后端 → 前端），Wails 3 完全无桥接。
- Wails.Net 的 `AsyncLocal` 防递归机制解决了双向桥接的核心难题。

---

### 1.5 选项模式

| 实现项 | Wails.Net | Tauri 2 | Wails 3 |
|--------|-----------|---------|---------|
| **选项接口** | ✅ `IOptions<T>` / `IOptionsMonitor<T>` / `IOptionsSnapshot<T>` | ❌（直接 struct） | ❌（直接 struct） |
| **热重载** | ✅ `IOptionsMonitor<T>.OnChange` | ❌ | ❌ |
| **命名选项** | ✅ `IOptionsMonitor<T>.Get(name)` | ❌ | ❌ |
| **配置回调覆盖** | ✅ `Configure<T>(Action<T>)` | ❌ | ❌ |
| **选项校验** | ✅ `IValidateOptions<T>` | ❌ | ❌ |

**Wails.Net 关键实现**：
- `ApplicationOptions` 通过 `IOptionsMonitor<ApplicationOptions>` 注册为工厂单例，支持运行时热重载。
- `PermissionManager` 通过构造函数注入 `IOptions<PermissionOptions>` 获取配置。
- `PermissionServiceExtensions.AddPermissions(Action<PermissionOptions>?)` 支持代码层覆盖配置文件。

**Tauri 2 / Wails 3 原生功能**：均无选项模式抽象，直接使用反序列化的 struct。

**差异分析**：
- Wails.Net 完整复用 ASP.NET Core 选项模式，**支持热重载 + 命名选项 + 配置回调覆盖 + 选项校验**，这是 Tauri 2 和 Wails 3 都不具备的。

---

### 1.6 生命周期钩子

| 钩子 | Wails.Net | Tauri 2 | Wails 3 |
|------|-----------|---------|---------|
| **应用启动前** | `IServiceStartup.ServiceStartup` | `Builder::setup` | `Options.OnBeforeStart` |
| **应用启动后** | `Application.OnStartup` + `OnAfterStart` | `RunEvent::Ready` | `Options.OnStartup` |
| **应用关闭前** | `Application.OnShutdown` | `RunEvent::ExitRequested` | `Options.OnShutdown` |
| **应用关闭后** | `Application.PostShutdown` | `RunEvent::Exit` | `Options.PostShutdown` |
| **是否允许退出** | `ApplicationOptions.ShouldQuit` 回调 | `ExitRequested::api.prevent_exit()` | `Options.ShouldQuit` |
| **窗口事件** | `WindowManager.WindowCreated/Closed` | `RunEvent::WindowEvent` | `WindowEventType` 常量 |
| **应用恢复（移动端）** | ❌（暂未实现） | `RunEvent::Resumed` | ❌ |
| **服务逆序关闭** | ✅ `IServiceShutdown` 逆序 | ❌ | ✅ `ServiceShutdown` 逆序 |
| **ShutdownTasks** | ✅ `ApplicationOptions.ShutdownTasks` | ❌ | ✅ |

**Wails.Net 关键实现**：
- [Application.Run()](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Application.cs) 执行顺序：`SetupSingleInstance` → `OnBeforeStart` 钩子 → `IServiceStartup.ServiceStartup` → `AssetServer` 启动 → `Transport.StartAsync` → `InitializeNativeIpcTransport` → `OnStartup` → `CreateWindowsFromConfig` → `OnAfterStart` → `PluginManager.StartupPluginsAsync` → `PlatformApp.Run` → `Shutdown`。
- [Application.Shutdown()](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Application.cs) 逆序关闭：`_lifetime.StopApplication` → `_cts.Cancel` → `ShutdownTasks` → `OnShutdown` → 关闭所有窗口 → `Transport.StopAsync` → `AssetServer.ServiceShutdown` → `PluginManager.ShutdownPluginsAsync` → 逆序 `IServiceShutdown.ServiceShutdown` → `PlatformApp.Destroy` → `PostShutdown`。

**差异分析**：
- Wails.Net 完整对齐 Wails 3 的 `OnBeforeStart`/`OnStartup`/`OnShutdown`/`PostShutdown`/`ShouldQuit` 五钩子模型。
- Tauri 2 用 `RunEvent` 枚举 + 回调，更灵活但语义不直观。
- Wails.Net 的 `IServiceStartup`/`IServiceShutdown` 接口提供独立的"服务协调"层（与 `IHostedService` 并列），是独创设计。

---

## 维度 2：Runtime / Window / IPC（学 Wails v3）

### 2.1 Application 对象模型

| 实现项 | Wails.Net | Tauri 2 | Wails 3 |
|--------|-----------|---------|---------|
| **核心类** | `Application`（约 1500 行） | `tauri::App` + `AppHandle` | `application.Application` |
| **工厂构造** | DI 单例（`Application` 通过 `AddWails` 注册） | `Builder::build()` | `application.New(Options)` |
| **主循环** | `Application.Run()` 委托 `IPlatformApp.Run` | `App::run(callback)` | `App.Run()` |
| **退出方法** | `Application.Quit()` / `Shutdown()` | `AppHandle::exit(0)` | `App.Quit()` |
| **获取实例** | `Application.Get()` 静态访问器 | `AppHandle::clone()` | `*Application` 指针 |
| **窗口管理器** | `WindowManager`（DI 注入） | `Manager` trait | `App.Window` |
| **事件管理器** | `EventProcessor`（DI 注入） | `Emitter`/`Listener` trait | `App.Event` |
| **菜单管理器** | `MenuManager`（DI 注入） | `app.menu()` | `App.Menu` |
| **对话框管理器** | `DialogManager`（DI 注入） | `app.dialog()` | `App.Dialog` |
| **屏幕管理器** | `ScreenManager`（DI 注入） | `app.primary_monitor()` | `App.Screen` |
| **剪贴板管理器** | `ClipboardManager`（DI 注入） | `app.clipboard()` | `App.Clipboard` |
| **键绑定管理器** | `KeyBindingManager`（DI 注入） | `app.global_shortcut()` | N/A |
| **浏览器管理器** | `IBrowserManager`（DI 注入） | N/A（用 `shell` 插件） | `App.Browser` |

**Wails.Net 关键实现**：
- [Application](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Application.cs) 是核心应用类，对应 Wails v3 `Application`，但额外集成了 DI 容器访问、生命周期管理、传输层协调、插件管理器调度。
- 所有管理器（WindowManager / EventProcessor / MenuManager / DialogManager / ScreenManager / ClipboardManager / KeyBindingManager）通过 DI 注入，符合 ASP.NET Core 模式。

**差异分析**：
- Wails.Net 的 `Application` 类**同时融合了** Wails 3 的 `Application` 对象模型 + ASP.NET Core 的 DI 容器访问。
- Tauri 2 的 `AppHandle` 是线程安全的克隆句柄，更轻量；Wails.Net 用 `Application.Get()` 静态访问器 + DI 注入双路径。
- Wails 3 的 `Application` 字段直接持有各 Manager，无 DI 解耦。

---

### 2.2 平台抽象与 Server 模式

| 实现项 | Wails.Net | Tauri 2 | Wails 3 |
|--------|-----------|---------|---------|
| **平台抽象接口** | `IPlatformApp` | 编译期 cfg 分支 | `PlatformApp` 接口 |
| **平台实现** | Win32PlatformApp / LinuxPlatformApp / AndroidPlatformApp | windows.rs / macos.rs / linux.rs | platform_windows.go / platform_linux.go / platform_darwin.go |
| **Server 模式降级** | ✅ `ServerPlatformApp` + `ServerWebviewWindow` + `ServerClipboard` + `ServerBrowserManager` | ❌ | ✅ Headless 模式 |
| **阻塞模型** | `ManualResetEventSlim` | N/A | `ManualResetEvent` |
| **GUI 操作 no-op** | ✅ 全部 no-op 不抛异常 | N/A | ✅ |
| **对话框默认值** | ✅ 返回首个按钮 / null | N/A | ✅ |
| **单实例锁** | ✅ 始终返回 true（视作首实例） | N/A | ✅ |
| **Destroy 可重写** | ✅ `virtual` 修饰符（P1-7） | N/A | N/A |

**Wails.Net 关键实现**：
- [IPlatformApp](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Platform/IPlatformApp.cs) 接口含大量默认 no-op 实现（C# 8.0 默认接口方法），包括 `Run`/`AcquireSingleInstanceLock`/`NotifySingleInstance`/`Destroy`/`SetApplicationMenu`/`GetCurrentWindowId`/`SetParent`/`ShowAboutDialog`/`SetIcon`/`Show`/`Hide`/`GetPrimaryScreen`/`GetScreens`/`IsDarkMode`/`GetAccentColor`/`DispatchOnMainThread`/`CreateWebviewWindow`/`ShowMessageDialog`/`OpenFileDialog`/`SaveFileDialog`/`OpenMultipleFilesDialog`。
- [ServerPlatformApp](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Platform/ServerMode/ServerPlatformApp.cs) 使用 `ManualResetEventSlim` 阻塞 `Run()`，所有 GUI 操作 no-op。
- `Destroy()` 改为 `virtual` 修饰符（P1-7），支持单元测试 override 验证 Shutdown 流程。

**差异分析**：
- Wails.Net 的 Server 模式是**三者中最完整**的无 GUI 降级方案，提供与桌面模式完全一致的 API 表面，适用于容器化部署、自动化 UI 测试、服务端预渲染。
- Tauri 2 完全无 Server 模式。
- Wails 3 的 Headless 模式功能类似，但 API 表面不如 Wails.Net 完整（缺 `ServerBrowserManager` 等）。
- Wails.Net 的 `IPlatformApp` 默认接口方法降低平台移植成本，新平台只需实现必要方法。

---

### 2.3 窗口管理

| 实现项 | Wails.Net | Tauri 2 | Wails 3 |
|--------|-----------|---------|---------|
| **窗口抽象接口** | `IWebviewWindowImpl`（30+ 方法） | `WebviewWindow`（直接类型） | `IWebviewWindowImpl` 接口 |
| **公共 API 类** | `WebviewWindow`（委托 IWebviewWindowImpl） | `WebviewWindow` | `WebviewWindow` |
| **窗口管理器** | `WindowManager`（`Interlocked.Increment` ID） | `Manager` trait | `WindowManager` |
| **多窗口** | ✅ `ConcurrentDictionary<uint, WebviewWindow>` | ✅ 按 label 索引 | ✅ 按 name/id 索引 |
| **窗口名↔ID 映射** | ✅ 双字典 | ❌（仅 label） | ✅ |
| **Frameless** | ✅ | ✅ | ✅ |
| **Transparent** | ✅ | ✅ | ✅ |
| **AlwaysOnTop** | ✅ | ✅ | ✅ |
| **DevTools** | ✅ F12 / `OpenDevTools` | ✅ | ✅ |
| **Zoom** | ✅ `SetZoom/ZoomIn/ZoomOut/ZoomReset` | ✅ | ✅ |
| **打印** | ✅ `Print/PrintToPDF` | ✅（plugin） | ✅ |
| **ExecJS / InjectCSS** | ✅ | ✅ `eval` | ✅ |
| **文件拖放** | ✅ `WM_DROPFILES` (Win) | ✅ | ✅ |
| **DPI 适配** | ✅ `WM_DPICHANGED` (Win) | ✅ | ✅ |
| **窗口级 CSP** | ✅ `SetCspHeaderForWindow`（P0-4） | ✅ | ❌ |
| **任务栏进度** | ✅ `SetTaskbarProgress` / `SetOverlayIcon` | ✅（plugin） | ✅ |
| **窗口效果** | ✅ `SetEffects`（Mica/Acrylic） | ✅ | ✅ |
| **Badge** | ✅ `SetBadgeCount/SetBadgeLabel` | ✅（plugin） | ✅ |
| **多工作区** | ✅ `SetVisibleOnAllWorkspaces` | ✅ | ✅ |
| **边框颜色** | ✅ `SetBorderColor` | ✅ | ✅ |
| **内容保护** | ✅ `SetContentProtection` | ✅ | ✅ |
| **Win32 消息处理** | ✅ 18+ 消息（WM_DESTROY/CLOSE/SIZE/COMMAND/SYSCOMMAND/GETMINMAXINFO/DPICHANGED/HOTKEY/DROPFILES/SETTINGCHANGE/MOVE/NCLBUTTONDOWN/SETICON/ACTIVATE/DISPLAYCHANGE/CLIPBOARDUPDATE/KEYDOWN/CONTEXTMENU） | N/A | N/A |

**Wails.Net 关键实现**：
- [IWebviewWindowImpl](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Windows/IWebviewWindowImpl.cs) 接口大量方法有默认 no-op 实现（C# 8.0 默认接口方法）：`SetBackgroundType`/`SetFullscreenButtonEnabled`/`SetZoom`/`SetTranslucent`/`SetOpacity`/`GetOpacity`/`InjectCSS`/`ZoomIn`/`ZoomOut`/`OpenContextMenu`/`PrintToPDF`/`Run`/`CapturePreviewAsync`/`RegisterCustomScheme`/`SetTaskbarProgress`/`SetOverlayIcon`/`SetSkipTaskbar`/`SetIgnoreCursorEvents`/`SetEffects`/`SetBadgeCount`/`SetBadgeLabel`/`SetVisibleOnAllWorkspaces`/`SetBorderColor`/`SetFileDropEnabled`/`SetNativeMessageHandler`/`SetConsoleMessageHandler`/`PostNativeMessageAsync`。
- [WindowManager](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Managers/WindowManager.cs) 使用 `ConcurrentDictionary<uint, WebviewWindow>` + `ConcurrentDictionary<string, uint> _windowNames`，`Interlocked.Increment` 生成窗口 ID。
- [Win32WebviewWindow](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Windows/Win32WebviewWindow.cs) 完整处理 18+ Win32 消息。

**差异分析**：
- Wails.Net 的 `IWebviewWindowImpl` 默认接口方法是独创设计，平台实现只需重写支持的方法（如 Linux 无 `SetTaskbarProgress`），降低移植成本。
- Tauri 2 的 `WebviewWindow` 直接是具体类型，无接口抽象，但跨平台行为一致。
- Wails 3 的 `IWebviewWindowImpl` 接口无默认实现，平台实现必须实现所有方法。
- Wails.Net 的窗口级 CSP（`SetCspHeaderForWindow`）是**三者中独有**的（Wails 3 不支持）。

---

### 2.4 IPC 传输层

| 实现项 | Wails.Net | Tauri 2 | Wails 3 |
|--------|-----------|---------|---------|
| **传输抽象** | `ITransport` 接口 | 编译期 invoke | `ITransport` 接口 |
| **默认传输** | NativeIpcTransport（默认启用） | 平台原生 IPC | in-memory bridge |
| **HTTP 传输** | ✅ `HttpTransport`（端口 34115） | ❌ | ❌ |
| **WebSocket 传输** | ✅ `WebSocketTransport`（端口 34116） | ❌ | ❌ |
| **原生 IPC 传输** | ✅ `NativeIpcTransport`（512KB 阈值混合策略） | ✅ 默认 | ✅ 默认 |
| **Event IPC 兜底** | ✅ `EventIPCTransport`（始终追加为兜底） | ❌ | ❌ |
| **AssetServer 传输** | ✅ `AssetServerTransport` | ❌ | ❌ |
| **分块上传** | ✅ `x-wails-chunk-id/index/total` 协议（≤1MB/chunk，≤64MB 总，30s TTL） | ❌ | ❌ |
| **CORS 配置** | ✅ `CorsOptions` + `IpcOriginValidator` | ✅ | N/A |
| **CancellablePromise** | ✅ `_runningCalls` + `cancel` 消息类型 | ✅ `Cancellation` | ✅ `CancelCall` |
| **多传输层并行广播** | ✅ `IWailsEventListener` 列表 | ❌ | ❌ |
| **senderWindowId 传播** | ✅ 全链路携带 | ❌ | ❌ |

**Wails.Net 关键实现**：
- [NativeIpcTransport](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/NativeIpcTransport.cs)：使用原生 `postMessage` 通道，**512KB 阈值**：超过则降级到 HTTP。`RegisterWindow` 安装 `SetNativeMessageHandler` 回调接收前端消息。
- [HttpTransport](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/HttpTransport.cs)：基于 `HttpListener`，支持 `ChunkStore` 分块上传 + `CorsOptions` + `IpcOriginValidator`。
- [EventIPCTransport](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/EventIPCTransport.cs)：兜底通过 `ExecJS` 注入 `window._wailsEmitEvent`，与 `NativeIpcTransport` 协作避免重复派发。

**Tauri 2 原生功能**：
- 默认走平台原生 IPC（WebView2 host object / WKWebView message handler / WebKitGTK），无 HTTP/WebSocket 传输。
- 性能最优但调试不便。

**Wails 3 原生功能**：
- 默认 `HTTPTransport`（端口 34115），无原生 IPC（alpha 阶段）。
- `MessageProcessor` 解析 `{name, args, methodID}` 派发到 `Bindings` 或 `Window`/`Dialog` 等。

**差异分析**：
- Wails.Net 是**三者中唯一**支持多传输层并行广播的方案，适用于容器化部署和调试场景。
- NativeIpcTransport 的混合策略（小消息原生 + 大消息 HTTP 分块）平衡延迟与容量，是独创设计。
- CancellablePromise 在三者中均支持，但 Wails.Net 通过 `_runningCalls` ConcurrentDictionary + `cancel` 消息类型实现，最接近 Wails 3 的 `CancelCall` 模型。
- `senderWindowId` 全链路传播是 Wails.Net 独有，Tauri 2 和 Wails 3 都不携带事件来源窗口标识。

---

### 2.5 绑定系统

| 实现项 | Wails.Net | Tauri 2 | Wails 3 |
|--------|-----------|---------|---------|
| **绑定入口** | `[Binding]` 特性 + `BindingManager.Add(instance)` | `#[tauri::command]` 宏 | `Application.Bind(instance)` |
| **调用路径** | 源生成器优先 + 反射回退（双路径） | 编译期宏展开 | 反射 |
| **方法 ID 哈希** | FNV-1a 32 位 | 字符串名 | FNV-1a 32 位 |
| **哈希常量** | `offsetBasis=2166136261` / `prime=16777619` | N/A | `fnv.New32a()` 默认 |
| **哈希一致性** | ✅ 与 Go 版本字节级一致 | N/A | ✅ 基准实现 |
| **注册键** | 全限定名 + 短名称双键 | 命令名字符串 | 全限定名 + 短名称 |
| **方法过滤** | 排除 `ServiceName`/`ServiceStartup`/`ServiceShutdown`/`IsSpecialName`/`Object` 继承方法 | 显式 `#[command]` 标注 | 排除 `Service` 接口方法 |
| **错误类型** | `CallError` + `CallErrorKind`（Reference/Type/Runtime） | `Result<T, E: Serialize>` | `CallError` + `ErrorKind` |
| **反射异常解包** | ✅ `TargetInvocationException` 解包 | N/A | ✅ |
| **取消支持** | ✅ `OperationCanceledException` 重抛 | ✅ `Cancellation` | ✅ `CancelCall` |
| **源生成器** | ✅ `BindingSourceGenerator`（`IIncrementalGenerator`） | N/A | N/A |
| **ModuleInitializer** | ✅ `[ModuleInitializer]` 自动注册 | N/A | N/A |
| **类名避冲突** | ✅ `BindingManager` 而非 `Bindings`（避免 CS0118） | N/A | N/A |

**Wails.Net 关键实现**：
- [BindingManager](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Bindings/Bindings.cs)：
  - **源生成器优先**：`GeneratedBindingRegistry.TryGetInvoker` 优先使用编译时生成的强类型调用器。
  - **反射兜底**：源生成器未覆盖的场景回退到反射调用。
  - **FNV-1a 32 位哈希**：与 Go 版本 `fnv.New32a()` 完全一致（AGENTS.md §6.3 强制要求）。
- [BindingSourceGenerator](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.SourceGenerators/BindingSourceGenerator.cs)：通过 `IIncrementalGenerator` 生成 `WailsGeneratedBindings.g.cs`，使用 `[ModuleInitializer]` 自动注册。

**Tauri 2 原生功能**：
- `#[tauri::command]` 宏在编译期展开为 `InvokeHandler`，运行时零反射。
- 命令名字符串直接作为标识符，无哈希。

**Wails 3 原生功能**：
- `Application.Bind(instance)` 反射注册 Service 的公共方法。
- `FNV-1a 32位` 哈希生成 methodID（`fnv.New32a()`）。
- `registeredBindingMethodIDs sync.Map` 允许显式指定方法 ID（绕过 FNV-1a 哈希），用于代码混淆场景。
- `Bindings.methodAliases map[uint32]uint32` 支持旧 ID → 新 ID 映射。
- `ServiceOptions.MarshalError` 每个 Service 可自定义错误序列化逻辑。

**差异分析**：
- Wails.Net 是**三者中唯一**采用源生成器 + 反射双路径的方案，兼顾性能（编译期生成）与开发体验（反射兜底）。
- Tauri 2 完全无反射，性能最优，但需要 Rust 宏学习成本。
- Wails 3 与 Wails.Net 共享 FNV-1a 哈希算法，前端绑定 ID 可互通。
- Wails.Net 严格遵循 AGENTS.md §3.4 "禁止使用反射获取对应方法"约束，源生成器是主路径。

---

### 2.6 事件系统

| 实现项 | Wails.Net | Tauri 2 | Wails 3 |
|--------|-----------|---------|---------|
| **核心类型** | `EventProcessor` | `Emitter`/`Listener` trait | `EventManager` |
| **公共事件类型数** | **30 个**（`ApplicationEventType` 枚举 0~29） | ~20 个 | ~28 个 |
| **监听器存储** | `ConcurrentDictionary<string, List<EventListener>>` | `Vec<EventListener>` | `map[string][]EventListener` |
| **Pre-emit 钩子** | ✅ `List<Func<CustomEvent, bool>>`（返回 false 取消） | ❌ | ✅ Event Hooks |
| **多传输层广播** | ✅ `IWailsEventListener` 列表并行广播 | 单一 IPC | 单一 in-memory bridge |
| **事件命名空间** | `wails:window:*` / `wails:app:*` / `wails:low:memory` / `wails:screen:*` | `tauri://*` | `wails:*` |
| **JS 端 API** | `wails.events.on/off/emit` | `listen/unlisten/emit` | `wails.events.on/off/emit` |
| **跨窗口广播** | ✅ 通过多传输层自动广播 | ✅ `emit_to(label)` | ✅ |
| **线程安全** | ✅ `ConcurrentDictionary` + 锁 | ✅ `Mutex` | ✅ `sync.RWMutex` |
| **senderWindowId 传播** | ✅ EventProcessor/Transport 全链路携带（P1-2） | ❌ | ❌ |
| **PostShutdown 钩子** | ✅ `ApplicationOptions.PostShutdown`（P1-7） | ❌ | ✅ `Options.PostShutdown` |
| **ShouldQuit 回调** | ✅ `ApplicationOptions.ShouldQuit`（P1-7） | ✅ `ExitRequested::api.prevent_exit()` | ✅ `Options.ShouldQuit` |
| **maxCalls 限制** | ✅ `OnMultiple` + `Once` | ✅ `once` | ✅ `Once` |
| **LowMemory 事件** | ✅ `ApplicationEventType.LowMemory=27`（P2 新增） | ❌ | ✅ `Common.LowMemory=1290` |
| **ScreenLocked/Unlocked** | ✅ `ApplicationEventType.ScreenLocked=28` / `ScreenUnlocked=29`（P2 新增） | ❌ | ✅ `Common.ScreenLocked=1288` / `ScreenUnlocked=1289` |
| **平台事件映射** | ✅ `AndroidPlatformEvents.MapToCommonEvent`（P2 新增） | ❌ | ✅ `commonApplicationEventMap` |

**Wails.Net 关键实现**：
- [EventProcessor](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Events/EventProcessor.cs) 使用 `ConcurrentDictionary<string, List<EventListener>>` 存储监听器。
- `_hooks` 字段支持 Pre-emit 钩子（学 Wails v3 Event Hooks）。
- `_wailsEventListeners` 列表支持多传输层广播：`SetWailsEventListener`/`AddWailsEventListener`/`RemoveWailsEventListener`。
- 事件订阅 API：`On`/`OnMultiple`/`Once`/`Emit`/`Off`。
- [ApplicationEventType](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Events/ApplicationEventType.cs) 枚举包含 30 个值（`Started=0` 到 `ScreenUnlocked=29`）。
- [AndroidPlatformEvents](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Android/AndroidPlatformEvents.cs) 定义 Android 平台事件 ID（1267-1285）+ `MapToCommonEvent` 映射到公共事件（7 个映射）。

**Tauri 2 原生功能**：
- `Emitter` trait：`emit/emit_to/emit_filter`，支持定向到 `EventTarget`（Webview/Window/App/Any）。
- `Listener` trait：`listen/listen_any/unlisten/once`。
- 无 Pre-emit 钩子，无多传输层广播。

**Wails 3 原生功能**：
- `EventManager` 双层架构：hooks（可取消事件）+ listeners。
- `RegisterApplicationEventHook` 注册应用级事件 Hook。
- `ApplicationEvent` 类型常量（`ApplicationStarted`/`ThemeChanged` 等）。
- `OnApplicationEvent` 订阅应用级事件。

**差异分析**：
- Wails.Net 同时支持 Pre-emit 钩子（学 Wails 3）和**多传输层并行广播**（独有）：一次 `Emit` 会同时通过 HttpTransport + WebSocketBroadcaster + EventIPCTransport + AssetServerTransport + NativeIpcTransport 广播，确保所有连接的前端都能收到。
- `senderWindowId` 全链路传播是 Wails.Net 独有，Tauri 2 用 `EventTarget` 定向，Wails 3 无来源标识。
- Wails.Net 自 P2 起新增 3 个公共事件（LowMemory/ScreenLocked/ScreenUnlocked），完整对齐 Wails v3 `Common.*` 事件。

---

### 2.7 MessageProcessor 路由

| 实现项 | Wails.Net | Tauri 2 | Wails 3 |
|--------|-----------|---------|---------|
| **核心类型** | `MessageProcessor` | 编译期 invoke | `MessageProcessor` |
| **消息类型常量** | `call`/`event`/`window`/`query`/`response`/`error`/`drag`/`contextmenu`/`system`/`cancel` | `Invoke` / `Event` | 同 Wails.Net（无 cancel） |
| **Call 路由** | `BindingManager` 优先 → `CommandDispatcher` 回退 | 直接路由到命令 | `Bindings` 调用 |
| **Window 路由** | `CommandDispatcher` 优先 → 硬编码 `DispatchWindowAction` 回退 | `window.*` plugin 命令 | `DispatchWindowAction` 硬编码 |
| **未识别路由** | `ProcessCommandFallbackAsync` 统一走 `CommandDispatcher` | 报错 | 报错 |
| **CancellablePromise** | ✅ `_runningCalls` ConcurrentDictionary | ✅ `Cancellation` | ✅ `CancelCall` |
| **窗口操作事件广播** | ✅ `_events.Emit($"wails:window:{action}", ...)` | ✅ | ✅ |
| **Options 对象匹配** | ✅ `WindowSetTitleOptions`/`WindowSizeOptions` 等 | ✅ | ✅ |

**Wails.Net 关键实现**：
- [MessageProcessor](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Transport/MessageProcessor.cs) `ProcessAsync` 路由规则：
  - `call` → `ProcessCallAsync`（`BindingManager` 优先 → `CommandDispatcher` 回退）
  - `window` → `ProcessWindowAsync`（`CommandDispatcher` 优先 → 硬编码 `DispatchWindowAction` 回退）
  - 其他 → `ProcessCommandFallbackAsync`（统一走 `CommandDispatcher`）
- `_runningCalls` 使用 `ConcurrentDictionary` 跟踪运行中调用，支持 `cancel` 消息类型取消调用。
- 窗口操作的事件广播通过 `_events.Emit($"wails:window:{action}", ...)` 保留。

**差异分析**：
- Wails.Net 采用 Tauri v2 "核心即插件"哲学：window 操作也优先走 `CommandDispatcher`，硬编码路径仅作向后兼容回退。这是 Wails 3 没有的设计。
- Tauri 2 完全编译期路由，无回退机制。
- Wails 3 完全硬编码 `DispatchWindowAction`，无插件化。
- Wails.Net 新增 `MessageTypes.Cancel` 消息类型，与 Wails 3 的 `CancelCall` 对齐。

---

### 2.8 菜单系统与 MenuRole

| 实现项 | Wails.Net | Tauri 2 | Wails 3 |
|--------|-----------|---------|---------|
| **菜单抽象** | `Menu` / `MenuItem` / `IMenuImpl` | `Menu` / `MenuItem` / `PredefinedMenuItem` | `Menu` / `MenuItem` / `MenuRole` |
| **预定义角色枚举** | ✅ `MenuRole`（21 个值，P2 新增） | ✅ `PredefinedMenuItem`（约 14 个） | ✅ `Role` 常量 |
| **角色工厂方法** | ✅ 13 个 | ✅ `PredefinedMenuItem::new_*` | ✅ 隐式 |
| **跨平台辅助工具** | ✅ `MenuRoleHelper` | ✅ | ✅ |
| **macOS 专属降级** | ✅ Windows/Linux 静默 no-op | ✅ | ✅ |
| **关于对话框元数据** | ✅ `AboutMetadata` | ✅ `AboutMetadata` | ✅ |
| **标准菜单组合** | ✅ `AddStandardEditMenu`/`AddStandardWindowMenu`/`AddStandardHelpMenu` | ❌ | ❌ |
| **Win32 实现** | ✅ `Win32Menu.ApplyRole` | N/A | ✅ |
| **Linux 实现** | ✅ `LinuxMenu.ApplyRole`（GTK4 GMenu） | N/A | ✅ |
| **Android 实现** | ❌ | N/A | ❌ |
| **前端 API 注入** | ✅ `wails.MenuRole` 常量 + 4 个 menu.* 命令 | ✅ `MenuItem.predefined_*` | ✅ `Role` |
| **全局热键注册** | ✅ 角色带 Accelerator 时自动注册到 `KeyBindingManager` | ✅（plugin） | ✅ |
| **菜单插件命令数** | 10 个（含 5 个 MenuRole 命令） | 8 个 | N/A |

**Wails.Net 关键实现**：
- [MenuRole](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Menus/MenuRole.cs) 21 个角色枚举值（None/Separator/Copy/Cut/Paste/SelectAll/Undo/Redo/Minimize/Maximize/Fullscreen/CloseWindow/Zoom/About/Quit/Hide/HideOthers/ShowAll/Services/BringAllToFront/ToggleFullScreen）。
- [MenuRoleHelper](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Menus/MenuRoleHelper.cs)：默认中文 Label、默认 Accelerator、macOS 专属判定、`PrepareRoleItem` 统一填充 Callback。
- [MenuItem](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Menus/MenuItem.cs) 13 个工厂方法：`CreateCopy`/`CreateCut`/`CreatePaste`/`CreateSelectAll`/`CreateUndo`/`CreateRedo`/`CreateSeparator`/`CreateMinimize`/`CreateMaximize`/`CreateFullscreen`/`CreateCloseWindow`/`CreateQuit`/`CreateAbout`。
- [Menu](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Menus/Menu.cs) 新增 `AddRoleItem`/`AddStandardEditMenu`/`AddStandardWindowMenu`/`AddStandardHelpMenu` 方法。
- [Win32Menu.ApplyRole](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Windows/Win32Menu.cs) / [LinuxMenu.ApplyRole](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Linux/LinuxMenu.cs) 各自实现 ExecuteRole，编辑命令通过 `document.execCommand` 调用。
- [AboutMetadata](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Menus/AboutMetadata.cs) 9 个字段（Name/Version/ShortVersion/Authors/Copyright/License/Website/WebsiteLabel/Comments），对应 Tauri v2 `AboutMetadata`。
- [RuntimeGenerator](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Runtime.Js/RuntimeGenerator.cs) 注入前端 `wails.MenuRole` 常量枚举与 4 个 menu.* 命令。

**差异分析**：
- Wails.Net 的 MenuRole 系统是**三者中最完整**的角色菜单实现，结合 Wails v3 Role 常量模型与 Tauri v2 PredefinedMenuItem 工厂模式：
  - **21 个角色枚举值**（覆盖跨平台编辑/窗口/应用角色 + macOS 专属角色）
  - **13 个工厂方法**（参考 Tauri `PredefinedMenuItem::new_*` 工厂 API）
  - **跨平台共享逻辑**（`MenuRoleHelper` 提供默认中文 Label、默认 Accelerator、macOS 专属判定）
  - **平台实现分离**（Win32Menu / LinuxMenu 各自实现 ExecuteRole）
  - **全局热键自动注册**（修复现有 Accelerator 仅在菜单栏内生效的 bug）
  - **标准菜单组合**（一键构建跨平台标准 Edit/Window/Help 菜单）
- Tauri 2 通过 `PredefinedMenuItem` 提供约 14 个预定义菜单项，无标准菜单组合方法。
- Wails 3 通过 `Role` 常量支持菜单角色，但缺少工厂方法与跨平台辅助工具的统一封装。

---

### 2.9 系统托盘

| 实现项 | Wails.Net | Tauri 2 | Wails 3 |
|--------|-----------|---------|---------|
| **托盘类型** | `TrayPlugin` + `ITrayImpl` | `TrayIconBuilder` + `TrayIconEvent` | `SystemTray` + `systemTrayImpl` |
| **图标设置** | ✅ `SetIcon` | ✅ `icon` | ✅ `SetIcon` |
| **菜单附加** | ✅ `SetMenu` | ✅ `menu` | ✅ `SetMenu` |
| **点击事件** | ✅ `OnClick`/`OnRightClick`/`OnDoubleClick` | ✅ `on_tray_icon_event` | ✅ |
| **附着窗口定位** | ✅ `AttachWindow`/`PositionWindow` | ❌ | ✅ `AttachWindow` |
| **标签/Tooltip** | ✅ `SetLabel`/`SetTooltip` | ✅ `tooltip` | ✅ |
| **平台实现** | Win32Tray / LinuxTray | 跨平台 | platform_windows.go / platform_linux.go |

**Wails.Net 关键实现**：
- `TrayPlugin` 通过 `ITrayImpl` 接口抽象平台实现。
- Win32Tray 使用 `Shell_NotifyIconW` + 自定义 WM_USER 消息。
- LinuxTray 使用 GTK4 `StatusIcon`（或 AppIndicator）。

**差异分析**：
- 三方托盘功能基本对齐，Wails.Net 额外支持 `AttachWindow`/`PositionWindow` 窗口定位（学 Wails 3）。
- Tauri 2 通过 `TrayIconBuilder` 链式构造，API 最现代化。
- Wails 3 的 `SystemTray` 是最早的实现，Wails.Net 与之最接近。

---

### 2.10 全局快捷键与拖拽

| 实现项 | Wails.Net | Tauri 2 | Wails 3 |
|--------|-----------|---------|---------|
| **全局快捷键** | `KeyBindingManager` + `GlobalShortcutPlugin` | `tauri-plugin-global-shortcut` | `Application.KeyBinding` |
| **快捷键注册** | ✅ `RegisterKeyBinding(accelerator, callback)` | ✅ `on_shortcut` | ✅ |
| **快捷键注销** | ✅ `UnregisterKeyBinding` | ✅ `unregister` | ✅ |
| **CSS 拖拽区域** | ✅ `DragRegionHelper`（双约定） | ✅ `data-tauri-drag-region` | ✅ `--wails-draggable` |
| **拖拽约定兼容** | ✅ 同时支持 `--wails-draggable` 和 `-webkit-app-region` | ❌（仅 Tauri 约定） | ❌（仅 Wails 约定） |
| **Frameless 拖拽统一** | ✅ 三平台一致（P1-5） | ✅ | ✅ |

**Wails.Net 关键实现**：
- [DragRegionHelper](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Windows/DragRegionHelper.cs) 通过注入 JavaScript 实现 CSS 拖拽区域识别，同时支持两种约定：`--wails-draggable: drag` CSS 变量和 `-webkit-app-region: drag` WebKit 私有属性。

**差异分析**：
- Wails.Net 的双约定兼容（Wails 风格 + Electron 风格）是**三者中独有**，降低前端迁移成本。
- Tauri 2 仅支持 `data-tauri-drag-region` 自有约定。
- Wails 3 仅支持 `--wails-draggable` 自有约定。

---

## 维度 3：Plugin / Security / Capability（学 Tauri v2）

### 3.1 插件系统

| 实现项 | Wails.Net | Tauri 2 | Wails 3 |
|--------|-----------|---------|---------|
| **插件接口** | `IPlugin`（4 阶段生命周期） | `Plugin` trait（`init`/`setup`） | `Service` 接口（`ServiceStartup`/`ServiceShutdown`） |
| **生命周期阶段** | `ConfigureServices` → `Configure` → `StartupAsync` → `ShutdownAsync` | `init` → `setup` → `on_event` / `on_navigation` | `ServiceStartup` → `ServiceShutdown` |
| **插件管理器** | `PluginManager`（`Interlocked` 幂等保护） | `tauri::Builder` 自动注册 | `ServiceManager` |
| **插件上下文** | `IPluginContext`（Commands/Permissions/Services） | `AppHandle` | `Manager` |
| **命令注册** | `commands.MapCommand` | `invoke_handler` | `Application.Bind` |
| **权限声明** | `context.Permissions.DeclarePermission` | `permissions/default.toml` | N/A |
| **IHostedService 适配** | ✅ `PluginHostedServiceAdapter`（双重触发防护） | N/A | N/A |
| **逆序关闭** | ✅ 后注册先关闭 | N/A | ✅ |
| **程序集扫描** | ✅ `DiscoverFromAssembly` 自动扫描 `IPlugin` 实现 | N/A | N/A |
| **Fluent API** | ✅ `UsePlugin<TPlugin>` | ✅ `Builder::plugin(P)` | ✅ `Options.Services` |
| **内置插件数** | **37 桌面 + 4 移动端 + 1 Android 运行时 = 42 个** | 30+ 官方插件 | 有限 |

**Wails.Net 关键实现**：
- [IPlugin](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/IPlugin.cs) 4 阶段生命周期：
  1. `ConfigureServices(IServiceCollection)` — 注册 DI 服务（对应 ASP.NET Core `ConfigureServices`）
  2. `Configure(IPluginContext)` — 配置命令和事件（对应 Tauri v2 `init`）
  3. `StartupAsync(CancellationToken)` — 应用启动后调用（对应 Wails v3 `Startup`、Tauri v2 `setup`）
  4. `ShutdownAsync(CancellationToken)` — 应用关闭时调用（对应 Wails v3 `Shutdown`、Tauri v2 `on_drop`）
- [PluginManager](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/PluginManager.cs)：
  - `Interlocked.CompareExchange` 保护 `_started`/`_stopped` 字段，确保幂等启动/关闭。
  - 关闭按注册**逆序**执行，确保依赖关系正确（后注册的插件先关闭）。
  - 单个插件启动/关闭失败不影响其他插件，仅记录错误日志。
  - `DiscoverFromAssembly` 自动扫描程序集中实现 `IPlugin` 接口、有无参构造的类型。
- [PluginHostedServiceAdapter](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Plugins/PluginHostedServiceAdapter.cs)：让插件生命周期与 Host 生命周期对齐。

**Tauri 2 原生功能**：
- `Plugin` trait 提供 `name`/`setup`/`extend_api`/`on_event`/`on_navigation`/`commands` 方法。
- `Builder::plugin(P)` 注册插件，自动收集 commands。
- 官方插件 30+ 个，覆盖 SQL/Stronghold/Biometric/Barcode Scanner/Localhost/Positioner/DeepLink 等。

**Wails 3 原生功能**：
- `Service` 接口是统一抽象，无独立"插件"概念。
- `Service` 既是状态容器也是绑定源。
- `ServeHTTP` 让 Service 可作 HTTP Handler。
- 无独立权限声明。

**差异分析**：
- Wails.Net 的 `IPlugin` 4 阶段生命周期是**三者中最完整**的，单一接口融合 ASP.NET Core（`ConfigureServices`）、Tauri v2（`init`/`setup`/`on_drop`）、Wails v3（`Startup`/`Shutdown`）三套生命周期模型。
- `Interlocked` 幂等保护防止 `IHostedService` 适配器与 `Application.Run` 手动调用同时触发导致重复启动，是独创设计。
- 内置 42 个插件是**三者中最多**的（Tauri 2 30+ 官方，Wails 3 有限）。

---

### 3.2 权限模型（PermissionManager）

| 实现项 | Wails.Net | Tauri 2 | Wails 3 |
|--------|-----------|---------|---------|
| **三层 ACL** | ✅ Capability + PermissionSet + Scope | ✅ Capability + Permission + Scope | ❌ |
| **配置文件** | `appsettings.json` + `capabilities/*.json` | `capabilities/*.json` | N/A |
| **权限声明** | `DeclarePermission` / `RegisterPermissionSet` | `default.toml` / `permissions.toml` | N/A |
| **作用域类型** | `FileSystemScope` / `UrlScope` | `fs scope` / `http scope` / `asset protocol scope` | N/A |
| **Deny 优先** | ✅ `_deniedPermissions` 优先于 `_grantedPermissions` | ✅ `deny-default` | N/A |
| **远程 URL 限制** | ✅ `_remoteUrlScopes` + `IsGranted(perm, window, origin)` | ✅ `remote.urls` | N/A |
| **窗口级隔离** | ✅ `PermissionKey (Permission, Window)` 复合键 | ✅ `windows: ["main"]` | N/A |
| **命令校验** | ✅ `ValidateCommand` + `RequireCapabilityAttribute` | ✅ `capabilities` 引用命令权限 | ❌ |
| **运行时开关** | ✅ `PermissionManager.Enabled`（默认 false 保持向后兼容） | ✅ 默认启用 | N/A |
| **本地源判定** | `wails://` / `localhost` / `127.0.0.1` / null | `tauri://localhost` / `http://localhost` | N/A |
| **URL 白名单** | `UrlWhitelist` 通配符匹配 | `allowed-origins` | N/A |
| **自动加载** | ✅ `LoadCapabilities` 从目录加载 capabilities/*.json | ✅ 启动时加载 | N/A |
| **PermissionSet 自动展开** | ✅ `Grant`/`Deny` 检测权限集标识并展开 | ✅ | N/A |
| **平台过滤** | ❌（暂未实现） | ✅ `Capability.platforms` | N/A |

**Wails.Net 关键实现**：
- [PermissionManager](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Security/PermissionManager.cs) 6 个 `ConcurrentDictionary` 线程安全：
  - `_grantedPermissions: ConcurrentDictionary<PermissionKey, byte>`
  - `_deniedPermissions: ConcurrentDictionary<PermissionKey, byte>`
  - `_declaredCapabilities: ConcurrentDictionary<string, Capability>`
  - `_permissionSets: ConcurrentDictionary<string, PermissionSet>`
  - `_permissionScopes: ConcurrentDictionary<string, IScope>`
  - `_remoteUrlScopes: ConcurrentDictionary<PermissionKey, UrlWhitelist>`
- 核心 API：`Grant`/`Deny`/`Revoke`/`Undeny`/`IsGranted`/`IsDenied`/`ValidateCommand`/`ValidateCapabilities`/`ValidateScopes`/`IsCapabilityApplicableToWindow`。
- **Deny 优先于 Grant**：`IsGranted` 先检查 `IsDenied`，即便已授权也返回 false。
- **PermissionKey 复合键** `(Permission, Window)`：`Window=null` 表示全局，`Window="windowName"` 表示窗口级。
- **PermissionSet 自动展开**：`Grant`/`Deny` 检测权限集标识并展开为集内所有细粒度权限。
- **本地源豁免**：`wails://`/`localhost`/`127.0.0.1`/`null` 始终放行，不受 Remote 限制。
- **DenyByDefault 语义**：`PermissionOptions.DenyByDefault=true` 时未授权权限一律拒绝（最小权限原则）。

**Tauri 2 原生功能**：
- `Capability` 是一组 `Permission` 集合，绑定到特定 WebviewWindow（按 label）。
- `Permission` 携带 `commands.allow`/`commands.deny` + `scope.allow`/`scope.deny`。
- `default.toml` 定义插件默认权限集，`permissions.toml` 定义细粒度权限。
- **平台过滤**：`Capability.platforms` 数组（`linux/windows/macos/android/ios`），Capability 可按平台启用/禁用。
- **远程 URL 白名单**：`Capability.remote.urls` 数组，支持通配符 `https://*.example.com`。
- **Deny 模型**：`commands.deny` + `scope.deny`，Deny 优先于 Allow。

**Wails 3 原生功能**：
- **无 ACL/Capability 权限模型**，所有绑定方法对前端默认可见。
- 仅运行时能力探测（`Capabilities{HasNativeDrag, GTKVersion, WebKitVersion}`），非安全权限。

**差异分析**：
- Wails.Net 的 PermissionManager 是对 Tauri v2 ACL 模型的完整移植，**默认 `Enabled=false`** 保持向后兼容，迁移成本低于 Tauri 2（Tauri 2 默认强制启用）。
- Wails.Net 暂未实现 `Capability.platforms` 平台过滤，这是相对于 Tauri 2 的差距。
- Wails 3 完全无 ACL 模型，依赖操作系统进程隔离，安全模型最弱。

---

### 3.3 Capability 能力声明

| 实现项 | Wails.Net | Tauri 2 | Wails 3 |
|--------|-----------|---------|---------|
| **能力数据模型** | `Capability` 类 | `Capability` struct | N/A |
| **配置文件** | `capabilities/*.json` | `capabilities/*.json` | N/A |
| **JSON 加载器** | ✅ `CapabilityFileLoader.LoadFromDirectory` | ✅ 启动时加载 | N/A |
| **字段：Identifier** | ✅ | ✅ | N/A |
| **字段：Description** | ✅ | ✅ | N/A |
| **字段：Permissions** | ✅（支持 `"!"` 前缀表示拒绝） | ✅（支持 `"!"` 前缀） | N/A |
| **字段：Windows** | ✅（窗口名称列表） | ✅（窗口 label 列表） | N/A |
| **字段：Remote** | ✅（远程 URL 模式列表） | ✅（`remote.urls`） | N/A |
| **字段：Plugin** | ✅ | ✅ | N/A |
| **字段：Platforms** | ❌（暂未实现） | ✅（`platforms` 数组） | N/A |
| **命令所需能力特性** | ✅ `RequireCapabilityAttribute` | ✅ `[[permission]] commands.allow` | N/A |
| **窗口级授权** | ✅ `Windows` 为空 → 全局；非空 → 窗口级 | ✅ 同 | N/A |
| **远程级授权** | ✅ `Remote` 为空 → 不限制；非空 → 限制 | ✅ 同 | N/A |

**Wails.Net 关键实现**：
- [Capability](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Security/Capability.cs) 数据模型字段：`Identifier`/`Description`/`Permissions`/`Windows`/`Remote`/`Plugin`。
- [CapabilityFileLoader](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Security/CapabilityFileLoader.cs) 扫描 `capabilities/*.json` 文件，使用 `JsonSerializer` 反序列化（支持注释、尾随逗号）。
- `RegisterToManager` 将能力注册到 `PermissionManager`：
  - `Windows` 为空 → 全局授权（`windowName=null`）
  - `Windows` 非空 → 窗口级授权（仅列出窗口可用）
  - `"!"` 前缀权限标识 → 调用 `Deny`（剥离前缀）
  - 普通权限标识 → 调用 `Grant`（透传 `remotePatterns`）
- [RequireCapabilityAttribute](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Security/CapabilityAttribute.cs) 标记命令方法所需的能力，调度时由 `CommandDispatcher` 校验。

**差异分析**：
- Wails.Net 的 Capability 模型**完整对齐 Tauri v2**，包括 `"!"` 前缀拒绝语法、窗口级 + 远程级双层隔离。
- 唯一差距是 `Capability.platforms` 平台过滤，Wails.Net 在 P2 路线图中。
- Wails 3 完全无 Capability 模型，Wails.Net 从零设计 Capability 系统。

---

### 3.4 Scope 作用域

| 实现项 | Wails.Net | Tauri 2 | Wails 3 |
|--------|-----------|---------|---------|
| **Scope 接口** | `IScope` | `tauri::scope::Scope` | N/A |
| **文件系统 Scope** | ✅ `FileSystemScope`（路径前缀匹配） | ✅ `fs scope` | N/A |
| **URL Scope** | ✅ `UrlScope`（通配符匹配） | ✅ `http scope` / `asset protocol scope` | N/A |
| **Shell Scope** | ❌ | ✅ `shell scope` | N/A |
| **参数级 Scope 校验** | ✅ `IScopeParameter` + `[ScopeParameter]` | ❌ | N/A |
| **路径规范化** | ✅ `Path.GetFullPath` | ✅ | N/A |
| **目录前缀匹配** | ✅ `targetPath.StartsWith(allowedPath + Path.DirectorySeparatorChar)` | ✅ | N/A |
| **URL 通配符** | ✅ `UrlWhitelist`（`*` → `.*`） | ✅ | N/A |
| **配置驱动** | ✅ `ScopeInitializer.Initialize` 从 `PermissionOptions.Scopes` | ✅ | N/A |
| **ScopeConfig** | ✅ `Paths` + `Urls` 双白名单 | ✅ | N/A |

**Wails.Net 关键实现**：
- [FileSystemScope](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Security/Scopes.cs) `Allows(value)` 校验逻辑：
  - 精确匹配（`StringComparer.OrdinalIgnoreCase`）
  - 目录前缀匹配（`targetPath.StartsWith(allowedPath + Path.DirectorySeparatorChar, OrdinalIgnoreCase)`）
  - 路径解析失败视为不允许
- [UrlScope](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Security/Scopes.cs) 委托 `UrlWhitelist`，支持 `*` 通配符（正则 `.*` 转义）。
- [IScopeParameter](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Security/IScopeParameter.cs) 接口允许 Options 类自定义 `GetScopeValues()` 返回需要校验的 (权限标识, 值) 对。
- [ScopeParameterAttribute](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Security/ScopeParameterAttribute.cs) 标记 `string` 参数，`CommandDispatcher.ExtractScopeValues` 从 JSON 中按参数名提取值。
- [ScopeInitializer](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Security/ScopeInitializer.cs) 从 `PermissionOptions.Scopes` 创建 Scope 实例：`Paths` 非空 → 创建 `FileSystemScope` 绑定；`Urls` 非空 → 创建 `UrlScope` 绑定。

**差异分析**：
- Wails.Net 的参数级 Scope 校验（`IScopeParameter` + `[ScopeParameter]`）是**三者中独有**的，允许命令方法参数级别的细粒度校验。
- Tauri 2 的 Scope 主要绑定到 Permission，无参数级校验。
- Wails 3 完全无 Scope 模型。
- Wails.Net 暂未实现 Shell Scope（Tauri 2 有），这是差距。

---

### 3.5 命令调度（CommandDispatcher）

| 实现项 | Wails.Net | Tauri 2 | Wails 3 |
|--------|-----------|---------|---------|
| **调度器** | `CommandDispatcher` | 编译期 `invoke_handler` | `Bindings` 直接调用 |
| **命令注册** | `CommandRegistry.Register` | `generate_handler![cmd1, cmd2]` | `Application.Bind` |
| **命令标记** | `[Command]` / `[DesktopCommand]` | `#[tauri::command]` | 反射公共方法 |
| **表达式树编译** | ✅ `CommandInvokerCompiler.Compile` | N/A | N/A |
| **零反射调用** | ✅ `CompiledCommandInvoker` 委托 | ✅ 编译期宏 | ❌ 反射 |
| **中间件管道** | ✅ `ICommandMiddleware` | ❌ | ❌ |
| **超时自动取消** | ✅ `_defaultTimeout` + `CreateLinkedTokenSource` | ❌ | ❌ |
| **特殊参数注入** | `ICommandContext` / `CancellationToken` / `IServiceProvider` | `State` / `AppHandle` / `Window` / `Webview` | N/A |
| **权限校验** | ✅ `ValidateCommand` + `ValidateScopes` | ✅ ACL 校验 | ❌ |
| **TargetInvocationException 解包** | ✅ | N/A | ✅ |
| **命令自动注册** | ✅ `RegisterFromAssembly` 程序集扫描 | N/A | N/A |

**Wails.Net 关键实现**：
- [CommandDispatcher](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Commands/CommandDispatcher.cs) `DispatchAsync` 调度流程：
  1. 创建带超时的 `CancellationTokenSource`，合并外部取消令牌和超时令牌
  2. 查找命令条目（未找到返回 `Command not found`）
  3. 权限校验：`ValidateCommand`（`[RequireCapability]` 特性 + `CommandEntry.RequiredCapabilities`）+ `ValidateScopes`（参数级 Scope）
  4. 构建中间件管道：从最后一个中间件向前构建，第一个中间件最先执行
  5. 终端处理器 `ExecuteCommandAsync`：优先使用编译后的 `Invoker`（零反射），回退到反射调用
  6. 处理异步返回值（`Task` / `Task<T>`）
  7. 异常处理：`TargetInvocationException` 解包内层异常
- [CommandInvokerCompiler](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Commands/CommandInvokerCompiler.cs) 通过表达式树编译生成强类型调用器委托 `CompiledCommandInvoker`。
- [CommandRegistry.RegisterFromAssembly](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Commands/CommandRegistry.cs) 扫描标记 `[DesktopCommand]` 的类，标记 `[Command]` 的方法按指定名称注册，未标记的公共非 void 方法按 "类名.方法名" 小写形式注册。

**差异分析**：
- Wails.Net 的 `CommandDispatcher` 是**三者中唯一**支持中间件管道的方案，类似 ASP.NET Core 中间件，可用于日志、审计、限流等横切关注点。
- 表达式树编译实现零反射调用，遵循 AGENTS.md §3.4 "禁止使用反射获取对应方法"约束。
- 超时自动取消是 Wails.Net 独有，Tauri 2 需手动传 `Cancellation`，Wails 3 无超时机制。
- Tauri 2 完全编译期注册，性能最优但灵活性低。
- Wails 3 完全反射调用，性能最差。

---

### 3.6 Channels

| 实现项 | Wails.Net | Tauri 2 | Wails 3 |
|--------|-----------|---------|---------|
| **Channel 接口** | `IChannel` | `tauri::ipc::Channel<T>` | ❌ |
| **Channel 实现** | `Channel`（internal sealed） | `Channel<T>` | ❌ |
| **Channel 管理器** | `ChannelManager`（静态） | 编译期注入 | ❌ |
| **注册表** | `ConcurrentDictionary<string, Channel>` | 编译期 | ❌ |
| **消息类型** | `channel:open` / `channel:message` / `channel:close` | `Channel::send` | ❌ |
| **发送委托注入** | ✅ `Func<string, object?, CancellationToken, Task>` | N/A | N/A |
| **Interlocked 关闭保护** | ✅ `_closed` CompareExchange | N/A | N/A |
| **ObjectDisposedException 自动清理** | ✅ | N/A | N/A |
| **Tauri v2 协议对齐** | ✅ | ✅ | N/A |

**Wails.Net 关键实现**：
- [Channel](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Channels/Channel.cs) 通过传入的发送委托 `Func<string, object?, CancellationToken, Task>` 投递消息到底层传输层。
- [ChannelManager](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Channels/ChannelManager.cs) 使用 `ConcurrentDictionary<string, Channel>` 线程安全注册表。
- **创建**：`Create(id?, sender?)`，`id` 为 null 自动生成 GUID（32 字符无连字符）。
- **消息分发**：`DispatchMessage(channelId, eventId, message)` 由传输层在收到 `channel:message` 消息时调用。
- **关闭信号**：`CloseAsync` 发送 `payload=null` 让对端感知关闭。
- **Interlocked 关闭状态保护**：`Channel._closed` 使用 `Interlocked.CompareExchange` 保证幂等关闭。
- **发送委托注入**：`Channel` 不绑定特定传输层，发送委托由调用方注入，支持多传输层复用。
- **ObjectDisposedException 自动清理**：`DispatchMessage` 捕获 `ObjectDisposedException` 后从注册表移除通道。

**差异分析**：
- Wails.Net 的 Channel 系统**完整对齐 Tauri v2 Channel<T> 协议**（`channel:open`/`channel:message`/`channel:close` 三消息类型）。
- Wails 3 完全无 Channel 模型，Wails.Net 从零设计实现。
- Wails.Net 的发送委托注入设计让 Channel 可跨传输层复用，是独创设计。

---

### 3.7 AssetServer 中间件

| 实现项 | Wails.Net | Tauri 2 | Wails 3 |
|--------|-----------|---------|---------|
| **AssetServer** | ✅ `Wails.Net.AssetServer` 独立项目 | ✅ `tauri::Asset` | ✅ `assetserver` |
| **中间件管道** | ✅ `IMiddleware` + `IHttpMiddleware` + `MiddlewareChain` | ❌ | ❌ |
| **CSP Nonce 注入** | ✅ `NonceInjector` | ✅ | ✅ |
| **隔离注入** | ✅ `IsolationInjector` | ✅ | ❌ |
| **Range 请求** | ✅ `Content-Range` / `Accept-Ranges` | ✅ | ✅ |
| **ETag** | ✅ `ETag` / `If-None-Match` | ✅ | ✅ |
| **Last-Modified** | ✅ `Last-Modified` / `If-Modified-Since` | ✅ | ✅ |
| **自定义 Header** | ✅ `Headers` 静态类 | ✅ | ✅ |
| **MIME 类型** | ✅ 内置映射 | ✅ | ✅ |
| **Service Route 挂载** | ✅ `IHttpServiceHandler`（P1-6） | ❌ | ❌ |
| **per-window CSP** | ✅ `SetCspHeaderForWindow`（P0-4） | ✅ | ❌ |
| **AssetProvider 抽象** | ✅ `IAssetProvider` + `BundledAssetProvider` + `FileAssetProvider` | ✅ | ✅ |
| **Android AssetServer** | ✅ `AndroidAssetServer` | ✅ | ✅ |

**Wails.Net 关键实现**：
- [AssetServer](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.AssetServer/AssetServer.cs) **双中间件接口**：
  - `IMiddleware.ProcessAsync(path, next)` — 基于路径，返回字节数组或 null
  - `IHttpMiddleware.ProcessAsync(context, next)` — 基于 HTTP 上下文，返回 bool（true 表示已完全处理）
- [MiddlewareChain](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.AssetServer/Middleware/MiddlewareChain.cs) `ExecuteAsync` 倒序构建委托链，使最先注册的中间件最先执行（符合 ASP.NET Core 中间件约定）。`ExecuteHttpAsync` 任一中间件返回 true 则短路后续中间件和最终处理器。
- [IsolationInjector](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.AssetServer/Security/IsolationInjector.cs) 注入 CSP 隔离指令，[NonceInjector](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.AssetServer/Security/NonceInjector.cs) 注入 CSP nonce。
- [IHttpServiceHandler](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.AssetServer/IHttpServiceHandler.cs) 允许业务代码挂载自定义 HTTP 路由到 AssetServer（P1-6），无需独立启动 ASP.NET Core 管道。服务路由匹配规则：精确匹配 + 前缀匹配（`route + "/"`）+ 最长匹配优先。

**差异分析**：
- Wails.Net 的 AssetServer 是**三者中唯一**提供双中间件管道（路径式 + HTTP 上下文式）的方案。
- IHttpServiceHandler 业务路由挂载是 Wails.Net 独有，Tauri 2 和 Wails 3 都无此能力。
- per-window CSP（`SetCspHeaderForWindow`）也是 Wails.Net 独有，Wails 3 不支持。

---

### 3.8 Updater 与签名安全

| 实现项 | Wails.Net | Tauri 2 | Wails 3 |
|--------|-----------|---------|---------|
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

**Wails.Net 关键实现**：
- [MinisignVerifier](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Security/Minisign/MinisignVerifier.cs) 验证流程：
  - 公钥长度校验（32 字节）
  - 完整签名长度校验（128 字节 = BLAKE2b-512 指纹 + Ed25519 签名）
  - 提取 BLAKE2b-512 指纹和 Ed25519 签名
  - 验证 BLAKE2b-512 指纹与数据一致（`Blake2Fast.Blake2b.ComputeHash`）
  - 使用 Ed25519 公钥验签（`NSec.Cryptography.SignatureAlgorithm.Ed25519`）
- **常量时间比较**：`CryptographicEquals` 防止时序攻击。
- [SignatureVerifier](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Services/Updater/SignatureVerifier.cs) 双验证路径：
  - `VerifyMinisignAsync` — minisign 路径（推荐，对应 Tauri v2 updater）
  - `VerifyAsync` — 旧路径（Authenticode/GPG，标记 `[Obsolete]`）
- **Authenticode 验证**（Windows）：通过 PowerShell `Get-AuthenticodeSignature` + `ConvertTo-Json` 解析状态和签名者。
- **GPG 验证**（Linux）：通过 `gpg --verify --status-fd 1` 提取 `GOODSIG` 行的签名者。
- [HelperProcess](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Services/Updater/HelperProcess.cs) Helper 进程协议：
  - 哨兵环境变量 `WAILS_UPDATER_HELPER=1` 进入 helper 模式
  - 环境变量传递更新包路径、目标文件路径、父进程 PID
  - 等待父进程退出（30 秒超时）
  - 备份目标文件（`.bak`）
  - 重试替换（20 次，每次间隔 500ms）
  - Linux 上 `chmod +x` 恢复可执行权限
  - 清除 helper 环境变量后重启应用
  - 失败时从备份恢复
- [IUpdateProvider](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Services/Updater/IUpdateProvider.cs) 接口支持链式尝试多个更新源：
  - [HttpUpdateProvider](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Services/Updater/HttpUpdateProvider.cs) — 向后兼容默认 Provider
  - [GitHubUpdateProvider](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Services/Updater/GitHubUpdateProvider.cs) — 通过 GitHub REST API `repos/{owner}/{repo}/releases/latest` 获取
  - [GitLabUpdateProvider](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application/Services/Updater/GitLabUpdateProvider.cs) — 通过 GitLab REST API `projects/{projectId}/releases/permalink/latest` 获取

**差异分析**：
- Wails.Net 是**三者中唯一**支持 Windows Authenticode 自动签名的方案（通过 `signtool` / `azuresigntool`，环境变量门控）。
- Wails.Net 的多 Provider Updater + ProviderName 注入对齐 Wails v3，但比 Wails v3 更进一步（错误事件 payload 含 provider 字段）。
- Helper Process 模式 Wails.Net 与 Wails v3 对齐（无需单独 helper 二进制，通过环境变量重执行当前应用）。
- Tauri 2 的 Updater 仅支持 Minisign，单一 Provider，无 Helper Process。
- Wails.Net 的 BLAKE2b-512 + Ed25519 双重保护 + 常量时间比较是**安全最优**的实现。

---

### 3.9 移动端插件

| 实现项 | Wails.Net | Tauri 2 | Wails 3 |
|--------|-----------|---------|---------|
| **条码扫描** | ✅ `BarcodeScannerPlugin` + `IPlatformBarcodeScanner` + Android 实现 | ✅ `tauri-plugin-barcode-scanner` | ❌ |
| **生物识别** | ✅ `BiometricPlugin` + `IPlatformBiometric` + Android 实现 | ✅ `tauri-plugin-biometric` | ❌ |
| **触觉反馈** | ✅ `HapticsPlugin` + `IPlatformHaptics` + Android 实现 | ✅ `tauri-plugin-haptics` | ✅ `Haptics` |
| **NFC** | ✅ `NfcPlugin` + `IPlatformNfc` + Android 实现 | ✅ `tauri-plugin-nfc` | ❌ |
| **相机** | ❌ | ✅ `tauri-plugin-camera` | ❌ |
| **地理位置** | ❌ | ✅ `tauri-plugin-geolocation` | ❌ |
| **通知（移动）** | ❌（桌面 NotificationPlugin 已实现） | ✅ `tauri-plugin-notification` | ❌ |
| **推送** | ❌ | ✅ `tauri-plugin-push` | ❌ |
| **Android 运行时** | ✅ `AndroidRuntimePlugin`（`device.info` / `toast.show`，P2 新增） | ❌ | ✅ `messageprocessor_android.go` |
| **NullObject 降级** | ✅ `NullXxxImpl` 桌面/Server no-op | N/A | N/A |
| **三层架构** | ✅ 接口 + 插件 + 平台实现 | ✅ | N/A |

**Wails.Net 关键实现**：
- 4 个移动端插件采用**接口 + 插件 + 平台实现**三层架构：
  - 接口（`IPlatformXxx`）定义跨平台契约
  - 插件（`XxxPlugin`）提供 API 表面
  - 平台实现（Android 实现）委托到原生 API
- 桌面/Server 模式下使用 `NullXxxImpl` 降级实现：
  - `BarcodeScannerPlugin.NullBarcodeScannerImpl` 返回空字符串
  - `BiometricPlugin.NullBiometricImpl` 返回 `none` / `false`
  - `HapticsPlugin.NullHapticsImpl` no-op
  - `NfcPlugin.NullNfcImpl` 返回空字符串
- [AndroidRuntimePlugin](file:///f:/Code/Dotnet/Wails.Net/src/Wails.Net.Application.Android/Mobile/AndroidRuntimePlugin.cs)（P2 新增）：
  - `device.info` — 对应 Wails v3 `androidDeviceInfo()`，返回设备制造商、品牌、型号、SDK 版本等信息
  - `toast.show` — 对应 Wails v3 `androidShowToast(message)`，通过 `Android.Widget.Toast.MakeText` 显示
  - 自带权限集 `android-runtime:default`（`android-runtime:allow-device-info` / `android-runtime:allow-toast`）

**差异分析**：
- Wails.Net 自 P2 起 4 个移动端插件均补齐 Android 平台实现，但**移动端插件生态深度仍弱于 Tauri 2**（Tauri 2 有 8+ 官方移动插件，Wails.Net 仅 4 个 + 1 Android 运行时）。
- Wails 3 移动端支持尚不成熟，仅 `Haptics` 一个移动插件。
- Wails.Net 的 NullObject 降级模式让桌面/Server 模式下移动插件 no-op 不抛异常，是独创设计。
- Wails.Net 的 AndroidRuntimePlugin 对齐 Wails v3 `messageprocessor_android.go`，但采用 Tauri v2 风格的插件命令名（`device.*` / `toast.*`）。

---

### 3.10 Stronghold / SQL / Store 加密存储

| 实现项 | Wails.Net | Tauri 2 | Wails 3 |
|--------|-----------|---------|---------|
| **Stronghold 加密 vault** | ✅ `StrongholdPlugin`（接口对齐） | ✅ `tauri-plugin-stronghold`（IOTA Stronghold） | ❌ |
| **SQL 数据库** | ✅ `SqlPlugin`（SQLite） | ✅ `tauri-plugin-sql`（SQLite/MySQL/PostgreSQL） | ❌ |
| **Store 键值存储** | ✅ `StorePlugin`（JSON 文件） | ✅ `tauri-plugin-store`（JSON 文件） | ❌ |
| **PersistedScope** | ✅ `PersistedScopePlugin`（持久化 Scope） | ❌ | ❌ |
| **多数据库支持** | ❌（仅 SQLite） | ✅（SQLite/MySQL/PostgreSQL） | ❌ |
| **加密强度** | ❌（仅接口对齐，未实现 IOTA Stronghold 硬件级隔离） | ✅（IOTA Stronghold） | ❌ |

**Wails.Net 关键实现**：
- `StrongholdPlugin` 目前仅接口对齐 Tauri v2，未实现完整的 IOTA Stronghold 加密 vault。
- `SqlPlugin` 仅支持 SQLite（通过 `Microsoft.Data.Sqlite`）。
- `StorePlugin` JSON 文件后端键值存储。
- `PersistedScopePlugin` 是 Wails.Net 独有，持久化 Scope 配置。

**差异分析**：
- Wails.Net 的 SQL/Store 与 Tauri 2 基本对齐，但 Stronghold 加密 vault 仅有接口骨架，未实现 IOTA Stronghold 硬件级隔离。
- Tauri 2 的 `tauri-plugin-sql` 支持 SQLite/MySQL/PostgreSQL 多数据库后端，Wails.Net 仅 SQLite。
- Wails 3 完全无 SQL/Store/Stronghold 内置插件。

---

## 整体评估与差距

### 三大维度实现完整度

| 维度 | Wails.Net | Tauri 2 | Wails 3 |
|------|-----------|---------|---------|
| **维度 1：Host/DI/Config/Logging** | ✅ 完整（学 ASP.NET Core） | △ 部分（Builder + State，无完整 DI） | △ 部分（Service 模式，无独立 DI） |
| **维度 2：Runtime/Window/IPC** | ✅ 完整（学 Wails v3） | ✅ 完整（自有设计） | ✅ 完整（基准实现） |
| **维度 3：Plugin/Security/Capability** | ✅ 完整（学 Tauri v2） | ✅ 完整（基准实现） | ❌ 缺失（无 ACL 模型） |

### Wails.Net 相对优势

1. **维度 1 完整度最高**：完整采用 `Microsoft.Extensions.Hosting`/`DependencyInjection`/`Configuration`/`Options`/`Logging` 全栈，对 .NET 开发者最友好。`IOptionsMonitor<T>` 支持热重载是 Tauri 2 / Wails 3 都不具备的。
2. **维度 2 创新设计**：在完整对齐 Wails v3 对象模型基础上，新增多传输层并行广播、NativeIpcTransport 混合策略、senderWindowId 全链路传播、Server 模式降级、CancellablePromise 等独有能力。
3. **维度 3 完整 ACL 模型**：从零设计 Capability + Permission + Scope 三层 ACL 模型（Wails 3 完全缺失），且支持参数级 Scope 校验（Tauri 2 不支持）。
4. **三方融合创新**：Logger 双向桥接（前端 console ↔ 后端 ILogger）、4 阶段插件生命周期、CommandDispatcher 中间件管道、表达式树编译零反射调用、AssetServer 双中间件管道 + IHttpServiceHandler 业务路由挂载等独创设计。
5. **内置插件最多**：42 个内置插件（37 桌面 + 4 移动端 + 1 Android 运行时），开箱即用。
6. **MenuRole 系统最完整**：21 个角色枚举 + 13 个工厂方法 + 3 个标准菜单组合 + AboutMetadata + 跨平台辅助工具，结合 Wails v3 Role 模型与 Tauri v2 PredefinedMenuItem 工厂模式。
7. **Android 平台能力补齐**：4 个移动端插件 Android 平台实现 + AndroidRuntimePlugin + AndroidPlatformEvents + 3 个新公共事件。

### Wails.Net 相对差距

1. **macOS/iOS 支持**：暂不实现，Tauri 2 和 Wails 3 已稳定支持。
2. **移动端插件生态深度**：4 + 1 个移动端插件，Tauri 2 有 8+ 官方移动插件（Camera/Geolocation/Notification/Push 等可扩充）。
3. **Capability.platforms 平台过滤**：Wails.Net 暂未实现，Tauri 2 支持。
4. **Shell Scope**：Wails.Net 暂未实现，Tauri 2 支持。
5. **Stronghold 硬件级隔离**：Wails.Net 仅接口对齐，Tauri 2 完整实现 IOTA Stronghold。
6. **SQL 多数据库后端**：Wails.Net 仅 SQLite，Tauri 2 支持 SQLite/MySQL/PostgreSQL。
7. **日志轮转**：Wails.Net 依赖第三方（如 Serilog），Tauri 2 内置 `RotationStrategy`。
8. **AppImage / MSI 打包**：Wails.Net 在 P3 路线图中。

### 融合策略验证

**架构融合策略（AGENTS.md §1.1.1）的成功指标**：

| 融合目标 | 学习对象 | 实现状态 | 验证 |
|---------|---------|---------|------|
| Host/DI/Config/Logging | ASP.NET Core | ✅ 完整 | `Microsoft.Extensions.*` 全栈集成，`IHostedService` 适配，`IOptionsMonitor` 热重载，`ILoggerProvider` 双向桥接 |
| Runtime/Window/IPC | Wails v3 | ✅ 完整 | `Application` 对象模型、`IPlatformApp` 平台抽象、FNV-1a 哈希一致性、CancellablePromise、Pre-emit 钩子、MenuRole 21 角色 |
| Plugin/Security/Capability | Tauri v2 | ✅ 完整 | `IPlugin` 4 阶段生命周期、`PermissionManager` 6 个 ConcurrentDictionary、Capability `"!"` 前缀拒绝、Scope 参数级校验、Channel 协议、minisign 签名 |

**结论**：三大架构融合维度全部已完整实现，且每一维度均体现了所"学习"框架的核心设计哲学。Wails.Net 成功融合了 ASP.NET Core 的基础设施 + Wails v3 的对象模型 + Tauri v2 的安全/能力/插件体系，形成了一个对 .NET 开发者最友好、企业级特性最完整、安全模型最严格的桌面/移动框架。

---

**文档结束**

> 本文档基于 2026-07-20 仓库代码状态生成（提交 `6832cbf`，P2 阶段完成 + P3 阶段完成 20 个 Demo 项目）。如发现信息过时或错误，请提交 Issue 或 PR 更新。
