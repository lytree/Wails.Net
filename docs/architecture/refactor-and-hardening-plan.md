# Wails.Net 深度对比与重构加固方案（refactor-and-hardening-plan）

> ## 状态漂移说明（2026-08-18 审计基线）
>
> 本文档编写于 2026-08-16，部分问题在审计基线（2026-08-18）已确认完成实现但文档未及时更新：
>
> | 编号 | 当前实现状态 | 落地位置 |
> |------|------------|---------|
> | RH-WIN-01 WebviewWindow Impl 断链 | ✅ 已实现 | `WindowManager.cs:108` 与 `Application.cs:921` 完成 `window.Impl = impl` 回填 |
> | SEC-H-004 缺陷 A Strict + Enabled=false 能力加载脱节 | ✅ 已实现 | `DesktopApplicationBuilder.cs:380-385` 加载条件改为 `!permissionManager.Enabled`（与检查生效条件对齐） |
> | R0-3 Native IPC 强制覆盖 WindowId/Origin | ✅ 已实现 | `NativeIpcTransport.cs` 新增 `ResolveOriginFromPlatform` + `parsed.WindowId = windowId` |
> | R0-4 Strict 拒绝无安全声明命令 | ✅ 已实现 | `PermissionManager.IsStrict` + `CommandDispatcher` Strict 守卫 |
> | R0-5 window.* 禁止无权限 fallback | ✅ 已实现 | `MessageProcessor.ProcessCommandFallbackAsync` 失败时返回错误响应而非 null |
>
> 详细 R0 止血清单见 `architecture-audit-2026-08-18.md` §6 R0-R4。
>
> **本文档其余条目尚未重新核对当前实现**，如需引用应以 2026-08-18 审计基线为准。


> - **分析日期**：2026-08-16（基线提交 `9efef61`）
> - **分析方法**：对 `src/`（核心宿主层、IPC/传输层、安全/权限层、四平台实现、40 个插件包）、`packages/`（npm 前端包）、`tests/`（36 个测试项目）、CLI/源生成器/模板/CI 做全量事实调研，并与 **Wails v3 `v3.0.0-beta.4`**（2026-08-05）的关键机制逐项对标。
> - **与既有文档的关系**：本文是 `fusion-architecture-audit.md`（2026-08-15 审计）与 `optimization-roadmap.md`（OPT-* 系列）的**下一轮升级**。差异在于：**本文明确允许不兼容变更与破坏性更改（Breaking Changes）**，用于在 1.0 之前的版本窗口一次性收敛结构性债务；OPT-* 未完成项在本文中以 RH-* 编号继承并扩展。
> - **本文件同时补齐一个悬空引用**：`tests/Wails.Net.Application.Tests/Security/CommandSecurityEnforcementTests.cs:14` 引用了本文件 §2.1.4，此前该文件在仓库中不存在。
> - **本文只做分析与方案设计，不伴随任何代码修改。**

---

## 1. 总体结论

三条架构主线（Host/DI/Config/Logging 参考 ASP.NET Core；Runtime/Window/IPC 参考 Wails v3；Plugin/Security/Capability 自研）**方向判定维持不变**，前两轮审计列出的 P0/P1 大部分已真实落地（复核确认：DeferredPermissionRegistrar、ApplicationLifecycleState、IIpcTransportStrategy、事件通配符/定向、manifest 三件套、Strict 模板、`wails-net check`、NativeAOT CI 均存在且有效）。

本轮深度调研**新发现四类结构性问题**，它们无法靠增量修补解决，适合在允许破坏性更改的前提下集中处理：

| # | 问题 | 性质 | 对标差距 | 编号 |
|---|------|------|---------|------|
| 1 | **核心 `WebviewWindow` facade 与平台实现断链**：`Impl` 属性全仓库无赋值点，facade 全部方法抛 `InvalidOperationException`；Linux/macOS/Android 的事件下行与 contextmenu 路径实际不可用或空转 | P0 级缺陷 | Wails v3：窗口对象模型端到端可用 | RH-WIN-01 |
| 2 | **安全模型"机制完备、接入率 6%"**：40 个插件中仅 FileSystem/Shell/Http（4/5）接入 `CommandSecurity`；`[RequireCapability]` 零使用；Strict 与 capability 加载存在脱节 bug；`window.*` 硬编码 switch 绕过权限链 | 缺陷 + 覆盖缺口 | 设计要求：ACL 默认拒绝、权限随插件分发、core 命令同样受 ACL 管辖 | RH-SEC-* |
| 3 | **四平台各持一份 IPC+资源+ready 管线拷贝**：POST /wails/\* 处理 4 份、静态资源+SPA 回退 4 份、runtime 注入 3 份、WindowRuntimeReady 4 处不同触发点，且全部在 UI 线程同步阻塞等待异步命令 | 结构性重复 + 阻塞风险 | Wails v3：平台层共享 messageProcessor/assetserver 装配 | RH-IPC-02 |
| 4 | **四套并行窗口注册表 + 静态服务定位器**：`Application._windows` 与 `WindowManager._windows` 互不同步、各平台 App 自持 `_windows`、`NativeIpcTransport` 再持一份；`Application.Get()` 被 20+ 平台文件静态调用 | 结构性耦合 | ASP.NET Core：无全局静态定位器；Wails v3：单一 window registry | RH-WIN-02 / RH-HOST-03 |

**对标差距 Top 清单**（详见 §2–§5 分项）：

- 对 Wails v3 beta.4：可安装插件（前端资产随插件分发）、Channel 前后端协议、dev server 反向代理、窗口对象模型完整性。
- 设计目标：权限清单随包分发 + JSON Schema 自动生成（IDE 补全）、`convertFileSrc` 资产协议 scope、默认拒绝的实际全覆盖、core 命令纳入 ACL。

---

## 2. 安全加固主线（SEC-H 系列，自研 ACL 加固）

> SEC-H-001（CommandSecurity API）、SEC-H-002（源生成器安全诊断分析器）已落地；SEC-H-005 已用于 store.watch 泄漏修复。SEC-H-003/004/006+ 为本文**提议**的后续编号（原始计划文档缺失，本文即其重建）。

### 2.1 命令安全声明与执行合一（SEC-H-001/002，已落地阶段固化）

#### 2.1.1 现状（已落地）

- **声明侧**：`Commands/CommandSecurity.cs`（`Require` / `Public` / `WithScope`），`MapCommandExtensions.cs:1009` 起 SEC-H-001 重载与 `:1184-1325` SEC-H-002 补充重载接线到 `CommandRegistry.Register(name, invoker, parameterTypes, CommandSecurity?)`（`CommandRegistry.cs:94-111`，存入 `CommandEntry.Security`）。
- **执行侧**：`CommandDispatcher.CollectRequiredCapabilities`（`CommandDispatcher.cs:214-247`）三来源合并（CommandEntry.RequiredCapabilities + `Security?.RequiredCapabilities` + 源生成器 `[RequireCapability]` 提取），`ExtractScopeValues`（`:266-308`）优先读 `Security.ScopeMappings`，否则用源生成器 ScopeExtractor。
- **构建期守门**：`MapCommandSecurityDiagnosticAnalyzer`（WNET001：`[RequireCapability]` 方法组经 MapCommand 注册未传 CommandSecurity；WNET002：`[ScopeParameter]` 未传 CommandSecurity），`TreatWarningsAsErrors=true` 下即构建失败。
- **清单侧**：`ManifestGenerator.FindUncoveredCapabilities`（`Manifests/ManifestGenerator.cs:329-389`）：`IsPublic` 跳过；"声明了 Security 但无能力且非 Public" 按 fail-closed 报未覆盖；deny 条目（`!perm`）不计入覆盖集合。

#### 2.1.2 剩余差距

> **2026-11-14 复核：以下四项已全部收敛**（原结论基于 2026-08-16 基线，已严重滞后于代码）：
>
> | 原差距 | 复核结果 | 证据 |
> |---|---|---|
> | 接入率 6%（约 47 插件仅 3 个接 `CommandSecurity`） | ✅ 已全量迁移：内置插件 **302 条命令注册中 301 条**显式传 `CommandSecurity` | 全仓库 `MapCommand*` 调用审计（F# 括号平衡解析，2026-11-14） |
> | lambda 注册盲区（`http.fetch` 等无法触发 WNET001/002） | ✅ 已收敛：`WNET003` 分析器已落地（重载声明了 `CommandSecurity?` 形参而未传即告警）；`http.fetch` 由 lambda 改为方法组并补 `Require("http:allow-fetch").WithScope("url", ...)` | `MapCommandSecurityDiagnosticAnalyzer.Wnet003`、`HttpPlugin.cs` |
> | `[RequireCapability]` 零使用 | 🟡 特性轨仍空转，但**不再是缺口**：注册面已由 `WNET003` + 全量 `CommandSecurity` 覆盖，特性轨与 MapCommand 轨并存属设计冗余 | `MapCommandSecurityDiagnosticAnalyzer` |
> | `GeneratePluginsManifest` 的 Commands/PermissionSets 恒为空 | ✅ 已填充：命令/权限集带注册作用域归属（`CommandEntry.PluginName` / `PermissionSet.OwnerPlugin`），按插件分组输出 | `ManifestGenerator.GeneratePluginsManifest` 三参重载、`DesktopApplication.GeneratePluginsManifest` |
>
> 下列原文保留原始分析供追溯，**实施前须以复核结论为准**（教训：照 08-16 的现状表直接开工会重复劳动）。

1. **接入率 6%**：约 47 个插件中仅 3 个接 `CommandSecurity`（FileSystem 21/21、Shell 4/4、Http 4/5）；Store/Updater/Stronghold 等高敏插件只声明权限集、命令不绑定；约 20 个插件（Stronghold、Opener、Keychain、Tray、Log、Deeplink、FsWatch 等）**完全无权限声明**。
2. **lambda 注册盲区**：分析器只解析方法组引用，lambda 注册（如 `HttpPlugin.cs:44` 的 `http.fetch`）无法触发 WNET001/002，成为声明缺失的逃逸通道。
3. **`[RequireCapability]` 特性零使用**：特性、分析器、dispatcher 三方管线都已就绪，但没有任何插件真正用它（Wails.Net 双轨中"特性轨"实际空转）。
4. **`ManifestGenerator.GeneratePluginsManifest` 的 Commands/PermissionSets 恒为空数组**（`ManifestGenerator.cs:284-285`），插件级安全清单不可用。

#### 2.1.3 后续重构方案（SEC-H-003：插件安全声明全量迁移）

> **状态（2026-11-14）：第 1–3 点已落地，第 4 点（B2 flip）本轮完成。**
>
> - 第 1 点（40 插件迁移）：302 条命令全部显式声明，仅 `http.fetch` 曾遗留（本轮补齐）→ **302/302**。
> - 第 2 点（lambda → 方法组）：`WNET003` 已落地作长期守门；`http.fetch` 已方法组化。
> - 第 3 点（补全 plugins.manifest）：`GeneratePluginsManifest` 新增 `commandRegistry` / `permissionSets` 可选参数，
>   按注册作用域归属分组输出 `Commands` 与 `PermissionSets`；`DesktopApplication` 已传入真实注册结果。
> - 第 4 点（B2 flip）：`FindUncoveredCapabilities` 已移除"未声明即跳过"的存量豁免 ——
>   未传 `CommandSecurity` 且无 `RequiredCapabilities` 的命令现在报"未显式声明安全姿态"。
>   守门测试：`tests/Wails.Net.Application.Tests/Security/PluginCommandSecurityPostureTests.cs`
>   （全部已引用插件的命令姿态审计 + B2 flip 交叉验证）。

1. **分批迁移 40 个插件**：每个 `MapCommand` 调用点补 `CommandSecurity.Require(...)`（或 `.Public()` 显式标注公开命令）；高敏插件优先（Stronghold、Store、Updater、Keychain、Shell、Process、Opener、Sql、Upload、Websocket、FsWatch、Deeplink）。
2. **lambda 注册改方法组**：`http.fetch` 等改为私有方法 + 方法组注册，纳入分析器覆盖；分析器增加 WNET003（提案）：对无 CommandSecurity 参数的 `MapCommand` lambda 注册给出 Information/Warning，迫使显式 `.Public()`。
3. **补全 plugins.manifest**：`GeneratePluginsManifest` 从 `CommandRegistry` 按插件前缀填充 Commands 与 PermissionSets（数据源已存在，纯接线工作）。
4. **B2 flip**：全量迁移完成后，将 `FindUncoveredCapabilities` 中"RequiredCapabilities 为空且 Security 为 null → 跳过"的存量豁免（`ManifestGenerator.cs:362-363` 注释）翻转为**报未覆盖**，实现"命令必须显式声明安全姿态"的终态。

#### 2.1.4 验收标准

> 本节为 `CommandSecurityEnforcementTests.cs:14` 引用的验收标准（已达成项固化 + 后续阶段新增）：

**已达成（SEC-H-001 阶段一，测试 `CommandSecurityEnforcementTests` 守门）：**

1. `CommandSecurity.Require(...)` 通过 `CommandDispatcher` 的 capability 校验链（拒绝时返回 `Permission denied: {Method}`）；
2. `CommandSecurity.WithScope(...)` 显式 ScopeMapping 优先于源生成器 extractor；
3. `CommandSecurity.Public()` 被 `ManifestGenerator` 识别为公开命令（不计入未覆盖清单）。

**后续阶段（SEC-H-003 完成时，2026-11-14 全部达成）：**

4. ✅ 全部内置插件的 `MapCommand` 注册要么传 `CommandSecurity`，要么显式 `.Public()`，CI manifest 校验零未覆盖
   （302/302；守门测试 `PluginCommandSecurityPostureTests.AllBuiltinPluginCommands_DeclareExplicitSecurityPosture`）；
5. ✅ `plugins.manifest.json` 的 Commands/PermissionSets 按插件分组输出，与实际注册一致
   （`ManifestGeneratorTests.GeneratePluginsManifest_FillsCommandsAndPermissionSets_FromRegistrationScope`；
   全插件全量快照尚未固化，属可选加固）；
6. ✅ WNET001/002 之外无 lambda 逃逸通道（`WNET003` 生效，且 `http.fetch` 已方法组化）。

### 2.2 Strict 默认化与 Compatibility 语义修正（SEC-H-004，含破坏性更改）

#### 现状与缺陷

- **缺陷 A（Strict + Enabled=false 的 capability 加载脱节）**：`PermissionManager.Enabled = (Mode == Strict || options.Enabled)`（`PermissionManager.cs:66`），Strict 下即使 `Enabled=false` 权限检查也生效；但 `DesktopApplicationBuilder.LoadCapabilities` 仅在 `permissionOptions.Enabled == true` 时加载 capability 文件（`DesktopApplicationBuilder.cs` LoadCapabilities 内 `!permissionOptions.Enabled` → 跳过）。结果：**Strict + Enabled=false 的组合下检查生效但授权永远为空，`DenyByDefault=true` 会拒掉所有带能力要求的命令**，且用户无从知道原因。
- **缺陷 B（Compatibility 的 DenyByDefault 完全无效）**：`IsGranted` 第一步 `!Enabled → return true`（`PermissionManager.cs:379`）。Compatibility + Enabled=false 时 DenyByDefault 不参与任何决策，与配置字面语义（"默认拒绝"）相矛盾，仅在构造时输出一次 warning。
- **`DesktopHostOptions.Permissions`（List\<string\>）为死配置字段**：无任何消费点，制造"配置了就生效"的错觉。

#### 重构方案（破坏性）

1. **修缺陷 A**：capability 自动加载条件改为 `PermissionManager.Enabled`（即 `Mode == Strict || Enabled`），与检查生效条件对齐。
2. **重定义开关语义**（二选一，推荐 a）：
   - a. **移除 `PermissionOptions.Enabled` 总开关**，仅保留 `Mode`（Compatibility | Strict）。Compatibility 显式降级为"记录放行日志的开发模式"，Strict 为唯一生产语义——语义单一，无组合爆炸；
   - b. 保留 `Enabled` 但文档明确其只影响 Compatibility 模式，且 Strict 下配置 `Enabled=false` 触发启动 warning。
3. **默认值翻转**（破坏性）：`PermissionMode` 默认值 `Compatibility` → `Strict`（模板已默认 Strict，翻转只影响手写配置的存量项目，靠启动日志 + 迁移指南缓冲）。
4. **移除死字段** `DesktopHostOptions.Permissions`（破坏性，见 §4.1）。

#### 验收标准

- Strict + 未配置 Enabled 时 capability 正常加载；
- `DenyByDefault` 在两种模式下行为都与其字面语义一致；
- 存量 Compatibility 项目的迁移路径在 `permission-migration-guide.md` 中可一站式完成。

### 2.3 权限清单随插件包分发 + JSON Schema 自动生成（SEC-H-006，权限清单随包分发 + Schema 生成）

#### 对标事实

- **Wails.Net 现状**：权限只存在于代码中的 `RegisterPermissionSet` 调用；CLI `check` 需要**手动传 `--permissions-manifest`** 目录才能校验权限存在性（`Check/CapabilityChecker.cs:289-341`）；无 schema 生成，capability 编写无补全；`window.*` 等核心命令的权限模型依赖 WindowPlugin 是否注册（见 2.2 缺陷 C）。

#### 重构方案

1. **NuGet 包内携带权限清单**：每个 `Wails.Net.Plugins.*` 在 csproj 中以内容文件打包 `permissions/plugin.json`（插件名、权限标识、权限集、scope 描述），与 `plugins.manifest.json` 生成链打通（SEC-H-003 的数据源）。
2. **CLI 自动发现**：`wails-net check` / `build` 前置校验默认从项目引用的 NuGet 包（`PackageReference` 解析 + 包内 `permissions/` 目录）收集已知权限，替代手动 `--permissions-manifest`。
3. **Schema 生成**：`wails-net generate schema`（或 check 附带）输出 capability JSON Schema（含全部已知权限枚举），供 IDE 补全。
4. **核心命令 ACL 化**：`MessageProcessor.ProcessWindowAsync` 的硬编码 `DispatchWindowAction` switch（`MessageProcessor.cs:871-1077`）**不做任何权限检查**——当 WindowPlugin 未注册时，前端发 `window.*` 消息可直接操纵窗口（SetTitle/Fullscreen/Close…），绕过整个权限链（缺陷 C）。方案：硬编码 switch 仅保留为 Strict 之外的兼容回退；Strict 模式下 `window.*` 必须走 WindowPlugin 注册的命令路径（受 `window:default` 等权限约束），未注册即拒绝。

#### 验收标准

- `wails-net check` 无需手动参数即可校验全部引用插件的权限标识；
- Strict 模式下 `window.*` 消息无授权时被拒绝（新增安全负向测试）；
- capability 文件在 VS Code 获得 schema 补全。

### 2.4 asset protocol scope / `convertFileSrc`（继承 OPT-SEC-005，升级为 SEC-H-007）

维持 `optimization-roadmap.md` §6.4 方案（`wails-asset://` 协议 + `AssetProtocolScope` + capability 授权 + 前端 `convertFileSrc`），本文补充两点：

1. **配置单一来源**：asset 协议访问应由 capability 单一来源授权（`asset:allow-read-<pattern>` 权限 + scope），避免多处配置漂移。
2. **实现落点**：四平台 scheme/请求拦截层统一后（RH-IPC-02）只需实现一次；Range 请求支持 AssetServer 已有（`AssetServer.cs:912` 起），协议层透传即可。

### 2.5 安全卫生修复清单（小项，随批处理）

| 项 | 位置 | 问题 | 处置 |
|---|------|------|------|
| ScopeInitializer 覆盖 | `Security/ScopeInitializer.cs:30-50` | 同一 permissionId 同时配 Paths 与 Urls 时 UrlScope 静默覆盖 FileSystemScope | 启动期报错或支持组合 scope |
| DeclarePermission 不重放 | `Plugins/DeferredPermissionRegistrar.cs:59-76` | `_declaredPermissions` 收集后无访问器、不 Replay | 暴露只读集合并在 ReplayInto 中登记到 manifest 数据源 |
| OriginInfo 未接线 | `Security/IpcOriginValidator.cs:39-61` | 三字段版本（iframe 场景 Origin+TopOrigin）无生产调用方 | 在 WebView 拦截路径（RH-IPC-02 统一管线）传入 OriginInfo |
| AssetServer CORS 固定 `*` | `AssetServer.cs:557` | 与 IPC 层白名单回显策略双轨、不一致 | 资源 CORS 也可配置化，或文档明确两者边界（IPC 严、静态资源宽） |
| `Grant(string,string?)` 死重载 | `PermissionManager.cs:185` | 无调用方 | 移除或 internal 化 |
| WebSocket 无窗口过滤 | `WebSocketBroadcaster.cs:60-86` | Server 模式全量广播，targetWindowId 仅透传载荷 | 为 WS client 建立窗口映射（握手首消息带 windowId），实现真定向 |
| Updater 返回 `"{}"` 吞错 | `UpdaterPlugin` 命令 | 失败返回空 JSON 字符串而非错误对象 | 返回结构化 CallError |

---

## 3. 运行时 / IPC / 窗口主线（对标 Wails v3）

### 3.1 [P0 缺陷] `WebviewWindow` facade 与平台实现断链（RH-WIN-01）

#### 事实链（全部复核确认）

1. `WebviewWindow.Impl` 为 `{ get; internal set; }`（`Windows/WebviewWindow.cs:37`），全仓库（src/ 下）**无任何赋值点**；
2. `WindowManager.CreateWebviewWindow`（`Managers/WindowManager.cs:78-113`）创建 facade 后调用 `_platformApp.CreateWebviewWindow(id, options)`，但平台返回/创建的实现对象（`Win32WebviewWindow` 等）**从未回填** `window.Impl`；`Application.CreateWebviewWindow`（`Application.cs:910-922`，自持 `_windows`/`_nextWindowId++`）同样不回填；
3. facade 所有方法经 `ImplRequired`（`WebviewWindow.cs:17`）抛 `InvalidOperationException("窗口平台实现尚未设置。")`，包括 `ExecJS`（`:452`）；
4. **后果矩阵**：
   - `MessageProcessor` contextmenu 路径 `window?.Impl is not null`（`MessageProcessor.cs:723` 附近）恒 false → "P1-4 对齐 Wails v3 OpenContextMenu" 实际不可达；
   - `EventIPCTransport.NotifyEvent` 对每个窗口 `window.ExecJS(js)`（`EventIPCTransport.cs:146-162`）→ Windows 因 NativeIpcTransport 抢占而提前 return（`:96-100`）**掩盖了该缺陷**；Linux/macOS/Android（无 NativeIpcTransport 注册）事件下行要么异常被 catch 吞掉、要么窗口列表为空提前返回——**后端→前端自定义事件在非 Windows 桌面平台不可达**（或仅偶然可达）；
   - 用户 API `Application.Windows` / `GetWindow(id).SetTitle(...)` 等 100+ 方法全部不可用。

#### 重构方案

1. **建立回填契约**：`IPlatformApp.CreateWebviewWindow` 改为返回 `IWebviewWindowImpl`（或增加 `TryCreateWebviewWindow(out impl)`），`WindowManager` 拿到后 `window.Impl = impl` 完成装配；平台 App 内部字典降级为缓存或删除。
2. **回归验证**：新增端到端测试（WailsTestHost + Mock 平台已具备 `MockWebviewWindow`）断言 facade 全方法可用；非 Windows 平台事件下行用 `WailsTestHost` + 真实 EventIPCTransport 验证。
3. 该项与 RH-WIN-02（注册表统一）同期实施，避免二次破坏。

#### 验收标准

- `Application.GetWindow(id)` 返回的 facade 可调用 `SetTitle`/`ExecJS`/`CapturePreviewAsync` 等全部方法；
- Linux（WSL GuiContract 或实机）与 Server 模式下后端 `Events.Emit` 前端可收到（不再依赖 Windows 的 NativeIpc 掩盖）；
- contextmenu IPC 全链路测试通过。

### 3.2 [破坏性] 窗口注册表统一（RH-WIN-02）

#### 现状

四套并行注册表互不同步：

| 注册表 | 位置 | 维护者 |
|---|---|---|
| `Application._windows` + `_nextWindowId`（非原子 `++`） | `Application.cs:32-33, 910-922` | `Application.CreateWebviewWindow` |
| `WindowManager._windows/_windowNames/_nextWindowId`（Interlocked） | `Managers/WindowManager.cs:18-28, 78-113` | `WindowManager.CreateWebviewWindow` |
| 各平台 App `_windows` 字典 | `WindowsPlatformApp.cs:120`、`LinuxPlatformApp`、`AndroidPlatformApp` 等 | 平台创建路径 |
| `NativeIpcTransport._windowImpls` | `NativeIpcTransport.cs:40` | 仅 Windows + Testing Mock 注册 |

两条创建路径（Application vs WindowManager）互不感知；`EventIPCTransport` 走 `WindowManager.GetAllWindows()`、`NativeIpcTransport` 走自己的表、`MessageProcessor` 走注入的 lookup——同一窗口在不同子系统中有不同可见性。

#### 重构方案（破坏性）

1. **单一事实源 = WindowManager**：删除 `Application._windows`/`_nextWindowId` 与 `Application.CreateWebviewWindow`，`Application.Windows`/`GetWindow`/`DestroyWindow` 全部委托 WindowManager（facade API 不变，内部收敛）。
2. **平台字典降级**：平台 App 不再自持窗口表，创建即返回 impl 给 WindowManager（与 3.1 联动）；`NativeIpcTransport` 的注册改由 WindowManager 在 `Impl.SetNativeMessageHandler` 装配时统一完成，Windows 平台 App 中的特判（`WindowsPlatformApp.cs:497-501`）删除。
3. 窗口 ID 生成全部走 `Interlocked.Increment`。

#### 验收标准

- 全仓库仅存一份窗口注册表；
- 多窗口 demo（MultiWindow/Frameless/DragAndDrop）行为不变；
- 并发创建窗口压力测试无 ID 冲突。

#### 实施进展（2026-11-14）

已完成（提交见 `refactor(runtime): RH-WIN-02 统一窗口注册表与原生 IPC 装配`）：

| 项 | 落地方式 |
|---|---|
| 单一事实源 = WindowManager | R1-2 起 `Application` 已无 `_windows`/`_nextWindowId`，`Windows`/`GetWindow`/`DestroyWindow`/`ShutdownAsync` 全部委托，ID 走 `Interlocked.Increment` |
| 原生 IPC 注册统一 | `WindowManager.CreateWebviewWindow` 在回填 `Impl` 后依据新增的 `IPlatformApp.SupportsNativeMessageChannel` 调用 `RegisterWindow`；`WindowsPlatformApp.cs` 与 `MockPlatformApp` 内的注册特判删除 |
| 原生 IPC 注销统一 | `WindowManager.ReleaseWindow`（`WindowClosed` 事件与 `DestroyWindow` 双路径共用、幂等）负责 `UnregisterWindow`；`Win32WebviewWindow.Close` 内的注销与 `Application.Get()` 静态定位器删除 |
| 平台字典降级为缓存 | 新增 `IPlatformApp.OnWindowDestroyed(uint)`（默认空实现），Windows/Linux/macOS/Android/Mock 在窗口销毁时移除自身缓存条目——修复此前"平台字典只增不删，退出/换主题时仍遍历已销毁窗口"的缺陷 |
| 能力门控（防回归） | `SupportsNativeMessageChannel` 默认 `false`：Linux 的 `SetNativeMessageHandler` 是空操作、Android 走 `JavascriptInterface` 独立桥，误标为真会让 `EventIPCTransport` 认为原生通道已接管而跳过 `ExecJS` 兜底，重演 RH-WIN-01 掩盖的事件丢失 |
| 测试 | `WindowNativeIpcRegistrationTests`（5 项）：能力为真时注册、为假时跳过、销毁反注册 + 通知平台、重复销毁幂等、32 并发建窗 ID 唯一且逐个注册 |

#### 实施进展（2026-11-14 终态）

平台窗口表已彻底移除（本项收尾，提交 `refactor(runtime): 平台层不再持有窗口表，统一查询 WindowManager`）：

| 项 | 落地方式 |
|---|---|
| 平台窗口表删除 | Windows / Linux / macOS / Android 的 `Dictionary<uint, XxxWebviewWindow> _windows` 全部删除，改为只读查询 |
| 查询入口 | 新增 `IPlatformWindowAccessor`（唯一实现 = `WindowManager`）+ `IPlatformApp.AttachWindowAccessor`（DIM）；`WindowManager` 在构造与 `SetPlatformApp` 两处注入，覆盖"构造期已有 platformApp"与"DI 后置注入"两条路径 |
| 平台枚举方式 | `EnumerateWindows()` 私有 helper：遍历 accessor 快照 + `is XxxWebviewWindow` 类型判断（零反射），承载 Show/Hide/应用菜单/图标/暗主模式/主窗口句柄/GetCurrentWindowId |
| 快照语义 | 只返回 `Impl` 已回填的窗口；创建过程中的窗口不可见（测试 `WindowPlatformAccessorTests` 守门），避免平台操作半初始化实例 |
| 保留项 | `IPlatformApp.OnWindowDestroyed` 保留给需要平台侧资源清理的实现（Mock / 后续原生资源回收），不再是"缓存一致性"补丁 |
| 测试 | 新增 `WindowPlatformAccessorTests`（4 项）：构造注入、后置注入、生命周期快照、空集合语义 |

剩余（未做）：`IIpcTransportStrategy` 在桌面四平台仍未推广（RH-IPC-02 §3.3 第 4 点）；RH-IPC-02 进展见 §3.3。


### 3.3 [破坏性] 四平台 IPC/资源/ready 管线统一（RH-IPC-02，继承 OPT-IPC-003 剩余）

#### 实施进展（2026-11-14）

下方「现状」表记录的是 2026-08-18 基线，**其中前三行（POST /wails、静态资源 + SPA 回退、上行 IPC 同步阻塞的管线化）已由 R2-4 完成**：核心包新增 `Wails.Net.Application.Webview.WebviewRequestPipeline` + `WebviewRequest` / `WebviewResponse` / `WebviewPipelineSync`，五处回调（Win32 / Linux / macOS 的 scheme 与 WebMessage、Android 两处）全部改为「原生壳 + `pipeline.HandleAsync`」，详见 `fusion-architecture-audit.md` 的 R2-4 条目。

本轮补齐表中后三行：

| 项 | 落地方式 |
|---|---|
| runtime JS 注入（原 3 份） | 新增 `WindowRuntimeBootstrap`（核心包，非反射）：统一"只注册一次"的脚本获取（`TryGetScript`，生成失败可重试）、注入成功上报（`MarkRuntimeInjected`）、控制器重建复位（`ResetInjection`）。三平台只保留各自的 document-start 注册 API（`AddScriptToExecuteOnDocumentCreatedAsync` / `UserContentManager.AddScript` / `WKUserContentController.AddUserScript`） |
| WindowRuntimeReady 派发（原 4 处 4 种时机） | 统一为**导航完成 + 运行时注入完成，二者皆满足且仅派发一次**。Windows `NavigationCompleted`、Linux `LoadEvent.Finished`、macOS `OnNavigationFinished`、Android `OnPageFinished` 只上报事实；Android 因 runtime 由资源拦截随文档供给，以 `runtimeInjectionRequired: false` 构造，只等导航完成（与改造前时机一致） |
| 上行 IPC 阻塞（macOS 脚本通道） | `HandleScriptMessage` 不再在 AppKit 主线程 `GetAwaiter().GetResult()`，改为后台等待后用 `DispatchOnMainThread` 回主线程投递响应，消除"命令内部回到主线程即死锁"的路径 |

行为变化（需知悉）：

- **Linux 的 ready 时机后移**：此前在注入完成（导航尚未完成）时就派发，现在等 `LoadEvent.Finished`；订阅方拿到 ready 时页面才真正就绪。
- **macOS 的 ready 增加注入前置条件**：此前 `OnNavigationFinished` 无条件派发，现在注入失败则不派发（可由后续导航重试）。

剩余（未做）：frameless 拖拽脚本与 DropTargetDetails 仍各平台本地实现（仅 Windows/Linux 各一份，逻辑相同但注入 API 不同）；`IIpcTransportStrategy` 推广（方案第 4 点）未动。

#### 现状（平台层重复度实测）

| 逻辑 | Windows | Linux | macOS | Android | 份数 |
|---|---|---|---|---|---|
| POST /wails/\* IPC 处理（读 body → `HandleMessageFromFrontend(...).GetAwaiter().GetResult()` → JSON 响应，默认响应串三处重复） | `Win32WebviewWindow.cs:762-792` | `LinuxWebviewWindow.cs:446-506` | `MacOSWebviewWindow.cs:1319-1362` | `WailsWebMessageListener.cs:44-67` | 4 |
| 静态资源 + SPA 回退（同构算法；仅 Android 多 `api/` 前缀排除） | `:794-838` | `:514-552` | `:1369-1405` | `WailsWebViewClient.cs:92-120` | 4 |
| runtime JS 注入（`_runtimeInjected` 守卫 + 平台 document-start API） | `:1155-1184` | `:363-404` | `:441-468` | 无（依赖资源同供） | 3 |
| WindowRuntimeReady 派发点 | NavigationCompleted `:1144` | 注入完成后 `:397` | DidFinishNavigation `:1251` | OnPageFinished | 4（触发点各不相同） |
| frameless 拖拽脚本注入 | `:1131-1140` | `:846-856` | — | — | 2 |
| DropTargetDetails 查询 | `:1281-1326` | `:787-815` | — | — | 2 |
| JsonOptions 定义 | 自建 `_jsonOptions` | 自建 `_jsonOptions` | 用 `JsonOptions.Default` | 用 `JsonOptions.Default` | 2 种写法 |

**额外风险**：四个平台的上行 IPC 全部在 **UI/消息循环线程上 `.GetAwaiter().GetResult()` 同步阻塞**等待任意异步命令完成（命令可能包含任意耗时 IO）。Wails v3 平台层为异步回调装配；当前写法在命令耗时/内部分派回 UI 线程（`DispatchOnMainThread`）时有**死锁与卡 UI 风险**。

#### 重构方案（破坏性，平台包内部）

1. **抽取共享 `WebviewRequestPipeline`**（核心包内，非反射、AOT 安全）：
   - `HandleIpcPost(body, windowId) → Task<string>`：统一 body 读取、消息处理、响应序列化（含默认响应常量）；
   - `ServeAssetAsync(path, windowName) → AssetResult`：统一静态资源 + SPA 回退 + `api/` 前缀排除策略（Android 特例参数化）；
   - `BuildRuntimeInjection()`：统一 runtime JS + console hook + frameless 拖拽脚本 + DropTarget 脚本的装配清单，平台只提供 document-start 注入 API；
   - `RaiseRuntimeReady(windowId)`：统一 WindowRuntimeReady 派发语义（建议统一为"导航完成 + runtime 注入完成"双条件）。
2. **平台窗口层改为"原生壳 + 管线调用"**：每个平台保留 WebView 创建、scheme 注册、WndProc/信号桥接等原生细节，通用逻辑全部调用管线。预期净删 800-1200 行平台代码。
3. **异步化上行 IPC**：WebView2 `WebResourceRequested` 支持异步 deferral；WebKitGTK URISchemeTask 可异步完成；Android `JavascriptInterface` 同步限制保留但文档标注（其桥本身在后台线程，风险较低）。逐步把 `GetResult()` 改为平台原生异步完成模式，消除 UI 线程阻塞。
4. **`IIpcTransportStrategy` 推广**（审计 P1-4 遗留）：桌面模式的传输装配（NativeIpc 优先 → scheme POST 常备 → EventIPC 兜底）从 `Application.cs:336-359` 的硬编码与平台内联注册，收敛进 `DefaultIpcTransportStrategy`，平台窗口只暴露能力（HasNativeMessageChannel）。

#### 验收标准

- 四平台的 IPC/资源/ready 行为由同一管线测试矩阵覆盖（每平台仅原生壳测试）；
- UI 线程不再同步等待用户命令完成（除 Android 同步桥，需文档说明）；
- `Application` 中事件传输装配代码删除，策略服务可单测。

### 3.4 HttpTransport 终局决策（RH-IPC-04）

现状：`HttpTransport` 安全逻辑完整（token/Origin/分块/CORS）但生产零装配（`IpcTransportStrategy.cs:104-114` 注释：HTTP 端点 URL 注入协议未扩展，前端不可达）；前端 `transport.ts:86` 的 `messageUrl = flags.assetServerUrl + "/wails/message"` 在桌面模式落到平台拦截（不经过 HttpTransport），Server 模式落到的 `WebSocketTransport` 无 `/wails/message` POST 分支——**该端点两种模式下实际都不可达**。

二选一（建议 a）：

- a. **删除独立 HttpTransport 与前端 HTTP 分块协议**：桌面大载荷走 Native IPC（Windows 已支持）或 scheme POST（Linux/macOS），Server 模式统一 WS（WS 本身无 512KB 限制）；前端 transport 三态策略简化为二态。删除约 600 行死代码与一套协议面。
- b. 接入生产：`_wails` flags 注入 `httpMessageUrl`，Server 模式 `WebSocketTransport` 增加 `/wails/message` POST 处理（复用 HttpTransport 处理器）。仅在确有"无 WS 环境"诉求时选择。

### 3.5 Channel 协议落地缺口与 opt-in 加密（RH-IPC-05，继承 OPT-RUNTIME-003）

现状复核：`channel:open/message/close` **只存在于注释**（`ChannelManager.cs:10-11`），`MessageProcessor.MessageTypes` 无 channel 类型、`DispatchMessage`（`:102`）无任何调用方、前端 runtime 无对应发送实现。即：**前端无法创建通道，Channel 仅后端单侧可用**（`ChannelManager.Create` + 注入 sender）。这与 Wails v3（Channel 为前后端双向流原语）存在实质差距。

方案：

1. 协议落地：`channel:open`（前端创建通道，返回 channelId）/ `channel:message` / `channel:close` 进入 MessageProcessor；前端 `wails.channel` API（`createChannel`/`onMessage`/`send`）。
2. 与命令系统打通：`MapCommand` 委托参数支持 `IChannel`（由 dispatcher 从 payload 的 channelId 解析注入，与 `ICommandContext` 同类特殊参数）——即 Channel-on-argument 模式，这是长耗时任务进度回传的关键原语（Updater 下载进度等场景直接受益）。
3. 加密（opt-in）：`IpcOptions.ChannelEncryption`（默认 false）；开启后建链时下发会话密钥（CSPRNG，随 runtime bootstrap 注入），消息体 AES-GCM 加密，前端 WebCrypto 对应解密；关闭时零开销。仅 Server 模式模板建议默认开启。
4. 顺带修正：`ChannelManager` 为 static class（见 §4.4 DI 化破坏项）。

### 3.6 生命周期阶段细化（RH-IPC-06，继承 OPT-RUNTIME-002 剩余）

现状：`ApplicationLifecycleState` 仅有 Created→Starting→PlatformStarted→Running→Stopping→Stopped（`ApplicationLifecycleState.cs:12-45`），枚举注释自述规划的 HostStarted/FirstWindowCreated/RuntimeInjected/IpcReady/AppReady 未落地；`DesktopHostedService.StartAsync` 仅 await `PlatformStarted`（30s 超时后仅 warning 继续）。

方案：补齐四阶段信号（TCS + 事件各一份），`WaitUntilReadyAsync(stage)` 可参数化等待；`WindowRuntimeReady` 派发点统一后（3.3）RuntimeInjected 阶段天然可得；DevTools/诊断面板（未来）可直接消费阶段流水。**破坏性面**：`ApplicationLifecycleState` 枚举增加值（源兼容）。

### 3.7 Native IPC 通道平台扩展（RH-IPC-07）

- **macOS**：`MacOSWebviewWindow` 已实现 `SetNativeMessageHandler`（`:1528-1531`）且 `HandleScriptMessage` 已优先走 handler（`:1277-1281`），但 `MacOSPlatformApp` 从不调用 `NativeIpcTransport.RegisterWindow` —— handler 无人注册，**接线即得**原生通道（含事件下行抢占，摆脱 ExecJS 兜底）。
- **Linux**：WebKitGTK 无 WebView2 式 WebMessage API，但 `UserContentManager` 支持 script message handler（GirCore 0.8.0 覆盖情况需验证）；若可用则注册 `window.webkit.messageHandlers.wails` 通道，与 macOS 同构；不可用则维持 scheme POST 并在传输矩阵标注。
- 完成后 `SupportsBinary`（`NativeIpcTransport.cs:52`，当前 Windows-only）可按通道能力声明。

### 3.8 前后端协议契约共享（RH-IPC-08）

现状：消息类型字符串（`call`/`cancel`/`event.emit`/`contextmenu`/`query`…）、HTTP 头（`x-wails-token`/`x-wails-chunk-*`）、事件载荷字段（`senderWindowId`/`targetWindowId`）在前后端**各自硬编码**，无共享常量源；`transport.ts:141` 还残留"本包会挂载 window.wails.invoke"的陈旧注释（实际不挂载）。协议演进（如 3.5 channel）会放大漂移风险。

方案：`packages/wails-net-protocol`（或 runtime 包内 `internal/protocol.ts` + 后端 `ProtocolConstants.cs` 由同一 F#/mjs 脚本生成两侧），CI 校验两侧常量一致；`MessageTypes.System`（已定义无分支，`MessageProcessor.cs`）一并处置（实现或删除）。

### 3.9 dev server 反向代理（RH-IPC-09）

现状：CLI `dev` 并行启动 vite dev server + `dotnet watch` 并打印 `Frontend.DevServerUrl`（`DevCommand.cs:169-171`），但 **AssetServer 无任何 Proxy/DevServerUrl 处理**（`AssetOptions` 无相关字段）——开发态 WebView 加载的页面与后端服务的装配关系不明/依赖手工约定；Wails v3 的 assetserver 内置 dev URL 反向代理（hmr websocket 透传）。

方案：`AssetOptions.ExternalDevServerUrl`（或 `Wails:Assets:DevServer`），启用时 AssetServer 将非 `/wails/*` 请求反向代理到 dev server（含 WS 透传），runtime 注入继续由后端完成；`wails.json` 的 `frontend:devServerUrl` 由 CLI `dev` 注入环境变量。这是 `wails dev` 体验闭环的关键缺口（HMR/端口漂移/多窗口一致）。

---

## 4. 宿主主线（对标 ASP.NET Core）

### 4.1 [破坏性] Options 兼容字段移除（RH-HOST-01）

| 字段 | 位置 | 现状 | 处置 |
|---|---|---|---|
| `DesktopHostOptions.AssetsDirectory` | `DesktopHostOptions.cs:23` | `Assets.RootPath` 为空时回退 + 迁移 warning（注释自认"未来主版本移除"） | 删除，保留一个版本的编译错误迁移指南 |
| `DesktopHostOptions.Permissions` | `:41` | 死字段，无消费 | 删除（并入 §2.2） |
| `HostingAppConfig.Security.CapabilitiesDir` | `AppOptions.cs` | `PermissionOptions.CapabilitiesDirectory` 的回退路径 | 删除回退，单一来源 |

破坏窗口内一次清掉，`AppSettingsTemplateDriftTests` 增加负向用例（旧键出现即失败）。

### 4.2 Application 协调器化收尾（RH-HOST-02，继承审计 P1-1）

已落地 `RuntimeServicesCoordinator`（`Coordinators/`）；`IRuntimeCoordinator.cs:12-16` 注释规划的 `FrontendRuntimeCoordinator` / `PlatformLifecycleCoordinator` 未实现。仍内联在 `Application`（1716 行）的职责与去向：

| 内联职责 | 位置 | 去向 |
|---|---|---|
| service route 暂存/挂载 | `Application.cs:446-454, 804-817` | `IServiceRouteRegistry`（挂 AssetServer） |
| 自定义 scheme 表 + 锁 | `:483-563` | `ICustomSchemeRegistry` |
| shutdown tasks / OnBeforeStart hooks | `:1140-1150, 1268` | `IApplicationLifecycleHooks` |
| 单实例 | `:961-981` | `ISingleInstanceCoordinator` |
| GenerateRuntimeJs / flags 注入 | `:1559-1624` | `FrontendRuntimeCoordinator`（含 HttpTransport 分支清理，见 3.4） |
| 事件传输监听器装配 | `:336-359` | `IIpcTransportStrategy`（见 3.3-4） |

验收：`Application` 降到 <800 行、字段数显著下降，public facade API 不变。

### 4.3 `Application.Get()` 静态定位器收敛（RH-HOST-03）

`Application._globalApplication`（`Application.cs:126`）被核心 6 处 + 四平台约 20 个文件 + 多个插件静态调用。平台窗口/平台 App 已有构造注入上下文，却仍回退静态获取（如 `Win32WebviewWindow` 内 `Application.Get()?.DispatchWindowEvent(...)`）。

方案（非破坏优先）：平台窗口构造时注入 `IApplicationRuntime`（窗口事件派发、AssetServer、Events 的窄接口），静态 `Get()` 保留但降级为"仅启动早期与兼容路径可用"，新代码审查项禁止（加入 AGENTS.md §7.3 检查清单）。插件侧经 `IPluginContext` 已有正规通道，逐步迁移。

### 4.4 [破坏性] `ChannelManager` 静态类 → DI 实例（RH-HOST-04）

static `ConcurrentDictionary` 全进程共享，多测试宿主/多 Application 实例互相污染（现有 `Clear()` 即为测试打的补丁）。改造为 `IChannelManager` 单例注入 `Application`/`MessageProcessor`；`ChannelManager.Create` 静态入口保留一个版本作转发并标记 Obsolete。

### 4.5 EventProcessor 并发与 API 卫生（RH-HOST-05）

- `RegisterHook` 无锁添加到 `List<Func<CustomEvent,bool>>`（`EventProcessor.cs:29, 128-131`）——并发注册/触发竞争，改 CopyOnWrite 或锁；
- `SetWailsEventListener`（清空只留一个，`:72-79`）与 `Clear`/`Reset` 语义重复（`:404/:418`）——破坏窗口移除兼容方法；
- `Application.CreateWebviewWindow` 的 `_nextWindowId++` 非原子（`:912-914`）——随 RH-WIN-02 一并消除。

---

## 5. 插件生态主线（可安装插件 + 双包模型）

### 5.1 前端资产承载（RH-PLUGIN-01，继承 OPT-PLUGIN-001/002）

`IPluginAssetRegistrar` / `/plugins/{name}/` 路由至今未实现（审计与 roadmap 均标注"未动"）。方案维持 roadmap §5.4（manifest + MapAssets + AssetServer 虚拟路径 + 注入），补充：

- 与 SEC-H-003 的 plugins.manifest 合并设计：manifest 同时描述 commands/permissions/assets 三块，成为插件包的单一元数据文件；
- 默认 **autoInject=false**：脚本注入必须是显式声明（安全默认），注入清单进 capability 审计（哪个插件向哪个窗口注入了脚本）。

### 5.2 npm 前端包治理（RH-PLUGIN-02）

- 40 个 `wails-net-plugin-*` 中绝大多数为 4 行 stub；`wails-net-plugin-updater` 的 `dist/index.js` 与 4 行 `src` 不一致（dist 疑为旧产物）——**发布产物与源漂移**，应 CI 化（`verify-versions.mjs` 扩展为 dist 新鲜度校验：dist 必须由当前 src 构建）；
- SEC-H-003 迁移时同步补齐每个插件的 TS wrapper（从 commands.manifest 生成，统一 `plugin-<name>-api` 命名），stub 包降级为纯类型包或删除。

### 5.3 manifest 生成链补全（RH-PLUGIN-03）

`commands.manifest.json` 已含 TypeScript 参数类型映射（`ManifestGenerator.cs:146-203`）；缺：插件级归属（commands → plugin 分组）、权限集展开视图、前端 wrapper 全量生成（P2-1 目标）。落地后 `wails-net generate bindings --commands-manifest` 可为全部插件产出 TS API，消除手写漂移。

---

## 6. 平台一致性与文档治理

### 6.1 平台重复逻辑统一

见 §3.3 清单；另有个案：`LoadIconFromBytes` 两份（`WindowsPlatformApp.cs:822-857` 与 `Win32WebviewWindow.cs:3193-3222`，后者注释自认复用）、初始窗口选项应用三处异构（Win32 `ApplyInitialOptions:454-524` / MacOS `ApplyWindowOptions:313-435` / Linux 构造内分散）——统一为 `WebviewWindowOptionsApplier`（核心包，调用 impl 接口）。

### 6.2 macOS 实际状态与文档漂移（RH-DOC-01）

AGENTS.md 与 docs 多处称 macOS 为"骨架/ServerMode 降级"，实际 `#if MACOS` 分支已有 **完整 AppKit + WKWebView 实现**（`MacOSPlatformApp.cs` 1043 行、`MacOSWebviewWindow.cs` 1778 行、菜单/托盘/Keychain/对话框齐备；`#else` 才是 Server 降级）。文档与 ADR-0004 之后的认知严重漂移；且 macOS 实现缺少对应测试与 CI（无法在 Windows/Linux CI 编译验证 `#if MACOS` 分支，建议增加 macos runner 的编译级 CI（仅 build，不跑 GUI 测试））。文档更正 + `MacOSPlatformApp` 接 NativeIpcTransport（3.7）一并处理。

### 6.3 Android 专项

- `WailsBridge.invoke` 同步桥在 JS 线程阻塞等待后端（`WailsWebMessageListener.cs:59-60`）——长命令冻结前端主线程；短期：文档警示 + Updater 等长任务改 Channel/事件回传；长期：评估 AndroidX `WebMessageListener`（port 消息异步通道，注释 `:12-18` 记录了未采用的现状）。
- `AndroidWebviewWindow` 大量 no-op（Maximise/Frameless 等，`:309-653`）符合移动端预期，但建议在 `WebviewWindowOptions` 校验层给出显式 warning（而非静默 no-op），并在文档中记录移动端能力矩阵。

### 6.4 文档治理

- 本文件即修复 `refactor-and-hardening-plan.md` 缺失；
- ~~`PLUGINS-ANALYSIS.md`（2026-08-07）描述拆分前的内置插件状态，应重定向到 `plugins.manifest.json` 生成或归档~~ → **已归档**（2026-11-12 移至 `docs/archive/plugins-analysis-2026-08-07.md`），现行内容以 `docs/plugins.md` 与 `docs/architecture/plugin-system.md` 为准；
- `docs/api/frontend-api.md` 宣称 `window.wails` 全局对象与 46 命名空间，与 runtime 实际（命名导出、无全局挂载）不符，需对齐。

---

## 7. 测试与工程治理（继承 OPT-TEST-001..003 并扩展）

| 项 | 现状 | 目标 |
|---|---|---|
| 源生成器测试 | `SourceGenerators.Tests` 仅 2 文件（Binding + 分析器） | snapshot 测试覆盖全部生成器输出（OPT-TEST-001） |
| 前端 runtime 测试 | 3 个 vitest 文件（`commands` / `transport` / `stream`），由 CI `test-frontend` 作业执行 | invoke/cancel/events/channels/fallback 全覆盖（OPT-TEST-002），含 3.5 新协议 |
| IPC 全链路无头测试 | WailsTestHost 具备底座 | 增加"真实 EventIPCTransport + Mock 窗口"组合，守住 3.1 类断链（OPT-TEST-003）<br>**已补**：`Wails.Net.Testing.Tests/EventDownlinkHeadlessTests.cs` 三条契约——不支持原生通道必走 ExecJS、已装配原生通道不得重复注入、声明支持但未装配必须回退而非丢事件 |
| 插件测试 | 17 个插件零测试（Tray/Menu/Window/Notification 等） | SEC-H-003 迁移时按插件补命令级测试（含权限拒绝路径） |
| NativeAOT CI | 仅 `Wails.Net.Cli` | 扩展到模板 App 产物（真实验证禁反射收益），Linux RID 提升为必过 |
| Linux CI | ubuntu 测试 allowed-failure | GirCore 依赖固定安装后转必过，消除平台一致性盲区 |
| Android E2E | 目录存在但 0 文件 | 恢复 fsx 驱动的 E2E 到 CI（模拟器可选 job） |

---

## 8. 破坏性更改清单与版本策略

| 编号 | 破坏性更改 | 影响面 | 迁移成本 |
|---|---|---|---|
| BK-01 | 移除 `PermissionOptions.Enabled`（或语义重定义）+ Strict 默认 | 手写配置的存量项目 | 低（启动日志 + 迁移指南） |
| BK-02 | `Application.CreateWebviewWindow` 内部化，窗口注册表统一 | 直接调用该 API 的代码 | 极低（现路径本就断链） |
| BK-03 | 平台窗口基类/管线重构 | 平台包内部 + 自定义平台实现者 | 中（平台实现者需适配基类） |
| BK-04 | 删除 `HttpTransport` + 前端 HTTP 分块协议（若选 3.4a） | 无生产用户（死代码） | 无 |
| BK-05 | `ChannelManager` 静态 → DI | 直接用静态类的代码 | 低 |
| BK-06 | 移除 `AssetsDirectory`/`Permissions`/`CapabilitiesDir` 兼容字段 | 旧配置键 | 低（一键替换表） |
| BK-07 | Strict 下 `window.*` 硬编码 switch 拒绝 | 未注册 WindowPlugin 又用 window 消息的项目 | 低（模板已带 WindowPlugin） |
| BK-08 | EventProcessor 兼容方法移除（SetWailsEventListener/Clear/Reset 之一） | 少数内部调用 | 极低 |
| BK-09 | 四平台 WindowRuntimeReady 统一触发语义 | 依赖旧时序的代码 | 低 |

**版本策略**：以上收敛在一个 minor 破坏窗口（建议 `0.2.0`，或直接作为 `1.0.0-rc` 前的清障版本），release note 逐条列出并附迁移表；此后进入 1.0 冻结期不再接受非安全类破坏。

---

## 9. 实施批次

| 批次 | 内容 | 依赖 |
|---|---|---|
| **R0（立即，1 迭代）** | RH-WIN-01 断链修复；§2.2 缺陷 A/B 修复（不翻转默认值）；§2.5 卫生项；RH-DOC-01 文档更正 | 无 |
| **R1（1-2 迭代）** | RH-WIN-02 注册表统一；RH-IPC-02 管线统一（含 UI 线程异步化）；RH-IPC-09 dev 代理 | R0 |
| **R2（破坏窗口，2 迭代）** | SEC-H-003 插件全量迁移 + manifest 随包分发 + schema；BK-01 Strict 默认翻转；BK-06/BK-07/BK-08；B2 flip | R1（schema 依赖管线） |
| **R3（1-2 迭代）** | RH-PLUGIN-01 前端资产；RH-PLUGIN-02/03 npm 与 manifest 链 | R2 |
| **R4（1-2 迭代）** | RH-IPC-05 Channel 协议+加密；RH-IPC-07 Native IPC 扩展；RH-IPC-06 生命周期细化；RH-IPC-04 HttpTransport 终局；RH-IPC-08 协议契约包 | R1 |
| **R5（持续）** | §7 测试/CI 治理；RH-HOST-02/03 协调器与定位器收敛（随各批顺带） | 各批 |

---

## 10. 总验收清单

- [ ] `WebviewWindow` facade 在四平台端到端可用（RH-WIN-01/02）；
- [x] 非 Windows 平台后端→前端事件下行有测试守门（无头层 `EventDownlinkHeadlessTests` 三条契约；各平台原生回调仍由 Windows 平台测试与 Android E2E 覆盖）；
- [ ] Strict 为默认且语义自洽：capability 加载与检查生效条件一致，DenyByDefault 无失效组合；
- [ ] 全部内置插件命令显式声明安全姿态（✅ 2026-11-14：302/302 + 姿态 drift 测试守门），`wails-net check` 自动发现插件权限并生成 schema（未做）；
- [ ] Strict 下无绕过权限链的消息路径（含 `window.*`）；
- [ ] 四平台 IPC/资源/ready 逻辑单点实现，UI 线程无同步阻塞等待用户命令；
- [ ] Channel 前后端协议可用，加密 opt-in；
- [ ] `wails dev` 经 AssetServer 反向代理闭环（HMR 可用）；
- [ ] 兼容配置字段清零，drift 测试覆盖；
- [ ] `Application` < 800 行且无新增静态定位器依赖；
- [ ] NativeAOT CI 覆盖模板 App；Linux CI 必过；
- [x] 本文档 §2.1.4 全部条目可勾选（含 SEC-H-003 阶段三条；2026-11-14）；

---

## 附：本轮对标参考

- Wails v3 `v3.0.0-beta.4`：Services 模型（`application.NewService`/`NewServiceWithOptions`，生命周期感知）、静态分析生成 TS 绑定、统一事件系统、Channel 原语、可安装插件（前端资源随插件分发）、updater manifest 协议 —— 见 [v3 文档](https://v3.wails.io/) 与仓库内 `docs/development/wails-v3-beta-comparison.md`。

---

**最后更新**：2026-08-16（首版：五路代码事实调研 + Wails v3 beta.4 对标；同时补齐 `CommandSecurityEnforcementTests.cs` 引用的缺失文档）
