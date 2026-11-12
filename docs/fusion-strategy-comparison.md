# Wails.Net 架构策略对比详情

> 本文档按 **架构策略**（AGENTS.md §1.1.1）的三大主线，详细对比 **Wails.Net**（当前项目）与 **Wails v3**（Go 参考实现，`v3.1.0`，2026-11-01 weekly）在功能实现项上的差别。
>
> - **三条主线**：
>   - 维度 1：Host/DI/Config/Logging → 参考 ASP.NET Core（Microsoft.Extensions.* 全栈）
>   - 维度 2：Runtime/Window/IPC → 参考 Wails v3（对象模型、IPC、多窗口、事件总线）
>   - 维度 3：Plugin/Security/Capability → **Wails.Net 自研能力模型**（插件契约与 Wails v3 对齐，权限模型、Capability 声明、Scope 校验由本项目自主设计）
> - **更新日期**：2026-11-12（文档体系收敛：移除外部框架对比基线，第三维度改为自研表述；正文基于当前 `src/` 状态）
> - **对比基线**：基于本仓库当前 `src/` 实际代码状态（提交 `6a812ef`；架构优化路线图 `docs/architecture/optimization-roadmap.md` 已落地 P0 批次：配置模型收敛、Options 启动期校验、IPC handshake token、权限 Strict 模式、`Capability.platforms`）
>
> ## 本文档事实来源（OPT-DOC-001）
>
> 本文档为**高层的两方能力对比**，不替代下述权威文档。以下文档随代码演进，内容不一致时以它们为准：
>
> - [Wails v3 beta 差异详情](../docs/development/wails-v3-beta-comparison.md) —— Wails v3 维度逐项差异（含 beta 行为变更）
> - [插件前后端一体发布](../docs/development/plugin-packaging.md) —— 插件双包模型（NuGet + npm）与发布流水线
> - [插件平台拆分](../docs/development/plugin-platform-split.md) —— 插件按平台拆分策略（桌面/移动端）
> - [安全与权限模型](../docs/architecture/security-and-permissions.md) —— 权限系统、Capability、Scope、IPC 安全设计
> - [架构优化修改路线图](../docs/architecture/optimization-roadmap.md) —— 优化项任务编号（`OPT-*`）与实施状态
>
> ## 文档维护规则（OPT-DOC-001）
>
> - 每次完成架构性功能后，**必须**更新本文档或在 PR 描述中说明「无需更新」及理由。
> - 文中所有「差距」条目须绑定 `OPT-*` 任务编号或明确标记「无对应任务」。
> - 代码链接使用**相对路径**（`../src/...`），禁止 Windows 绝对路径（`file:///f:/...`）。
> - 插件数量、平台状态等计数一律以仓库实际代码为准，不在此维护数字（避免漂移）。

---

## 目录

- [维度 1：Host / DI / Config / Logging（参考 ASP.NET Core）](#维度-1host--di--config--logging参考-aspnet-core)
  - [1.1 Host 模型](#11-host-模型)
  - [1.2 DI 容器](#12-di-容器)
  - [1.3 配置系统](#13-配置系统)
  - [1.4 日志系统](#14-日志系统)
  - [1.5 选项模式](#15-选项模式)
  - [1.6 生命周期钩子](#16-生命周期钩子)
- [维度 2：Runtime / Window / IPC（参考 Wails v3）](#维度-2runtime--window--ipc参考-wails-v3)
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
- [维度 3：Plugin / Security / Capability（自研能力模型）](#维度-3plugin--security--capability自研能力模型)
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

## 维度 1：Host / DI / Config / Logging（参考 ASP.NET Core）

### 1.1 Host 模型

| 实现项 | Wails.Net | Wails v3 |
|--------|-----------|---------|
| **应用入口** | `DesktopApplicationBuilder` + `DesktopApplication`（封装 `HostApplicationBuilder` + `IHost`） | `application.New(Options).Run()` |
| **底层 Host** | `Microsoft.Extensions.Hosting.HostApplicationBuilder` | Go main goroutine + 平台事件循环 |
| **Builder 模式** | ✅ Fluent API（`UsePlugin<TPlugin>`、`AddWails`） | ❌ 工厂函数 `New(Options)` |
| **主线程模型** | 专用 **STA 线程**运行 UI 主循环 | 平台主线程（GTK/Win32） |
| **应用句柄** | `Application` 单例（DI 注入） | `*Application`（指针） |
| **跨线程访问** | `Application.DispatchOnMainThread(action)` | `Application.DispatchOnMainThread` |
| **异常兜底** | ✅ `AppDomain.UnhandledException` + `TaskScheduler.UnobservedTaskException` | ❌ 无统一兜底 |
| **退出码** | ✅ `RunAsync` 返回 int 退出码 | ❌ 仅 error |

**Wails.Net 关键实现**：
- [DesktopApplicationBuilder](../src/Wails.Net.Application/Hosting/DesktopApplicationBuilder.cs)：在构造时创建 `HostApplicationBuilder`，注册 `DesktopHostOptions`（绑定 "Wails" 节）、`ApplicationOptions` 工厂单例（`IOptionsMonitor<ApplicationOptions>` 支持热重载）、`Application` 单例、`DesktopHostedService`、`PluginHostedServiceAdapter`。
- [DesktopHostedService](../src/Wails.Net.Application/Hosting/DesktopHostedService.cs)：`StartAsync` 在专用 STA 线程运行 `Application.Run()`，`StopAsync` 触发 `Shutdown` 并 `Join` UI 线程 5 秒。
- **Build() 初始化顺序**：插件 `InitializeAll` → 创建 `CommandDispatcher` → 自动创建 `FileAssetServer`（若配置 `AssetsDirectory`）→ `LoadCapabilities` 自动加载 → `SetWindowConfigs` 延迟窗口创建。


**Wails v3 原生功能**：
- `application.New(Options)` 工厂构造，`Options` 携带 `Name`/`Services`/`Logger`/`Bindings` 等。
- `App.Run()` 启动平台事件循环。
- 信号处理器自动捕获 SIGINT/SIGTERM，触发 `OnShutdown`。

**差异分析**：
- Wails.Net 是**两者中唯一**采用 `IHost` 抽象的方案，原生支持 `IHostedService`、`IHostApplicationLifetime`、`IHostEnvironment`，可无缝接入 ASP.NET Core 生态（如 `Microsoft.Extensions.Hosting.WindowsServices` 可让 Wails 应用作为 Windows 服务运行）。
- Wails v3 是"自托管"模型，不依赖外部 Host 抽象。
- Wails.Net 的 STA 线程模型对 Win32 GUI 友好；Wails v3 直接复用平台主线程。

---

### 1.2 DI 容器

| 实现项 | Wails.Net | Wails v3 |
|--------|-----------|---------|
| **DI 容器** | ✅ `Microsoft.Extensions.DependencyInjection` | ❌ 无独立容器，Service 模式 |
| **服务注册** | `services.AddSingleton<TService, TImpl>()` | `Options.Services []Service` |
| **生命周期** | Singleton / Transient / Scoped 三种 | 单例（Service 实例） |
| **构造函数注入** | ✅ `ActivatorUtilities.CreateInstance` | ❌ 手动 struct 字段 |
| **服务定位** | `IServiceProvider.GetService<T>()` | `app.GetService(name)` |
| **插件 DI 集成** | ✅ `IPlugin.ConfigureServices(IServiceCollection)` | ✅ `Service` 即绑定即注入 |
| **管理器单例** | EventProcessor / BindingManager / WindowManager 等全部单例 | 通过 `App` 字段共享 |
| **命令容器 DI** | ✅ `[DesktopCommand]` 标记类自动 DI | ❌ Service 实例方法 |

**Wails.Net 关键实现**：
- [ServiceCollectionExtensions](../src/Wails.Net.Application/Hosting/ServiceCollectionExtensions.cs) 提供 `AddWailsManagers`（注册 5 个核心管理器）+ `AddWailsServices`（注册 6 个内置服务）+ `AddWailsLogging`（注册 `LogServiceLoggerProvider`）三个扩展方法，`AddWails` 是一站式入口。
- [CommandRegistry.RegisterFromAssembly](../src/Wails.Net.Application/Commands/CommandRegistry.cs)：使用 `ActivatorUtilities.CreateInstance` 创建标记 `[DesktopCommand]` 的命令容器，支持构造函数注入任意 DI 服务。


**Wails v3 原生功能**：
- 通过 `Options.Services` 注册 `Service` 实例。
- 无独立 DI 容器，Service 既是状态容器也是绑定源。
- 依赖关系通过 struct 字段手动注入。

**差异分析**：
- Wails.Net 是**两者中唯一**提供完整 DI 容器（Singleton/Transient/Scoped 三种生命周期）+ 构造函数注入 + 服务定位的方案。
- Wails v3 完全无 DI 抽象，依赖 Go struct 字段。
- Wails.Net 的 DI 集成让插件可通过 `IPlugin.ConfigureServices` 注入自身服务，与 ASP.NET Core `IStartup.ConfigureServices` 模式一致。

---

### 1.3 配置系统

| 实现项 | Wails.Net | Wails v3 |
|--------|-----------|---------|
| **配置文件** | ✅ `appsettings.json` + 环境变量 + 命令行 | ❌ 无内置配置文件 |
| **配置抽象** | `IConfiguration` | `Options` struct |
| **平台覆盖** | ✅ 环境变量覆盖 | ❌ |
| **热重载** | ✅ `IOptionsMonitor<T>` + `IOptionsSnapshot<T>` | ❌ |
| **强类型选项** | ✅ `IOptions<T>` 绑定 | ✅ struct |
| **命名选项** | ✅ `IOptionsMonitor<T>.Get(name)` | ❌ |
| **配置根节** | 统一 "Wails" 节 | N/A |
| **插件配置** | ✅ 各插件 `ConfigureServices` 内读取 | ✅ `Options.Services` 字段 |
| **启动期校验** | ✅（OPT-CONFIG-002：`DesktopHostOptionsValidator` / `PermissionOptionsValidator` / `IpcOptionsValidator` + `ValidateOnStart()`，错误在启动期暴露并指出配置路径） | ❌ |

**Wails.Net 关键实现**：
- [DesktopHostOptions](../src/Wails.Net.Application/Hosting/DesktopHostOptions.cs)：宿主层配置（`ApplicationName`/`Window`/`AssetsDirectory`（兼容字段，OPT-CONFIG-001 推荐 `Assets.RootPath`）/`DevServerUrl`/`SingleInstance`/`Permissions`（兼容字段，推荐 `PermissionOptions`）/`Assets`/`App`）。
- [ApplicationOptions](../src/Wails.Net.Application/Options/ApplicationOptions.cs)：对应 Wails v3 `application_options.go`，包含 `OnStartup`/`OnShutdown`/`PostShutdown`/`ShouldQuit` 钩子、`ShutdownTasks`、`Capabilities`、`Csp`、`AllowedUrls`。
- [WebviewWindowOptions](../src/Wails.Net.Application/Options/WebviewWindowOptions.cs)：窗口选项，支持 `Csp` 窗口级覆盖。
- [PermissionOptions](../src/Wails.Net.Application/Security/PermissionOptions.cs)：权限配置（`Mode`/`Permissions`/`Enabled`/`DenyByDefault`/`Scopes`/`CapabilitiesDirectory`）。
- [IpcOptions](../src/Wails.Net.Application/Security/IpcOptions.cs)：IPC 传输层安全配置（`AllowHttpFallback`/`RequireHandshakeToken`/`RequireOriginValidation`/端口范围/`AllowedOrigins`）。
- 所有配置类绑定到 "Wails" 根节下的子节（如 `Wails:Permissions`/`Wails:Assets`/`Wails:App`/`Wails:Ipc`），避免与 ASP.NET Core 标准节（`Logging`/`Kestrel`）冲突。


**Wails v3 原生功能**：
- 不内置 config 文件系统，所有配置通过代码内 `Options` 结构传递。
- 可结合 `viper`/`envconfig` 等第三方库实现配置加载。

**差异分析**：
- Wails.Net 复用 `Microsoft.Extensions.Configuration` 抽象，**原生支持 JSON / 环境变量 / 命令行 / XML / INI 多种源**，可叠加自定义 `IConfigurationSource`。
- Wails v3 完全无内置配置系统，是最弱的方案。
- Wails.Net 的 `IOptionsMonitor<T>` 支持**运行时热重载**，是两者中独有的能力。

---

### 1.4 日志系统

| 实现项 | Wails.Net | Wails v3 |
|--------|-----------|---------|
| **日志抽象** | ✅ `ILogger<T>` + `ILoggerProvider` | `slog.Logger` |
| **日志类别** | ✅ `ILogger<T>` 泛型类别 | ✅ `logger.WithGroup/WithAttr` |
| **日志过滤** | ✅ `AddFilter` + 配置 `Logging:LogLevel` | ✅ `LoggerLevel` |
| **多目标输出** | ✅ 多 `ILoggerProvider` 注册 | ✅ `slog.Handler` 多路 |
| **日志轮转** | ❌（依赖第三方如 Serilog） | ❌ |
| **前端 console 桥接** | ✅ **双向**（接收 + 转发） | ❌ |
| **日志范围** | ✅ `ILogger.BeginScope` | ✅ `slog` scope |
| **结构化日志** | ✅ `LoggerExtensions` 字典参数 | ✅ `slog` kv |
| **AsyncLocal 防递归** | ✅ `_isWriting` | ❌ |

**Wails.Net 关键实现**：
- [LogServiceLoggerProvider](../src/Wails.Net.Application/Logging/LogServiceLoggerProvider.cs)：`ILoggerProvider` 实现，桥接 `ILogger<T>` → `LogService`，使用 `AsyncLocal<bool> _isWriting` 防止日志写入触发新的日志导致无限递归。
- [BrowserConsoleLogForwarder](../src/Wails.Net.Application/Logging/BrowserConsoleLogForwarder.cs)：反向桥接，将 `LogService` 输出推送到前端 `console`，使用 `AsyncLocal<bool> _isForwarding` + 检查 `source=browser` 字段防止前端→后端→前端的循环转发。
- **完整链路**：`ILogger<T>`（.NET 标准）↔ `LogService`（Wails 服务）↔ 前端 `console`（开发者工具）。


**Wails v3 原生功能**：
- 默认使用 Go 1.21+ 的 `slog` 标准库。
- `Application` 持有 `Logger *slog.Logger`，提供 `debug/info/warn/error` 辅助方法。
- 无前端 console 桥接。

**差异分析**：
- Wails.Net 是**两者中唯一**支持日志双向桥接的方案：前端 `console.*` 自动进入 .NET 日志管道，后端 `ILogger` 输出反向转发到前端 DevTools。
- Wails v3 完全无日志桥接能力。
- Wails.Net 的 `AsyncLocal` 防递归机制解决了双向桥接的核心难题。

---

### 1.5 选项模式

| 实现项 | Wails.Net | Wails v3 |
|--------|-----------|---------|
| **选项接口** | ✅ `IOptions<T>` / `IOptionsMonitor<T>` / `IOptionsSnapshot<T>` | ❌（直接 struct） |
| **热重载** | ✅ `IOptionsMonitor<T>.OnChange` | ❌ |
| **命名选项** | ✅ `IOptionsMonitor<T>.Get(name)` | ❌ |
| **配置回调覆盖** | ✅ `Configure<T>(Action<T>)` | ❌ |
| **选项校验** | ✅ `IValidateOptions<T>` + `ValidateOnStart()`（OPT-CONFIG-002：`DesktopHostOptionsValidator` / `PermissionOptionsValidator` / `IpcOptionsValidator`） | ❌ |

**Wails.Net 关键实现**：
- `ApplicationOptions` 通过 `IOptionsMonitor<ApplicationOptions>` 注册为工厂单例，支持运行时热重载。
- `PermissionManager` 通过构造函数注入 `IOptions<PermissionOptions>` 获取配置。
- `PermissionServiceExtensions.AddPermissions(Action<PermissionOptions>?)` 支持代码层覆盖配置文件。
- `DesktopApplicationBuilder.ConfigureServices` / `AddWailsManagers` 中注册三个 Options validator 并启用 `ValidateOnStart()`，配置错误在 Host 启动期抛出 `OptionsValidationException`，错误消息包含具体配置路径（如 `Wails:Window:Width`）。

**Wails v3 原生功能**：无选项模式抽象，直接使用反序列化的 struct。

**差异分析**：
- Wails.Net 完整复用 ASP.NET Core 选项模式，**支持热重载 + 命名选项 + 配置回调覆盖 + 选项校验（启动期）**，这是 Wails v3 不具备的。

---

### 1.6 生命周期钩子

| 钩子 | Wails.Net | Wails v3 |
|------|-----------|---------|
| **应用启动前** | `IServiceStartup.ServiceStartup` | `Options.OnBeforeStart` |
| **应用启动后** | `Application.OnStartup` + `OnAfterStart` | `Options.OnStartup` |
| **应用关闭前** | `Application.OnShutdown` | `Options.OnShutdown` |
| **应用关闭后** | `Application.PostShutdown` | `Options.PostShutdown` |
| **是否允许退出** | `ApplicationOptions.ShouldQuit` 回调 | `Options.ShouldQuit` |
| **窗口事件** | `WindowManager.WindowCreated/Closed` | `WindowEventType` 常量 |
| **应用恢复（移动端）** | ❌（暂未实现） | ❌ |
| **服务逆序关闭** | ✅ `IServiceShutdown` 逆序 | ✅ `ServiceShutdown` 逆序 |
| **ShutdownTasks** | ✅ `ApplicationOptions.ShutdownTasks` | ✅ |

**Wails.Net 关键实现**：
- [Application.Run()](../src/Wails.Net.Application/Application.cs) 执行顺序：`SetupSingleInstance` → `OnBeforeStart` 钩子 → `IServiceStartup.ServiceStartup` → `AssetServer` 启动 → `Transport.StartAsync` → `InitializeNativeIpcTransport` → `OnStartup` → `CreateWindowsFromConfig` → `OnAfterStart` → `PluginManager.StartupPluginsAsync` → `PlatformApp.Run` → `Shutdown`。
- [Application.Shutdown()](../src/Wails.Net.Application/Application.cs) 逆序关闭：`_lifetime.StopApplication` → `_cts.Cancel` → `ShutdownTasks` → `OnShutdown` → 关闭所有窗口 → `Transport.StopAsync` → `AssetServer.ServiceShutdown` → `PluginManager.ShutdownPluginsAsync` → 逆序 `IServiceShutdown.ServiceShutdown` → `PlatformApp.Destroy` → `PostShutdown`。

**差异分析**：
- Wails.Net 完整对齐 Wails v3 的 `OnBeforeStart`/`OnStartup`/`OnShutdown`/`PostShutdown`/`ShouldQuit` 五钩子模型。
- Wails.Net 的 `IServiceStartup`/`IServiceShutdown` 接口提供独立的"服务协调"层（与 `IHostedService` 并列），是独创设计。

---

## 维度 2：Runtime / Window / IPC（参考 Wails v3）

### 2.1 Application 对象模型

| 实现项 | Wails.Net | Wails v3 |
|--------|-----------|---------|
| **核心类** | `Application`（约 1500 行） | `application.Application` |
| **工厂构造** | DI 单例（`Application` 通过 `AddWails` 注册） | `application.New(Options)` |
| **主循环** | `Application.Run()` 委托 `IPlatformApp.Run` | `App.Run()` |
| **退出方法** | `Application.Quit()` / `Shutdown()` | `App.Quit()` |
| **获取实例** | `Application.Get()` 静态访问器 | `*Application` 指针 |
| **窗口管理器** | `WindowManager`（DI 注入） | `App.Window` |
| **事件管理器** | `EventProcessor`（DI 注入） | `App.Event` |
| **菜单管理器** | `MenuManager`（DI 注入） | `App.Menu` |
| **对话框管理器** | `DialogManager`（DI 注入） | `App.Dialog` |
| **屏幕管理器** | `ScreenManager`（DI 注入） | `App.Screen` |
| **剪贴板管理器** | `ClipboardManager`（DI 注入） | `App.Clipboard` |
| **键绑定管理器** | `KeyBindingManager`（DI 注入） | N/A |
| **浏览器管理器** | `IBrowserManager`（DI 注入） | `App.Browser` |

**Wails.Net 关键实现**：
- [Application](../src/Wails.Net.Application/Application.cs) 是核心应用类，对应 Wails v3 `Application`，但额外集成了 DI 容器访问、生命周期管理、传输层协调、插件管理器调度。
- 所有管理器（WindowManager / EventProcessor / MenuManager / DialogManager / ScreenManager / ClipboardManager / KeyBindingManager）通过 DI 注入，符合 ASP.NET Core 模式。

**差异分析**：
- Wails.Net 的 `Application` 类**同时融合了** Wails v3 的 `Application` 对象模型 + ASP.NET Core 的 DI 容器访问。
- Wails.Net 用 `Application.Get()` 静态访问器 + DI 注入双路径获取应用实例。
- Wails v3 的 `Application` 字段直接持有各 Manager，无 DI 解耦。

---

### 2.2 平台抽象与 Server 模式

| 实现项 | Wails.Net | Wails v3 |
|--------|-----------|---------|
| **平台抽象接口** | `IPlatformApp` | `PlatformApp` 接口 |
| **平台实现** | Win32PlatformApp / LinuxPlatformApp / AndroidPlatformApp | platform_windows.go / platform_linux.go / platform_darwin.go |
| **Server 模式降级** | ✅ `ServerPlatformApp` + `ServerWebviewWindow` + `ServerClipboard` + `ServerBrowserManager` | ✅ Headless 模式 |
| **阻塞模型** | `ManualResetEventSlim` | `ManualResetEvent` |
| **GUI 操作 no-op** | ✅ 全部 no-op 不抛异常 | ✅ |
| **对话框默认值** | ✅ 返回首个按钮 / null | ✅ |
| **单实例锁** | ✅ 始终返回 true（视作首实例） | ✅ |
| **Destroy 可重写** | ✅ `virtual` 修饰符（P1-7） | N/A |

**Wails.Net 关键实现**：
- [IPlatformApp](../src/Wails.Net.Application/Platform/IPlatformApp.cs) 接口含大量默认 no-op 实现（C# 8.0 默认接口方法），包括 `Run`/`AcquireSingleInstanceLock`/`NotifySingleInstance`/`Destroy`/`SetApplicationMenu`/`GetCurrentWindowId`/`SetParent`/`ShowAboutDialog`/`SetIcon`/`Show`/`Hide`/`GetPrimaryScreen`/`GetScreens`/`IsDarkMode`/`GetAccentColor`/`DispatchOnMainThread`/`CreateWebviewWindow`/`ShowMessageDialog`/`OpenFileDialog`/`SaveFileDialog`/`OpenMultipleFilesDialog`。
- [ServerPlatformApp](../src/Wails.Net.Application/Platform/ServerMode/ServerPlatformApp.cs) 使用 `ManualResetEventSlim` 阻塞 `Run()`，所有 GUI 操作 no-op。
- `Destroy()` 改为 `virtual` 修饰符（P1-7），支持单元测试 override 验证 Shutdown 流程。

**差异分析**：
- Wails.Net 的 Server 模式是**两者中最完整**的无 GUI 降级方案，提供与桌面模式完全一致的 API 表面，适用于容器化部署、自动化 UI 测试、服务端预渲染。
- Wails v3 的 Headless 模式功能类似，但 API 表面不如 Wails.Net 完整（缺 `ServerBrowserManager` 等）。
- Wails.Net 的 `IPlatformApp` 默认接口方法降低平台移植成本，新平台只需实现必要方法。

---

### 2.3 窗口管理

| 实现项 | Wails.Net | Wails v3 |
|--------|-----------|---------|
| **窗口抽象接口** | `IWebviewWindowImpl`（30+ 方法） | `IWebviewWindowImpl` 接口 |
| **公共 API 类** | `WebviewWindow`（委托 IWebviewWindowImpl） | `WebviewWindow` |
| **窗口管理器** | `WindowManager`（`Interlocked.Increment` ID） | `WindowManager` |
| **多窗口** | ✅ `ConcurrentDictionary<uint, WebviewWindow>` | ✅ 按 name/id 索引 |
| **窗口名↔ID 映射** | ✅ 双字典 | ✅ |
| **Frameless** | ✅ | ✅ |
| **Transparent** | ✅ | ✅ |
| **AlwaysOnTop** | ✅ | ✅ |
| **DevTools** | ✅ F12 / `OpenDevTools` | ✅ |
| **Zoom** | ✅ `SetZoom/ZoomIn/ZoomOut/ZoomReset` | ✅ |
| **打印** | ✅ `Print/PrintToPDF` | ✅ |
| **ExecJS / InjectCSS** | ✅ | ✅ |
| **文件拖放** | ✅ `WM_DROPFILES` (Win) | ✅ |
| **DPI 适配** | ✅ `WM_DPICHANGED` (Win) | ✅ |
| **窗口级 CSP** | ✅ `SetCspHeaderForWindow`（P0-4） | ❌ |
| **任务栏进度** | ✅ `SetTaskbarProgress` / `SetOverlayIcon` | ✅ |
| **窗口效果** | ✅ `SetEffects`（Mica/Acrylic） | ✅ |
| **Badge** | ✅ `SetBadgeCount/SetBadgeLabel` | ✅ |
| **多工作区** | ✅ `SetVisibleOnAllWorkspaces` | ✅ |
| **边框颜色** | ✅ `SetBorderColor` | ✅ |
| **内容保护** | ✅ `SetContentProtection` | ✅ |
| **Win32 消息处理** | ✅ 18+ 消息（WM_DESTROY/CLOSE/SIZE/COMMAND/SYSCOMMAND/GETMINMAXINFO/DPICHANGED/HOTKEY/DROPFILES/SETTINGCHANGE/MOVE/NCLBUTTONDOWN/SETICON/ACTIVATE/DISPLAYCHANGE/CLIPBOARDUPDATE/KEYDOWN/CONTEXTMENU） | N/A |

**Wails.Net 关键实现**：
- [IWebviewWindowImpl](../src/Wails.Net.Application/Windows/IWebviewWindowImpl.cs) 接口大量方法有默认 no-op 实现（C# 8.0 默认接口方法）：`SetBackgroundType`/`SetFullscreenButtonEnabled`/`SetZoom`/`SetTranslucent`/`SetOpacity`/`GetOpacity`/`InjectCSS`/`ZoomIn`/`ZoomOut`/`OpenContextMenu`/`PrintToPDF`/`Run`/`CapturePreviewAsync`/`RegisterCustomScheme`/`SetTaskbarProgress`/`SetOverlayIcon`/`SetSkipTaskbar`/`SetIgnoreCursorEvents`/`SetEffects`/`SetBadgeCount`/`SetBadgeLabel`/`SetVisibleOnAllWorkspaces`/`SetBorderColor`/`SetFileDropEnabled`/`SetNativeMessageHandler`/`SetConsoleMessageHandler`/`PostNativeMessageAsync`。
- [WindowManager](../src/Wails.Net.Application/Managers/WindowManager.cs) 使用 `ConcurrentDictionary<uint, WebviewWindow>` + `ConcurrentDictionary<string, uint> _windowNames`，`Interlocked.Increment` 生成窗口 ID。
- [Win32WebviewWindow](../src/Wails.Net.Application.Windows/Win32WebviewWindow.cs) 完整处理 18+ Win32 消息。

**差异分析**：
- Wails.Net 的 `IWebviewWindowImpl` 默认接口方法是独创设计，平台实现只需重写支持的方法（如 Linux 无 `SetTaskbarProgress`），降低移植成本。
- Wails v3 的 `IWebviewWindowImpl` 接口无默认实现，平台实现必须实现所有方法。
- Wails.Net 的窗口级 CSP（`SetCspHeaderForWindow`）是**两者中独有**的（Wails v3 不支持）。

---

### 2.4 IPC 传输层

| 实现项 | Wails.Net | Wails v3 |
|--------|-----------|---------|
| **传输抽象** | `ITransport` 接口 | `ITransport` 接口 |
| **默认传输** | NativeIpcTransport（默认启用） | in-memory bridge |
| **HTTP 传输** | ✅ `HttpTransport`（端口 34115） | ❌ |
| **WebSocket 传输** | ✅ `WebSocketTransport`（端口 34116） | ❌ |
| **原生 IPC 传输** | ✅ `NativeIpcTransport`（512KB 阈值混合策略） | ✅ 默认 |
| **Event IPC 兜底** | ✅ `EventIPCTransport`（始终追加为兜底） | ❌ |
| **AssetServer 传输** | ✅ `AssetServerTransport` | ❌ |
| **分块上传** | ✅ `x-wails-chunk-id/index/total` 协议（≤1MB/chunk，≤64MB 总，30s TTL） | ❌ |
| **CORS 配置** | ✅ `CorsOptions` + `IpcOriginValidator`（白名单回显，非 `*`） | N/A |
| **Handshake Token** | ✅（OPT-IPC-002：`IpcOptions.RequireHandshakeToken`，进程级随机 token，HTTP 头 `x-wails-token` / WS 查询参数，缺失或错误 403） | ❌ |
| **CancellablePromise** | ✅ `_runningCalls` + `cancel` 消息类型 | ✅ `CancelCall` |
| **多传输层并行广播** | ✅ `IWailsEventListener` 列表 | ❌ |
| **senderWindowId 传播** | ✅ 全链路携带 | ❌ |

**Wails.Net 关键实现**：
- [NativeIpcTransport](../src/Wails.Net.Application/Transport/NativeIpcTransport.cs)：使用原生 `postMessage` 通道，**512KB 阈值**：超过则降级到 HTTP。`RegisterWindow` 安装 `SetNativeMessageHandler` 回调接收前端消息。
- [HttpTransport](../src/Wails.Net.Application/Transport/HttpTransport.cs)：基于 `HttpListener`，支持 `ChunkStore` 分块上传 + `CorsOptions` + `IpcOriginValidator`。
- [EventIPCTransport](../src/Wails.Net.Application/Transport/EventIPCTransport.cs)：兜底通过 `ExecJS` 注入 `window._wailsEmitEvent`，与 `NativeIpcTransport` 协作避免重复派发。


**Wails v3 原生功能**：
- 默认 `HTTPTransport`（端口 34115），无原生 IPC（alpha 阶段）。
- `MessageProcessor` 解析 `{name, args, methodID}` 派发到 `Bindings` 或 `Window`/`Dialog` 等。

**差异分析**：
- Wails.Net 是**两者中唯一**支持多传输层并行广播的方案，适用于容器化部署和调试场景。
- NativeIpcTransport 的混合策略（小消息原生 + 大消息 HTTP 分块）平衡延迟与容量，是独创设计。
- CancellablePromise 在两者中均支持，但 Wails.Net 通过 `_runningCalls` ConcurrentDictionary + `cancel` 消息类型实现，最接近 Wails v3 的 `CancelCall` 模型。
- `senderWindowId` 全链路传播是 Wails.Net 独有，Wails v3 不携带事件来源窗口标识。

---

### 2.5 绑定系统

| 实现项 | Wails.Net | Wails v3 |
|--------|-----------|---------|
| **绑定入口** | `[Binding]` 特性 + `BindingManager.Add(instance)` | `Application.Bind(instance)` |
| **调用路径** | 源生成器优先 + 反射回退（双路径） | 反射 |
| **方法 ID 哈希** | FNV-1a 32 位 | FNV-1a 32 位 |
| **哈希常量** | `offsetBasis=2166136261` / `prime=16777619` | `fnv.New32a()` 默认 |
| **哈希一致性** | ✅ 与 Go 版本字节级一致 | ✅ 基准实现 |
| **注册键** | 全限定名 + 短名称双键 | 全限定名 + 短名称 |
| **方法过滤** | 排除 `ServiceName`/`ServiceStartup`/`ServiceShutdown`/`IsSpecialName`/`Object` 继承方法 | 排除 `Service` 接口方法 |
| **错误类型** | `CallError` + `CallErrorKind`（Reference/Type/Runtime） | `CallError` + `ErrorKind` |
| **反射异常解包** | ✅ `TargetInvocationException` 解包 | ✅ |
| **取消支持** | ✅ `OperationCanceledException` 重抛 | ✅ `CancelCall` |
| **源生成器** | ✅ `BindingSourceGenerator`（`IIncrementalGenerator`） | N/A |
| **ModuleInitializer** | ✅ `[ModuleInitializer]` 自动注册 | N/A |
| **类名避冲突** | ✅ `BindingManager` 而非 `Bindings`（避免 CS0118） | N/A |

**Wails.Net 关键实现**：
- [BindingManager](../src/Wails.Net.Application/Bindings/Bindings.cs)：
  - **源生成器优先**：`GeneratedBindingRegistry.TryGetInvoker` 优先使用编译时生成的强类型调用器。
  - **反射兜底**：源生成器未覆盖的场景回退到反射调用。
  - **FNV-1a 32 位哈希**：与 Go 版本 `fnv.New32a()` 完全一致（AGENTS.md §6.3 强制要求）。
- [BindingSourceGenerator](../src/Wails.Net.SourceGenerators/BindingSourceGenerator.cs)：通过 `IIncrementalGenerator` 生成 `WailsGeneratedBindings.g.cs`，使用 `[ModuleInitializer]` 自动注册。


**Wails v3 原生功能**：
- `Application.Bind(instance)` 反射注册 Service 的公共方法。
- `FNV-1a 32位` 哈希生成 methodID（`fnv.New32a()`）。
- `registeredBindingMethodIDs sync.Map` 允许显式指定方法 ID（绕过 FNV-1a 哈希），用于代码混淆场景。
- `Bindings.methodAliases map[uint32]uint32` 支持旧 ID → 新 ID 映射。
- `ServiceOptions.MarshalError` 每个 Service 可自定义错误序列化逻辑。

**差异分析**：
- Wails.Net 是**两者中唯一**采用源生成器 + 反射双路径的方案，兼顾性能（编译期生成）与开发体验（反射兜底）。
- Wails v3 与 Wails.Net 共享 FNV-1a 哈希算法，前端绑定 ID 可互通。
- Wails.Net 严格遵循 AGENTS.md §3.4 "禁止使用反射获取对应方法"约束，源生成器是主路径。

---

### 2.6 事件系统

| 实现项 | Wails.Net | Wails v3 |
|--------|-----------|---------|
| **核心类型** | `EventProcessor` | `EventManager` |
| **公共事件类型数** | **30 个**（`ApplicationEventType` 枚举 0~29） | ~28 个 |
| **监听器存储** | `ConcurrentDictionary<string, List<EventListener>>` | `map[string][]EventListener` |
| **Pre-emit 钩子** | ✅ `List<Func<CustomEvent, bool>>`（返回 false 取消） | ✅ Event Hooks |
| **多传输层广播** | ✅ `IWailsEventListener` 列表并行广播 | 单一 in-memory bridge |
| **事件命名空间** | `wails:window:*` / `wails:app:*` / `wails:low:memory` / `wails:screen:*` | `wails:*` |
| **JS 端 API** | `wails.events.on/off/emit` | `wails.events.on/off/emit` |
| **跨窗口广播** | ✅ 通过多传输层自动广播 | ✅ |
| **线程安全** | ✅ `ConcurrentDictionary` + 锁 | ✅ `sync.RWMutex` |
| **senderWindowId 传播** | ✅ EventProcessor/Transport 全链路携带（P1-2） | ❌ |
| **PostShutdown 钩子** | ✅ `ApplicationOptions.PostShutdown`（P1-7） | ✅ `Options.PostShutdown` |
| **ShouldQuit 回调** | ✅ `ApplicationOptions.ShouldQuit`（P1-7） | ✅ `Options.ShouldQuit` |
| **maxCalls 限制** | ✅ `OnMultiple` + `Once` | ✅ `Once` |
| **LowMemory 事件** | ✅ `ApplicationEventType.LowMemory=27`（P2 新增） | ✅ `Common.LowMemory=1290` |
| **ScreenLocked/Unlocked** | ✅ `ApplicationEventType.ScreenLocked=28` / `ScreenUnlocked=29`（P2 新增） | ✅ `Common.ScreenLocked=1288` / `ScreenUnlocked=1289` |
| **平台事件映射** | ✅ `AndroidPlatformEvents.MapToCommonEvent`（P2 新增） | ✅ `commonApplicationEventMap` |

**Wails.Net 关键实现**：
- [EventProcessor](../src/Wails.Net.Application/Events/EventProcessor.cs) 使用 `ConcurrentDictionary<string, List<EventListener>>` 存储监听器。
- `_hooks` 字段支持 Pre-emit 钩子（参考 Wails v3 Event Hooks）。
- `_wailsEventListeners` 列表支持多传输层广播：`SetWailsEventListener`/`AddWailsEventListener`/`RemoveWailsEventListener`。
- 事件订阅 API：`On`/`OnMultiple`/`Once`/`Emit`/`Off`。
- [ApplicationEventType](../src/Wails.Net.Events/ApplicationEventType.cs) 枚举包含 30 个值（`Started=0` 到 `ScreenUnlocked=29`）。
- [AndroidPlatformEvents](../src/Wails.Net.Application.Android/AndroidPlatformEvents.cs) 定义 Android 平台事件 ID（1267-1285）+ `MapToCommonEvent` 映射到公共事件（7 个映射）。


**Wails v3 原生功能**：
- `EventManager` 双层架构：hooks（可取消事件）+ listeners。
- `RegisterApplicationEventHook` 注册应用级事件 Hook。
- `ApplicationEvent` 类型常量（`ApplicationStarted`/`ThemeChanged` 等）。
- `OnApplicationEvent` 订阅应用级事件。

**差异分析**：
- Wails.Net 同时支持 Pre-emit 钩子（参考 Wails v3）和**多传输层并行广播**（独有）：一次 `Emit` 会同时通过 HttpTransport + WebSocketBroadcaster + EventIPCTransport + AssetServerTransport + NativeIpcTransport 广播，确保所有连接的前端都能收到。
- `senderWindowId` 全链路传播是 Wails.Net 独有，Wails v3 无来源标识。
- Wails.Net 自 P2 起新增 3 个公共事件（LowMemory/ScreenLocked/ScreenUnlocked），完整对齐 Wails v3 `Common.*` 事件。

---

### 2.7 MessageProcessor 路由

| 实现项 | Wails.Net | Wails v3 |
|--------|-----------|---------|
| **核心类型** | `MessageProcessor` | `MessageProcessor` |
| **消息类型常量** | `call`/`event`/`window`/`query`/`response`/`error`/`drag`/`contextmenu`/`system`/`cancel` | 同 Wails.Net（无 cancel） |
| **Call 路由** | `BindingManager` 优先 → `CommandDispatcher` 回退 | `Bindings` 调用 |
| **Window 路由** | `CommandDispatcher` 优先 → 硬编码 `DispatchWindowAction` 回退 | `DispatchWindowAction` 硬编码 |
| **未识别路由** | `ProcessCommandFallbackAsync` 统一走 `CommandDispatcher` | 报错 |
| **CancellablePromise** | ✅ `_runningCalls` ConcurrentDictionary | ✅ `CancelCall` |
| **窗口操作事件广播** | ✅ `_events.Emit($"wails:window:{action}", ...)` | ✅ |
| **Options 对象匹配** | ✅ `WindowSetTitleOptions`/`WindowSizeOptions` 等 | ✅ |

**Wails.Net 关键实现**：
- [MessageProcessor](../src/Wails.Net.Application/Transport/MessageProcessor.cs) `ProcessAsync` 路由规则：
  - `call` → `ProcessCallAsync`（`BindingManager` 优先 → `CommandDispatcher` 回退）
  - `window` → `ProcessWindowAsync`（`CommandDispatcher` 优先 → 硬编码 `DispatchWindowAction` 回退）
  - 其他 → `ProcessCommandFallbackAsync`（统一走 `CommandDispatcher`）
- `_runningCalls` 使用 `ConcurrentDictionary` 跟踪运行中调用，支持 `cancel` 消息类型取消调用。
- 窗口操作的事件广播通过 `_events.Emit($"wails:window:{action}", ...)` 保留。

**差异分析**：
- Wails.Net 采用「核心即插件」的插件化设计：window 操作也优先走 `CommandDispatcher`，硬编码路径仅作向后兼容回退。这是 Wails v3 没有的设计。
- Wails v3 完全硬编码 `DispatchWindowAction`，无插件化。
- Wails.Net 新增 `MessageTypes.Cancel` 消息类型，与 Wails v3 的 `CancelCall` 对齐。

---

### 2.8 菜单系统与 MenuRole

| 实现项 | Wails.Net | Wails v3 |
|--------|-----------|---------|
| **菜单抽象** | `Menu` / `MenuItem` / `IMenuImpl` | `Menu` / `MenuItem` / `MenuRole` |
| **预定义角色枚举** | ✅ `MenuRole`（21 个值，P2 新增） | ✅ `Role` 常量 |
| **角色工厂方法** | ✅ 13 个 | ✅ 隐式 |
| **跨平台辅助工具** | ✅ `MenuRoleHelper` | ✅ |
| **macOS 专属降级** | ✅ Windows/Linux 静默 no-op | ✅ |
| **关于对话框元数据** | ✅ `AboutMetadata` | ✅ |
| **标准菜单组合** | ✅ `AddStandardEditMenu`/`AddStandardWindowMenu`/`AddStandardHelpMenu` | ❌ |
| **Win32 实现** | ✅ `Win32Menu.ApplyRole` | ✅ |
| **Linux 实现** | ✅ `LinuxMenu.ApplyRole`（GTK4 GMenu） | ✅ |
| **Android 实现** | ❌ | ❌ |
| **前端 API 注入** | ✅ `wails.MenuRole` 常量 + 4 个 menu.* 命令 | ✅ `Role` |
| **全局热键注册** | ✅ 角色带 Accelerator 时自动注册到 `KeyBindingManager` | ✅ |
| **菜单插件命令数** | 10 个（含 5 个 MenuRole 命令） | N/A |

**Wails.Net 关键实现**：
- [MenuRole](../src/Wails.Net.Application/Menus/MenuRole.cs) 21 个角色枚举值（None/Separator/Copy/Cut/Paste/SelectAll/Undo/Redo/Minimize/Maximize/Fullscreen/CloseWindow/Zoom/About/Quit/Hide/HideOthers/ShowAll/Services/BringAllToFront/ToggleFullScreen）。
- [MenuRoleHelper](../src/Wails.Net.Application/Menus/MenuRoleHelper.cs)：默认中文 Label、默认 Accelerator、macOS 专属判定、`PrepareRoleItem` 统一填充 Callback。
- [MenuItem](../src/Wails.Net.Application/Menus/MenuItem.cs) 13 个工厂方法：`CreateCopy`/`CreateCut`/`CreatePaste`/`CreateSelectAll`/`CreateUndo`/`CreateRedo`/`CreateSeparator`/`CreateMinimize`/`CreateMaximize`/`CreateFullscreen`/`CreateCloseWindow`/`CreateQuit`/`CreateAbout`。
- [Menu](../src/Wails.Net.Application/Menus/Menu.cs) 新增 `AddRoleItem`/`AddStandardEditMenu`/`AddStandardWindowMenu`/`AddStandardHelpMenu` 方法。
- [Win32Menu.ApplyRole](../src/Wails.Net.Application.Windows/Win32Menu.cs) / [LinuxMenu.ApplyRole](../src/Wails.Net.Application.Linux/LinuxMenu.cs) 各自实现 ExecuteRole，编辑命令通过 `document.execCommand` 调用。
- [AboutMetadata](../src/Wails.Net.Application/Menus/AboutMetadata.cs) 9 个字段（Name/Version/ShortVersion/Authors/Copyright/License/Website/WebsiteLabel/Comments）。
- [@wails-net/runtime](../packages/wails-net-runtime/src/core/runtime.ts) 注入前端 `wails.MenuRole` 常量枚举与 4 个 menu.* 命令。

**差异分析**：
- Wails.Net 的 MenuRole 系统是**两者中最完整**的角色菜单实现，结合 Wails v3 Role 常量模型与自研工厂方法：
  - **21 个角色枚举值**（覆盖跨平台编辑/窗口/应用角色 + macOS 专属角色）
  - **13 个工厂方法**（按角色提供专用工厂 API，便于编程式构建）
  - **跨平台共享逻辑**（`MenuRoleHelper` 提供默认中文 Label、默认 Accelerator、macOS 专属判定）
  - **平台实现分离**（Win32Menu / LinuxMenu 各自实现 ExecuteRole）
  - **全局热键自动注册**（修复现有 Accelerator 仅在菜单栏内生效的 bug）
  - **标准菜单组合**（一键构建跨平台标准 Edit/Window/Help 菜单）
- Wails v3 通过 `Role` 常量支持菜单角色，但缺少工厂方法与跨平台辅助工具的统一封装。

---

### 2.9 系统托盘

| 实现项 | Wails.Net | Wails v3 |
|--------|-----------|---------|
| **托盘类型** | `TrayPlugin` + `ITrayImpl` | `SystemTray` + `systemTrayImpl` |
| **图标设置** | ✅ `SetIcon` | ✅ `SetIcon` |
| **菜单附加** | ✅ `SetMenu` | ✅ `SetMenu` |
| **点击事件** | ✅ `OnClick`/`OnRightClick`/`OnDoubleClick` | ✅ |
| **附着窗口定位** | ✅ `AttachWindow`/`PositionWindow` | ✅ `AttachWindow` |
| **标签/Tooltip** | ✅ `SetLabel`/`SetTooltip` | ✅ |
| **平台实现** | Win32Tray / LinuxTray | platform_windows.go / platform_linux.go |

**Wails.Net 关键实现**：
- `TrayPlugin` 通过 `ITrayImpl` 接口抽象平台实现。
- Win32Tray 使用 `Shell_NotifyIconW` + 自定义 WM_USER 消息。
- LinuxTray 使用 GTK4 `StatusIcon`（或 AppIndicator）。

**差异分析**：
- 托盘功能与 Wails v3 基本对齐，Wails.Net 额外支持 `AttachWindow`/`PositionWindow` 窗口定位（参考 Wails v3）。
- Wails v3 的 `SystemTray` 是最早的实现，Wails.Net 与之最接近。

---

### 2.10 全局快捷键与拖拽

| 实现项 | Wails.Net | Wails v3 |
|--------|-----------|---------|
| **全局快捷键** | `KeyBindingManager` + `GlobalShortcutPlugin` | `Application.KeyBinding` |
| **快捷键注册** | ✅ `RegisterKeyBinding(accelerator, callback)` | ✅ |
| **快捷键注销** | ✅ `UnregisterKeyBinding` | ✅ |
| **CSS 拖拽区域** | ✅ `DragRegionHelper`（双约定） | ✅ `--wails-draggable` |
| **拖拽约定兼容** | ✅ 同时支持 `--wails-draggable` 和 `-webkit-app-region` | ❌（仅 Wails 约定） |
| **Frameless 拖拽统一** | ✅ 三平台一致（P1-5） | ✅ |

**Wails.Net 关键实现**：
- [DragRegionHelper](../src/Wails.Net.Application/Windows/DragRegionHelper.cs) 通过注入 JavaScript 实现 CSS 拖拽区域识别，同时支持两种约定：`--wails-draggable: drag` CSS 变量和 `-webkit-app-region: drag` WebKit 私有属性。

**差异分析**：
- Wails.Net 的双约定兼容（Wails 风格 + Electron 风格）是**两者中独有**，降低前端迁移成本。
- Wails v3 仅支持 `--wails-draggable` 自有约定。

---

## 维度 3：Plugin / Security / Capability（自研能力模型）

### 3.1 插件系统

| 实现项 | Wails.Net | Wails v3 |
|--------|-----------|---------|
| **插件接口** | `IPlugin`（4 阶段生命周期） | `Service` 接口（`ServiceStartup`/`ServiceShutdown`） |
| **生命周期阶段** | `ConfigureServices` → `Configure` → `StartupAsync` → `ShutdownAsync` | `ServiceStartup` → `ServiceShutdown` |
| **插件管理器** | `PluginManager`（`Interlocked` 幂等保护） | `ServiceManager` |
| **插件上下文** | `IPluginContext`（Commands/Permissions/Services） | `Manager` |
| **命令注册** | `commands.MapCommand` | `Application.Bind` |
| **权限声明** | `context.Permissions.DeclarePermission` | N/A |
| **IHostedService 适配** | ✅ `PluginHostedServiceAdapter`（双重触发防护） | N/A |
| **逆序关闭** | ✅ 后注册先关闭 | ✅ |
| **程序集扫描** | ✅ `DiscoverFromAssembly` 自动扫描 `IPlugin` 实现 | N/A |
| **Fluent API** | ✅ `UsePlugin<TPlugin>` | ✅ `Options.Services` |
| **内置插件数** | **37 桌面 + 4 移动端 + 1 Android 运行时 = 42 个** | 有限 |

**Wails.Net 关键实现**：
- [IPlugin](../src/Wails.Net.Application/Plugins/IPlugin.cs) 4 阶段生命周期：
  1. `ConfigureServices(IServiceCollection)` — 注册 DI 服务（对应 ASP.NET Core `ConfigureServices`）
  2. `Configure(IPluginContext)` — 配置命令和事件
  3. `StartupAsync(CancellationToken)` — 应用启动后调用（对应 Wails v3 `Startup`）
  4. `ShutdownAsync(CancellationToken)` — 应用关闭时调用（对应 Wails v3 `Shutdown`）
- [PluginManager](../src/Wails.Net.Application/Plugins/PluginManager.cs)：
  - `Interlocked.CompareExchange` 保护 `_started`/`_stopped` 字段，确保幂等启动/关闭。
  - 关闭按注册**逆序**执行，确保依赖关系正确（后注册的插件先关闭）。
  - 单个插件启动/关闭失败不影响其他插件，仅记录错误日志。
  - `DiscoverFromAssembly` 自动扫描程序集中实现 `IPlugin` 接口、有无参构造的类型。
- [PluginHostedServiceAdapter](../src/Wails.Net.Application/Plugins/PluginHostedServiceAdapter.cs)：让插件生命周期与 Host 生命周期对齐。


**Wails v3 原生功能**：
- `Service` 接口是统一抽象，无独立"插件"概念。
- `Service` 既是状态容器也是绑定源。
- `ServeHTTP` 让 Service 可作 HTTP Handler。
- 无独立权限声明。

**差异分析**：
- Wails.Net 的 `IPlugin` 4 阶段生命周期是**两者中最完整**的，单一接口融合 ASP.NET Core（`ConfigureServices`）与 Wails v3（`Startup`/`Shutdown`）两套生命周期模型，并补充 `Configure` 命令注册阶段。
- `Interlocked` 幂等保护防止 `IHostedService` 适配器与 `Application.Run` 手动调用同时触发导致重复启动，是独创设计。
- 内置 42 个插件是**两者中最多**的（Wails v3 内置插件有限）。

---

### 3.2 权限模型（PermissionManager）

| 实现项 | Wails.Net | Wails v3 |
|--------|-----------|---------|
| **三层 ACL** | ✅ Capability + PermissionSet + Scope | ❌ |
| **配置文件** | `appsettings.json` + `capabilities/*.json` | N/A |
| **权限声明** | `DeclarePermission` / `RegisterPermissionSet` | N/A |
| **作用域类型** | `FileSystemScope` / `UrlScope` | N/A |
| **Deny 优先** | ✅ `_deniedPermissions` 优先于 `_grantedPermissions` | N/A |
| **远程 URL 限制** | ✅ `_remoteUrlScopes` + `IsGranted(perm, window, origin)` | N/A |
| **窗口级隔离** | ✅ `PermissionKey (Permission, Window)` 复合键 | N/A |
| **命令校验** | ✅ `ValidateCommand` + `RequireCapabilityAttribute` | ❌ |
| **运行时开关** | ✅ `PermissionManager.Enabled`（默认 false；OPT-SEC-001 引入 `PermissionMode`：Strict 模式强制启用） | N/A |
| **本地源判定** | `wails://` / `localhost` / `127.0.0.1` / null | N/A |
| **URL 白名单** | `UrlWhitelist` 通配符匹配 | N/A |
| **自动加载** | ✅ `LoadCapabilities` 从目录加载 capabilities/*.json | N/A |
| **PermissionSet 自动展开** | ✅ `Grant`/`Deny` 检测权限集标识并展开 | N/A |
| **平台过滤** | ✅（OPT-SEC-002：`Capability.Platforms` + `PermissionManager.CurrentPlatform`，大小写不敏感，未知平台名 warning） | N/A |

**Wails.Net 关键实现**：
- [PermissionManager](../src/Wails.Net.Application/Security/PermissionManager.cs) 6 个 `ConcurrentDictionary` 线程安全：
  - `_grantedPermissions: ConcurrentDictionary<PermissionKey, byte>`
  - `_deniedPermissions: ConcurrentDictionary<PermissionKey, byte>`
  - `_declaredCapabilities: ConcurrentDictionary<string, Capability>`
  - `_permissionSets: ConcurrentDictionary<string, PermissionSet>`
  - `_permissionScopes: ConcurrentDictionary<string, IScope>`
  - `_remoteUrlScopes: ConcurrentDictionary<PermissionKey, UrlWhitelist>`
- 核心 API：`Grant`/`Deny`/`Revoke`/`Undeny`/`IsGranted`/`IsDenied`/`ValidateCommand`/`ValidateCapabilities`/`ValidateScopes`/`IsCapabilityApplicableToWindow`/`IsCapabilityApplicableToPlatform`。
- **Deny 优先于 Grant**：`IsGranted` 先检查 `IsDenied`，即便已授权也返回 false。
- **PermissionKey 复合键** `(Permission, Window)`：`Window=null` 表示全局，`Window="windowName"` 表示窗口级。
- **PermissionSet 自动展开**：`Grant`/`Deny` 检测权限集标识并展开为集内所有细粒度权限。
- **本地源豁免**：`wails://`/`localhost`/`127.0.0.1`/`null` 始终放行，不受 Remote 限制。
- **DenyByDefault 语义**：`PermissionOptions.DenyByDefault=true` 时未授权权限一律拒绝（最小权限原则）。
- **平台过滤（OPT-SEC-002）**：`Capability.Platforms` 空 → 所有平台生效；非空 → 仅当前平台（`CurrentPlatform`，大小写不敏感）生效；未知平台名加载时 warning。
- **权限模式（OPT-SEC-001）**：`PermissionMode.Compatibility`（默认，未授权放行，旧应用迁移）vs `PermissionMode.Strict`（未授权拒绝，推荐生产，即便漏配 `Enabled=true` 也强制启用）。


**Wails v3 原生功能**：
- **无 ACL/Capability 权限模型**，所有绑定方法对前端默认可见。
- 仅运行时能力探测（`Capabilities{HasNativeDrag, GTKVersion, WebKitVersion}`），非安全权限。

**差异分析**：
- Wails.Net 的 PermissionManager 采用自研三层 ACL 模型，**默认 `Enabled=false`（Compatibility 模式）** 保持向后兼容；OPT-SEC-001 已引入 `PermissionMode.Strict` 向最小权限姿态迁移。
- Wails.Net 已实现 `Capability.platforms` 平台过滤（OPT-SEC-002），可按平台裁剪能力声明。
- Wails v3 完全无 ACL 模型，依赖操作系统进程隔离，安全模型最弱。

---

### 3.3 Capability 能力声明

| 实现项 | Wails.Net | Wails v3 |
|--------|-----------|---------|
| **能力数据模型** | `Capability` 类 | N/A |
| **配置文件** | `capabilities/*.json` | N/A |
| **JSON 加载器** | ✅ `CapabilityFileLoader.LoadFromDirectory` | N/A |
| **字段：Identifier** | ✅ | N/A |
| **字段：Description** | ✅ | N/A |
| **字段：Permissions** | ✅（支持 `"!"` 前缀表示拒绝） | N/A |
| **字段：Windows** | ✅（窗口名称列表） | N/A |
| **字段：Remote** | ✅（远程 URL 模式列表） | N/A |
| **字段：Plugin** | ✅ | N/A |
| **字段：Platforms** | ✅（OPT-SEC-002：`platforms` 数组，平台过滤；未知平台名加载时 warning） | N/A |
| **命令所需能力特性** | ✅ `RequireCapabilityAttribute` | N/A |
| **窗口级授权** | ✅ `Windows` 为空 → 全局；非空 → 窗口级 | N/A |
| **远程级授权** | ✅ `Remote` 为空 → 不限制；非空 → 限制 | N/A |

**Wails.Net 关键实现**：
- [Capability](../src/Wails.Net.Application/Security/Capability.cs) 数据模型字段：`Identifier`/`Description`/`Permissions`/`Windows`/`Remote`/`Platforms`/`Plugin`。
- [CapabilityFileLoader](../src/Wails.Net.Application/Security/CapabilityFileLoader.cs) 扫描 `capabilities/*.json` 文件，使用 `JsonSerializer` 反序列化（支持注释、尾随逗号）。
- `RegisterToManager` 将能力注册到 `PermissionManager`：
  - `Windows` 为空 → 全局授权（`windowName=null`）
  - `Windows` 非空 → 窗口级授权（仅列出窗口可用）
  - `"!"` 前缀权限标识 → 调用 `Deny`（剥离前缀）
  - 普通权限标识 → 调用 `Grant`（透传 `remotePatterns`）
  - `Platforms` 非空且不匹配当前平台 → 跳过授权（能力仍声明，权限不生效）
- [RequireCapabilityAttribute](../src/Wails.Net.Application/Security/CapabilityAttribute.cs) 标记命令方法所需的能力，调度时由 `CommandDispatcher` 校验。

**差异分析**：
- Wails.Net 的 Capability 模型**为自研完整实现**，包括 `"!"` 前缀拒绝语法、窗口级 + 远程级双层隔离，以及 `platforms` 平台过滤（OPT-SEC-002 已实现）。
- Wails v3 完全无 Capability 模型，Wails.Net 从零设计 Capability 系统。

---

### 3.4 Scope 作用域

| 实现项 | Wails.Net | Wails v3 |
|--------|-----------|---------|
| **Scope 接口** | `IScope` | N/A |
| **文件系统 Scope** | ✅ `FileSystemScope`（路径前缀匹配） | N/A |
| **URL Scope** | ✅ `UrlScope`（通配符匹配） | N/A |
| **Shell Scope** | ❌ | N/A |
| **参数级 Scope 校验** | ✅ `IScopeParameter` + `[ScopeParameter]` | N/A |
| **路径规范化** | ✅ `Path.GetFullPath` | N/A |
| **目录前缀匹配** | ✅ `targetPath.StartsWith(allowedPath + Path.DirectorySeparatorChar)` | N/A |
| **URL 通配符** | ✅ `UrlWhitelist`（`*` → `.*`） | N/A |
| **配置驱动** | ✅ `ScopeInitializer.Initialize` 从 `PermissionOptions.Scopes` | N/A |
| **ScopeConfig** | ✅ `Paths` + `Urls` 双白名单 | N/A |

**Wails.Net 关键实现**：
- [FileSystemScope](../src/Wails.Net.Application/Security/Scopes.cs) `Allows(value)` 校验逻辑：
  - 精确匹配（`StringComparer.OrdinalIgnoreCase`）
  - 目录前缀匹配（`targetPath.StartsWith(allowedPath + Path.DirectorySeparatorChar, OrdinalIgnoreCase)`）
  - 路径解析失败视为不允许
- [UrlScope](../src/Wails.Net.Application/Security/Scopes.cs) 委托 `UrlWhitelist`，支持 `*` 通配符（正则 `.*` 转义）。
- [IScopeParameter](../src/Wails.Net.Application/Security/IScopeParameter.cs) 接口允许 Options 类自定义 `GetScopeValues()` 返回需要校验的 (权限标识, 值) 对。
- [ScopeParameterAttribute](../src/Wails.Net.Application/Security/ScopeParameterAttribute.cs) 标记 `string` 参数，`CommandDispatcher.ExtractScopeValues` 从 JSON 中按参数名提取值。
- [ScopeInitializer](../src/Wails.Net.Application/Security/ScopeInitializer.cs) 从 `PermissionOptions.Scopes` 创建 Scope 实例：`Paths` 非空 → 创建 `FileSystemScope` 绑定；`Urls` 非空 → 创建 `UrlScope` 绑定。

**差异分析**：
- Wails.Net 的参数级 Scope 校验（`IScopeParameter` + `[ScopeParameter]`）是**两者中独有**的，允许命令方法参数级别的细粒度校验。
- Wails v3 完全无 Scope 模型。
- Wails.Net 暂未实现 Shell Scope，这是已知差距。

---

### 3.5 命令调度（CommandDispatcher）

| 实现项 | Wails.Net | Wails v3 |
|--------|-----------|---------|
| **调度器** | `CommandDispatcher` | `Bindings` 直接调用 |
| **命令注册** | `CommandRegistry.Register` / `MapCommand` / `[Command]` + 源生成器 | `Application.Bind` |
| **命令标记** | `[Command]` | 反射公共方法 |
| **调用器构建** | ✅ `MapCommand` 泛型闭包 + 源生成器生成调用器 | N/A |
| **零反射调用** | ✅ `CompiledCommandInvoker` 委托 | ❌ 反射 |
| **中间件管道** | ✅ `ICommandMiddleware` | ❌ |
| **超时自动取消** | ✅ `_defaultTimeout` + `CreateLinkedTokenSource` | ❌ |
| **特殊参数注入** | `ICommandContext` / `CancellationToken` / `IServiceProvider` | N/A |
| **权限校验** | ✅ `ValidateCapabilities` + `ValidateScopes` | ❌ |
| **异常类型映射** | ✅ 按具体异常类型（无 `TargetInvocationException` 解包） | ✅ |
| **命令自动注册** | ✅ 源生成器 `[ModuleInitializer]` 自动注册 | N/A |

**Wails.Net 关键实现**：
- [CommandDispatcher](../src/Wails.Net.Application/Commands/CommandDispatcher.cs) `DispatchAsync` 调度流程：
  1. 创建带超时的 `CancellationTokenSource`，合并外部取消令牌和超时令牌
  2. 查找命令条目（未找到返回 `Command not found`）
  3. 权限校验：`ValidateCapabilities`（`[RequireCapability]` 编译期能力 + `CommandEntry` 显式声明）+ `ValidateScopes`（参数级 Scope）
  4. 构建中间件管道：从最后一个中间件向前构建，第一个中间件最先执行
  5. 终端处理器 `ExecuteCommandAsync`：只调用编译期构建的 `Invoker`（零反射），无反射回退
  6. 处理异步返回值（`Task` / `Task<T>`，委托统一返回 `Task<object?>`）
  7. 异常处理：按具体异常类型返回（`MethodInfo.Invoke` 的 `TargetInvocationException` 解包已随反射删除）
- [`MapCommandExtensions`](../src/Wails.Net.Application/Commands/MapCommandExtensions.cs) 的泛型重载在注册点构造强类型闭包，产出 `CompiledCommandInvoker` 委托（原 `CommandInvokerCompiler` 表达式树编译已删除）。
- 命令由源生成器在 `[ModuleInitializer]` 中注册；`[DesktopCommand]` 的程序集扫描（`RegisterFromAssembly`）已随反射实现移除而停用。

**差异分析**：
- Wails.Net 的 `CommandDispatcher` 是**两者中唯一**支持中间件管道的方案，类似 ASP.NET Core 中间件，可用于日志、审计、限流等横切关注点。
- 编译期闭包与源生成器共同实现零反射调用，遵循 AGENTS.md §3.4 "禁止使用反射获取对应方法"约束。
- 超时自动取消是 Wails.Net 独有，Wails v3 无超时机制。
- Wails v3 完全反射调用，性能最差。

---

### 3.6 Channels

| 实现项 | Wails.Net | Wails v3 |
|--------|-----------|---------|
| **Channel 接口** | `IChannel` | ❌ |
| **Channel 实现** | `Channel`（internal sealed） | ❌ |
| **Channel 管理器** | `ChannelManager`（静态） | ❌ |
| **注册表** | `ConcurrentDictionary<string, Channel>` | ❌ |
| **消息类型** | `channel:open` / `channel:message` / `channel:close` | ❌ |
| **发送委托注入** | ✅ `Func<string, object?, CancellationToken, Task>` | N/A |
| **Interlocked 关闭保护** | ✅ `_closed` CompareExchange | N/A |
| **ObjectDisposedException 自动清理** | ✅ | N/A |

**Wails.Net 关键实现**：
- [Channel](../src/Wails.Net.Application/Channels/Channel.cs) 通过传入的发送委托 `Func<string, object?, CancellationToken, Task>` 投递消息到底层传输层。
- [ChannelManager](../src/Wails.Net.Application/Channels/ChannelManager.cs) 使用 `ConcurrentDictionary<string, Channel>` 线程安全注册表。
- **创建**：`Create(id?, sender?)`，`id` 为 null 自动生成 GUID（32 字符无连字符）。
- **消息分发**：`DispatchMessage(channelId, eventId, message)` 由传输层在收到 `channel:message` 消息时调用。
- **关闭信号**：`CloseAsync` 发送 `payload=null` 让对端感知关闭。
- **Interlocked 关闭状态保护**：`Channel._closed` 使用 `Interlocked.CompareExchange` 保证幂等关闭。
- **发送委托注入**：`Channel` 不绑定特定传输层，发送委托由调用方注入，支持多传输层复用。
- **ObjectDisposedException 自动清理**：`DispatchMessage` 捕获 `ObjectDisposedException` 后从注册表移除通道。

**差异分析**：
- Wails.Net 的 Channel 系统**完整实现 Channel 协议**（`channel:open`/`channel:message`/`channel:close` 三消息类型）。
- Wails v3 完全无 Channel 模型，Wails.Net 从零设计实现。
- Wails.Net 的发送委托注入设计让 Channel 可跨传输层复用，是独创设计。

---

### 3.7 AssetServer 中间件

| 实现项 | Wails.Net | Wails v3 |
|--------|-----------|---------|
| **AssetServer** | ✅ `Wails.Net.AssetServer` 独立项目 | ✅ `assetserver` |
| **中间件管道** | ✅ `IMiddleware` + `IHttpMiddleware` + `MiddlewareChain` | ❌ |
| **CSP Nonce 注入** | ✅ `NonceInjector` | ✅ |
| **隔离注入** | ✅ `IsolationInjector` | ❌ |
| **Range 请求** | ✅ `Content-Range` / `Accept-Ranges` | ✅ |
| **ETag** | ✅ `ETag` / `If-None-Match` | ✅ |
| **Last-Modified** | ✅ `Last-Modified` / `If-Modified-Since` | ✅ |
| **自定义 Header** | ✅ `Headers` 静态类 | ✅ |
| **MIME 类型** | ✅ 内置映射 | ✅ |
| **Service Route 挂载** | ✅ `IHttpServiceHandler`（P1-6） | ❌ |
| **per-window CSP** | ✅ `SetCspHeaderForWindow`（P0-4） | ❌ |
| **AssetProvider 抽象** | ✅ `IAssetProvider` + `BundledAssetProvider` + `FileAssetProvider` | ✅ |
| **Android AssetServer** | ✅ `AndroidAssetServer` | ✅ |

**Wails.Net 关键实现**：
- [AssetServer](../src/Wails.Net.AssetServer/AssetServer.cs) **双中间件接口**：
  - `IMiddleware.ProcessAsync(path, next)` — 基于路径，返回字节数组或 null
  - `IHttpMiddleware.ProcessAsync(context, next)` — 基于 HTTP 上下文，返回 bool（true 表示已完全处理）
- [MiddlewareChain](../src/Wails.Net.AssetServer/Middleware/MiddlewareChain.cs) `ExecuteAsync` 倒序构建委托链，使最先注册的中间件最先执行（符合 ASP.NET Core 中间件约定）。`ExecuteHttpAsync` 任一中间件返回 true 则短路后续中间件和最终处理器。
- [IsolationInjector](../src/Wails.Net.AssetServer/Security/IsolationInjector.cs) 注入 CSP 隔离指令，[NonceInjector](../src/Wails.Net.AssetServer/Security/NonceInjector.cs) 注入 CSP nonce。
- [IHttpServiceHandler](../src/Wails.Net.AssetServer/IHttpServiceHandler.cs) 允许业务代码挂载自定义 HTTP 路由到 AssetServer（P1-6），无需独立启动 ASP.NET Core 管道。服务路由匹配规则：精确匹配 + 前缀匹配（`route + "/"`）+ 最长匹配优先。

**差异分析**：
- Wails.Net 的 AssetServer 是**两者中唯一**提供双中间件管道（路径式 + HTTP 上下文式）的方案。
- IHttpServiceHandler 业务路由挂载是 Wails.Net 独有，Wails v3 无此能力。
- per-window CSP（`SetCspHeaderForWindow`）也是 Wails.Net 独有，Wails v3 不支持。

---

### 3.8 Updater 与签名安全

| 实现项 | Wails.Net | Wails v3 |
|--------|-----------|---------|
| **Updater 服务** | ✅ `UpdaterService`（`IServiceStartup` / `IServiceShutdown`） | ✅ `Updater` |
| **UpdateInfo 结构** | ✅ `Version` / `DownloadUrl` / `UpdateAvailable` / `ReleaseNotes` | ✅ |
| **多 Provider** | ✅ `IUpdateProvider` 接口 + 链式尝试（P1-8） | ✅ 多 provider |
| **Provider 列表** | ✅ HttpUpdateProvider / GitHubUpdateProvider / GitLabUpdateProvider（P1-8） | 内置 |
| **ProviderName 注入** | ✅ `UpdateManifest.ProviderName`（P1-8） | ✅ |
| **错误事件 payload** | ✅ `UpdateErrorInfo { stage, message, provider }`（P1-8） | ✅ `ErrorInfo` |
| **下载器** | ✅ `UpdateDownloader`（断点续传 + SHA256 校验） | ✅ |
| **解压器** | ✅ `UpdateExtractor`（.zip / .tar.gz / .tgz / .tar + 路径遍历保护） | ✅ |
| **Helper Process** | ✅ `HelperProcess`（等待父进程退出 + 重试替换 + 重启应用） | ✅ |
| **事件广播** | ✅ 通过 `EventProcessor` 广播 10 个更新事件 | ✅ |
| **签名校验** | ✅ Minisign（BLAKE2b-512 + Ed25519）+ Authenticode + GPG | ✅ Minisign |
| **Windows 自动签名** | ✅ Authenticode | ❌ |
| **向后兼容** | ✅ 未注册 Provider 时回退到 `UpdateURL`（P1-8） | N/A |
| **平台特定安装** | ✅ Windows .msi(msiexec) / .exe(/SILENT)；Linux .deb(dpkg) / .rpm(rpm) / .AppImage(chmod+x) | ✅ |

**Wails.Net 关键实现**：
- [MinisignVerifier](../src/Wails.Net.Application/Security/Minisign/MinisignVerifier.cs) 验证流程：
  - 公钥长度校验（32 字节）
  - 完整签名长度校验（128 字节 = BLAKE2b-512 指纹 + Ed25519 签名）
  - 提取 BLAKE2b-512 指纹和 Ed25519 签名
  - 验证 BLAKE2b-512 指纹与数据一致（`Blake2Fast.Blake2b.ComputeHash`）
  - 使用 Ed25519 公钥验签（`NSec.Cryptography.SignatureAlgorithm.Ed25519`）
- **常量时间比较**：`CryptographicEquals` 防止时序攻击。
- [SignatureVerifier](../src/Wails.Net.Plugins.Updater/Services/Updater/SignatureVerifier.cs) 双验证路径：
  - `VerifyMinisignAsync` — minisign 签名验证路径（推荐）
  - `VerifyAsync` — 旧路径（Authenticode/GPG，标记 `[Obsolete]`）
- **Authenticode 验证**（Windows）：通过 PowerShell `Get-AuthenticodeSignature` + `ConvertTo-Json` 解析状态和签名者。
- **GPG 验证**（Linux）：通过 `gpg --verify --status-fd 1` 提取 `GOODSIG` 行的签名者。
- [HelperProcess](../src/Wails.Net.Plugins.Updater/Services/Updater/HelperProcess.cs) Helper 进程协议：
  - 哨兵环境变量 `WAILS_UPDATER_HELPER=1` 进入 helper 模式
  - 环境变量传递更新包路径、目标文件路径、父进程 PID
  - 等待父进程退出（30 秒超时）
  - 备份目标文件（`.bak`）
  - 重试替换（20 次，每次间隔 500ms）
  - Linux 上 `chmod +x` 恢复可执行权限
  - 清除 helper 环境变量后重启应用
  - 失败时从备份恢复
- [IUpdateProvider](../src/Wails.Net.Plugins.Updater/Services/Updater/IUpdateProvider.cs) 接口支持链式尝试多个更新源：
  - [HttpUpdateProvider](../src/Wails.Net.Plugins.Updater/Services/Updater/HttpUpdateProvider.cs) — 向后兼容默认 Provider
  - [GitHubUpdateProvider](../src/Wails.Net.Plugins.Updater/Services/Updater/GitHubUpdateProvider.cs) — 通过 GitHub REST API `repos/{owner}/{repo}/releases/latest` 获取
  - [GitLabUpdateProvider](../src/Wails.Net.Plugins.Updater/Services/Updater/GitLabUpdateProvider.cs) — 通过 GitLab REST API `projects/{projectId}/releases/permalink/latest` 获取

**差异分析**：
- Wails.Net 是**两者中唯一**支持 Windows Authenticode 自动签名的方案（通过 `signtool` / `azuresigntool`，环境变量门控）。
- Wails.Net 的多 Provider Updater + ProviderName 注入对齐 Wails v3，但比 Wails v3 更进一步（错误事件 payload 含 provider 字段）。
- Helper Process 模式 Wails.Net 与 Wails v3 对齐（无需单独 helper 二进制，通过环境变量重执行当前应用）。
- Wails.Net 的 BLAKE2b-512 + Ed25519 双重保护 + 常量时间比较是**安全最优**的实现。

---

### 3.9 移动端插件

| 实现项 | Wails.Net | Wails v3 |
|--------|-----------|---------|
| **条码扫描** | ✅ `BarcodeScannerPlugin` + `IPlatformBarcodeScanner` + Android 实现 | ❌ |
| **生物识别** | ✅ `BiometricPlugin` + `IPlatformBiometric` + Android 实现 | ❌ |
| **触觉反馈** | ✅ `HapticsPlugin` + `IPlatformHaptics` + Android 实现 | ✅ `Haptics` |
| **NFC** | ✅ `NfcPlugin` + `IPlatformNfc` + Android 实现 | ❌ |
| **相机** | ❌ | ❌ |
| **地理位置** | ❌ | ❌ |
| **通知（移动）** | ❌（桌面 NotificationPlugin 已实现） | ❌ |
| **推送** | ❌ | ❌ |
| **Android 运行时** | ✅ `AndroidRuntimePlugin`（`device.info` / `toast.show`，P2 新增） | ✅ `messageprocessor_android.go` |
| **NullObject 降级** | ✅ `NullXxxImpl` 桌面/Server no-op | N/A |
| **三层架构** | ✅ 接口 + 插件 + 平台实现 | N/A |

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
- [AndroidRuntimePlugin](../src/Wails.Net.Application.Android/Mobile/AndroidRuntimePlugin.cs)（P2 新增）：
  - `device.info` — 对应 Wails v3 `androidDeviceInfo()`，返回设备制造商、品牌、型号、SDK 版本等信息
  - `toast.show` — 对应 Wails v3 `androidShowToast(message)`，通过 `Android.Widget.Toast.MakeText` 显示
  - 自带权限集 `android-runtime:default`（`android-runtime:allow-device-info` / `android-runtime:allow-toast`）

**差异分析**：
- Wails.Net 自 P2 起 4 个移动端插件均补齐 Android 平台实现，但**移动端插件数量仍有限**（Wails.Net 仅 4 个 + 1 Android 运行时，可扩充 Camera / Geolocation / Notification / Push 等）。
- Wails v3 移动端支持尚不成熟，仅 `Haptics` 一个移动插件。
- Wails.Net 的 NullObject 降级模式让桌面/Server 模式下移动插件 no-op 不抛异常，是独创设计。
- Wails.Net 的 AndroidRuntimePlugin 对齐 Wails v3 `messageprocessor_android.go`，但采用 `device.*` / `toast.*` 形式的插件命令名。

---

### 3.10 Stronghold / SQL / Store 加密存储

| 实现项 | Wails.Net | Wails v3 |
|--------|-----------|---------|
| **Stronghold 加密 vault** | ✅ `StrongholdPlugin`（接口对齐） | ❌ |
| **SQL 数据库** | ✅ `SqlPlugin`（SQLite） | ❌ |
| **Store 键值存储** | ✅ `StorePlugin`（JSON 文件） | ❌ |
| **PersistedScope** | ✅ `PersistedScopePlugin`（持久化 Scope） | ❌ |
| **多数据库支持** | ❌（仅 SQLite） | ❌ |
| **加密强度** | ❌（仅接口对齐，未实现 IOTA Stronghold 硬件级隔离） | ❌ |

**Wails.Net 关键实现**：
- `StrongholdPlugin` 目前仅有接口骨架，未实现完整的 Stronghold 加密 vault。
- `SqlPlugin` 仅支持 SQLite（通过 `Microsoft.Data.Sqlite`）。
- `StorePlugin` JSON 文件后端键值存储。
- `PersistedScopePlugin` 是 Wails.Net 独有，持久化 Scope 配置。

**差异分析**：
- Wails.Net 的 SQL/Store 能力基本完整，Stronghold 加密 vault 仅有接口骨架，未实现硬件级隔离。
- Wails v3 完全无 SQL/Store/Stronghold 内置插件。

---

## 整体评估与差距

### 三大维度实现完整度

| 维度 | Wails.Net | Wails v3 |
|------|-----------|---------|
| **维度 1：Host/DI/Config/Logging** | ✅ 完整（参考 ASP.NET Core） | △ 部分（Service 模式，无独立 DI） |
| **维度 2：Runtime/Window/IPC** | ✅ 完整（参考 Wails v3） | ✅ 完整（基准实现） |
| **维度 3：Plugin/Security/Capability** | ✅ 完整（自研 ACL 模型） | ❌ 缺失（无 ACL 模型） |

### Wails.Net 相对优势

1. **维度 1 完整度最高**：完整采用 `Microsoft.Extensions.Hosting`/`DependencyInjection`/`Configuration`/`Options`/`Logging` 全栈，对 .NET 开发者最友好。`IOptionsMonitor<T>` 支持热重载是 Wails v3 不具备的。
2. **维度 2 创新设计**：在完整对齐 Wails v3 对象模型基础上，新增多传输层并行广播、NativeIpcTransport 混合策略、senderWindowId 全链路传播、Server 模式降级、CancellablePromise 等独有能力。
3. **维度 3 完整 ACL 模型**：自研 Capability + Permission + Scope 三层 ACL 模型（Wails v3 完全缺失），且支持参数级 Scope 校验。
4. **架构创新**：Logger 双向桥接（前端 console ↔ 后端 ILogger）、4 阶段插件生命周期、CommandDispatcher 中间件管道、表达式树编译零反射调用、AssetServer 双中间件管道 + IHttpServiceHandler 业务路由挂载等独创设计。
5. **内置插件最多**：42 个内置插件（37 桌面 + 4 移动端 + 1 Android 运行时），开箱即用。
6. **MenuRole 系统最完整**：21 个角色枚举 + 13 个工厂方法 + 3 个标准菜单组合 + AboutMetadata + 跨平台辅助工具，结合 Wails v3 Role 模型与自研工厂方法。
7. **Android 平台能力补齐**：4 个移动端插件 Android 平台实现 + AndroidRuntimePlugin + AndroidPlatformEvents + 3 个新公共事件。

### Wails.Net 相对差距（均绑定 `OPT-*` 任务编号）

1. **macOS/iOS 支持**：macOS 已有骨架实现（`Wails.Net.Application.MacOS`），iOS 暂不实现；Wails v3 已稳定支持。（无对应任务，遵循 AGENTS.md 范围）
2. **移动端插件生态深度**：移动端插件数量有限（Camera/Geolocation/Notification/Push 等可扩充）。（无对应任务，生态建设持续演进）
3. **Shell Scope**：`ShellPlugin` 尚未实现参数级 Scope 约束。（`OPT-SEC-003` 安全模板与迁移指南中覆盖）
4. **Stronghold 硬件级隔离**：仅接口骨架，未实现硬件级隔离。（无对应任务，依赖第三方）
5. **SQL 多数据库后端**：仅 SQLite，多后端支持待补齐。（无对应任务，需求驱动）
6. **日志轮转**：依赖第三方（如 Serilog），未内置 `RotationStrategy`。（无对应任务，可评估 `OPT-RUNTIME-002` 生命周期治理时一并考虑）
7. **AppImage / MSI 打包**：在打包路线图中推进。（无对应任务，见 `docs/development/build-and-pack.md`）
8. **统一 IPC 传输策略**：传输层选择与 fallback 链分散在平台实现中，缺少统一 capability 治理。（`OPT-IPC-003` 统一 IPC strategy）
9. **Host/UI/Window/IPC ready 语义**：`StartAsync` 返回与 UI 主循环/首窗口/runtime 注入/IPC 可用不等价。（`OPT-RUNTIME-002` 生命周期 ready 状态建模）
10. **插件前端资产承载**：插件更偏后端命令包，前端 runtime 与插件前端 API 缺少统一安装体验。（`OPT-PLUGIN-001` 插件资产 manifest / `OPT-PLUGIN-002` AssetServer 插件资源路由）
11. **插件命令/权限/前端 API 清单**：命令、权限、文档与前端包靠手写维护，存在漂移风险。（`OPT-PLUGIN-003` 插件命令权限清单生成）
12. **`Application` 聚合根职责**：facade 内部字段与协调职责过多，可测试性受影响。（`OPT-RUNTIME-001` Application 职责拆分）

### 架构策略验证

**架构策略（AGENTS.md §1.1.1）的成功指标**：

| 主线目标 | 参考来源 | 实现状态 | 验证 |
|---------|---------|---------|------|
| Host/DI/Config/Logging | ASP.NET Core | ✅ 完整 | `Microsoft.Extensions.*` 全栈集成，`IHostedService` 适配，`IOptionsMonitor` 热重载，`ILoggerProvider` 双向桥接 |
| Runtime/Window/IPC | Wails v3 | ✅ 完整 | `Application` 对象模型、`IPlatformApp` 平台抽象、FNV-1a 哈希一致性、CancellablePromise、Pre-emit 钩子、MenuRole 21 角色 |
| Plugin/Security/Capability | 自研 | ✅ 完整 | `IPlugin` 4 阶段生命周期、`PermissionManager` 6 个 ConcurrentDictionary、Capability `"!"` 前缀拒绝、Scope 参数级校验、Channel 协议、minisign 签名 |

**结论**：三大架构主线全部已完整实现。Host/DI 层沿用 ASP.NET Core 的成熟基础设施，Runtime/Window/IPC 层对齐 Wails v3 的对象模型，Plugin/Security/Capability 层由本项目自主设计并形成完整的安全能力体系，共同构成一个对 .NET 开发者最友好、企业级特性最完整、安全模型最严格的桌面/移动框架。后续优化按 [架构优化修改路线图](architecture/optimization-roadmap.md) 持续推进。

---

**文档结束**

> 本文档基于 2026-08-10 仓库代码状态生成（提交 `6a812ef`；P0 优化批次已落地）。如发现信息过时或错误，请提交 Issue 或 PR 更新，并遵循上文「文档维护规则」。
