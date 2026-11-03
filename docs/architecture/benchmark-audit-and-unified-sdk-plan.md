# Wails.Net 架构对标分析与统一 SDK 优化方案

> **审计日期**：2026-08-16
> **审计性质**：深度审计 + 可破坏性重构方案（本文档只输出分析与方案，不含代码修改）
> **对标基线**：Wails v3 `3.0.0-beta.4`（审计时版本）、Tauri v2 GA `2.11.5`（审计时版本）、ASP.NET Core（Microsoft.Extensions.* 全栈）；**当前基线已升级为 Wails v3 `v3.0.0`（GA，2026-10-26）/ Tauri `2.12.5`（2026-10-21），GA 后差异见 `docs/development/wails-v3-beta-comparison.md` §11**
> **前置文档**：`fusion-architecture-audit.md`（2026-08-15 三主线审计）、`optimization-roadmap.md`（OPT-* 任务基线）、`fusion-strategy-comparison.md`
>
> 与前置文档的关系：前置文档聚焦"三主线融合的差距与 backlog"；本文档聚焦两个新维度——
> 1. **逐项 API/功能核对**：Wails v3 对象模型/IPC/插件/多窗口/事件总线、Tauri v2 功能项全量矩阵；
> 2. **统一 SDK 方案**：调试时按当前环境自动选平台、打包时多平台编译（对标 Tauri 单 crate + tauri-cli 体验），允许破坏性重构。

---

## 1. 总体结论

1. **Wails v3 主线对齐度已很高**：Application/窗口/菜单/托盘/事件总线/多窗口/IPC 协议均已落地，事件通配符与 `EmitTo` 定向事件甚至超出 v3；主要残余缺口是窗口级 `RegisterKeyBinding`、Dialog 按钮定制、内置 live reload 等少量项。
2. **ASP.NET Core 主线骨架已成、细节未透**：Builder/DI/Options/Validator/中间件已闭环；但 `Application` 聚合根仍 1718 行/39 字段，DI 无 per-call scope，无 ActivitySource/Meter 可观测性，`MapCommandExtensions` 存在未入白名单的 `MethodInfo` 反射。
3. **Tauri v2 主线"后端命令面已接近全齐、三层缺口明显"**：40 个后端插件覆盖 Tauri 官方插件全集（含超集）；但 **28/40 前端 npm 包是空壳**（后端命令齐、无 JS 封装）；架构性缺失 multiwebview、JS 侧窗口创建、isolation、sidecar/externalBin、label 寻址；产物缺 msi/dmg/aab。
4. **统一 SDK 是当前最大结构性缺口**：平台选择依赖 **TFM 字符串**（非 RID），模板仍引用 `Wails.Net.Bundle.Windows` 单平台包且 `Program.cs` 显式 `UsePlatform<WindowsPlatformApp>()`，`--all-platforms` 只切 `WailsNetPlatform` 属性不切引用——距离 Tauri"单包 + 单命令多目标"体验差距显著，**必须破坏性重构模板与 SDK 包布局**。

---

## 2. 对标 Wails v3：API 逐项核对

### 2.1 对象模型

| 项 | 状态 | 证据 / 说明 |
|---|---|---|
| Application New/Run/Quit/Hide/Show | ✅ | `Application.cs`（含 GetPID/SetIcon/OnShutdown/RegisterService 超集） |
| 窗口选项全集 | ⚠️ | `WebviewWindowOptions.cs` 覆盖 Width/Height/Min/Max/Frameless/AlwaysOnTop/Hidden/Resizable/Zoom/Fullscreen/Translucent/BackgroundColour/HTML/JS/CSS；**缺** v3 的 `KeyBindings`（仅 App 级）、`StartState`（拆为 Minimised/Maximised）、`Permissions` |
| Window 方法全集 | ✅ 超集 | v3 ~60 方法全覆盖；**缺** `SnapAssist`、`SetScreen`、窗口级 `RegisterKeyBinding`；多出 SetOpacity/Badge/OverlayIcon/TaskbarProgress/StartDrag/PrintToPDF |
| Menu/MenuItem/ContextMenu | ✅ | `Menus/` + Role 工厂 |
| SystemTray | ✅ | `SystemTray/` 三平台实现 + `Plugins.Tray` |
| Screen | ✅ | `ScreenManager` |
| Dialog | ⚠️ | 仅 OpenFile/SaveFile/Message；缺按钮定制（AddButton/Default/Cancel） |
| KeyBindings | ⚠️ | App 级 `ApplicationOptions.KeyBindings` ✅；窗口级 ❌ |
| Drag&Drop | ✅ | `SetFileDropEnabled` + 完整拖放事件链 |

### 2.2 IPC 与绑定

| 项 | 状态 | 说明 |
|---|---|---|
| 方法绑定 | 🔀 有意差异 | v3 反射 Bind；Wails.Net `[Binding]` 源生成器 + `GeneratedBindingRegistry`（AOT 友好） |
| call 协议（id/method/args） | ✅ | `InvokeRequest.cs`；对齐 `messageprocessor_call.go` |
| cancel | ✅ | `MessageProcessor.cs:54` + 前端 `cancel(callId)`/CancellablePromise |
| 前端 TS 绑定生成 | ✅ | `Wails.Net.Generator` + CLI `generate` 命令 |
| Services/Route | ✅ | `ServiceRegistry`/`FileServerService` 对应 v3 `services.go` |

### 2.3 多窗口与事件总线

- 多窗口 ✅：`WindowManager`（Create/Get/GetByName/GetAll/GetActive/Destroy），ID+Name 双索引，对齐 v3 `window_manager.go`。
- 事件总线 ✅ 超集：On/OnMultiple/Once/Off/Emit/**EmitTo**/通配符（`*` 后缀前缀匹配，语义与 v3 一致）；`KnownEvents.cs` 50+ 通用事件为 v3 Common 超集；事件数据类型化（`TypedEvent`/`CustomEvent`）。

### 2.4 运行时 JS

v3 runtime 模块（calls/events/contextmenu/Window/Screens/System/Clipboard/Dialogs/Browser/Environment/Flags）在 Wails.Net 中**有意改为插件包分发**（对齐 Tauri 风格）——方向正确，但插件包空壳化使该优势未兑现（见 §4.2）。`flags` 经 `ApplicationOptions.Flags` 暴露，无独立 flags 模块。

### 2.5 Channels 与其他

- **Channels**：v3 beta.4 **不存在 channel API**，Wails.Net `ChannelManager` 为对标 Tauri Channel 的自加能力（超集）；对称加密未实现（OPT-RUNTIME-003，opt-in 评估中）。
- SingleInstance/SecondInstance ✅（对齐 `single_instance.go`）；FileAssociation ✅（插件，超集）；URLScheme ✅（`RegisterScheme` + Deeplink 插件，超集）；Notifications 🔀（v3 桌面无，Wails.Net 超集）；AssetServer 中间件 ✅；devtools ✅；**live reload ⚠️**（靠 FsWatch 插件组合，无 v3 式内置 websocket reload 广播）。

### 2.6 对 v3 的残余缺口清单（重构任务 W3-*）

| 编号 | 缺口 | 优先级 |
|---|---|---|
| W3-1 | 窗口级 `RegisterKeyBinding` / 窗口选项 `KeyBindings`、`StartState` | P2 |
| W3-2 | Dialog 按钮定制（AddButton/Default/Cancel 语义） | P2 |
| W3-3 | `SnapAssist`、`SetScreen` 窗口方法 | P2 |
| W3-4 | 内置 live reload 广播（不依赖 FsWatch 插件组合） | P2 |

---

## 3. 对标 ASP.NET Core：开发体验审计

> 已落地项（HostApplicationBuilder 封装、Options+Validator+ValidateOnStart、`UseCommandMiddleware`、`DeferredPermissionRegistrar` 移除临时 ServiceProvider、`ApplicationLifecycleState`）见 `fusion-architecture-audit.md`，不再重复。以下为**剩余差距**与可破坏式改进。

### 3.1 `Application` 聚合根仍过重（任务 DX-1，P1）

`Application.cs` 1718 行、39 字段、40+ public 方法/属性。仍直挂的五块职责（带行号证据）：

| 职责 | 位置 | 建议拆分 |
|---|---|---|
| 服务路由暂存/挂载 | `_pendingServiceRoutes` L93、AssetServer setter 侧效应 L440-455 | `ServiceRouteMounter` |
| 自定义 scheme 注册表 | `_customSchemes` L99-105、L483-549 | `SchemeRegistry` |
| 单实例 | `SetupSingleInstance` L963-983、L1303-1326 | `SingleInstanceCoordinator` |
| 传输层拼装 + runtime JS transport 探测 | L1000-1012、L371-382、L1578-1594 | `TransportBootstrapper` |
| 窗口级 CSP | `RegisterWindowCsp` L464-472 | `WindowCspRegistrar` |

目标：Application 只保留窗口注册表 + Run/Shutdown 编排，<400 行；内部拆分属内部重构，public API 保持稳定，破坏面小。

### 3.2 Builder API 形态差距（任务 DX-2，P1，**破坏性**）

`DesktopApplicationBuilder` 仅暴露 `Services`/`Configuration`/`Logging` 三属性。缺：

- `IHostEnvironment Environment`、`ConfigureHostConfiguration`、`ConfigureContainer<T>`；
- **应用侧 `builder.MapCommand(...)` 直达入口**：当前只能 Build 后从 DI 解析 `CommandRegistry` 再注册；命令元数据只有 `CommandSecurity` 重载，无 `WithDescription()/WithName()/RequireCapability()` 链式 endpoint builder。

方案：新增 `Commands` 属性 + `MapCommand` 返回 `CommandEndpointBuilder` 链式累积元数据（对齐 Minimal API 体验）。旧 Build 后注册路径可保留一个版本周期后删除。

### 3.3 DI 无 per-call scope（任务 DX-3，P1，**破坏性**）

全 src **零 `CreateScope`/`AddScoped`**；`CommandDispatcher` 捕获根 `IServiceProvider`（`CommandDispatcher.cs` L62/L69），`CommandContext.Services` 即根容器。Development 下 Host 默认 `ValidateScopes=true`，命令内解析 scoped 服务直接抛异常。

方案：`DispatchAsync` 每次调用 `CreateAsyncScope`，scoped provider 注入 `CommandContext`；文档明确 transient/scoped/singleton 在命令路径下的语义。行为变化：注入根容器的服务若依赖 scoped 服务将从"抛异常"变为"正常工作"——属修复性破坏。

### 3.4 关闭协议与生命周期治理（任务 DX-4，P1，**破坏性**）

- `ShouldQuit()` 全 src 无调用方：Windows `WmClose` 注释明示"不拦截关闭"（`Win32WebviewWindow.cs` L1486-1496），取消退出仅 Linux `ShowExitConfirmationDialog` 路径有效。
- `Application.Shutdown` 全程 `GetAwaiter().GetResult()` 同步阻塞 + `CancellationToken.None`；`DesktopHostedService.StopAsync` 忽略入参 token、硬编码 5s `Thread.Join`；无 `HostOptions.ShutdownTimeout` 治理。

方案：`ShouldQuit` 接入各平台关闭消息形成可取消 Closing 协议（统一三平台语义，Linux 对话框路径迁移其上）；Shutdown 全异步化并尊重 `HostOptions.ShutdownTimeout`。

### 3.5 可观测性零埋点（任务 DX-5，P2）

全 src 无 `ActivitySource`/`Meter`/`EventSource`/`BeginScope`；命令分发无 trace/span，WindowId/Method 未进 logging scope。对标 Tauri tracing 与 ASP.NET Core 诊断生态差距明显。

方案：新增 `Wails.Net` ActivitySource + Meter；`DispatchAsync` 开 span + `BeginScope(new{Method, WindowId})`；提供 `builder.Services.AddWailsDiagnostics()` 一键接 OpenTelemetry。

### 3.6 残余反射点（任务 DX-6，P0，小破坏）

`MapCommandExtensions.cs` L886 `ExtractBusinessParameterTypes(MethodInfo)` 通过 `delegate.Method` 反射读取参数元数据（约 20 处调用），**不在 AGENTS.md §3.4.3 白名单内**，仅靠注释自我豁免。方案：泛型重载内以 `typeof(T1)…` 显式数组替代（L1061 已有先例），统一后删除 `using System.Reflection;`。

其余反射点（`PlatformFactory` 程序集加载、`BindingManager` 类型名字符串）均在白名单内，合规。

### 3.7 配置热重载语义（任务 DX-7，P2，文档级破坏）

`ApplicationOptions` 是 Build 期快照单例（`DesktopApplicationBuilder.cs` L98-107），json 热改对框架自身选项实质无效。方案：文档显式声明"框架配置仅启动期生效"并移除误导性注释；或长远改为 `IOptionsMonitor` 订阅式（破坏大，暂列 P2 评估）。

---

## 4. 对标 Tauri v2：功能项全量矩阵

### 4.1 插件功能矩阵（40 后端插件 vs Tauri 官方插件全集）

| Tauri v2 功能项 | Wails.Net 对应 | 状态 | 备注 |
|---|---|---|---|
| app | Plugins.Appinfo / Application | ⚠️ | 前端包空壳 |
| clipboard-manager | Plugins.Clipboard | ⚠️ | 前端空壳 |
| deep-link | Plugins.Deeplink | ⚠️ | 前端极小封装 |
| dialog | Plugins.Dialog | ⚠️ | 前端空壳 |
| fs | Plugins.FileSystem（21 命令） | ⚠️ | 前端空壳；scope 走 CommandSecurity |
| global-shortcut | Plugins.Globalshortcut | ⚠️ | 前端空壳 |
| http | Plugins.Http | ✅ | |
| log | Plugins.Log | ✅ | |
| notification | Plugins.Notification | ✅ | |
| os | Plugins.Osinfo（13 命令） | ⚠️ | 前端空壳 |
| positioner / process / store / stronghold / window-state / autostart / sql / persisted-scope | 同名插件 | ⚠️ | **前端全部空壳** |
| shell / websocket / upload / opener / localhost | 同名插件 | ✅ | 双端均有 |
| single-instance | ApplicationOptions 内置 | ✅ | 非插件 |
| updater | Plugins.Updater（Minisign + GitHub/GitLab/Http provider + AssetMatcher） | ⚠️ | 后端对齐度最高；前端空壳；缺 endpoints 多镜像回退 |
| tray / menu | Plugins.Tray（8 命令）/ Plugins.Menu（9 命令） | ⚠️ | 前端空壳，无 JS menu/tray API |
| barcode-scanner/biometric/geolocation/haptics/nfc | Plugins.Mobile 聚合 | ✅ | 双端齐全（116 行封装） |
| **multiwebview** | 无 | ❌ | 架构性缺失 |
| **JS 侧窗口/ webview 创建** | 无 | ❌ | 前端无法 `new Window` / `getByLabel` |
| custom protocol + **convertFileSrc** + **asset scope** | AssetServer + RegisterScheme 存在 | ⚠️ | 无 convertFileSrc / 无 scope 校验（OPT-SEC-005） |
| CSP 内置 | CspOptions 全局/窗口级 + NonceInjector | ✅ | |
| **isolation pattern** | IsolationInjector.cs 存在 | ⚠️ | 已实现但需手动配置，非声明式 |
| **sidecar / externalBin / resources** | 无 | ❌ | 全库 0 匹配 |
| capabilities/permissions/scope | CommandSecurity + 诊断分析器 | ✅ | 对齐 Tauri ACL |
| dangerousDisableAssetCspModification / headers | 无 | ❌ | |

**超集项**（Tauri 无官方对等）：Cookie、Dpiscale、Fileassociation、FsWatch、Keychain、Localization、Powermanagement、Screen、Windows、Cli 插件等。

### 4.2 核心发现：28/40 前端包空壳（任务 TAURI-1，P0，性价比最高）

前端空壳清单：store、menu、tray、updater、sql、window-state、windows、positioner、persisted-scope、stronghold、keychain、autostart、clipboard、dialog、file-system、global-shortcut、os-info、path、process、screen、app-info、application、cli、dpi-scale、file-association、fs-watch、localization、power-management（`src/index.ts` 仅 import 无 export，store 连 dist 都未构建）。

方案：以 `defineCommand` 薄壳模式批量生成封装（与 http/log/shell 已完成包同构）；长期接入 P2-1「Runtime API wrapper 从 manifest 全量生成」消除漂移。**此单项完成后，§4.1 矩阵中 ~20 个 ⚠️ 直接升级为 ✅。**

### 4.3 事件寻址模型（任务 TAURI-2，P1，**破坏性**）

当前 `emitTo` 以 **windowId(number)** 寻址；Tauri 以 **label 字符串**寻址且支持 webview 级目标。方案：统一为 Name/label 寻址（number id 保留为内部索引），前端 API 改为 `emitTo(label, event, data)`；旧 number 签名删除或保留一个版本周期。

### 4.4 插件开发模型（任务 TAURI-3，P2）

双包模型（NuGet + npm）已完成 M1-M4 ✅。差距：无 Tauri 式移动端插件分离工程——移动端能力全部硬编码在 `Plugins.Mobile` + `Application.Android`，第三方无法编写移动端插件。方案：定义移动端插件 SPI（Android 侧 Kotlin/Java 互操作边界），后置。

### 4.5 CLI 与打包（任务 TAURI-4 ~ TAURI-7）

CLI 现有 18 命令（new/dev/build/publish/pack/plugin×6/doctor/setup/info/version/clean/update/generate/check/icon/signer/platform/deploy）。对照 Tauri CLI：

| 缺口 | 编号 | 优先级 | 说明 |
|---|---|---|---|
| `init`（注入现有项目） | TAURI-4 | P2 | |
| `completions`（shell 补全） | TAURI-4 | P2 | |
| `inspect`（capability/ACL 检查） | TAURI-4 | P1 | doctor 部分覆盖，需显式能力 |
| icon 缺 `.icns` / Android 自适应图标 | TAURI-5 | P2 | |
| 打包产物缺 **msi / dmg / aab** | TAURI-6 | P1 | wixConfig 字段悬空；Bundle.MacOS 无打包逻辑；aab/签名/keystore 流程无 |
| publish 参数组装过薄 | TAURI-7 | P1 | 仅 `-c/-r/--self-contained/-o`，不组 PublishSingleFile/Trim/AOT |
| deb/rpm 依赖硬编码 | TAURI-6 | P1 | Packager.cs L799/831 硬编码 `libgtk-4-1, libwebkitgtk-6.0-4`，未消费 `bundle.linux.debDependencies/rpmDependencies` 字段 |

### 4.6 资源管理（任务 TAURI-8，P1）

- ✅ 已有：FileAssetServer/BundledAssetServer、SPA fallback、**Range/206**、ETag/Cache-Control、中间件链、CSP nonce。
- ❌ 缺失：**externalBin/sidecar**（0 匹配）、**asset protocol scope / convertFileSrc**（OPT-SEC-005 未动）、gzip/brotli 响应压缩、CLI 自动资源嵌入/清单生成（嵌入资源需用户手写委托）。

### 4.7 wails.json vs tauri.conf.json

| Tauri 配置 | Wails.Net | 状态 |
|---|---|---|
| productName/version/identifier | name/version/bundle.identifier | ✅（命名不同） |
| build 4 钩子 / frontendDist / devUrl | 同名 / assetDir / frontend.devServerUrl | ✅ |
| bundle.targets / createUpdaterArtifacts | — | ❌ |
| bundle.windows.nsis/wix 模板/certificate | 字段悬空 | ❌ 实现 |
| bundle.macOS 节 / iOS / Android 节 | — | ❌ |
| bundle.resources / **externalBin** | resources 字段存在 | ⚠️ / ❌ |
| app.security（声明式 CSP 等） | 代码 API | ⚠️ 缺声明式节 |
| app.windows[]（多窗口声明） | — | ❌（窗口仅代码创建） |
| plugins 配置节 | — | ❌ |

---

## 5. 统一 SDK 优化方案（核心，可破坏性重构）

### 5.1 现状问题（事实基线）

1. **平台切换维度是 TFM 字符串，不是 RID**：`Wails.Net.Sdk.csproj` 按 `Contains('windows'/'android'/'macos')` 条件 ProjectReference（L56-73）；`Sdk.props` Debug 分支按构建机 OS 推断 `WailsNetDetectedPlatform`（L20-23），Release 默认 linux；`net10.0` 被硬编码等同 Linux。`dotnet build -r linux-x64`（TFM 不变）**不会**触发引用切换。
2. **模板未走 SDK**：`Templates/content/App/Company.AppName.csproj` 引用 `Wails.Net.Bundle.Windows` + TFM 硬编码 `net10.0-windows10.0.19041.0`；`Program.cs` 用 `OperatingSystem.IsWindows()/IsLinux()/IsAndroid()` 条件块**显式 `UsePlatform<WindowsPlatformApp>()`**——跨平台编译要求所有平台程序集同时可解析，与单平台引用矛盾。
3. **编译期/运行时两套选择逻辑**：编译期靠 TFM/props，运行时靠 `PlatformFactory` 6 级链（OS 检测，非 RID）；二者无 RID 交汇点。`wails dev --platform linux` 只注入 `WAILS_PLATFORM` 环境变量，产生编译期 Windows 引用 + 运行时 linux 检测的错配。
4. **`--all-platforms` 缺陷**：单 TFM 项目只循环 `-p:WailsNetPlatform=` 属性，不切 TFM/引用；无 macOS；`wails pack` 无 `--all-platforms`。
5. **SDK 包布局非传递**：`build/` 而非 `buildTransitive/`，多项目引用下 props/targets 不穿透。

### 5.2 目标体验（对标 Tauri）

```text
# 用户项目只引用一个包
<PackageReference Include="Wails.Net.Sdk" Version="..." />

# Program.cs 只有一行平台入口
var builder = DesktopApplicationBuilder.CreateBuilder(args);
builder.UseWailsPlatform();   // 运行时自动选平台，编译期无需引用具体平台类型

# 调试：按当前环境自动选平台（无需任何参数）
wails dev            # Windows 上 → Windows 平台；Linux 上 → Linux

# 打包：多平台编译
wails build --target win-x64
wails build --target linux-x64
wails build --target android-arm64
wails build --all-targets
```

### 5.3 重构方案（任务 SDK-1 ~ SDK-6）

#### SDK-1：引入 RID 维度平台映射（P0，**破坏性**）

在 Sdk 的 targets 中建立 `RuntimeIdentifier → WailsNetPlatform` 映射表：

| RID | 平台 |
|---|---|
| win-x64 / win-arm64 | windows |
| linux-x64 / linux-arm64 | linux |
| android-arm64 等 | android |
| osx-*（macOS 主机） | macos |
| 无 RID（开发期） | 按构建机 OS 推断（沿用现有 Debug 分支） |

效果：`dotnet build -r linux-x64` 在 Windows 上无需改 TFM 即切换平台引用（GirCore 纯托管 + `EnableWindowsTargeting` 已验证可行）。

#### SDK-2：包布局升级 buildTransitive + 条件依赖组（P0）

- `build/` → `buildTransitive/`，保证 props/targets 穿透多项目引用。
- 评估从「TFM 条件 ProjectReference」改为 nuspec 按 TFM 依赖组 + buildTransitive 内按 `$(RuntimeIdentifier)`/`$(WailsNetPlatform)` 条件 Reference 平台包，消除 TFM 字符串硬编码。

#### SDK-3：模板迁移单包 + 消除显式平台类型引用（P0，**破坏性**）

- 模板 csproj：`Wails.Net.Bundle.Windows` → `Wails.Net.Sdk`，TFM 改 `net10.0`（不再硬编码 windows TFM）。
- 模板 Program.cs：删除 `OperatingSystem.IsWindows()/...` 条件块与 `UsePlatform<WindowsPlatformApp>()`，统一 `UseWailsPlatform()`（即现有 `UseAutoPlatform()` 改名/别名，模块初始化器 + AutoLoad 已具备运行时能力）。
- 同步更新 `ProjectScaffolder`、全部 examples、`wails doctor` 增加 SDK 引用检查。
- **破坏面**：旧模板项目（引用 Bundle.*）仍可用——Bundle 包保留但标记为"高级/显式"路径，文档降级为非推荐。

#### SDK-4：CLI `--target` / `--all-targets`（P1，**破坏性**）

- `build/publish/pack` 统一 `--target <rid>`（替代/兼容 `--runtime`），`--all-targets` 循环 RID 集合 `{win-x64, linux-x64, android-arm64}`（macOS 仅 macOS 主机加入循环）。
- 每 RID 对应平台引用经 SDK-1 自动切换；产物按 RID 分目录输出。
- `pack --all-targets` 补齐；macOS 在 AllPlatforms 集合中加入（仅 macOS 主机）。

#### SDK-5：dev 体验收敛（P1）

- `wails dev` 移除/弱化 `--platform` 的误导性（编译期引用不随其切换）；若传入与构建机不一致的平台，直接报错并提示"交叉调试不支持，请在目标平台构建"。
- 中期补齐 Tauri 风格 dev-server 代理（模板 Program.cs 注释自述的后续项），打通 vite HMR 与 AssetServer。

#### SDK-6：CI 多平台矩阵示例（P1）

当前 ci.yml 是 3 个固定 job 产 SDK 包，无面向用户应用的多平台分发矩阵。方案：提供 `docs/development/` 级 CI 模板（windows-latest → nsis/zip、ubuntu-latest → deb/rpm/appimage、macos-latest → dmg(待 TAURI-6)、windows-latest 交叉 → apk），模板项目可选生成。

---

## 6. 重构任务总表（按优先级）

| 编号 | 任务 | 主线 | 破坏性 | 优先级 |
|---|---|---|---|---|
| DX-6 | 消除 MapCommand 反射 | ASP.NET Core | 小 | **P0 ✅** |
| TAURI-1 | 28 个前端空壳包批量生成封装 | Tauri | 无（纯增量） | **P0 ✅** |
| SDK-1 | RID→平台映射 | 统一 SDK | 中 | **P0 ✅** |
| SDK-2 | buildTransitive + 条件依赖 | 统一 SDK | 中 | **P0 ✅** |
| SDK-3 | 模板单包化 + UseWailsPlatform 统一入口 | 统一 SDK | **高（模板/示例全量改）** | **P0 ✅** |
| DX-1 | Application coordinator 化（5 块拆分） | ASP.NET Core | 小（内部） | P1 |
| DX-2 | Builder 补 Environment/Configure*/MapCommand 链式元数据 | ASP.NET Core | 中 | P1 |
| DX-3 | 命令 per-call scope | ASP.NET Core | 中（行为修复） | P1 |
| DX-4 | 可取消 Closing 协议 + Shutdown 异步化/超时治理 | ASP.NET Core | 中 | P1 |
| TAURI-2 | 事件 label 寻址统一 | Tauri | 中 | P1 |
| TAURI-4 | CLI inspect 等命令补齐 | Tauri | 无 | P1 |
| TAURI-6 | msi/dmg/aab + 消费 bundle.linux 依赖字段 | Tauri | 无 | P1 |
| TAURI-7 | publish 组装 SingleFile/Trim/AOT | Tauri | 无 | P1 |
| TAURI-8 | sidecar/externalBin + asset scope + 压缩 | Tauri | 中 | P1 |
| SDK-4 | --target/--all-targets 多 RID 构建 | 统一 SDK | 中 | P1 |
| SDK-5 | dev 平台参数语义收敛 + dev-server 代理 | 统一 SDK | 小 | P1 |
| SDK-6 | 用户侧 CI 多平台矩阵模板 | 统一 SDK | 无 | P1 |
| OPT-SEC-005 | asset protocol scope / convertFileSrc（沿用既有编号） | Tauri | 中 | P1 |
| W3-1~4 | v3 残余缺口（窗口级快捷键/Dialog 按钮/SnapAssist/live reload） | Wails v3 | 小 | P2 |
| DX-5 | ActivitySource/Meter + AddWailsDiagnostics | ASP.NET Core | 无 | P2 |
| DX-7 | 配置热重载语义声明或订阅化 | ASP.NET Core | 视方案 | P2 |
| TAURI-3 | 移动端插件 SPI | Tauri | 高 | P2 |
| TAURI-5 | icon icns/自适应图标 | Tauri | 无 | P2 |
| OPT-RUNTIME-003 | Channels 对称加密（opt-in） | Wails v3 超集 | 无 | P2 |

> 与 `optimization-roadmap.md` 既有编号（OPT-*）关系：OPT-SEC-005、OPT-RUNTIME-003、OPT-PLUGIN-001~003、OPT-TEST-001~003 仍然有效，本文档不重复展开；TAURI-1 完成后可为 OPT-PLUGIN-003（清单生成）提供数据基础。

---

## 7. 风险与缓解

| 风险 | 说明 | 缓解 |
|---|---|---|
| 模板破坏（SDK-3） | 存量用户项目引用 Bundle.* + 显式 UsePlatform | Bundle 包保留可用一个主版本；迁移指南 + `wails doctor` 检测旧形态并给修复指令；release note 明确 breaking |
| RID 映射不全（SDK-1） | 新 RID（如 linux-musl）未映射 | 未识别 RID 回退 OS 推断 + warning；映射表集中一处常量 |
| per-call scope（DX-3） | 插件若在根容器捕获 scoped 依赖，行为变化 | 先在 Strict 模式默认启用，Compatibility 模式可选关闭；测试矩阵覆盖 |
| 事件寻址统一（TAURI-2） | 前端 emitTo(number) 签名变化 | 旧签名保留一个版本周期 + 弃用警告 |
| 交叉编译假象 | Windows 可编译 Linux/Android，但 NSIS/AppImage 等打包层有宿主限制 | 文档明确"编译可交叉、打包分宿主"；CI 矩阵模板按宿主拆分 |

## 8. 验收清单

- [x] 模板项目仅引用 `Wails.Net.Sdk`，Program.cs 无平台条件块（SDK-3）
- [x] `dotnet build -r linux-x64` 在 Windows 上产出 Linux 平台程序集引用（SDK-1/2）
- [x] `wails build --all-targets` 产出 win/linux/android 三平台产物分目录（SDK-4）
- [ ] `wails dev` 在 Windows/Linux/macOS 上零参数自动选中本机平台（SDK-5）
- [x] 40 个前端包全部有实质封装且与后端命令一致（TAURI-1）
- [x] `src/` 生产代码 `using System.Reflection;` 仅剩白名单两处（DX-6）
- [ ] Application.cs <400 行，5 个 coordinator 独立可测（DX-1）
- [ ] 命令路径支持 scoped 服务（DX-3）
- [ ] 三平台关闭行为统一走 ShouldQuit 协议（DX-4）
- [ ] msi/dmg/aab 至少 msi + dmg 落地（TAURI-6）
