# Wails.Net 架构融合审计与优化清单

> 本文基于 2026-08-15 对当前仓库代码、测试与架构文档的再次深度读取，确认 Wails.Net 的三条融合主线、已完成的架构调整，以及下一批可优化项：
>
> - **Host / DI / Config / Logging** → 学 ASP.NET Core（Microsoft.Extensions.* 全栈）
> - **Runtime / Window / IPC** → 学 Wails v3（对象模型、IPC、多窗口、事件总线）
> - **Plugin / Security / Capability** → 学 Tauri v2（插件能力、权限模型、安全设计）
>
> 本文是“事实基线 + 优化 backlog”，不是新的技术选型；所有建议均以不违反 AGENTS.md 中 .NET 10、禁反射、TUnit、F# 脚本等约束为前提。

## 1. 总体结论

当前项目已经形成“**ASP.NET Core 宿主骨架 + Wails v3 运行时对象模型 + Tauri v2 插件权限边界**”的融合架构，并且相较上一轮审计，若干此前列为 P0/P1 的闭环项已经落地：

1. **Host/DI/Config/Logging 主线已基本闭环**：`DesktopApplicationBuilder` 封装 `HostApplicationBuilder`，默认注册 Options、Validator、核心管理器、插件生命周期适配器和日志 Provider；`CommandDispatcher` 也已经从 DI 收集 `ICommandMiddleware`，补齐了 ASP.NET Core 风格命令管道扩展入口。
2. **Runtime/Window/IPC 主线具备 Wails v3 风格核心且开始模块化**：`Application` 仍作为用户友好的 facade，但生命周期 ready 语义、事件通配符、定向窗口事件和 `IIpcTransportStrategy` 已经补上，运行时逐步从“大聚合根”向 coordinator/strategy 过渡。
3. **Plugin/Security/Capability 主线已从功能插件推进到最小权限模型**：插件显式启用、命令统一进入 `CommandDispatcher`，`PermissionManager` 支持 Strict/Compatibility、deny、permission set、window、remote、scope、platform 过滤，模板与脚手架也开始默认生成 Strict + capabilities 示例。
4. **主要剩余优化空间从“抽象缺失”转为“清单化、平台一致性、可运维性”**：本轮（2026-08-15）已补齐 Android `WindowRuntimeReady` 派发（P0-2）、移除插件初始化临时 ServiceProvider（A-2/P1-5）、建立 Options/模板 drift check（P0-5/A-3）并补齐 `plugins.manifest.json`（C-1）；后续重点应放在平台窗口 fallback 链集中化、`Application` coordinator 化、asset protocol scope、Channel 加密与 NativeAOT/裁剪专项验证。

## 2. 当前架构快照

```text
用户 Program.cs / 模板
  └─ DesktopApplicationBuilder.CreateBuilder(args)
      ├─ Microsoft.Extensions.Configuration / Options / Logging
      ├─ IServiceCollection
      │   ├─ AddWailsManagers()
      │   ├─ AddWailsServices()
      │   ├─ PermissionManager / IpcOptions / Validators
      │   ├─ ICommandMiddleware（按 DI 注册顺序注入 CommandDispatcher）
      │   └─ IHostedService: DesktopHostedService + PluginHostedServiceAdapter
      ├─ UsePlugin<T>() 显式注册插件
      ├─ UseCommandMiddleware<T>() 注册命令中间件
      ├─ UseAutoPlatform() 或 UsePlatform<TPlatform>()
      └─ Build()
          ├─ InitializePlugins()
          ├─ IHost.Build()
          ├─ Application.InitializeFromServiceProvider()
          ├─ CommandDispatcher 注入 Application
          ├─ Capability 文件加载
          └─ AssetServer / IPC strategy / fallback transport

Application 聚合根
  ├─ BindingManager / GeneratedBindingRegistry
  ├─ EventProcessor（通配符 + EmitTo）
  ├─ WindowManager / DialogManager / ScreenManager / MenuManager / ChannelManager
  ├─ IPlatformApp / IWebviewWindowImpl
  ├─ ITransport / MessageProcessor / IIpcTransportStrategy
  ├─ CommandDispatcher
  └─ ApplicationLifecycleState / PlatformStarted ready signal

CommandDispatcher
  ├─ CommandRegistry 查表
  ├─ PermissionManager 检查 capability / permission / scope / origin / window
  ├─ ICommandMiddleware 管道
  ├─ JSON 参数绑定 + ICommandContext/CancellationToken/IServiceProvider 特殊参数注入
  └─ CompiledCommandInvoker 强类型调用
```

## 3. 主线 A：Host / DI / Config / Logging → ASP.NET Core

### 3.1 已落地设计

| 维度 | 当前实现 | 判断 |
|------|----------|------|
| Host | `DesktopApplicationBuilder` 内部持有 `HostApplicationBuilder`，`DesktopApplication` 封装 `IHost` | 已对齐 Generic Host |
| DI | 核心管理器、平台抽象、权限管理、插件管理器、日志 Provider、中间件均进入 `IServiceCollection` | 已具备统一容器 |
| Config | `DesktopHostOptions` 绑定 `Wails` 节，`PermissionOptions` 绑定 `Wails:Permissions`，`IpcOptions` 绑定 `Wails:Ipc` | 已从散落配置迁移到 Options |
| Validation | `DesktopHostOptionsValidator`、`PermissionOptionsValidator`、`IpcOptionsValidator` 均 `ValidateOnStart` | 启动期失败能力已具备 |
| Logging | 使用 `ILogger<T>`，并通过 `LogServiceLoggerProvider` 桥接到 Wails 日志服务 | 已具备后端日志统一入口 |
| Lifecycle | `DesktopHostedService` 适配 `Application`，`PluginHostedServiceAdapter` 适配插件生命周期 | 方向正确，ready 语义已增强 |
| Middleware | `UseCommandMiddleware<T>()` + `GetServices<ICommandMiddleware>()` 注入命令管道 | A-2 已闭环 |

### 3.2 架构评价

宿主层已经从传统桌面程序的“手写 main + 全局单例”迁移为接近 ASP.NET Core 的组合模型：先组合 `IServiceCollection`、Options 与日志，再 Build Host，最后由 hosted service 管理生命周期。此方向带来三项收益：

- **可测试性**：核心能力可通过 DI 替换为 mock / server mode 实现。
- **可观测性**：日志、配置校验、生命周期事件可以复用 .NET 标准生态。
- **可组合性**：插件服务、命令、权限声明和命令中间件统一进入 Host 构建管线。

### 3.3 已完成的架构调整

#### A-DONE-1：命令中间件 Builder 级入口已闭环

此前审计认为 `CommandDispatcher` 支持中间件但 Build 阶段未从 DI 注入。当前代码已经提供 `UseCommandMiddleware<TMiddleware>()`，并在 `Build()` 中通过 `host.Services.GetServices<ICommandMiddleware>().ToArray()` 注入 `CommandDispatcher`。这使日志、审计、限流、指标等横切能力可以像 ASP.NET Core middleware 一样组合。

#### A-DONE-2：插件双阶段生命周期约束已写入 API 文档

`IPlugin.ConfigureServices` 已明确只做 DI 注册，不应依赖已构建的 `IServiceProvider`；`IPlugin.Configure` 已明确用于命令、权限和配置，不应再追加必须参与 Host 构造的服务。该约束降低了临时 ServiceProvider 与最终 Host 实例不一致的风险。

#### A-DONE-3：配置根节与启动期校验已收敛

Options 已统一绑定 `Wails` 根节及其子节，并通过 Validators + `ValidateOnStart` 前置配置错误。安全模板和脚手架已开始生成 `Wails:Permissions`、`Wails:Ipc`、`Wails:App:Security` 与 capabilities 目录示例。

### 3.4 剩余优化项

#### A-1：热重载边界需要产品级文档固化

代码使用 `IOptionsMonitor` 读取当前配置，但桌面应用中并非所有配置都适合热刷新。应在配置文档中明确：

- **启动期配置**：平台选择、主窗口初始参数、WebView 初始化参数、资源根目录、权限模式。
- **可运行时刷新配置**：日志级别、部分插件选项、可安全重建的业务配置。
- **不保证热刷新配置**：平台 runtime 参数、已创建窗口的 WebView 环境、Strict/Compatibility 模式切换、capabilities 目录切换。

> ✅ **已落地（2026-08-15）**：热重载边界已写入 `docs/architecture/hosting-and-lifecycle.md` §A-4（启动期快照 / 可运行时刷新 / 不保证热刷新 三分类，并约定 `IOptionsMonitor.OnChange` 显式订阅）。

#### A-2：临时 ServiceProvider 仍是插件初始化的风险点

`InitializePlugins()` 为了在 Host 构建前获得 `LoggerFactory` 和 `PermissionManager` 会构建临时 ServiceProvider，并在拿到 `PermissionManager` 后重新注册实例。该设计可工作，但属于维护风险点：

- 插件若在 `ConfigureServices` 注册有状态 singleton，临时容器与最终 Host 容器可能产生双实例。
- `PermissionManager` 通过重注册实例保持一致，但其它服务不具备这种保护。

**建议**：中期引入专用 `PluginBuildContext`，将 LoggerFactory 替换为构建期 logger abstraction 或 NullLogger，权限声明先写入轻量收集器，Host 构建后再统一 materialize 到 `PermissionManager`，从而移除临时容器。

> ✅ **已落地（2026-08-15）**：新增 `DeferredPermissionRegistrar`（`src/Wails.Net.Application/Plugins/DeferredPermissionRegistrar.cs`）轻量权限声明收集器；`InitializePlugins()` 不再构建临时 ServiceProvider，改用 `NullLoggerFactory.Instance` 作为构建期 logger abstraction，权限集/作用域声明先写入收集器；`Build()` 在 Host 构建后、`LoadCapabilities` 之前统一 `ReplayInto(permissionManager)` materialize，Scope 初始化同步移至最终容器执行，消除双实例风险。

#### A-3：Options 与模板需要持续做 drift check

模板、Demo、文档、CLI 脚手架已经向 `Wails` 根节收敛，但后续每次新增配置都应同步：

1. `DesktopHostOptions` / 子 Options。
2. Validator。
3. 模板 `appsettings.json`。
4. Demo 示例。
5. 配置文档。
6. CLI `check` 或测试中的示例配置验证。

> ✅ **已落地（2026-08-15）**：新增 `tests/Wails.Net.Cli.Tests/AppSettingsTemplateDriftTests.cs`——解析脚手架模板与 Demo 的 `appsettings.json`，校验 `Wails` 节全部配置键都能绑定到 `DesktopHostOptions` / `PermissionOptions` / `IpcOptions` 的公共属性路径，模板键漂移（模板改了 Options 未改，或反之）会直接测试失败。

## 4. 主线 B：Runtime / Window / IPC → Wails v3

### 4.1 已落地设计

| 维度 | 当前实现 | 判断 |
|------|----------|------|
| 聚合根 | `Application` 管理绑定、事件、窗口、服务、平台应用 | 与 Wails v3 对齐 |
| 绑定 | `BindingManager` + `GeneratedBindingRegistry`，生产代码避免运行时反射调用 | 比传统反射式绑定更 AOT 友好 |
| 窗口 | `WindowManager` 管理 `WebviewWindow`，平台侧实现 `IWebviewWindowImpl` | 多窗口模型清晰 |
| 事件 | `EventProcessor` 支持订阅、once、hook、sender window、通配符和定向窗口事件 | Wails 风格事件总线增强 |
| IPC | `MessageProcessor` 统一处理 call/cancel/event；Native、HTTP、WebSocket、EventIPCTransport 多传输并存 | 有较强降级能力 |
| Strategy | `IIpcTransportStrategy` / `DefaultIpcTransportStrategy` 管理 Server mode 等场景装配 | 传输选择开始集中化 |
| Lifecycle | `ApplicationLifecycleState` + `WaitUntilPlatformStartedAsync` | Host/UI ready 语义更清晰 |
| 平台 | `PlatformFactory` 支持自动检测、模块初始化器注册与 Server 模式 | 适合三平台 + 容器测试 |

### 4.2 架构评价

运行时层保留 Wails v3 的核心心智模型：一个 `Application` 管理运行时资源，窗口是对象，事件是跨窗口总线，IPC 是前后端调用底层协议。与 Go Wails 的运行时动态绑定不同，Wails.Net 选择编译期生成与显式注册，这对 NativeAOT、裁剪、泛型类型安全和禁反射约束更友好。

平台层最重要的调整是：使用 `IPlatformApp` + `[ModuleInitializer]` 注册委托替代反射创建平台对象。这在遵守“生产代码禁止反射发现/调用”的同时，仍保留 `UseAutoPlatform()` 的体验。

### 4.3 已完成的架构调整

#### B-DONE-1：事件模型补齐通配符与跨窗口定向

后端 `EventProcessor` 与前端 runtime 已支持 `*` 后缀前缀匹配，`CustomEvent.TargetWindowID`、`EventProcessor.EmitTo`、`Application.EmitTo` 和窗口侧 dispatch 能将事件限定到目标窗口。该方向对齐 Wails v3 多窗口事件模型，也减少前端自行过滤事件的错误空间。

#### B-DONE-2：Host/UI ready 语义已从“启动线程已创建”推进到“平台主循环就绪”

`ApplicationLifecycleState`、`_platformStartedTcs` 和 `WaitUntilPlatformStartedAsync` 让 `DesktopHostedService.StartAsync` 可以等待平台 ready 或快速感知平台启动异常。此前未 await 的 `_startedTcs` 语义问题已修正。

#### B-DONE-3：IPC 传输策略开始集中化

`IIpcTransportStrategy` 与 `DefaultIpcTransportStrategy` 已经把 Server 模式装配从散落逻辑中抽出，后续可继续承接桌面平台 fallback 链、Android WebMessageListener fallback 与 HTTP/WebSocket 安全策略。

### 4.4 剩余优化项

#### B-1：`Application` 聚合根仍偏重，需要继续执行 coordinator 化

`Application` 仍同时承担运行时对象聚合、平台注入、服务生命周期、插件生命周期、窗口管理代理、资源服务注入、命令调度器挂载、自定义 scheme 与 shutdown hooks。短期保持 facade 对用户友好是正确的，但新增职责应优先进入内部协调器：

- `RuntimeServicesCoordinator`：负责 `IServiceStartup` / `IServiceShutdown`。
- `FrontendRuntimeCoordinator`：负责 runtime JS、asset server、IPC transport 绑定。
- `PlatformLifecycleCoordinator`：负责 `IPlatformApp`、窗口主循环、server mode lifecycle。
- `ServiceRouteCoordinator`：负责 service route、custom scheme、asset protocol scope。

#### B-2：平台自动加载失败诊断仍可更面向普通用户

`PlatformFactory` 能自动加载平台程序集触发模块初始化器。若平台包未引用或 native 依赖缺失，错误对维护者足够清晰，但普通用户仍需要可复制修复指令。

**建议**：异常消息补充：

- Windows：引用 `Wails.Net.Application.Windows` 或 `Wails.Net.Bundle.Windows`，调用 `builder.UseWindows()` 或保留包引用以供 `UseAutoPlatform()` 加载。
- Linux：引用 `Wails.Net.Application.Linux`，并安装 GTK4/WebKitGTK 原生依赖。
- Android：使用 `net10.0-android36.0`，引用 Android 平台包，并从 Activity 生命周期初始化。
- Server：若无 GUI 依赖，明确提示如何显式选择 Server mode。

#### B-3：IPC transport 选择矩阵需进入正式文档并绑定实现状态

当前建议矩阵如下，后续应写入 `docs/architecture/transport-and-ipc.md` 并随实现同步：

| 场景 | 首选 | 回退 | 取消调用 | 多窗口广播 | 状态 |
|------|------|------|----------|------------|------|
| Windows WebView2 | Native IPC | EventIPC / HTTP / WebSocket | 支持 | EventIPC/WebSocket | fallback 链待集中化 |
| Linux WebKitGTK | Native IPC | EventIPC / HTTP / WebSocket | 支持 | EventIPC/WebSocket | fallback 链待集中化 |
| Android WebView | WebMessageListener | HTTP | 支持 | Android event bridge | ✅ ready 派发已补齐（2026-08-15） |
| Server Mode | HTTP | WebSocket | 部分支持 | WebSocket | strategy 已接入 |

> ✅ **已落地（2026-08-15）**：选择矩阵已写入 `docs/architecture/transport-and-ipc.md` §1.1。Android 端 `WailsWebViewClient.OnPageFinished` 新增 `WindowRuntimeReady` 派发（对齐 Windows `NavigationCompleted` / Linux runtime 注入 / macOS 的 `emit(WindowRuntimeReady)`），三平台 ready 语义一致。

#### B-4：绑定系统“双轨”需继续文档化与测试化

应用业务 API 推荐 `[Binding]` + source generator；系统/插件能力推荐 `IPlugin` + `MapCommand` + capability。后续内置能力不得绕过 `CommandDispatcher` 权限链。建议为 `docs/architecture/binding-and-command-system.md` 增补：

- 绑定路径适用场景。
- 命令路径适用场景。
- 命令别名命名规范。
- capability 与 `[RequireCapability]` 的来源优先级。
- 禁止新增生产反射绑定路径的检查清单。

> ✅ **已落地（2026-08-15）**：`docs/architecture/binding-and-command-system.md` §1.1 已含双轨职责边界与推荐路径表；本次增补 §1.1.1（命令命名规范、capability 来源优先级表、禁止反射绑定路径检查清单），并同步 P1-2 机器可读清单（commands/capabilities/plugins manifest）说明。

## 5. 主线 C：Plugin / Security / Capability → Tauri v2

### 5.1 已落地设计

| 维度 | 当前实现 | 判断 |
|------|----------|------|
| 插件启用 | `UsePlugin<T>` 显式注册，反射扫描入口已弃用并直接抛错 | 符合 AOT 与最小装配 |
| 命令注册 | `CommandRegistry` + `MapCommand`，Minimal API 风格 | 体验清晰 |
| 权限校验 | `CommandDispatcher` 调用 `PermissionManager` | 权限链已接入 |
| Capability | `CapabilityFileLoader`、`Capability`、`PermissionSet`、`RequireCapabilityAttribute` | 已具备声明模型 |
| deny 优先 | `PermissionManager` 有 denied permissions，支持 `!permission` | 接近 Tauri deny 语义 |
| Window/Remote | capability 支持窗口范围和 remote URL 限制 | 已支持核心安全边界 |
| Platform | `Capability.Platforms` + `PermissionManager.CurrentPlatform` | 平台级隔离已落地 |
| Scope | `IScope`、`ScopeParameterAttribute`、文件/路径类插件参数级限制 | 方向正确 |
| IPC 安全 | `IpcOriginValidator`、`IpcHandshakeToken`、`IpcOptions` | 覆盖本地/远程 origin 风险 |
| 模板 | 新模板开始生成 Strict、capabilities、CSP 示例 | 安全默认值已前移 |

### 5.2 架构评价

插件安全模型已经不只是“命令命名空间”，而是具备 Tauri v2 capability 的关键元素：权限可以声明、组合为 permission set、限定窗口、限定 remote origin、deny、按平台过滤、对参数做 scope 校验。对 Wails.Net 来说，这是一条重要差异化路线：保持 Wails 的轻量运行时，同时引入 Tauri 的显式安全边界。

### 5.3 已完成的架构调整

#### C-DONE-1：Strict 权限模板与迁移指南已落地

库层保留 Compatibility 以兼容旧应用；模板、脚手架和 Demo 已开始默认提供 Strict + `DenyByDefault` + capability 文件示例。该策略符合“库层兼容、模板推动安全默认”的迁移节奏。

#### C-DONE-2：Capability 平台过滤和构建期校验已落地

`Capability.platforms` 已进入运行时授权路径，CLI `check` / build 前置校验可发现 JSON schema、平台名、权限标识格式与权限存在性问题。安全错误从运行期提前到构建期，是 Tauri v2 风格能力模型的重要补强。

#### C-DONE-3：CSP 模板默认启用

全局 CSP 已能注入 AssetServer，模板也开始生成 CSP 示例。当前仍应允许用户按应用放宽策略，但生产模板默认收紧是正确方向。

### 5.4 剩余优化项

#### C-1：插件命令、权限与前端 API 需要统一 manifest

当前 capability 与命令注册都存在，但维护者仍需要人工确认“命令是否都声明了权限”“权限是否有无效命令”“前端 wrapper 是否与后端命令一致”。

**建议**：新增测试期/构建期 manifest：

1. 从 `CommandRegistry` 注册结果输出 `commands.manifest.json`。
2. 从 capability 文件输出 `capabilities.manifest.json`。
3. 从插件包元数据输出 `plugins.manifest.json`。
4. 测试断言：敏感内置命令必须被 capability 覆盖；未覆盖命令必须显式标注 `AllowAnonymous` 或 `NoCapabilityRequired`。
5. 前端 TypeScript wrapper、插件文档命令表和 CLI `plugin inspect` 从 manifest 生成。

> ✅ **已落地（2026-08-15）**：commands/capabilities manifest 此前已由 `ManifestGenerator` 提供；本次补齐 `plugins.manifest.json`（`GeneratePluginsManifest` / `GeneratePluginsManifestJson` + `PluginsManifestFileName` 常量 + `DesktopApplication.GeneratePluginsManifest` / `WritePluginsManifestToDirectory` 导出），并修复 `FindUncoveredCapabilities` 中 `!` 前缀显式拒绝被误计入覆盖集合的 bug（拒绝不是授权）。三份清单统一由 `ManifestsJsonSerializerContext` 源生成序列化（NativeAOT 兼容）。

#### C-2：插件包拆分后需要架构边界测试

应建立分层约束并用测试保护：

- `Wails.Net.Application` 只包含插件抽象、命令调度、安全模型、平台基础接口。
- `Wails.Net.Plugins.*` 实现具体插件，依赖 Application，不允许 Application 反向依赖插件包。
- `Wails.Net.Bundle.*` 只做平台与常用插件组合包，不承载核心运行时逻辑。
- CLI 模板选择 bundle 或推荐插件组，避免用户手动拼装大量包。

建议新增架构测试：解析项目引用图，断言 `src/Wails.Net.Application/Wails.Net.Application.csproj` 不引用 `Wails.Net.Plugins.*`，Bundle 不被核心项目引用。

#### C-3：asset protocol scope / `convertFileSrc` 仍是 Tauri 对齐缺口

当前静态资源与 CSP 已增强，但“前端受控访问本地文件”的 asset protocol scope 尚未闭环。建议新增：

- `Wails:Security:AssetProtocol` 或 `Wails:App:Security:AssetProtocol` 配置。
- capability/scope 控制可暴露路径。
- 前端 `convertFileSrc(path)` 只生成受控 scheme URL。
- AssetServer / 平台 custom scheme 拦截时再次校验 scope。

#### C-4：Store/Stronghold/PersistedScope 与 capability 可进一步整合

持久化、密钥和路径授权属于高敏感能力。建议后续将 Store、Stronghold、PersistedScope 的默认命令能力声明纳入 manifest，并在模板 capabilities 中给出最小授权示例，而不是只靠插件文档说明。

## 6. 横向优化路线图

### P0：短期必须收敛（建议 1~2 个迭代）

| 编号 | 优化项 | 收益 | 风险 | 状态 |
|------|--------|------|------|------|
| P0-1 | 将 transport 选择矩阵写入 `docs/architecture/transport-and-ipc.md` | 降低三平台调试成本 | 低 | ✅ 已落地 |
| P0-2 | Android `WindowRuntimeReady` / runtime ready 派发补齐 | 消除移动端生命周期缺口 | 中 | ✅ 已落地（2026-08-15） |
| P0-3 | 平台自动加载失败消息补充可复制修复指令 | 降低首跑失败成本 | 低 | ✅ 已落地 |
| P0-4 | 配置热重载边界文档化 | 避免用户误以为所有配置可热刷新 | 低 | ✅ 已落地 |
| P0-5 | 建立 Options/模板/文档 drift check | 防止配置示例漂移 | 中 | ✅ 已落地（2026-08-15） |

### P1：中期结构优化（建议 3~5 个迭代）

| 编号 | 优化项 | 收益 | 风险 | 状态 |
|------|--------|------|------|------|
| P1-1 | 拆出 `Application` 内部 coordinator | 防止聚合根继续膨胀 | 中 | 待实施 |
| P1-2 | 插件 commands/capabilities/plugins manifest | 支撑文档、测试、前端 API 统一 | 中 | ✅ 已落地（plugins.manifest 2026-08-15 补齐） |
| P1-3 | CLI `doctor` 增加安全/平台/原生依赖诊断 | 提升用户可运维性 | 低 | ✅ 已落地 |
| P1-4 | 平台窗口 fallback 链集中到 `IIpcTransportStrategy` | 降低传输层维护成本 | 中 | 待实施 |
| P1-5 | 移除插件初始化临时 ServiceProvider | 消除双 singleton 风险 | 中 | ✅ 已落地（2026-08-15） |
| P1-6 | 架构边界测试（Application 不依赖 Plugins.*） | 防止分层腐化 | 低 | ✅ 已落地 |

### P2：长期演进（版本边界内规划）

| 编号 | 优化项 | 收益 | 风险 |
|------|--------|------|------|
| P2-1 | Runtime API wrapper 从 manifest 全量生成 | 消除前后端 API 漂移 | 中高 |
| P2-2 | asset protocol scope / `convertFileSrc` | 对齐 Tauri v2 本地文件安全体验 | 中 |
| P2-3 | Channels 对称加密 | 对齐 Wails v3 channel 加密能力 | 中 |
| P2-4 | NativeAOT / trimming 发布专项 CI | 验证禁反射收益 | 中 |
| P2-5 | Capability 与 Store/Stronghold/PersistedScope 深度整合 | 接近 Tauri 安全体验 | 中 |

## 7. 建议的目标架构边界

```text
Wails.Net.Application
  允许：
    - Host / DI / Options / Logging 基础设施
    - Application facade 与 runtime manager/coordinator
    - CommandDispatcher / CommandRegistry / middleware abstraction
    - Security / Permission / Capability abstraction
    - Platform abstraction / Server mode
  禁止：
    - 依赖具体 Wails.Net.Plugins.* 包
    - 新增运行时反射发现/调用业务方法
    - 直接绕过 CommandDispatcher 暴露敏感原生能力

Wails.Net.Plugins.*
  允许：
    - 依赖 Wails.Net.Application
    - 注册命令、服务、权限集、capability
    - 调用平台抽象接口
  禁止：
    - 自行建立独立 IPC 协议绕过权限链
    - 将 CancellationToken 作为前端业务参数

Wails.Net.Application.{Windows,Linux,Android,MacOS}
  允许：
    - 实现 IPlatformApp / IWebviewWindowImpl / 平台服务接口
    - 通过 ModuleInitializer 注册平台委托
  禁止：
    - 让 Application 核心反向引用具体平台实现
    - 引入非选型 GUI 框架

Wails.Net.Bundle.*
  允许：
    - 聚合平台包和推荐插件包
    - 面向模板提供一站式引用
  禁止：
    - 承载核心运行时逻辑
```

## 8. 决策建议

1. **保持当前三主线融合方向不变**：不要回退到手写宿主、反射绑定或无权限插件模型。
2. **短期优先补齐“平台一致性和可诊断性”**：Android ready、transport 矩阵、平台加载错误与配置热重载边界最能降低用户踩坑。
3. **中期用 manifest 打通插件、安全、前端 API 与文档**：这会显著降低插件继续扩展时的维护成本。
4. **长期控制 `Application` 聚合根复杂度**：新增运行时职责优先通过 coordinator、strategy 或服务进入 DI，而不是继续堆到 `Application`。
5. **安全策略坚持库层兼容、模板严格**：库层 Compatibility 保护迁移体验，模板/CLI/check/doctor 推动生产项目使用 Strict。

## 9. 架构 PR 验收清单

后续每次架构相关 PR 可按以下清单自检：

- [ ] 新增服务是否进入 `IServiceCollection`，而不是全局单例？
- [ ] 新增配置是否使用 Options + Validator + `Wails` 根节？
- [ ] 新增日志是否使用 `ILogger<T>`，而不是 `Console.WriteLine`？
- [ ] 新增窗口/IPC 能力是否经过 `Application`/manager/coordinator/transport 分层？
- [ ] 新增插件命令是否经过 `CommandDispatcher`？
- [ ] 新增敏感命令是否声明 capability / permission / scope？
- [ ] 新增平台能力是否在平台包内通过抽象接口暴露？
- [ ] 生产代码是否仍然遵守禁用运行时反射发现/调用的约束？
- [ ] 文档、前端 API、命令名、capability 是否保持一致？
- [ ] 模板、Demo、Options、Validator 和配置文档是否同步更新？
- [ ] 若修改 web runtime 或可视 UI，是否按要求截图或说明无需截图？


---

## 10. 2026-08-18 审计基线：R0 止血 + R1-R4 路线图

> 本节为 2026-08-18 架构审计（详见 `architecture-audit-2026-08-18.md`）的精简路线图，
> 与上文 §1-9 形成闭环跟踪：本文档（2026-08-15 事实基线 + backlog）+ 审计（2026-08-18 P0/P1/P2 + R0-R4）。
> 详细事实链、P0 缺陷与 P1/P2 优化建议请查阅审计文档本体。

### 10.1 R0：安全和正确性止血（审计基线起算）

| 项 | 状态 | 落地位置 | 测试 |
|---|---|---|---|
| R0-1 Stronghold nonce 复用修复 | ✅ 已完成 | `src/Wails.Net.Plugins.Stronghold/StrongholdPlugin.cs` 每次加密前生成 12 字节 freshNonce + 原子文件替换 | 9/9 单元测试通过 |
| R0-2 IpcOriginValidator 精确化 | ✅ 已完成 | `Security/IpcOriginValidator.cs` `Uri.TryCreate` + 严格 Host 比对替代 StartsWith 前缀匹配 | 18/18 测试通过（12 新增绕过串 + 6 原有） |
| R0-3 Native IPC 强制覆盖 WindowId/Origin | ✅ 已完成 | `Transport/NativeIpcTransport.cs` `parsed.WindowId = windowId` + `ResolveOriginFromPlatform` | 27/27 测试通过（6 新增 observer.record） |
| R0-4 Strict 拒绝无安全声明命令 | ✅ 已完成 | `Security/PermissionManager.cs` `IsStrict` + `Commands/CommandDispatcher.cs` Strict 守卫 | 12/12 测试通过（5 新增） |
| R0-5 window.* 禁止无权限 fallback | ✅ 已完成 | `Transport/MessageProcessor.cs` `ProcessCommandFallbackAsync` 失败时返回错误响应 | 3/3 新增测试通过 |
| R0-6 Server Mode transport 启动顺序 | ✅ 已完成 | `Hosting/ServerModeTransportExtensions.cs` 装配前移到 DI 单例工厂阶段 | 3/3 新增测试通过 |
| R0-7 refactor-and-hardening-plan.md 文档漂移 | ✅ 已完成 | 文档头部添加状态漂移说明表 | N/A（文档修复） |
| R0-8 fusion-architecture-audit.md 合并路线图 | ✅ 已完成 | 本节即 R0-8 落点 | N/A（文档合并） |

### 10.2 R1：统一运行时事实源（下一批次）

- Application 使用 DI 构造注入，移除手动 new + SetPlatformApp 路径。
- Binding/Event/Window 注册表唯一化（删除 `Application._windows` 备份）。
- 移除 `Application.Get()` 在平台和插件中的新增使用（保留向后兼容 shim）。
- 建立真实平台 Ready 信号与 UI 线程 Shutdown 超时统一。

### 10.3 R2：统一调用与传输管线

- 建立 `InvocationGateway` 统一 Bind/Command 入口。
- Binding 与 Plugin Command 共用授权、Scope、DI Scope、错误模型。
- WebSocket mailbox、控制通道和背压机制。
- 四平台 `WebviewRequestPipeline` 异步化（消除 UI 线程 `.GetAwaiter().GetResult()` 阻塞）。

### 10.4 R3：完成 Tauri 风格 ACL

- 全量迁移 47 插件 304 命令到 `Require/Public`。
- 权限清单随 NuGet 包分发（对齐 Tauri `permissions/*.toml`）。
- Capability JSON Schema 与构建期校验。
- Scope 与 capability/window/origin grant 绑定。
- 完成后再将库默认从 Compatibility 翻转为 Strict（破坏性变更，需配合迁移指南）。

### 10.5 R4：清理与生态闭环

- `PluginDescriptor` 与完整 manifest 三件套（commands/capabilities/events）。
- Channel 协议（前后端加密通道）。
- Asset protocol scope（`wails-asset://` + `AssetProtocolScope` + capability 授权）。
- Options 单一来源（消灭散落 `_jsonOptions` / `_runtimeOptions` 副本）。
- 结构化日志与可观测性（统一 ILogger + traceId 关联）。

### 10.6 核心验收标准（继承自审计 §7）

- [x] Host、Application、Server Transport 解析出的 BindingManager/EventProcessor 引用完全相同。
- [ ] 全仓库每个命令显式标记 `Require` 或 `Public`（R3 进行中）。
- [x] `[Binding]` 和插件命令均经过同一 ACL（`CommandDispatcher` 统一入口）。
- [x] 低权限窗口无法伪造高权限 WindowId（R0-3 强制覆盖）。
- [x] `localhost.evil.example` 被拒绝（R0-2 IpcOriginValidator 精确化）。
- [ ] 未声明 remote 的 capability 拒绝远程页面（R3 Scope/Remote 校验进行中）。
- [x] 单连接长调用期间 cancel 可立即生效（已有 CancellationToken 串联）。
- [x] 同一 WebSocket 不发生并发 SendAsync（R2-2 已完成：`WebSocketBroadcaster` 改为每连接 `ClientMailbox`（Channel<ReadOnlyMemory<byte>> 单写者）；生产者经 `EnqueueOrLogAsync` 统一入队，邮箱满时阻塞形成背压；`RemoveClientAsync` 与 `StopAsync` 通过 `DisposeAsync(drainTimeout)` 等待写入循环退出，避免悬挂 SendAsync 访问已释放 socket）。
- [x] Host Stop 后 UI 线程在 shutdown timeout 内退出（R1-4 已完成：ApplicationOptions.ShutdownTimeout 默认 InfiniteTimeSpan；IPlatformApp 新增 ShutdownAsync(CancellationToken) 默认实现委派 Destroy；Application 新增 ShutdownAsync(CancellationToken) 异步关闭路径串联外部 CancellationToken + ShutdownTimeout；DesktopHostedService.StopAsync 改用 ShutdownAsync，UI 线程 Join 超时在外部 cancellationToken 已触发时立即放弃；新增 ShutdownAsyncTests 7/7 场景覆盖）。
- [ ] 所有 `IpcOptions` 字段都有行为测试，或从公共 API 删除（R3/R4 进行中）。
- [x] 全仓库只保留一个逻辑窗口注册表（R1-2 已完成：删除 `Application._windows`/`_nextWindowId`，统一委托给 WindowManager；新增 `WindowRegistryUnificationTests` 4/4 通过；连带修复 `WindowStatePlugin.SaveState/RestoreState` 平台未绑定早返回）。
- [x] 移除 `Application.Get()` 在新增代码中的使用（R1-3 已完成：WindowManager 构造期接受 Application 引用注入，新增 `SetApplication` 方法处理 DI 覆盖路径，移除 WindowManager 内部 2 处 `Application.Get()` 调用；新增 `ApplicationResolutionTests` 5/5 场景覆盖；历史遗留的 100+ 处 `Application.Get()` 调用保留向后兼容 shim）。
- [ ] 四平台 Asset/CSP/nonce/runtime-ready 使用同一共享管线（R2/R4 进行中）。

> **跟踪说明**：上述条目与审计文档 `architecture-audit-2026-08-18.md` §6 R0-R4 + §7 同步维护；
> 后续每完成一项 R1-R4 子任务，应同时更新两处状态，避免文档再次漂移（参考审计 §8 文档治理建议）。
