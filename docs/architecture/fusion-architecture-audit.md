# Wails.Net 架构融合审计与优化清单

> 本文基于当前仓库代码与既有架构文档，对 Wails.Net 的三条融合主线进行深度读取后的落地分析：
>
> - **Host / DI / Config / Logging** → 学 ASP.NET Core（Microsoft.Extensions.* 全栈）
> - **Runtime / Window / IPC** → 学 Wails v3（对象模型、IPC、多窗口、事件总线）
> - **Plugin / Security / Capability** → 学 Tauri v2（插件能力、权限模型、安全设计）
>
> 目标不是重新定义技术选型，而是确认当前架构调整是否已经在代码中闭环，并给出可执行、可排序的优化项。

## 1. 总体结论

当前项目已经形成“**ASP.NET Core 宿主骨架 + Wails v3 运行时对象模型 + Tauri v2 插件权限外壳**”的融合架构：

1. **Host/DI/Config/Logging 主线基本落地**：`DesktopApplicationBuilder` 封装 `HostApplicationBuilder`，暴露 `Services`、`Configuration`、`Logging`，默认注册 Options 校验、核心管理器、插件生命周期适配器和日志 Provider，方向与 ASP.NET Core 的组合式启动模型一致。
2. **Runtime/Window/IPC 主线已具备 Wails v3 风格核心**：`Application` 仍是运行时聚合根，`WindowManager`、`EventProcessor`、`BindingManager`、`MessageProcessor`、多传输实现和平台抽象分层清晰，Server 模式提供无 GUI 降级路径。
3. **Plugin/Security/Capability 主线已从“功能插件”推进到“能力边界”**：插件通过 `IPlugin` + `UsePlugin<T>` 显式启用，命令进入 `CommandDispatcher` 后统一做权限校验，`PermissionManager` 支持 capability、permission set、window、remote、deny、scope 与平台过滤。
4. **主要优化空间在一致性与可运维性**：当前代码已经较多完成架构调整，但还存在若干“概念已具备、体验未完全收敛”的点，例如插件初始化与 Host 生命周期边界、配置节/文档命名一致性、命令中间件注入闭环、权限策略默认模式迁移、平台自动加载失败的诊断体验、前端 runtime API 与后端命令目录的机器可读对齐。

## 2. 当前架构快照

```text
用户 Program.cs
  └─ DesktopApplicationBuilder.CreateBuilder(args)
      ├─ Microsoft.Extensions.Configuration / Options / Logging
      ├─ IServiceCollection
      │   ├─ AddWailsManagers()
      │   ├─ AddWailsServices()
      │   ├─ PermissionManager / IpcOptions / Validators
      │   └─ IHostedService: DesktopHostedService + PluginHostedServiceAdapter
      ├─ UsePlugin<T>() 显式注册插件
      ├─ UseAutoPlatform() 或 UsePlatform<TPlatform>()
      └─ Build()
          ├─ InitializePlugins()
          ├─ IHost.Build()
          ├─ Application.InitializeFromServiceProvider()
          ├─ CommandDispatcher 注入 Application
          └─ AssetServer / Native IPC / fallback transport

Application 聚合根
  ├─ BindingManager / GeneratedBindingRegistry
  ├─ EventProcessor
  ├─ WindowManager / DialogManager / ScreenManager / MenuManager / ChannelManager
  ├─ IPlatformApp / IWebviewWindowImpl
  ├─ ITransport / MessageProcessor
  └─ CommandDispatcher

CommandDispatcher
  ├─ CommandRegistry 查表
  ├─ PermissionManager 检查 capability / permission / scope / origin
  ├─ ICommandMiddleware 管道
  ├─ JSON 参数绑定 + 特殊参数注入
  └─ CompiledCommandInvoker 强类型调用
```

## 3. 主线 A：Host / DI / Config / Logging → ASP.NET Core

### 3.1 已落地设计

| 维度 | 当前实现 | 判断 |
|------|----------|------|
| Host | `DesktopApplicationBuilder` 内部持有 `HostApplicationBuilder`，`DesktopApplication` 封装 `IHost` | 已对齐 Generic Host |
| DI | 核心管理器、平台抽象、权限管理、插件管理器、日志 Provider 均进入 `IServiceCollection` | 已具备统一容器 |
| Config | `DesktopHostOptions` 绑定 `Wails` 节，`PermissionOptions` 绑定 `Wails:Permissions`，`IpcOptions` 绑定 `Wails:Ipc` | 已从散落配置迁移到 Options |
| Validation | `DesktopHostOptionsValidator`、`PermissionOptionsValidator`、`IpcOptionsValidator` 均 `ValidateOnStart` | 具备启动期失败能力 |
| Logging | 使用 `ILogger<T>`，并通过 `LogServiceLoggerProvider` 桥接到 Wails 日志服务 | 已具备后端日志统一入口 |
| Lifecycle | `DesktopHostedService` 适配 `Application`，`PluginHostedServiceAdapter` 适配插件生命周期 | 方向正确，但需继续收敛边界 |

### 3.2 架构评价

宿主层已经不再是传统桌面程序的“手写 main + 全局单例”模式，而是接近 ASP.NET Core：先组合 `IServiceCollection` 与 Options，再 Build Host，最后由 hosted service 管理生命周期。这个方向能带来三项长期收益：

- **可测试性**：核心能力可通过 DI 替换为 mock / server mode 实现。
- **可观测性**：日志、配置校验、生命周期事件可以复用 .NET 标准生态。
- **插件可组合性**：插件服务、命令、权限声明统一进入 Host 构建管线。

### 3.3 发现的问题与优化项

#### A-1：插件 `ConfigureServices` 与 `Configure` 的双阶段初始化需要进一步显式化

当前 `UsePlugin` 会立即调用 `ConfigureServices`，`DesktopApplicationBuilder.Build()` 又会在 Host 构建前调用插件 `Configure`。这符合“服务注册先于 Host 构建”的要求，但文档与 API 需要明确两件事：

- `ConfigureServices` 必须只做 DI 注册，不应依赖已构建的 `IServiceProvider`。
- `Configure` 可以注册命令、声明权限、读取配置，但不应再追加必须参与 Host 构造的服务。

**建议**：新增 `IPluginStartup` 或在 `IPlugin.Configure` 文档中定义“禁止注册运行时必需服务”的约束；长期可考虑拆成 `ConfigureServices(IServiceCollection, IConfiguration)` 与 `ConfigureCommands(IPluginRuntimeContext)` 两个上下文，减少误用。

#### A-2：`CommandDispatcher` 的中间件注入尚未形成 Builder 级入口

`CommandDispatcher` 构造函数支持 `IReadOnlyList<ICommandMiddleware>`，但 `DesktopApplicationBuilder.Build()` 当前直接构造时只注入 registry、service provider、logger、permission manager，未从 DI 收集 `ICommandMiddleware`。这意味着中间件能力存在于核心类型中，但应用侧的 ASP.NET Core 风格扩展体验还未闭环。

**建议**：在 Build 阶段通过 `host.Services.GetServices<ICommandMiddleware>().ToArray()` 注入，并提供 `builder.Services.AddSingleton<ICommandMiddleware, T>()` 或 `builder.UseCommandMiddleware<T>()` 示例。

#### A-3：配置命名需要统一到 `Wails` 根节并清理旧文档残留

代码注释与实现已经使用 `Wails` 根节，部分旧文档仍描述 `Desktop` 节。此类不一致会导致用户照文档配置但运行时不生效。

**建议**：以 `DesktopApplicationBuilder.ConfigureServices()` 为准，将所有文档示例统一为：

```json
{
  "Wails": {
    "ApplicationName": "MyApp",
    "Permissions": { "Mode": "Strict" },
    "Ipc": { "ValidateOrigin": true }
  }
}
```

#### A-4：Options 热重载能力已经使用 `IOptionsMonitor`，但运行时重载边界需标注

`ApplicationOptions` 工厂读取 `IOptionsMonitor<DesktopHostOptions>.CurrentValue`，但很多桌面运行时状态（已创建窗口、平台主循环、WebView 环境）并不适合热重载。

**建议**：在配置文档中明确：

- 启动期配置：窗口初始尺寸、平台、资源路径、权限模式。
- 可运行时刷新配置：日志级别、部分插件选项。
- 禁止/不保证热刷新配置：主窗口 WebView 初始化参数、平台 runtime 参数、权限 Strict/Compatibility 模式切换。

## 4. 主线 B：Runtime / Window / IPC → Wails v3

### 4.1 已落地设计

| 维度 | 当前实现 | 判断 |
|------|----------|------|
| 聚合根 | `Application` 管理绑定、事件、窗口、服务、平台应用 | 与 Wails v3 对齐 |
| 绑定 | `BindingManager` + `GeneratedBindingRegistry`，生产代码避免运行时反射调用 | 比传统 Wails 反射式绑定更 AOT 友好 |
| 窗口 | `WindowManager` 管理 `WebviewWindow`，平台侧实现 `IWebviewWindowImpl` | 多窗口模型清晰 |
| 事件 | `EventProcessor` 支持订阅、发布、once、hook 与 sender window | Wails 风格事件总线已具备 |
| IPC | `MessageProcessor` 统一处理 call/cancel/event；Native、HTTP、WebSocket、EventIPCTransport 多传输并存 | 有较强降级能力 |
| 平台 | `PlatformFactory` 支持自动检测、模块初始化器注册与 Server 模式 | 适合三平台 + 容器测试 |

### 4.2 架构评价

运行时层保留 Wails v3 的核心心智模型：一个 `Application` 管全部运行时资源，窗口是对象，事件是跨窗口总线，IPC 是前后端调用的底层协议。与 Go Wails 不同的是，Wails.Net 把“动态绑定发现”调整成编译期生成与显式注册，这对 .NET NativeAOT、裁剪、泛型类型安全更有利。

平台层最重要的调整是：使用 `IPlatformApp` + `[ModuleInitializer]` 注册委托替代反射创建平台对象。这在遵守“生产代码禁止反射发现/调用”的同时，仍保留 `UseAutoPlatform()` 的体验。

### 4.3 发现的问题与优化项

#### B-1：`Application` 聚合根职责偏重，需防止继续膨胀

`Application` 同时承担运行时对象聚合、平台注入、服务生命周期、插件生命周期、窗口管理代理、资源服务注入和命令调度器挂载。当前可接受，但后续继续扩展移动端、更新器、权限和 diagnostics 时容易变成“上帝对象”。

**建议**：逐步拆出三个内部协调器：

- `RuntimeServicesCoordinator`：负责 `IServiceStartup` / `IServiceShutdown`。
- `FrontendRuntimeCoordinator`：负责 runtime JS、asset server、IPC transport 绑定。
- `PlatformLifecycleCoordinator`：负责 `IPlatformApp`、窗口主循环、server mode lifecycle。

短期不需要大重构，但新增职责应优先进入 coordinator，而不是继续加到 `Application`。

#### B-2：平台自动加载失败诊断可以更面向用户

`PlatformFactory` 已能自动加载平台程序集触发模块初始化器。若平台包未引用，会抛出异常提示注册委托缺失。该错误对贡献者足够清晰，但对普通用户仍可能不知道应该引用哪个包或调用哪个扩展。

**建议**：异常消息补充可复制修复指令，例如：

- Windows：引用 `Wails.Net.Application.Windows` 并调用 `builder.UseWindows()` 或保持项目引用以供 `UseAutoPlatform()` 加载。
- Linux：引用 `Wails.Net.Application.Linux` 并确保 GTK/WebKitGTK 原生依赖。
- Android：使用 `net10.0-android36.0` 目标框架并引用 Android 平台包。

#### B-3：IPC transport 策略需要一张“选择矩阵”固化

当前已经存在 `NativeIpcTransport`、`HttpTransport`、`AssetServerTransport`、`WebSocketTransport`、`EventIPCTransport` 等实现，但用户和维护者需要知道何时选哪一个、fallback 顺序是什么、失败后是否影响 cancellable promise。

**建议**：在 `docs/architecture/transport-and-ipc.md` 增补 transport selection matrix：

| 场景 | 首选 | 回退 | 取消调用 | 多窗口广播 |
|------|------|------|----------|------------|
| WebView2 桌面 | Native IPC | HTTP/EventIPC | 支持 | WebSocket/EventIPC |
| Linux WebKitGTK | Native IPC | HTTP/EventIPC | 支持 | WebSocket/EventIPC |
| Android WebView | WebMessageListener | HTTP | 支持 | Event bridge |
| Server Mode | HTTP | WebSocket | 部分支持 | WebSocket |

#### B-4：绑定系统的“双轨”概念需进一步清理

代码中存在 `BindingManager`、`GeneratedBindingRegistry`、`CommandRegistry` 与插件 `MapCommand` 两套前后端调用路径相关概念。当前设计上可以并存：应用业务绑定更接近 Wails，插件能力更接近 Tauri。但文档应明确推荐路径：

- 应用业务 API：`[Binding]` + source generator。
- 系统/插件能力：`IPlugin` + `MapCommand` + capability。
- 新增内置能力不得绕过 `CommandDispatcher` 权限链。

**建议**：把“绑定 vs 命令”的职责边界写入 `docs/architecture/binding-and-command-system.md`。

## 5. 主线 C：Plugin / Security / Capability → Tauri v2

### 5.1 已落地设计

| 维度 | 当前实现 | 判断 |
|------|----------|------|
| 插件启用 | `UsePlugin<T>` 显式注册，反射扫描入口已弃用并直接抛错 | 符合 AOT 与最小装配 |
| 命令注册 | `CommandRegistry` + `MapCommand`，Minimal API 风格 | 体验清晰 |
| 权限校验 | `CommandDispatcher` 调用 `PermissionManager.CheckCommand` | 权限链已接入 |
| Capability | `CapabilityFileLoader`、`Capability`、`PermissionSet`、`RequireCapabilityAttribute` | 已具备声明模型 |
| deny 优先 | `PermissionManager` 有 denied permissions，支持 `!permission` | 接近 Tauri deny 语义 |
| Window/Remote | capability 支持窗口范围和 remote URL 限制 | 已支持核心安全边界 |
| Scope | `IScope`、`ScopeParameterAttribute`、文件/路径类插件可收敛到参数级限制 | 方向正确 |
| IPC 安全 | `IpcOriginValidator`、`IpcHandshakeToken`、`IpcOptions` | 已覆盖本地/远程 origin 风险 |

### 5.2 架构评价

插件安全模型已经不只是“命令命名空间”，而是具备 Tauri v2 中 capability 的关键元素：权限可以声明、可以组合为 permission set、可以限定窗口、可以限定 remote origin、可以 deny、可以按平台过滤、可以对参数做 scope 校验。对于 Wails.Net 来说，这是一条重要差异化路线：保持 Wails 的轻量运行时，同时引入 Tauri 的显式安全边界。

### 5.3 发现的问题与优化项

#### C-1：权限默认兼容模式有利迁移，但需要生产模式模板

`PermissionManager.Enabled` 在 Strict 模式始终启用，在 Compatibility 模式受 `Enabled` 控制，默认关闭以保证向后兼容。这对已有 demo 和迁移友好，但新项目模板如果默认不启用权限，会削弱 Tauri 风格能力模型的存在感。

**建议**：

- Demo/迁移项目可以继续 Compatibility。
- `wails new` 新模板应默认生成 `Mode=Strict`、`DenyByDefault=true` 的配置样例。
- CLI `doctor` 增加安全建议：生产构建若权限未启用则 warning。

#### C-2：Capability 文件与插件命令目录应可机器校验

当前 capability 与命令注册都存在，但维护者仍需要人工确认“命令是否都声明了权限”“权限是否有无效命令”。

**建议**：新增构建期或测试期清单生成：

1. 扫描 `CommandRegistry` 注册结果，输出 `commands.manifest.json`。
2. 扫描 capability 文件，输出 `capabilities.manifest.json`。
3. 测试中断言：所有内置插件敏感命令必须被 capability 覆盖；未覆盖命令必须显式标注 `AllowAnonymous` 或 `NoCapabilityRequired`。

该检查可先放在测试项目中，不必引入生产反射；插件显式注册后由测试宿主读取 registry 即可。

#### C-3：插件包拆分后需要稳定“核心即插件”的边界

部分插件已迁移到独立 `src/Wails.Net.Plugins.*` 包，核心项目仍保留插件接口、命令、权限、移动端抽象等基础能力。该拆分合理，但需要避免核心层重新引入具体插件实现。

**建议**：建立分层约束：

- `Wails.Net.Application`：只包含插件抽象、命令调度、安全模型、平台基础接口。
- `Wails.Net.Plugins.*`：实现具体插件，依赖 Application，不允许 Application 反向依赖插件包。
- `Wails.Net.Bundle.*`：只做平台与常用插件组合包。
- CLI 模板选择 bundle，而不是让用户逐个引用全部插件。

#### C-4：前端 API 与后端命令别名需要统一生成来源

许多插件为了兼容同时注册短名和前端 API 名。如果别名靠人工维护，容易出现前端 runtime 文档、TypeScript API 和后端命令注册不一致。

**建议**：以命令 manifest 为源，生成：

- TypeScript runtime API wrapper。
- 插件文档命令表。
- capability 默认权限清单。
- CLI `plugin inspect` 输出。

## 6. 横向优化路线图

### P0：短期必须收敛（建议 1~2 个迭代）

| 编号 | 优化项 | 收益 | 风险 |
|------|--------|------|------|
| P0-1 | `CommandDispatcher` 从 DI 注入 `ICommandMiddleware` | 命令管道真正可扩展 | 低 |
| P0-2 | 统一所有文档配置根节为 `Wails` | 避免用户配置失效 | 低 |
| P0-3 | 明确绑定 API 与插件命令边界 | 降低贡献者误用 | 低 |
| P0-4 | 在新模板加入 Strict 权限示例 | 安全默认值前移 | 中：可能影响 demo 体验 |
| P0-5 | 增补 IPC transport 选择矩阵 | 降低三平台调试成本 | 低 |

### P1：中期结构优化（建议 3~5 个迭代）

| 编号 | 优化项 | 收益 | 风险 |
|------|--------|------|------|
| P1-1 | 拆出 Application 内部 coordinator | 防止聚合根继续膨胀 | 中 |
| P1-2 | 生成 commands/capabilities manifest | 支撑文档、测试、前端 API 统一 | 中 |
| P1-3 | CLI doctor 增加安全/平台诊断 | 提升用户可运维性 | 低 |
| P1-4 | 平台自动加载错误给出包引用/扩展方法建议 | 降低首跑失败成本 | 低 |
| P1-5 | 插件生命周期契约拆分或强化文档 | 降低插件误用 | 中 |

### P2：长期演进（版本边界内规划）

| 编号 | 优化项 | 收益 | 风险 |
|------|--------|------|------|
| P2-1 | Runtime API wrapper 从 manifest 全量生成 | 消除前后端 API 漂移 | 中高 |
| P2-2 | Capability 与 Store/Stronghold/PersistedScope 深度整合 | 接近 Tauri 安全体验 | 中 |
| P2-3 | 建立架构边界测试（Application 不依赖 Plugins.*） | 防止分层腐化 | 低 |
| P2-4 | 对 NativeAOT 裁剪发布做专项 CI | 验证“禁反射”收益 | 中 |

## 7. 建议的目标架构边界

```text
Wails.Net.Application
  允许：
    - Host / DI / Options / Logging 基础设施
    - Application 聚合根与 runtime manager
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
2. **短期优先补齐“已存在抽象的使用闭环”**：尤其是命令中间件注入、配置文档一致性、transport 选择矩阵。
3. **中期用 manifest 打通插件、安全、前端 API 与文档**：这会显著降低 42+ 插件继续扩展时的维护成本。
4. **长期控制 `Application` 聚合根复杂度**：新增运行时职责优先通过 coordinator 或服务进入 DI，而不是继续堆到 `Application`。
5. **安全策略从兼容默认向模板严格默认迁移**：库层保持兼容，模板和生产构建工具推动 Strict 模式。

## 9. 验收清单

后续每次架构相关 PR 可按以下清单自检：

- [ ] 新增服务是否进入 `IServiceCollection`，而不是全局单例？
- [ ] 新增配置是否使用 Options + Validator + `Wails` 根节？
- [ ] 新增日志是否使用 `ILogger<T>`，而不是 `Console.WriteLine`？
- [ ] 新增窗口/IPC 能力是否经过 `Application`/manager/transport 分层？
- [ ] 新增插件命令是否经过 `CommandDispatcher`？
- [ ] 新增敏感命令是否声明 capability / permission / scope？
- [ ] 新增平台能力是否在平台包内通过抽象接口暴露？
- [ ] 生产代码是否仍然遵守禁用运行时反射发现/调用的约束？
- [ ] 文档、前端 API、命令名、capability 是否保持一致？
