# Wails.Net × Wails v3 最新开发分支对比：可参考项确认

> 分析日期：2026-08-07（beta.4 基线）；**2026-10-17 再确认：Wails v3 已演进至 v3.0.0-beta.28（2026-10-05），差异与待实现项复核见 §9**
> **2026-10-17 源码级复核**：§9.2/§9.1 各项已对照 Wails v3 `v3.0.0-beta.28` 官方 Go 源码（tag `v3.0.0-beta.28`，`v3/` 目录）逐项确认，结论与更正见 **§9.7**。
> 对比对象：wailsapp/wails **master 分支 v3/ 目录**（Wails v3 的实际开发流）
> 基线：AGENTS.md 参考版本 `v3.0.0-beta.4`（§1/§8 时点）；是否升级至 beta.28 待决策（见 §9.5）

---

## 1. 版本现状与基线差距

| 维度 | Wails v3（master） | Wails.Net |
|------|--------------------|-----------|
| 当前版本 | v3.0.0-beta.4（2026-08-05） | 参考 v3.0.0-*（alpha 时代），已落后约 1 个里程碑 |
| 里程碑 | 2026-08-02 alpha → beta 升级 | 8 阶段实现完成（含 Android） |
| 核心架构 | Go Services + 静态分析生成 TS 绑定；可安装插件（打包前端资源）；多窗口一等公民 | [Binding] 源生成器强类型调用器；IPlugin 插件体系（45+ BuiltIn）；多窗口支持 |
| 构建 | Taskfile + `wails3 build` + server 构建 | MSBuild/dotnet + `wails build` + Server 模式（WebSocketTransport） |
| 前端运行时 | @wailsio/runtime（v3.0.0-alpha.96，2026-07） | @wails-net/runtime（自研，已迁 npm 包） |
| 平台 | macOS / Linux（GTK4）/ Windows / iOS / Android（实验性） | Windows / Linux（GirCore GTK4）/ Android；macOS 骨架 |

**结论**：方向高度一致（编译期生成绑定、插件化、多窗口、Server 模式均已对齐），差距集中在 beta 期间新增的机制性功能与平台细节修复。

---

## 2. 分项对比矩阵

| # | Wails v3 特性 | 出处 | Wails.Net 现状 | 可参考性 |
|---|--------------|------|----------------|----------|
| 1 | **可安装插件**：服务可打包前端资源+脚本与后端 API 一起发布 | beta.0 亮点 | ❌ 插件体系仅有后端命令注册（IPlugin/PluginManager），无前端资源打包 | **P0** |
| 2 | **Updater：Wails Update Manifest 协议 + endpoint provider** | PR #5720（2026-07） | ⚠️ 有 IUpdateProvider（GitHub/GitLab/Http）+ UpdateManifest，但无 wails:// 协议层与 endpoint provider 抽象 | **P0** |
| 3 | Updater 默认排除 Windows installer 资产 | beta.2 #5861 | ❌ 未实现资产匹配过滤 | P1 |
| 4 | Windows 暗色模式菜单可读性（浅色系统回退）+ uxtheme 版本门槛 17763 | beta.3 #5876/#5877 | ⚠️ WindowsEnvironmentManager 有 DarkMode 处理，无菜单回退边界 | **P1** |
| 5 | GTK4 实时窗口尺寸 + surface 状态事件（resize/maximise/minimise/fullscreen） | beta.2 #5830 | ❓ 需核对 Linux 平台层事件来源 | P1 |
| 6 | Linux WebKit Blob/FormData fetch 崩溃修复（缺头传 undefined） | beta.2 #5854/#5865 | ❓ 需核对传输层 fetch shim | P1 |
| 7 | `wails3 setup` 交互式环境向导 | #5601（2026-06） | ⚠️ 有 DoctorCommand/InfoCommand，非交互式引导 | P1 |
| 8 | Linux dark mode flash / widget 创建前 isvisible 查询 | beta.4 #5898/#5899 | ❓ Linux 平台层初始化时序 | P2 |
| 9 | TS 绑定生成：静态分析保留注释与参数名 | beta.0 | ⚠️ SourceGenerators/Generator 已生成 TS 签名，注释保留质量可对齐 | P2 |
| 10 | Android 打包默认 arm64（非 HOST_ARCH） | beta.4 #5890 | ❓ 需核对打包 ABI 策略 | P2 |
| 11 | macOS：zoom restore / titlebar 按钮 / 圆角方角窗口 / 后台启动不抢焦点 | #5900/#5870/#5866/#5897 | ⚠️ macOS 为骨架（PlatformApp/Clipboard 4 文件） | P2（远期） |
| 12 | Windows server tag 构建约束（!server） | beta.4 #5892 | ✅ Server 模式已有（WebSocketTransport），构建约束可参考 | P3 |
| 13 | 事件系统重构（alpha/refactored-events 分支） | 未合并 master | ✅ 事件系统已实现（EventProcessor + EventIPCTransport） | 观望 |

---

## 3. 重点可参考项明细

### P0-1：可安装插件机制（打包前端资源）

- **v3 的做法**：Go Service 定义 API + 静态分析生成 TS 绑定；服务可将其**前端资源（assets）与脚本和后端 API 一起打包**发布，形成"可安装插件"，插件装好后前端脚本/资源自动可用。
- **Wails.Net 差距**：`IPlugin`/`PluginContext` 仅有命令注册与生命周期，没有任何前端资产承载（已确认 Plugins 目录无 AssetServer/静态资源关联）。
- **可落地的实现**：
  - 插件元数据增加 `Assets`/`Scripts` 声明（类似 `PluginManifest`）
  - 插件资源挂载到 `AssetServer`（新增虚拟路径如 `/plugins/{name}/`）
  - 插件前端脚本注入（页面加载时注入 `<script>`，复用现有 `window._wails` 注入管道）
  - 对齐 Tauri v2 的插件权限模型（Wails.Net 已有 Security/Capability 基础，可挂接）

### P0-2：Updater 的 Wails Update Manifest 协议 + endpoint provider

- **v3 的做法**（PR #5720）：updater 支持 `wails://update-manifest` 自定义协议，manifest 可由 **endpoint provider** 提供；资产选择通过匹配器实现（默认排除 Windows installer，beta.2 #5861）。
- **Wails.Net 差距**：`IUpdateProvider`（GitHub/GitLab/Http）已抽象 provider，但协议层是 HTTP URL 直连，无 `wails://` 协议解析与 endpoint provider 注册机制；资产过滤规则缺失。
- **可落地的实现**：
  - 新增 `EndpointUpdateProvider`（注册任意 URI/协议 → manifest）
  - `UpdateManifest.Assets` 增加匹配器（文件名/平台/架构过滤，默认排除 `*-installer.*`）
  - 对齐 beta.2 的资产默认排除行为

### P1-3：Windows 暗色模式菜单边界修复

- **v3 修复点**（#5876/#5877）：
  1. 应用请求暗色但系统为浅色时，原生菜单文字不可读 → 菜单用匹配的浅色原生背景回退
  2. uxtheme 暗色导出版本门槛从 build 18334 放宽到 **17763**（Windows 10 1809 / Server 2019 也能启用应用级暗色）
  3. `AllowDarkModeForWindow` 传 HWND 句柄 + 参数验证（#5877）
- **Wails.Net**：`WindowsEnvironmentManager` 已有 DarkMode 处理，需核对是否覆盖上述边界（特别是菜单背景回退与 17763 门槛）。

### P1-4：Linux 平台窗口/网络细节

- GTK4 实时窗口尺寸 + 从配置的 surface 发出 resize/maximise/minimise/fullscreen 状态事件（#5830）
- Linux WebKit fetch shim 对 Blob/FormData 缺头时传 `undefined`，避免崩溃（#5854/#5865）
- Linux 暗色模式闪白（#5899）、widget 创建前查询可见性崩溃（#5898）
- **Wails.Net**：Linux 平台层基于 GirCore GTK4，建议逐一核对上述时序与事件来源。

### P1-5：CLI 交互式 setup 向导

- **v3**：`wails3 setup` 引导式环境设置（依赖检测、安装引导），随后 `wails3 init` 创建项目。
- **Wails.Net**：已有 `DoctorCommand`（诊断）与 `NewCommand`，可增强为交互式引导（.NET SDK / WebView2 Runtime / GTK/WebKitGTK 依赖检测与提示），对齐 v3 的 setup 体验。

### P2-9：TS 绑定生成质量

- v3 静态分析生成的 TS 绑定**保留注释与有意义的参数名**。
- Wails.Net 的 SourceGenerators 生成强类型调用器、Generator 产出 TS 签名；可对照检查生成的 .d.ts 是否保留 XML 注释与参数名，提升开发者体验。

### P2-10：Android ABI 打包策略

- v3 默认打包 arm64 而非宿主机架构（#5890）；Wails.Net 的 Android TFM 打包策略需核对（避免打包宿主架构）。

---

## 4. 无需参考项（Go 生态特有 / 已覆盖）

| v3 项 | 说明 |
|-------|------|
| Taskfile 构建系统 | Wails.Net 用 MSBuild + CLI，无需移植 |
| internal 工具库（mailbox/debounce/optional/lo/wake/signal/sliceutil 等） | .NET BCL/LINQ 等价物 |
| iOS 支持 | AGENTS.md 明确不实现 iOS |
| wep（Wails Enhancement Proposal） | 项目治理流程，非技术项 |
| 事件系统重构分支（alpha/refactored-events） | 未合并 master，方向不明，观望 |

---

## 5. 建议行动顺序

1. **P0**：可安装插件机制（插件携带前端资源 → 挂载 AssetServer + 注入脚本）
2. **P0**：Updater 补齐 `wails://` manifest 协议 + endpoint provider + 资产匹配器（默认排除 installer）
3. **P1**：Windows 暗色菜单边界 + uxtheme 17763 门槛；Linux 窗口状态事件/时序修复（结合平台层测试）
4. **P1**：CLI 交互式 setup 向导（复用 Doctor/Info 能力）
5. **P2**：TS 绑定注释/参数名质量对齐；Android arm64 默认打包
6. **基线维护**：AGENTS.md 参考版本更新为 `v3.0.0-beta.4`，便于后续增量对齐

---

## 6. P0 收益分析（供决策，是否跟进）

### P0-1 可安装插件机制（插件携带前端资源）

| 维度 | 评估 |
|------|------|
| **收益** | ① 插件生态基础：第三方插件可将「前端组件/页面 + 后端 API」作为整体分发，是 v3/Tauri 插件模型的核心差异点；② 框架竞争力：Wails.Net 目前插件只有命令注册，无法做开箱即用的 UI 型插件（如托盘增强、调试面板、开发者工具）；③ 与 Tauri v2 插件权限模型（已有 Security/Capability 基础）形成闭环 |
| **成本（实施）** | 中高：插件 manifest 规范（Assets/Scripts 声明）+ AssetServer 虚拟路径挂载（`/plugins/{name}/`）+ 前端脚本注入管道 + 安全边界（资源隔离、权限校验）+ 模板/文档/测试 ≈ 3-5 个工作日 |
| **成本（维护）** | 中：新概念面（插件清单格式、资源打包约定），需长期维护并保持与 CLI/模板一致 |
| **风险** | 无硬性依赖阻塞；前端注入管道已有现成机制（window._wails 注入）可复用 |
| **决策建议** | **已定方向（2026-08-07）**：采纳「前后端一体双包」模型——每插件 = NuGet 后端包 + npm 前端包，同仓库同版本发布，vite 项目经 npm 依赖调用并获得完整 TS 类型提示（对齐 Tauri v2 插件模型）。完整方案见 `docs/development/plugin-packaging.md`，落地按 M1 示范插件（Updater）→ M2 联调验证 → M3 批量拆分 → M4 发布闭环推进。原"暂缓跟进"结论因前端类型按需安装与生态分发诉求调整为**分阶段执行** |

### P0-2 Updater：wails:// manifest 协议 + endpoint provider

| 维度 | 评估 |
|------|------|
| **收益** | ① 自托管更新源：任意 URI/协议（含本地文件、自定义协议）均可作为 manifest 来源，突破 GitHub/GitLab/Http 三 provider 的限制；② 对齐 v3 演进（PR #5720）；③ 资产匹配器（默认排除 installer）能减少误更新 |
| **成本（实施）** | 中：协议解析层（wails:// 前缀） + EndpointUpdateProvider 注册机制 + 资产匹配器（文件名/平台/架构过滤）+ 测试 ≈ 1.5-2.5 个工作日 |
| **成本（维护）** | 低：纯增量，不影响现有 provider |
| **风险** | 低：现有 IUpdateProvider 抽象可直接扩展 |
| **决策建议** | **部分跟进（低成本项先行）**：资产匹配器（默认排除 installer）改动小、收益直接，建议立即做；`wails://` 协议层价值取决于自托管更新需求，可随后续版本评估。本报告的「建议行动顺序」第 2 条拆分：资产匹配器并入 P1 立即执行，协议层挂起待决策 |

> 补充：资产匹配器已按上述建议列入 P1 执行（见实施记录），`wails://` 协议层保持挂起。
> 补充 2：P0-1 可安装插件机制已于 2026-08-07 细化落地为「前后端一体双包模型」，方案见 `docs/development/plugin-packaging.md`。

---

## 7. 附：Tauri 最新版本基线确认（v2 GA，非 beta）

> 用户询问"与 Tauri 最新 beta 版的差异"时需先澄清版本状态：**Tauri 2 已于 2024-10-02 GA**，当前稳定版 **2.11.5**（2026-07-01），**不存在 beta 阶段**；Tauri 3 仅有 `@tauri-apps/cli-cef-v3.0.0-cef.0` alpha 预览（2026-05-04，CEF 后端实验，不作参考）。因此 Wails.Net 的对比基线是 **Tauri v2 GA 2.11.5**（AGENTS.md §1 已同步）。

| # | Tauri 2.11.x 特性 | Wails.Net 现状 | 可参考性 |
|---|-------------------|----------------|----------|
| 1 | 插件生态（官方 30+ 插件，前端资源/权限分离打包） | IPlugin 后端命令注册 + Security/Capability 基础（学 Tauri 设计） | P1（对齐插件权限模型，已有基础） |
| 2 | 动态 ACL（`dynamic-acl` feature，可关闭减体积） | 无动态 ACL 概念（静态 Capability 声明） | 观望 |
| 3 | `data-tauri-drag-region` 拖拽区域 | ✅ 原生拖拽已有（HasNativeDrag=true） | 无需 |
| 4 | Linux D-Bus 主题检测 | ✅ Linux 平台层已有 D-Bus 系统主题事件监听 | 无需 |
| 5 | `Webview::eval_with_callback` | ❌ 需核对 WebView eval 是否支持回调 | P3 |
| 6 | Android 多窗口（activity embedding） | ❌ Android 单 Activity | P3（远期） |

**结论**：Wails.Net 与 Tauri 的差异集中在插件生态丰富度（Tauri 30+ 官方插件 vs Wails.Net 45+ BuiltIn 命令）与 ACL 机制（Tauri 动态 ACL vs Wails.Net 静态 Capability）；架构融合策略（AGENTS.md §1.1.1 维度 3）无需调整，方向一致。

---

## 8. P1 / P2 实施记录（2026-08-07）

按决策执行：「P0 分析后确认（P0-1 已转双包模型专项）；P1/P2 优先补齐」。已完成项：

| 项 | 状态 | 实现内容 |
|----|------|----------|
| P1 Windows 暗色模式菜单边界（#5876/#5877） | ✅ 已实现 | 新增 `src/Wails.Net.Application.Windows/Win32Theme.cs`（对齐 v3 theme.go）：uxtheme ordinal 132/133/135/136/104 动态加载（≥17763 门槛，不在 18334）、`SetPreferredAppMode(AllowDark)` 应用级暗色、`SetMenuTheme`（`SetWindowTheme("DarkMode_Explorer")` + 系统浅色回退防暗底暗字 + `AllowDarkModeForWindow(hwnd)` 传 HWND + `InvalidateRect`）；`WindowsPlatformApp.ApplyDarkModeToWindow` 委托 `Win32Theme.SetTheme`（DWM attribute 19/20 按版本 18985 选择），构造函数启动时 `Initialize()` |
| P1 Linux 窗口状态事件（#5830） | ✅ 部分实现 | `LinuxWebviewWindow.OnWindowNotify` 扩展 `is-maximized`/`is-fullscreen` → WindowMaximised/Unmaximised/Fullscreen/Unfullscreen（标题栏/WM 操作可感知）；**限制**：resize/minimise 外部通知需 GdkSurface layout 信号，GirCore 0.8.0 无 `Gtk.Widget.Surface` API，待升级 GirCore 后补 |
| P1 Linux 其他（#5854/#5898/#5899） | ✅ 核对 | fetch shim（Blob/FormData）不适用（前端传输仅 JSON/Uint8Array）；isvisible null 检查已有；IsDarkMode 已有（D-Bus color-scheme），闪白时序优化需 Linux 实机验证 |
| P1 CLI setup 向导（#5601） | ✅ 已实现 | `DoctorCommand.RunDiagnostics()` 提取为 internal 复用；新增 `SetupCommand`（`wails setup`）：复用诊断 + 分平台安装指引（Windows/Linux），已注册到 CLI |
| P1 Updater 资产匹配器（#5861） | ✅ 已实现 | 新增 `UpdateAssetMatcher`（默认排除 installer/setup/.msi）；接入 GitHub/GitLab provider 资产选择（用户显式 assetNamePattern 时尊重用户） |
| P2 TS 绑定注释保留 | ✅ 已实现 | 链路：`BoundMethodInfo/BoundParameterInfo` + `BoundMethodModel/ParameterModel` 加 `Summary`；源生成器 `XmlDocParser` 提取 `<summary>/<param>`（Roslyn 编译期，白名单内）；`TypeScriptGenerator` 输出 JSDoc（方法摘要 + @param） |
| P2 Android 默认 arm64（#5890） | ✅ 已实现 | `BuildCommand.BuildAllPlatformsAsync` 单 TFM 分支：platform=android 且未指定 --runtime 时注入 `RuntimeIdentifier=android-arm64` |
| P2 macOS 细节（#5900/#5870/#5866/#5897） | ⏸ 推迟 | macOS 平台当前为 stub（窗口/菜单未实现，仅 341 行骨架），细节优化无实现载体且无测试环境；建议 macOS 完整实现后再对齐 |

**构建验证说明**：代码改动已全部落地；本机环境存在文件锁问题（NuGet 漏洞审计缓存 `vuln_index.dat-new` 与部分 obj 产物写入被拒，被 TreatWarningsAsErrors 升级为 error，且 `dotnet build-server shutdown` 后仍间歇复现），CLI 构建验证受阻。建议在正常终端验证：
`dotnet build src/Wails.Net.Application.Windows/Wails.Net.Application.Windows.csproj`、`dotnet build src/Wails.Net.Cli/Wails.Net.Cli.csproj`、`dotnet build src/Wails.Net.SourceGenerators/Wails.Net.SourceGenerators.csproj`、`dotnet build src/Wails.Net.Generator/Wails.Net.Generator.csproj`
（若遇 NU1900 审计缓存错误：删除 `%LocalAppData%\NuGet\v3-cache` 下残留 `*.dat-new` 文件后重试。）

---

## 9. 2026-10-17 再确认：基线升级至 v3.0.0-beta.28

> 复核方式：官方 changelog（v3.wails.io/changelog，beta.4 → beta.28 共 24 个版本）+ 本仓库代码逐项验证。
> Wails.Net 侧时点：最后提交 2026-08-20（R0-R3 运行时/安全重构、SDK-4 多 RID 构建已落地）。

### 9.1 原文档待实现项复核

| 项 | 原状态（08-07） | 现状确认（10-17） |
|----|----------------|------------------|
| P0-1 插件前后端一体双包模型 | 规划 M1-M4 | ✅ **已完成**：`src/Wails.Net.Plugins.*` 43 个 NuGet 插件项目 + `packages/` 43 个 npm 包（`wails-net-plugin-*` + `wails-net-runtime`）+ CLI `plugin new` 双包脚手架（[PluginCommand.cs](../../src/Wails.Net.Cli/Commands/PluginCommand.cs)）+ R3-B 权限声明 |
| P0-2 Updater `wails://` 协议层 + endpoint provider | 挂起 | ✅ **已实现（2026-10-18）**：新增 `EndpointUpdateProvider`（任意 URI/scheme → manifest，经 `AddProvider` 接入，支持 `file`/自定义 scheme 注入）；资产匹配器已落地。**实测：v3 的 `wails://` 并非 updater 协议前缀**（实为 webview/资产服务器自定义 scheme），故未实现路由层；appcast 未实现（Sparkle XML 与 `UpdateManifest` 无对应签名槽位）。见 §9.12 |
| P1 Linux 外部 resize/minimise 事件 | 受阻于 GirCore | ✅ **已实现（2026-10-18）**：程序化 Minimise/UnMinimise 早已派发事件；**外部 WM 触发已补齐** —— `Gtk.Window.GetSurface()` + `Gdk.Surface` `notify::state`（读 `Gdk.Toplevel.GetState()` 的 `Minimized`）派发 WindowMinimised/UnMinimised，`Gdk.Surface` `layout` 派发 WindowResized。**原「受阻于 GirCore 0.8.0」结论不成立**（该 API 在 0.8.0 中已可用）。见 §9.12 |
| P2 macOS 细节优化 | 推迟（stub） | ✅ **平台已完成**：完整 AppKit + WKWebView 实现（`src/Wails.Net.Application.MacOS/` 共 12 个 `.cs`、约 4289 行，`MacOSWebviewWindow.cs` 1545 行含 DidResize/DidMiniaturize 事件；AGENTS.md §1 已标注）；细节优化转入 §9.3 P1/P2 项（源码级核对见 §9.7 待实现项 C） |

### 9.2 beta.5–beta.28 新增差异项（按优先级）

**P0（机制级，架构影响）**

| # | v3 特性（版本） | Wails.Net 现状 |
|---|----------------|----------------|
| 1 | **Streams 双向字节流**（beta.8）：Go↔JS WebSocket 形态双向流，`app.HandleStream(name, handler)` + 前端 `Stream(name)`，不绑 TCP 端口、不走 evalJS；server 构建走真实 WebSocket，应用代码跨构建一致 | ❌ 未实现。注意：现有 `Wails.Net.Plugins.Websocket` 是**前端 WS 客户端**（Tauri 式连接外部服务器），不是 in-app 双向流，二者不等价 |
| 2 | **事件投递硬化**（beta.6 #5930 / beta.7 #5934 / beta.8 #5851）：每窗口有序 JS 事件队列 + 背压 + 超大事件宿主侧有界存储 + 通用异步 FIFO mailbox | ❌ 未实现（src/ 无 backpressure/oversized event 处理；R2-2 WebSocketBroadcaster 单写者茧有有序性，但无背压与上界） |
| 3 | **WebView2 进程故障恢复**（beta.26 #6002、beta.28 #6220/#6222/#6184）：WebView2 崩溃后恢复而非白窗/卡死；回调保持存活至原生释放 | ❌ 未实现（`Wails.Net.Application.Windows` 无 ProcessFailed/恢复处理） |

**P1（功能/安全对齐）**

| # | v3 特性（版本） | Wails.Net 现状 |
|---|----------------|----------------|
| 4 | `wails3 mcp` 安全 MCP 服务器（beta.9 #5896）+ origin/token 认证硬化（beta.19 #6092） | ❌ CLI 无 mcp 命令（现有 19 个命令：build/dev/pack/publish/check/capability/doctor/setup/plugin 等） |
| 5 | macOS 媒体权限：WKUIDelegate media-capture 方法（beta.27），`PermissionAllow/Deny` 覆盖摄像头/麦克风 | ✅ **已实现（2026-10-18）**：`WebviewWindowOptions.Mac.CameraPermission/MicrophonePermission`（`Permission.Default/Allow/Deny`）+ `MacUiDelegate.RequestMediaCapturePermission` 覆写；相机/麦克风分别判定、取更严格者。macOS 原生代码未在本机编译验证（见 §9.10） |
| 6 | macOS notch 通知窗口 + NSPanel 窗口类型（beta.12 #6010/#6008） | ❌ 未实现 |
| 7 | Linux GTK application ID 覆盖（beta.23 #5972）+ single-instance 以应用自身 ID 占用 bus name（#5973） | ⚠️ single-instance 已有（跨平台 13 文件，含 LinuxPlatformApp）；GTK app ID 覆盖与 D-Bus 命名规范未核对到 |
| 8 | 运行时请求 64 MiB 上限 → HTTP 413（beta.19 #6091） | ✅ **已实现（2026-10-18）**：`HttpTransport.MaxRuntimeBodyBytes = 64 MiB`，Content-Length 预判 + 边读边计数 → 413（文案 `runtime request body exceeds 64 MiB`）；分块路径单块 1 MiB → 422。见 §9.10 |
| 9 | 自定义 scheme 请求中止/取消：Windows（beta.20 #5963/#5969）、Linux（beta.23 #6126）、macOS/iOS 请求上下文转发 | ⚠️ **部分（2026-10-18）**：macOS 每 URL scheme task 独立 CTS，`StopUrlSchemeTask` 取消且不再回调；Windows 每请求 CTS + 窗口关闭取消（未做 CDP `Fetch` 级中止）；Linux WebKitGTK 无取消钩子，保持原行为；令牌已贯穿管线。见 §9.10 |
| 10 | Windows 托盘 ICO 图标 + 任务栏主题跟随（beta.14 #6016） | ✅ **已实现（2026-10-18）**：`Win32SystemTray` 双图标（light/dark）+ 按 `Application.IsDarkMode()` 选择生效图标 + 订阅 `KnownEvents.ThemeChanged` 切换；**新增平台层顶层消息窗口 `PlatformMessageWindow`**，使无 webview 窗口时也能发出主题事件（限制已补全）。ICO 仍由 `IconCommand` 生成。见 §9.11 |

**P2（细节/质量）**

| # | v3 特性（版本） | 说明 |
|---|----------------|------|
| 11 | watcher 对前端 dev server 的 HTTP 就绪检查（beta.26 #6150） | ✅ **已实现（2026-10-18）**：`DevCommand` 在启动 `dotnet watch` 前对前端 URL 做 HTTP 就绪探测（默认 60s / 250ms 轮询；连接被拒/超时/4xx/5xx 继续重试；超时→报错并不启动应用）。URL 取值：`FRONTEND_DEVSERVER_URL` → `wails.json` 的 `frontend.devServerUrl`；未配置则跳过。见 §9.12 |
| 12 | `wails3 dev` 退出清理（beta.25 #6048）+ dev 资产代理 TCP churn 修复（beta.8） | ✅ **已实现（2026-10-18）**：退出清理 `DevProcessCleanup`（终止 `dotnet watch` 进程树 + 前端 dev server）+ **dev 资产反向代理** `DevAssetProxy`（`MaxConnectionsPerServer=512` + 重试）。见 §9.13 |
| 13 | macOS 细节：autoplay 偏好（beta.7）、Dock bounce（beta.6）、注册快捷键优先于 webview（beta.9 #5902）、Control- 键命名（beta.14）、modal loop 期间主线程工作（beta.13 #6026）、app 菜单回调替换崩溃修复（beta.6） | macOS 平台已完整，可按需逐项对齐 |
| 14 | Updater 备份失败后清除 helper 环境变量并重启原目标（beta.21 #6080） | ✅ **已实现（2026-10-18）**：`ClearHelperEnv()` 提前到备份**之前**执行；备份失败时重启原目标（`TryLaunch`）后再退出 12，与 Go `launch(target)` 一致。见 §9.11 |
| 15 | Windows 菜单 nil 菜单/资源释放/菜单栏重绘（beta.21 #6112） | ✅ **已实现**（§9.7 核对）：nil 菜单守卫 `Win32WebviewWindow.cs:2723-2729`、`DrawMenuBar` 重绘 `:2000/2015/2270/2280`、释放 `Win32Menu.cs:109/201` |
| 16 | MSIX 打包恢复（beta.21 #6115） | ✅ **已实现（2026-10-18）**：`--format msix`/`appx`，生成 `AppxManifest.xml`（对齐 v3 两个模板）+ Assets 占位图，`MakeAppx pack` 产出 `.msix`，可选 `signtool` 签名；工具缺失时保留 `*.structure` 目录并给出清晰错误（不静默失败）。见 §9.12 |
| 17 | JS/TS 绑定生成：泛型模型创建器引用后置声明 + 相互依赖泛型栈溢出修复（beta.21 #6062） | ⊘ **不适用**（§9.7 核对）：`TypeScriptGenerator` 仅生成方法签名 + id 映射，无「模型创建器」与依赖排序概念（泛型映射见 `TypeScriptTypeMapper.cs:73-124`）；v3 该修复针对 `internal/generator/render/renderer.go:90-127` 的 Models() 拓扑排序 |
| 18 | Linux 托盘右击同时触发 click handler 与菜单修复（beta.28 #6018） | ✅ **已实现**（§9.7 核对，架构不同）：`LinuxSystemTray.cs:242-244` 右击独立触发菜单、`:256-258` 左键独立；v3 `systemtray_linux.go:720-728` |
| 19 | WebView2 初始化 deadline + 消息泵（beta.8 #5952）、embed 超时 60s（beta.15 #6043） | ✅ **超时已对齐（2026-10-18）**：`EmbedTimeout` 由 30s 改为 **60s**（对齐 v3 `chromium.go:283`）；初始化 deadline / 控制器创建超时已实现；仍**无自建消息泵**（交由 WebView2 SDK）。见 §9.11 |
| 20 | Linux 旧式 GTK3 service-only 应用 screen discovery 崩溃防护（beta.9 #5966） | ✅ **已实现（等价）**（§9.7 核对）：`LinuxPlatformApp.cs:267-270` display 为 null 时返回空数组；ServerMode 不触 GTK |
| 21 | App.Run 期间启动默认信号处理器（beta.21 #6098） | ✅ **已实现（2026-10-18）**：`Hosting.DefaultSignalHandler`（`PosixSignalRegistration` 监听 SIGINT/SIGTERM），`Application.Run` 主循环前安装、`ShutdownAsync` 释放；新增 `ApplicationOptions.DisableDefaultSignalHandler`（对应 Go `Options.DisableDefaultSignalHandler`）。详见 §9.9 |
| 22 | setup 向导完善（beta.28 #6224 系统详情；beta.24 仅批准的包管理器命令——安全约束） | ✅ **已实现（2026-10-18）**：新增系统详情收集（OS/架构/.NET SDK/WebView2/GTK 等，缺失为 `unknown`）+ `--json`；安装命令**仅由白名单包管理器常量目录构造**（任意/危险命令被拒绝），`--install` 默认仅预览、`--yes` 才执行。见 §9.12 |
| 23 | macOS app 输出保持在 file provider 之外（beta.28 #6225） | ✅ **已实现（2026-10-18）**：`PackCommand` 新增 macOS 输出准备步骤（file provider 路径 → 重定位到本地文件系统 + 符号链接）；「路径判定/重定位决策」已抽为纯逻辑并单测，真实挂载探测为薄封装（本机未验证）。见 §9.12 |

### 9.3 不适用项（.NET 移植天然豁免）

| v3 项 | 豁免原因 |
|-------|---------|
| 移除原生 WebView2Loader.dll、纯 Go loader（beta.20 #6031） | Wails.Net 用托管 `Microsoft.Web.WebView2` 包，无嵌入 DLL 问题 |
| Cgo Calloc 内存泄漏 pointer receiver 修复（beta.18 #6083） | Go/Cgo 特有 |
| macOS 私有 API build tag 门控（beta.19 #6087） | Go build tag 机制；.NET 需要时可用 MSBuild 条件 + #if 等价，当前无使用需求 |
| `go install` 分发 / 桌面二进制发布管线移除（beta.8 #5946） | Wails.Net 走 dotnet tool / NuGet 分发 |
| 文档站点、CI、changelog 自动化类 PR | 非运行时功能 |

### 9.4 建议行动顺序（2026-10-17）

1. ✅ **P0（已完成 2026-10-17）**：事件投递硬化（有序队列 + 背压 + 超大事件有界存储）
   - Layer 1 `EventMailbox<T>`（PR #5851）：`EventProcessor` 前端派发改为单一消费者 FIFO 邮箱，`Emit` 永不阻塞
   - Layer 2 `PerWindowEventQueue`（PR #5934）：每窗口 FIFO 队列，容量 64，溢出阻塞（背压）而非丢弃
   - Layer 3 `EventPayloadStore` + `GET /wails/eventpayload/<id>`（PR #5930）：>8192 字节载荷停放宿主侧（64 MiB 上限 / TTL 30s / 读取即删 / 按窗口绑定），经 `EventIPCTransport` 注入 fetch 脚本取回
   - 测试：44 个 TUnit 单元测试覆盖三层（`EventMailboxTests` / `PerWindowEventQueueTests` / `EventPayloadStoreTests` / `EventPayloadEndpointTests`）
2. ✅ **P0（已完成 2026-10-17）**：Windows WebView2 进程故障恢复
   - 运行时可用性预检（`GetAvailableBrowserVersionString`）与控制器创建超时 `EmbedTimeout`（30s，对应 PR #6002）
   - 抽取 `AttachController` 使初始化与重建共用同一路径；订阅 `CoreWebView2.ProcessFailed`，浏览器进程退出后经 `DispatchOnMainThread` 重建控制器（对应 PR #6222）
   - `Close`/`ExecJS`/`LoadURL`/`SetHTML` 增加「已关闭」守卫（对应 PR #6222 的 nil/shuttingDown 守卫）；PR #6184（COM AddRef/Release）为上游自研绑定层问题，不适用于托管 SDK
   - 注：该部分为 Win32/COM 互操作代码，无 GUI 环境下无法单元测试，以构建通过 + 代码审查验收
3. **P1**：64 MiB 请求上限（413）、自定义 scheme 取消语义、macOS 媒体权限（WKUIDelegate）
4. **P1**：评估 Streams 机制与 `wails mcp` 子命令的产品定位（机制大、决策先行）
5. **P2**：macOS/Linux/Windows 细节按平台测试机会跟进
6. ✅ **基线维护（已完成 2026-10-17）**：AGENTS.md 参考版本已由 `v3.0.0-beta.4` 升级至 `v3.0.0-beta.28`（§1 参考版本、§1.1.1 参考基线、§8 资源链接同步）

### 9.5 回归验证基线（2026-10-17）

非 UI 测试类回归结果（`Wails.Net.Application.Tests`，按类名过滤，修复后重跑）：

| 测试类 | 结果 |
|--------|------|
| 新增 P0-1 测试（Mailbox / 队列 / 存储 / 端点） | 44 通过 |
| `*Event*` | 119 通过 |
| `*Transport*` | 37 通过 |
| `*Broadcaster*` | 12 通过 |
| `*Application*` | 104 通过 |
| `*Shutdown*` / `*Window*` / `*Registry*` | 7 / 96 / 40 全部通过 |

本次改动前即存在、现已一并修复的 6 项失败及其根因：

| 测试 | 根因 |
|------|------|
| `WebSocketBroadcasterTests.BroadcastEventAsync_DeliversMessageToClient` / `..._DeliversToMultipleClients` | R2-2 后 `ClientMailbox` 由后台写入循环实际 `SendAsync`，`BroadcastEventAsync` 仅完成入队即返回；测试断言过早。改为轮询等待写出 |
| `ServerModeEventApiTests.EndToEnd_EventEmit_ThroughWebSocketTransport_DeliversToClients` | 同上；且 NSubstitute 桩只拦截 `SendAsync(ArraySegment<byte>)` 重载，而生产代码调用 `SendAsync(ReadOnlyMemory<byte>)` 重载（代理吞掉调用）。桩改为按新重载拦截 |
| `NativeIpcTransportTests.RegisterWindow_InstallsHandlerThatCallsHandleIncomingAsync` / `HandleIncomingAsync_ValidCallMessage_PostsResponseBack` | 测试用 `GeneratedBindingsMetadata.Methods.First(m => m.ClassName == "EchoService" ...)` 过滤，但程序集内存在两个 `[Binding] EchoService`（Hosting 与 Transport 命名空间各一），`.First` 命中 Hosting 那个（其实例未注册 → `ReferenceError`）。改为同时限定命名空间 |
| `ApplicationApiAlignmentTests.Build_CollectsCommandMiddlewares_FromDI` | R3-B（WNET003）后 Strict 模式拒绝无安全声明的命令；测试内联插件命令未声明安全元数据。改为 `CommandSecurity.Public()` |

### 9.6 短名别名覆盖风险修复（2026-10-17）

源生成器为 `[Binding]` / `[Command]` 额外注册 `<ClassName>.<MethodName>` 短名别名与自定义名别名；
当同一名称键由多个类型提供时（跨命名空间同名类、重复的自定义名 / 命令名），生成代码会为同一键
生成多条 `GeneratedBindingRegistry.Register` 调用，运行时"后注册覆盖先注册"，按该名称调用会
静默分派到"最后生成"的那个实现。修复分两层：

| 层 | 措施 |
|----|------|
| 编译期（生成器） | 新增诊断 **WNET004**（Warning）：同一注册键由多个类型提供时报告冲突类型并**跳过**该键的注册（`Directory.Build.props` 的 `TreatWarningsAsErrors=true` 下即构建失败，强制消歧） |
| 运行期（注册表） | `GeneratedBindingRegistry.Register` 检测"同键不同所属类型"→ 标记歧义并撤销既有注册，该键拒绝后续注册与查询（fail-closed）；`BindingManager.Call` 对歧义键返回带冲突类型说明的 `ReferenceError`。覆盖跨程序集场景（编译期不可见） |

**行为变更**：跨程序集同名短名调用（例如应用与插件各有一个 `GreetingService.Greet`）由原先
"静默解析到某一个"改为**拒绝并返回可解释的 ReferenceError**；全限定名调用
（`Namespace.ClassName.MethodName`）不受影响。测试程序集内原有的两处同名绑定类
（`EchoService`、`TestService`）已重命名消歧。

相关测试：`BindingGeneratorTests` 新增 4 项（WNET004 诊断、冲突别名跳过、全限定名保留、唯一短名不受影响）；
`GeneratedBindingsTests` 新增 2 项（歧义键 fail-closed、同类型重复注册仍可解析）。

另：P0-1 使前端派发由同步改为异步邮箱串行，2 个既有测试（`EventSenderWindowPropagationTests.Emit_OneListenerThrows_OtherListenerStillReceivesSenderWindowID`、`ServerModeEventApiTests.BroadcastEvent_NotifiesRegisteredTransportListener`）已改为等待派发完成后断言。

---

## 9.7 源码级复核（2026-10-17）

> 方法：以 Wails v3 官方 Go 源码 tag **`v3.0.0-beta.28`**（`v3/` 目录）为唯一权威依据，对 §9.1/§9.2 各项逐条核对「v3 特性是否真实存在」与「Wails.Net 现状」，均给文件:行证据。相比 §9.2 基于 changelog 的判断，本节为源码级确认，并更正下列条目。

### 9.7.1 对 §9.2 的更正

| §9.2 条目 | 原判断 | 源码级确认 |
|-----------|--------|-----------|
| #17 JS/TS 泛型模型创建器排序/环修复 | 「需对照是否存在同类排序/递归问题」（隐含风险） | ⊘ **不适用**：Wails.Net `TypeScriptGenerator` 只产出方法签名 + id 映射，不存在「模型创建器/依赖排序」，与 v3 `internal/generator/render/renderer.go` 的 Models() 拓扑排序不是同一机制 |
| #21 App.Run 信号处理器 | 「.NET Generic Host 预期等价，待确认」 | ❌ **未实现**：`src/` 全仓无 `PosixSignalRegistration`/`SignalHandler`/`IHostLifetime`/`UseConsoleLifetime`；「等价」假设不成立，应列入待实现 |
| #19 WebView2 embed 超时 | 「Windows 启动健壮性」（未量化） | ⚠️ **部分 + 数值偏差**：`EmbedTimeout=30s`，v3 为 `60s`（`internal/webview2/pkg/edge/chromium.go:283`）；无自建消息泵 |
| #16 MSIX 打包 | 「需核对 MSIX 支持」 | ❌ **未实现**：`PackCommand` 仅 zip/targz/nsis/appimage/deb/rpm |
| #15 Windows 菜单健壮性 | 「Win32 菜单健壮性」（未判定） | ✅ **已实现** |
| #20 GTK3 screen discovery 防护 | 「ServerMode 启动时序守卫」（未判定） | ✅ **已实现（等价）** |
| #18 Linux 托盘右击 | 「Tray 插件 Linux 行为」（未判定） | ✅ **已实现（架构不同）** |
| #10 托盘 ICO + 主题跟随 | 「此细节未核对」 | ⚠️ **部分**：`SetDarkModeIcon` 为简化桩，不随主题切换 |

### 9.7.2 v3 侧特性存在性（Go 源码证据）

| # | 特性 | v3 源码证据（`v3.0.0-beta.28`） |
|---|------|-------------------------------|
| P0-1 | Streams 双向字节流 | `pkg/application/stream.go`（L54-118 边界/有界队列、L701-725 `WailsApp.HandleStream`）；`stream_server.go:13-29` server 走真实 WebSocket；`stream_prelude_server.go` 注入 WS factory；`stream_prelude_desktop.go` 桌面轮询 |
| P0-2 | 事件投递硬化 | `event_payload_store.go:9-56`（64MB 上限 + TTL）、`events.go:100-118`（`mailbox.Mailbox`）、`internal/mailbox/mailbox.go:6-50`（异步 FIFO）、`events.go:78-84`（按 emit 顺序串行契约） |
| P0-3 | WebView2 进程故障恢复 | `internal/webview2/pkg/edge/chromium.go:93-100/677-680/938-942`（ProcessFailed 回调注册与转发）、`:160-175`（ShuttingDown 清回调，防释放期悬挂） |
| P1-4 | MCP 安全服务器 | `internal/commands/mcp.go:75-95`（token 来源：`--token`/`WAILS_MCP_TOKEN`/随机）、`:125-149`（loopback + Bearer verifier）、`pkg/application/mcp_protocol_enabled.go:62-98`（Origin 校验 + 401） |
| P1-5 | macOS 媒体权限 | `permissions_darwin.go:32 resolveMediaCapturePermission`、`webview_window_darwin.m:1010` |
| P1-6 | notch/NSPanel | `notch_window.go`、`webview_panel_darwin.m`、`webview_notch_window_darwin.m` |
| P1-7 | GTK app ID + single-instance bus name | `application_linux_appid.go`、`single_instance_linux.go:43-67`（busName=`uniqueID+".SingleInstance"`） |
| P1-8 | 请求 64MiB → 413 | `transport_http.go:186-190`（`MaxBytesReader` + `StatusRequestEntityTooLarge`） |
| P1-9 | scheme 请求取消/上下文转发 | `request_cancellation_windows.go:40-64`（CDP Fetch/Network 中止）、`asset_request_context_test.go` |
| P1-10 | Windows 托盘 light/dark 双图标 | `systemtray_windows.go:30-33`（lightIcon/darkModeIcon + 主题切换） |
| P2-11 | watcher 前端就绪检查 | `internal/commands/watcher.go:83-98`（`process.Readiness{HTTP:frontendURL,Timeout:"60s"}`）、`dev.go:50-52`（`FRONTEND_DEVSERVER_URL`） |
| P2-12 | dev 退出清理 + TCP churn | `internal/assetserver/build_dev.go:20-32`（`devProxyMaxIdleConnsPerHost=512`、`retryTransport`） |
| P2-13 | macOS 细节 | autoplay `webview_window_darwin.go:166/1604`；Dock `:883 windowFlash`；快捷键 `global_shortcut_darwin.go:41`；Control `keys_darwin.go:7`；modal 主线程 `mainthread_darwin.go:13-24`；菜单 `menuitem_darwin.go:73-83` |
| P2-14 | Updater 备份失败清环境变量并重启 | `pkg/updater/helper.go:139 clearHelperEnv()`、`:147-155`（失败 → `launch(target)` relaunch 原目标） |
| P2-15 | Windows 菜单健壮性 | `webview_window_windows.go:129-186`（nil 菜单、释放前菜单/位图、`DrawMenuBar`） |
| P2-16 | MSIX 打包 | `internal/commands/msix.go`、`internal/flags/msix.go` |
| P2-17 | 泛型模型创建器排序/环 | `internal/generator/render/renderer.go:90-127`（Models() 后置/拓扑排序）、testcase `generic_class_type_deps` |
| P2-18 | Linux 托盘右击 | `systemtray_linux.go:720-728`（`opened` 不再触发 click）、`systemtray_linux_event_test.go:43-76` |
| P2-19 | WebView2 deadline/消息泵/60s | `internal/webview2/pkg/edge/chromium.go:283`（`embedTimeout=60s`）、`:261 pumpUntilInited`、`:288` 自建消息泵 |
| P2-20 | GTK3 screen discovery 防护 | `linux_cgo_gtk3.go:1121`、`screen_linux_gtk3_test.go:10-20` |
| P2-21 | 默认信号处理器 | `signal_handler_desktop.go:12-20`（`signal.NewSignalHandler(a.Quit)`）、`application_options.go:72-73`（开关） |
| P2-22 | setup 向导 | `internal/setupwizard/wizard.go:222 SystemInfo`、`:433-451 initSystemInfo`、`:1208-1336 safeInstallers` |
| P2-23 | macOS app 输出避开 file provider | `internal/commands/tool_prepare_macos_output.go:22/:100`（探测 `com.apple.fileprovider.fpfs`） |
| 待实现 A | Updater endpoint provider | `pkg/updater/providers/endpoint/endpoint.go`（+ `appcast`） |
| 待实现 B | Linux 外部 WM 事件 | `linux_cgo_gtk3.go` GDK surface/notify |

### 9.7.3 Wails.Net 现状（源码级确认）

| # | Wails.Net 判定 | 证据 |
|---|---------------|------|
| P0-1 Streams | ✅ **已实现（2026-10-17）** | `src/Wails.Net.Application/Streams/`（StreamManager/StreamConn/StreamSession/StreamChunkStore/StreamFrameCodec/StreamEndpoints + PulseGate）；`Application.Streams`/`HandleStream`；`WebviewRequestPipeline` 路由 `/wails/stream/*`；`WebviewRequest.Headers`+`RequestBodyBytes`；四平台透传 header + Windows/Linux/macOS 长轮询延迟完成（Android 不支持）；server 模式 `WebSocketTransport` 新增 `/wails/stream/ws`；前端 `packages/wails-net-runtime` `Stream(name)`。详见 §9.8 |
| P0-2 事件硬化 | ✅ 已实现 | `Events/EventMailbox.cs:31`、`Transport/PerWindowEventQueue.cs:31`、`Events/EventPayloadStore.cs:30`、`WebView/WebviewRequestPipeline.cs:76/121/136`；接入 `EventProcessor.cs:73/342`、`EventIPCTransport.cs:166-203` |
| P0-3 WebView2 恢复 | ✅ 已实现 | `Win32WebviewWindow.cs:254 EmbedTimeout`、`:655 AttachController`、`:685 ProcessFailed 订阅`、`:781 OnProcessFailed`、`:809 _recovering` |
| P1-4 MCP | ❌ 未实现 | `Cli/Commands/` 无 mcp 命令（现有 19 命令） |
| P1-5 macOS 媒体权限 | ✅ **已实现（2026-10-18）** | `MacOSWebviewWindow.cs` `MacUiDelegate.RequestMediaCapturePermission` + `ResolveMediaCapturePermission/CaptureDecisionFor/StrictestCaptureDecision`；`WebviewWindowMacOptions.CameraPermission/MicrophonePermission`（MACOS 原生未本机编译验证） |
| P1-6 notch/NSPanel | ❌ 未实现 | MacOS 项目无 `notch`/`NSPanel` |
| P1-7 GTK app ID/single-instance | ✅ **已实现（2026-10-18）** | `LinuxApplicationId.cs`（id 推导/校验/覆盖 + 程序名）、`LinuxDbusSingleInstance.cs`/`LinuxSingleInstanceNames.cs`（经 `org.freedesktop.DBus.RequestName` 以 `uniqueID+".SingleInstance"` 占 bus name，`DO_NOT_QUEUE`）；`LinuxPlatformApp.AcquireSingleInstanceLock` 占位、`Run()` 经 `GLib.Functions.SetPrgname` 应用程序名；D-Bus 不可用降级锁文件+socket。见 §9.13 |
| P1-8 64MiB→413 | ✅ **已实现（2026-10-18）** | `Transport/HttpTransport.cs` `MaxRuntimeBodyBytes = 64 MiB`；`HandleMessageAsync` Content-Length 预判 + 限量读取 → 413；分块单块 1 MiB → 422 |
| P1-9 scheme 取消 | ✅ **已实现（2026-10-18）** | macOS：每 task CTS + `MarkStopped` 取消且不再回调；Windows：`WindowsRequestCancellation.cs`（CDP `Fetch`/`Network` 逐请求中止，`X-Wails-Request-Id` 桥接头）+ 窗口关闭取消；Linux：WebKitGTK 无钩子（已注释）；管线令牌已透传。见 §9.13 |
| P1-10 托盘主题 | ✅ **已实现（2026-10-18）** | `Win32SystemTray.cs`：`_lightModeIcon`/`_darkModeIcon`(+Owned)、`GetEffectiveIcon`、`ApplyEffectiveIcon`、`SubscribeThemeChanged`/`UnsubscribeThemeChanged`、`ReleaseIcon`；新增 `PlatformMessageWindow.cs`（平台层顶层消息窗口）在无窗口时也发主题事件 |
| P2-11 前端就绪 | ✅ **已实现（2026-10-18）** | `src/Wails.Net.Cli/Build/FrontendDevServerReadiness.cs`；`DevCommand.cs` L263-313 启动前探测、L401-422 `ResolveFrontendDevServerUrl`；测试 10 项 |
| P2-12 dev 清理 + 资产代理 | ✅ **已实现（2026-10-18）** | 退出清理：`DevProcessCleanup.cs` + `DevCommand` finally 统一终止 `dotnet watch` 进程树 + 前端 dev server；**dev 资产反向代理**：`WebView/DevAssetProxy.cs`（转发资源请求到前端 dev server，`MaxConnectionsPerServer=512` + 重试），`DesktopApplicationBuilder` 在配置 `DevServerUrl` 时注入、`WebviewRequestPipeline` 资源请求优先委托，`wails dev` 经 `WAILS_DEV_SERVER_URL` 注入。见 §9.13 |
| P2-13 macOS 细节 | ✅ **已实现** | autoplay `MacOSWebviewWindow.cs:305`、快捷键 `MacOSKeyBindingManager.cs:283`、Control `:25`、菜单 `MacOSMenu.cs:416`；**Dock bounce** `MacOSWebviewWindow.Flash` → `NSApplication.RequestUserAttention(InformationalRequest)`；主线程用 `InvokeOnMainThread`（`MacOSPlatformApp.cs:425`，非 CFRunLoop）。见 §9.13 |
| P2-14 Updater 备份失败 | ✅ **已实现（2026-10-18）** | `HelperProcess.cs`：`ClearHelperEnv()` 移至备份前；备份失败 → `TryLaunch(target)` 重启原目标 + `Exit(12)`；覆盖 `IOException`/`UnauthorizedAccessException` |
| P2-15 Windows 菜单 | ✅ 已实现 | `Win32WebviewWindow.cs:2000/2015/2270/2280`、`:2723-2729`、`Win32Menu.cs:109/201` |
| P2-16 MSIX | ✅ **已实现（2026-10-18）** | `src/Wails.Net.Cli/Build/MsixPackageOptions.cs`/`MsixPackager.cs`；`PackCommand.cs` 新增 `--format msix`/`appx` 与 `--publisher/--cert/--cert-password/--use-msix-tool`；测试 18 项（含真实 `MakeAppx` 端到端，工具缺失时跳过） |
| P2-17 泛型生成器 | ⊘ 不适用 | `TypeScriptGenerator.cs:20/71/131`、`TypeScriptTypeMapper.cs:73-124` 无模型创建器 |
| P2-18 Linux 托盘右击 | ✅ 已实现 | `LinuxSystemTray.cs:242-244`（右击）`/256-258`（左键） |
| P2-19 WebView2 超时 | ✅ **已对齐（2026-10-18）** | `Win32WebviewWindow.cs:273` `EmbedTimeout = 60s`（原 30s）；初始化 deadline 与 `Task.WhenAny(createTask, Delay(EmbedTimeout))` 已存在；无自建消息泵 |
| P2-20 GTK3 screen guard | ✅ 已实现（等价） | `LinuxPlatformApp.cs:267-270` |
| P2-21 信号处理器 | ✅ **已实现（2026-10-18）** | `Hosting/DefaultSignalHandler.cs`；`Application.cs:1200 SetupSignalHandler()`（主循环前）、`:1316` Shutdown 释放；`ApplicationOptions.DisableDefaultSignalHandler` |
| P2-22 setup 向导 | ✅ **已实现（2026-10-18）** | `src/Wails.Net.Cli/Setup/SetupSystemInfo.cs`/`SafeInstallerCommand.cs`/`SetupInstallerCatalog.cs`；`SetupCommand.cs` 新增 `--json`/`--system`/`--install`/`--yes`；测试覆盖白名单 23 通过 / 22 拒绝 |
| P2-23 macOS 输出 | ✅ **已实现（2026-10-18）** | `src/Wails.Net.Cli/Build/MacOsOutputPreparer.cs`（`Decide` 纯逻辑 + `PrepareAsync` 薄封装）；`PackCommand.cs` L206-223 调用；测试 9 项（真实挂载探测未在本机验证） |
| 待实现 A | ✅ **已实现（2026-10-18）** | `EndpointUpdateProvider.cs`（任意 URI/scheme → manifest，约 740 行）+ 28 项测试；`wails://` 经核实**非** updater 协议前缀（webview scheme），未做路由层；appcast 未实现（无签名槽位） |
| 待实现 B | ✅ **已实现（2026-10-18）** | `LinuxWebviewWindow.cs`：`OnWindowRealize` 取 `Gtk.Window.GetSurface()`；`Gdk.Surface` `notify::state` → `Gdk.Toplevel.GetState()` 的 `Minimized` 派发 WindowMinimised/UnMinimised；`Gdk.Surface` `layout` → WindowResized。程序化路径不变；**原「受阻于 GirCore 0.8.0」结论已推翻** |
| 待实现 C | ✅ 已完成 | `src/Wails.Net.Application.MacOS/` 12 个 `.cs`、约 4289 行（`MacOSWebviewWindow.cs` 1545、`MacOSPlatformApp.cs` 943），`#if MACOS` 原生 / `#else` 降级 |

### 9.7.4 源码级复核后的待实现项（按优先级）

1. ~~**P0-1 Streams 双向字节流**（机制级）~~ ✅ **已于 2026-10-17 实现**，见 §9.8
2. ~~**P2-21 App.Run 信号处理器**~~ ✅ **已于 2026-10-18 实现**，见 §9.9
3. ~~**P1-8 请求 64MiB 上限 → 413**~~ ✅ 已于 2026-10-18 实现；~~**P1-5 macOS 媒体权限**~~ ✅ 已于 2026-10-18 实现；**P1-9 scheme 请求取消/上下文转发** ⚠️ 部分实现（macOS 完整、Windows 无 CDP 中止、Linux 无钩子）—— 见 §9.10
4. **P1-4 MCP 服务器**（暂缓，需先定实现方式：手写最小 MCP vs 引入 SDK）
5. ~~**P2-14 / P2-19 / P1-10**~~ ✅ 已于 2026-10-18 实现 —— 见 §9.11
6. ~~**P2-11**~~ ✅、~~**P2-16**~~ ✅、~~**P2-22**~~ ✅、~~**P2-23**~~ ✅ 已于 2026-10-18 实现 —— 见 §9.12
7. ~~**待实现 A**~~ ✅ `EndpointUpdateProvider`（`wails://` 非 updater 协议；appcast 未实现）—— 见 §9.12
8. ~~**待实现 B**~~ ✅ Linux 外部 WM 最小化/缩放事件（原 GirCore 受阻结论已推翻）—— 见 §9.12
9. ~~**P2-12** dev 清理 + 资产反向代理 / **P2-13** macOS Dock bounce / **P1-7** GTK app ID / **P1-9** Windows CDP~~ ✅ 已于 2026-10-18 实现 —— 见 §9.13

> 说明：§9.2/§9.7 中标注「✅ 已实现」「⊘ 不适用」的条目无需再跟进；上表仅为仍需实现/对齐的剩余项。

---

## 9.8 P0-1 Streams 双向字节流实现记录（2026-10-17）

依据 Wails v3 `v3.0.0-beta.28`（`pkg/application/stream.go` / `stream_session.go` / `stream_transport.go` / `stream_server.go` / `stream_prelude_*.go`，以及前端 `internal/runtime/desktop/@wailsio/runtime/src/stream.ts`）1:1 移植「WebSocket 编程模型但不绑 TCP 端口」的双向字节流。

### 9.8.1 交付内容

| 层 | 内容 | 位置 |
|----|------|------|
| 核心 | 常量/帧编解码/StreamConn/StreamSession/StreamManager/chunk 重组/HTTP 端点/异步条件变量 `PulseGate` | `src/Wails.Net.Application/Streams/`（9 个文件） |
| 应用 | `Application.Streams`、`Application.HandleStream(name, handler)`；Shutdown 时 Dispose；server 模式在 `GenerateRuntimeJs` 注入 `window._wails.streamFactory`（对应 Go `stream_prelude_server.go`） | [Application.cs](../../src/Wails.Net.Application/Application.cs) |
| 请求 | `WebviewRequest.Headers`（大小写不敏感）、`RequestBodyBytes`（二进制体）；`WebviewResponse` 扩展状态码/自定义 header 工厂 | `src/Wails.Net.Application/WebView/` |
| 路由 | `WebviewRequestPipeline` 在 `EventPayloadStore` 特判之后、`IsIpcRequest` 判定**之前**插入 `/wails/stream/*` 分支 | [WebviewRequestPipeline.cs](../../src/Wails.Net.Application/WebView/WebviewRequestPipeline.cs) |
| 平台 | 四平台透传 `x-wails-stream-*` header 与 body 原始字节；Windows（`GetDeferral` 异步完成）/ Linux（`FinishWithResponse` 延迟）/ macOS（`WKURLSchemeTask` 延迟）实现长轮询 | `src/Wails.Net.Application.{Windows,Linux,MacOS,Android}/` |
| Server | `WebSocketTransport` 新增 `/wails/stream/ws`（query `?name=`，Origin 同源校验） | [WebSocketTransport.cs](../../src/Wails.Net.Application/Transport/WebSocketTransport.cs) |
| 前端 | `Stream(name)` / `JSONStream(name)` / `WailsSocket`（held-poll、batch、chunk、退避重试、`"WS1\0"` 解码） | `packages/wails-net-runtime/src/internal/stream.ts`、`nanoid.ts`；`index.ts` 导出 |

### 9.8.2 线协议（与 v3 一致）

- 端点：`GET /wails/stream/poll`（HEAD → 405 + `Allow: GET`）、`POST /wails/stream/send`、server `/wails/stream/ws?name=`。
- 控制数据全部走 header：`x-wails-stream-{session,generation,conn,kind,name,batch,chunk,chunk-index,chunk-total}`。
- 帧格式：`connID(u32 BE) + kind(u8) + len(u32 BE)`；kind `Data=0 / Open=1 / Close=2 / Error=3`。
- poll 响应体：`"WS1\0" + flags(bit0=more) + count(u32 BE) + count×(connID + kind + len + payload)`。
- batch 体：`count(u32 BE) + count×(len u32 BE + payload)`。
- 状态码：`204/200/400/403/405/409/410/429(RETRY-AFTER: 0)`。

### 9.8.3 上界与生命周期（对齐 v3）

单窗口出站 256 帧 / 8 MiB；单连接入站 256 帧 / 8 MiB；单 session 连接 256；每窗口 session 16；全局 session 1024；单帧 64 MiB；全局出站/入站各 256 MiB + 8192 帧；连接/close/控制帧额度各 4096；chunk 128 MiB / 4096 片。hold 20s、session TTL 60s、sweep 20s、grace 10min、poll 单响应 1 MiB。**仅 poll 可 supersede** 旧 generation session；缺 `x-wails-stream-generation` → 410；控制帧/close 拥有独立额度，不受数据背压影响。

### 9.8.4 测试

- `tests/Wails.Net.Application.Tests/Streams/`（8 个文件）：**74 个 TUnit 测试全部通过**，覆盖帧编解码 round-trip、并发顺序性、背压（depth/bytes/单帧超限）、全局额度、TTL 与 generation 水位、reload supersede 窗口隔离、超大帧边界、drain 响应上限、控制帧/close 独立额度、端点状态码、chunk 乱序/重试/冲突/重复、close 语义、pipeline 路由；以及端到端：桌面全链路（open → poll ack → send data → handler 回送 → poll 收到数据帧 + close 帧）、server 模式 `/wails/stream/ws?name=` 真实 WebSocket 回环（含 NormalClosure 关闭握手）、全局 session 上限 1024、`GenerateRuntimeJs` 在 server/桌面模式的 `streamFactory` 注入差异。
- 修复（由 server WS 端到端测试暴露）：server 模式 `StreamConn.Close()` 先取消连接令牌再排队 close 帧，而 `WsStreamSink` 的关闭判定会因令牌已取消而拒绝**所有**帧，导致 close 帧永不写出、关闭握手未完成。已按 Go `streamSession.enqueue` 的控制帧豁免修正：控制帧（open ack / error / close）在连接令牌取消后仍可入队，仅数据帧受此限制。
- 前端 `packages/wails-net-runtime`：`tsc --noEmit` 通过；`stream.test.ts` 15 用例通过。

### 9.8.5 已知限制 / 未覆盖

- **Android 不支持桌面长轮询**：`ShouldInterceptRequest` 契约要求同一回调内同步返回，无法延迟完成；Android 端 Streams 需后续经事件通道方案支持（已在 `WailsWebViewClient.cs` 拦截器注释中说明）。
- 本机未安装 `android` 工作负载，**Android 平台改动未经编译验证**（改动仅为 header 透传，风险低）。
- 服务端 `/wails/stream/ws` 已有 WebSocket 回环单元测试；但**各平台长轮询（Windows `GetDeferral` / Linux 延迟 `FinishWithResponse` / macOS 延迟 `WKURLSchemeTask`）与前端 `Stream(name)` 的真实浏览器/设备端到端行为未验证**（平台「延迟完成」逻辑为代码审查验收）。
- Windows 长轮询慢路径按 Go 语义在**线程池线程**设置 `args.Response` 并 `Complete()`（WebView2 deferral 允许跨线程完成，与 Go handler goroutine 行为一致）；未在真实 Windows 运行验证。若实测要求 UI 线程，改用 `Application.DispatchOnMainThread` 派发。

### 9.8.6 回归

`Wails.Net.Application.Tests` 全量：1526 项、17 失败。经 `git worktree` 在 HEAD（不含本次改动）复现**完全相同的 17 项失败**，均属 `Plugins/`（Mobile Nfc/Camera、DpiScale、Cli 等，因 `ctx.Services` 为空触发 NRE）的**既有问题**，与本次改动无关。本次新增 Streams 74 项全部通过。

---

## 9.9 P2-21 默认信号处理器实现记录（2026-10-18）

依据 Wails v3 `v3.0.0-beta.28`：`pkg/application/signal_handler_desktop.go`（`App.setupSignalHandler`）+ `internal/signal/signal.go`（`SignalHandler`）。原判断为「未实现」（`src/` 无 `PosixSignalRegistration`/`SignalHandler`，「Generic Host 预期等价」不成立），本次补齐。

### 9.9.1 交付内容

| 内容 | 位置 |
|------|------|
| `DefaultSignalHandler`：监听 SIGINT/SIGTERM；首个信号 → 退出回调；重复信号逐级提示；达到上限（默认 3）→ 强制退出 | [Hosting/DefaultSignalHandler.cs](../../src/Wails.Net.Application/Hosting/DefaultSignalHandler.cs) |
| `Application.Run` 进入平台主循环前安装（对应 Go `a.setupSignalHandler(a.options)` 紧跟 `a.impl.run()` 的时序） | [Application.cs](../../src/Wails.Net.Application/Application.cs) |
| `Application.ShutdownAsync` 释放处理器，恢复系统默认信号处置 | 同上 |
| 新增 `ApplicationOptions.DisableDefaultSignalHandler`（默认 false；置 true 时不安装，Ctrl+C 沿用系统默认终止） | [ApplicationOptions.cs](../../src/Wails.Net.Application/Options/ApplicationOptions.cs) |

### 9.9.2 信号语义

- **首个信号**：日志「收到信号 X，正在退出应用……」，随后经 **`ShouldQuit()`** 判定 —— true → `Quit()`；false → 应用继续运行（与 Wails.Net `ShouldQuit` 既有文档语义一致）。
- **未达上限的重复信号**：日志提示「再按 N 次可强制退出」，不重复触发退出。
- **达到上限**（`MaxSignal`，默认 3）：日志「强制退出」+ `Environment.Exit(1)`。
- 注册使用 `PosixSignalRegistration`（Windows 与 Unix 通用，.NET 6+），并置 `ctx.Cancel = true` 抑制默认处置（等价 Go `signal.Notify`）；`Dispose` 恢复默认处置。

### 9.9.3 与 Go 的差异

Go `SignalHandler.Start()` 在首个信号分支 `break` 出监听循环，使 `i < MaxSignal`（逐级提示）与 `i == MaxSignal`（强制退出）两个分支**不可达**；本移植按这两个可见分支的设计意图实现完整阶梯（首个退出 → 提示 → 强制退出）。

### 9.9.4 测试

`tests/Wails.Net.Application.Tests/Hosting/DefaultSignalHandlerTests.cs`（**9 项全部通过**）：首个信号触发一次退出回调且不强制退出；重复信号未达上限不退出；达上限强制退出一次；`Start`/`Dispose` 幂等；`maxSignal` 参数校验；`DisableDefaultSignalHandler` 默认 false；`Application` 默认安装 / 禁用时跳过；`ShouldQuit` 返回 false 时不退出。

回归：全量 1535 项、17 失败（与基线 worktree 一致，均为 `Plugins/` 既有问题），无新增失败。

### 9.9.5 未覆盖

- 未做真实进程级信号注入测试（Go 通过子进程 `syscall.Kill` 验证端到端）；本移植以 internal 触发点覆盖分级逻辑，真实信号仅靠 `PosixSignalRegistration` 注册正确性保证。
- Windows 上 SIGTERM 由 `PosixSignalRegistration` 映射为控制台关闭/注销事件，未在真实关机/注销场景验证。

---

## 9.10 P1-8 / P1-9 / P1-5 实现记录（2026-10-18）

依据 Wails v3 `v3.0.0-beta.28`。本期完成 **P1-8**、**P1-5**，**P1-9** 部分完成；**P1-4 MCP** 按决策暂缓（需先定实现方式）。

### 9.10.1 P1-8 运行时请求 64 MiB 上限 → HTTP 413

| 内容 | 位置 |
|------|------|
| `HttpTransport.MaxRuntimeBodyBytes = 64 MiB`；`HandleMessageAsync` 先按 `ContentLength64` 预判、再边读边计数（覆盖 chunked 无长度场景），超限返回 **413** | [Transport/HttpTransport.cs](../../src/Wails.Net.Application/Transport/HttpTransport.cs) |
| 413 响应体文案 `runtime request body exceeds 64 MiB`（对齐 Go `http.Error`），`text/plain; charset=utf-8` | 同上 |
| 分块上传路径单块上限 1 MiB（`maxChunkBodyBytes`）→ **422**（对齐 Go） | 同上 |
| 未超限请求解码语义不变（保留 UTF-8 / BOM 行为） | 同上 |

测试：`tests/Wails.Net.Application.Tests/Transport/HttpTransportRequestBodyLimitTests.cs`（**5 项全部通过**）：超限 `Content-Length` → 413（不发实体，避免 64 MiB 分配）、未超限正常 IPC 成功、`ReadBodyWithLimitAsync` 以小上限验证「等于放行 / 超 1 字节拒绝」、常量值断言。
未覆盖：未端到端发送真实 64 MiB 实体；`WebSocketTransport` 的 IPC 走 WS 帧，Go 侧无对应 runtime body 上限，未改动。

### 9.10.2 P1-9 自定义 scheme 请求取消 / 上下文转发

| 平台 | 实现 |
|------|------|
| 管线 | `WebviewRequestPipeline.HandleAsync(request, ct)` 取消时 `throw;`（未改动）；令牌已传至 IPC / 资源处理器 |
| macOS | 每个 URL scheme task 建独立 `CancellationTokenSource`；`StopUrlSchemeTask` → `MarkStopped` 记录并 `Cancel()`；取消后不再回调 `DidReceiveResponse/DidReceiveData/DidFinish`（[MacOSWebviewWindow.cs](../../src/Wails.Net.Application.MacOS/MacOSWebviewWindow.cs)） |
| Windows | 每请求链接式 CTS（链接窗口级源）贯穿管线；窗口关闭 / Dispatcher 释放时取消，使在途长轮询尽快结束；异常路径同样释放；**取消以 `204 No Content` 收尾（不返回 500）**（[Win32WebviewWindow.cs](../../src/Wails.Net.Application.Windows/Win32WebviewWindow.cs)） |
| Linux | WebKitGTK `URISchemeRequest`(libsoup) 无取消/中止钩子，保持原行为并加注释说明 |

**（2026-10-18 更新）**：Windows 的 CDP `Fetch`/`Network` 逐请求中止**已实现**（`WindowsRequestCancellation.cs`，见 §9.13）；Linux 仍无取消钩子（WebKitGTK 限制）。
**硬约束已满足**：未取消路径的行为与改造前完全一致。

### 9.10.3 P1-5 macOS 媒体权限

| 内容 | 位置 |
|------|------|
| 策略选项 `WebviewWindowOptions.Mac.CameraPermission` / `MicrophonePermission`（`Permission.Default / Allow / Deny`） | [Options/WebviewWindowMacOptions.cs](../../src/Wails.Net.Application/Options/WebviewWindowMacOptions.cs) |
| `MacUiDelegate.RequestMediaCapturePermission` 覆写；相机 / 麦克风分别判定并取更严格者（Deny > Prompt > Grant），对齐 Go `mediaCaptureDecision` / `strictestCaptureDecision` | [MacOSWebviewWindow.cs](../../src/Wails.Net.Application.MacOS/MacOSWebviewWindow.cs) |

默认策略 `Permission.Default` 与 Go `PermissionDefault` 一致（交由 WebKit 原生提示）；origin / frame 与 Go 一样不参与判定。
**未编译验证**：本机为 Windows、无 macos workload，`#if MACOS` 分支不参与编译；`net10.0` 骨架路径编译通过。

### 9.10.4 回归

`Wails.Net.Application.Tests` 全量：1540 项、17 失败（与基线 worktree 一致，均为 `Plugins/` 既有问题），无新增失败；本期新增 P1-8 测试 5 项全部通过。核心 / Windows / Linux / macOS 四个项目均编译通过（0 警告）。

### 9.10.5 待复核

1. macOS `RequestMediaCapturePermission` 覆写签名与 `WKMediaCaptureType` / `WKPermissionDecision` 成员名需在 mac 主机编译确认。
2. `Permission` 枚举位于 `Options` 命名空间，如与后续跨平台权限类型命名冲突可更名。
3. ~~Windows 取消路径下被取消的在途请求会返回 500~~ ✅ **已修复（2026-10-18）**：取消以 **204 No Content** 收尾（不记为错误），快路径 / 慢路径延续 / 外层异常路径三处统一处理。

---

## 9.11 P2-14 / P2-19 / P1-10 实现记录（2026-10-18）

依据 Wails v3 `v3.0.0-beta.28`。本期完成 P2-14、P2-19、P1-10。

### 9.11.1 P2-14 Updater 备份失败恢复语义（beta.21 #6080）

Go `pkg/updater/helper.go`：父进程退出后、**备份之前**调用 `clearHelperEnv()`（使新应用与恢复的原应用都能正常启动）；备份失败 → `l.launch(target)` 重启原目标 + 返回 12。

Wails.Net `HelperProcess.cs` 对齐：
- `ClearHelperEnv()` 由「替换成功后」提前到「父进程退出后、备份前」，并移除成功后那处重复清除。
- 备份失败（`IOException` / `UnauthorizedAccessException`）→ `TryLaunch(target)` 重启原目标后再 `Environment.Exit(12)`；原先只 `Exit(12)` 且未重启。
- 新增 `TryLaunch`（非致命启动，对应 Go `l.launch`）；备份不再受 `File.Exists(target)` 静默跳过保护（与 Go `copyAny` 失败即恢复语义一致）。

未覆盖：无单元测试（`Environment.Exit` 使进程内测试不可行），建议在真实更新流程回归。

### 9.11.2 P2-19 WebView2 embed 超时对齐 60s

`Win32WebviewWindow.cs` `EmbedTimeout` 由 30s 改为 **60s**，对齐 v3 `internal/webview2/pkg/edge/chromium.go:283`（`embedTimeout = 60 * time.Second`）。初始化 deadline 与 `Task.WhenAny(createTask, Task.Delay(EmbedTimeout))` 已存在；**仍未自建消息泵**（交由 WebView2 SDK）。

### 9.11.3 P1-10 Windows 托盘 light/dark 双图标 + 主题跟随（beta.14 #6016）

`Win32SystemTray.cs`：
- 字段由单一 `_hicon` 改为 `_lightModeIcon` / `_darkModeIcon`（各带 Owned 标志）。
- `SetIcon` / `SetDarkModeIcon` 各自独立加载与保存，并按 Go 的「共享句柄同步」规则处理（另一模式未设置或与旧句柄相同时跟随、`Owned=false`）。
- `GetEffectiveIcon()` 依 `Application.IsDarkMode()` 选择（暗色优先 dark、缺失回退 light，反之亦然，皆空返回 NULL 不崩溃）；`CreateNotifyIconData` 使用生效图标。
- 订阅 `KnownEvents.ThemeChanged` → `ApplyEffectiveIcon()`（仅 `_visible` 时 `NIM_MODIFY`）；`Destroy` 时 `Events.Off` 取消订阅（`_themeListenerId` 守卫保证幂等）。
- `ReleaseIcon` 语义对齐 Go（仅 Owned 且未被其它字段引用时 `DestroyIcon`），避免泄漏与重复销毁；`Destroy` 释放双句柄。

**补全（2026-10-18）**：新增平台层**顶层隐藏消息窗口** `PlatformMessageWindow`（`src/Wails.Net.Application.Windows/PlatformMessageWindow.cs`），由 `WindowsPlatformApp.Run` 在消息循环前创建、`Destroy` 时销毁。该类窗口可接收系统广播 `WM_SETTINGCHANGE`，在类别为 `ImmersiveColorSet` 时发出 `KnownEvents.ThemeChanged` —— **无 webview 窗口时托盘同样跟随主题**，与 Go 在应用级消息窗口（`application_windows.go:280-296`，发出 `events.Windows.SystemThemeChanged`）一致。
保留说明：`Win32WebviewWindow` 仍会在自身收到 `WM_SETTINGCHANGE` 时发出同一事件，故**存在 webview 窗口时同一次主题切换可能发出 2 次 `ThemeChanged`**（托盘处理幂等，重复应用无副作用）。若要与 Go 完全一致（仅应用级发出），可移除窗口侧的发出。

### 9.11.4 验证

- `Wails.Net.Application.Windows`、`Wails.Net.Plugins.Updater`、测试项目均编译通过（0 警告）。
- 全量测试 1540 项、17 失败（与基线 worktree 一致，`Plugins/` 既有问题），**无新增失败**；本期 P2-14/P2-19 无新增测试（`Environment.Exit` 与 Win32 行为）。
- **P1-10 补全新增测试** `tests/Wails.Net.Application.Windows.Tests/PlatformMessageWindowTests.cs`（4 项）：纯逻辑 2 项（`ImmersiveColorSet` 发出 / 其它类别不发出）默认执行；窗口创建与销毁 2 项标记 `[RequiresGuiEnvironment]`（按 AGENTS §4.4 默认跳过）。设置 `WAILS_RUN_GUI_TESTS=1` 后 **4/4 通过**；Windows 测试项目整体 179 项（46 通过 / 133 跳过）。
- 托盘主题切换与 Updater helper 的端到端行为仍未在本环境验证（无真实桌面会话与更新流程）。

---

## 9.12 P2-11 / P2-16 / P2-22 / P2-23 / 待实现 A / 待实现 B 实现记录（2026-10-18）

依据 Wails v3 `v3.0.0-beta.28`。本批完成 §9.7.4 的剩余 P2 项与两个挂起/受阻项。

### 9.12.1 P2-11 dev 前端就绪检查（beta.26 #6150）

新增 `src/Wails.Net.Cli/Build/FrontendDevServerReadiness.cs`（就绪探测纯逻辑 + HTTP 单次探测 + 状态分类）；`DevCommand.cs` 在启动 `dotnet watch`**之前**探测（默认 60s 超时 / 250ms 轮询 / 单次请求 5s；2xx·3xx=就绪，连接被拒·超时·4xx·5xx 继续重试；超时→报错并**不启动**应用）。URL 取值：`FRONTEND_DEVSERVER_URL` → `wails.json` 的 `frontend.devServerUrl`，未配置/非法则跳过（保持既有启动行为）。
测试 `tests/Wails.Net.Cli.Tests/FrontendDevServerReadinessTests.cs` **10 项**（探测委托可注入，无真实网络）。

### 9.12.2 P2-16 MSIX 打包（beta.21 #6115）

新增 `src/Wails.Net.Cli/Build/MsixPackageOptions.cs` + `MsixPackager.cs`（架构映射 amd64→x64/386→x86、`AppxManifest.xml` 生成（对齐 v3 两个模板）、Assets 占位 PNG、结构化目录、`MakeAppx pack`、可选 `signtool sign`）；`PackCommand.cs` 新增 `--format msix|appx` 与 `--publisher/--cert/--cert-password/--use-msix-tool`。
**产物与降级**：本机 `MakeAppx.exe`/`signtool.exe` 位于 Windows SDK（PATH 未命中时按 SDK 版本降序回退）；工具缺失**不静默失败**——保留 `*.structure` 目录并返回清晰错误；signtool 缺失仍产出未签名包并 Warn。已实测真实 `MakeAppx` 打包成功。
测试 `MsixPackagerTests.cs` **18 项**（清单字段/转义、PNG 尺寸解析、架构映射、降级路径、端到端；工具缺失时跳过）。

### 9.12.3 P2-22 setup 向导完善（beta.28 #6224 / beta.24）

新增 `src/Wails.Net.Cli/Setup/SetupSystemInfo.cs`（系统详情：OS/架构/.NET SDK/WebView2/GTK 等，缺失统一 `unknown`；`--json` 输出）、`SafeInstallerCommand.cs`（逐条移植 Go `safeInstallers`/`getSafeCommand`：命令仅由白名单包管理器构造、无 shell、`ArgumentList` 直传 argv）、`SetupInstallerCatalog.cs`（依赖名→命令常量目录）；`SetupCommand.cs` 新增 `--json`/`--system`/`--install`（默认**仅预览**打印将执行命令）/`--yes`（确认后执行）；`DoctorCommand` 4 处 `private`→`internal` 以复用。
测试 `SetupCommandTests.cs`：系统详情必填字段、白名单 **23 条通过 / 22 条拒绝**（含 `rm -rf /`、`curl|sh`、`sudo -n`、`apt install &&` 等）。

### 9.12.4 P2-23 macOS app 输出避开 file provider（beta.28 #6225）

新增 `src/Wails.Net.Cli/Build/MacOsOutputPreparer.cs`：「路径判定 + 重定位决策」抽为纯逻辑 `Decide`（file provider→Relocate / 普通→MarkLocal / 项目外→建目录 / 已链接→确保目标本地 / 已标记→AlreadyLocal），`PrepareAsync` 为薄封装（非 macOS no-op）；`PackCommand.cs` 在打包前调用。
测试 `MacOsOutputPreparerTests.cs` **9 项**。真实挂载探测（xattr `com.apple.fileprovider.fpfs#P`/`FinderInfo`）与符号链接实际效果**未在本机验证**。

### 9.12.5 待实现 A Updater endpoint provider

新增 `src/Wails.Net.Plugins.Updater/Services/Updater/EndpointUpdateProvider.cs`（约 740 行，对应 Go `providers/endpoint`）：URL 模板占位符（platform/arch/version/channel）+ 未消费参数转 query；来源分发 `http/https`→HttpClient（204/404→null、非 2xx 抛错、响应 ≥8 MiB 上限）、`file`→本地文件、其它 scheme→注入的 `customSchemeLoader`；解析 `schemaVersion`(>1 拒绝)/`version`/`channel`/`artifacts`，按平台+架构选择（用户显式 `assetNamePattern` 优先），映射 Version/ReleaseNotes/DownloadURL/ContentLength/PublishedDate，sha256→Checksum、minisign→Signature，非法 base64 或签名缺 algo → `InvalidDataException`；并容忍 UTF-8 BOM（`JsonDocument.Parse` 不接受前导 BOM）。
**结论纠正**：v3 的 `wails://` **不是** updater 协议前缀（实为 webview/资产服务器自定义 scheme），故未实现路由层而提供 scheme 注入点；appcast 未实现（Sparkle XML 的 Ed25519 签名在 `UpdateManifest` 无对应槽位，照搬会静默丢签名）。
测试 `EndpointUpdateProviderTests.cs` **28 项全部通过**。

### 9.12.6 待实现 B Linux 外部 WM 最小化/缩放事件

`src/Wails.Net.Application.Linux/LinuxWebviewWindow.cs`：realize 后经 `Gtk.Window.GetSurface()` 取 `Gdk.Surface`；订阅 `Gdk.Surface` 的 **`notify::state`**，读 `Gdk.Internal.Toplevel.GetState`（`gdk_toplevel_get_state`）的 `Gdk.ToplevelState.Minimized`，与 `_minimised` 比较后派发 `WindowMinimised`/`WindowUnMinimised`（与程序化路径共用状态、不重复派发）；订阅 `Gdk.Surface` 的 **`layout`**（`LayoutSignalArgs`，含 Width/Height）派发 `WindowResized`（首次 layout 仅建基线）；`Close` 解绑信号并释放引用。
**结论纠正**：原「受阻于 GirCore 0.8.0（无 surface layout API、Gtk4 无 `is-minimised`）」**不成立** —— `Gtk.Window.GetSurface`、`Gdk.Surface.OnLayout`、`Gdk.Toplevel.GetState` 在 0.8.0 中均可用。另：GDK4 **并无公开的 `Gdk.Toplevel::state-changed` 信号**，正确触发方式是 `notify::state`（该属性可读且默认 notify，非 EXPLICIT_NOTIFY）。
构建：`Wails.Net.Application.Linux` 全量重建 **0 警告 0 错误**。运行时行为（X11/Wayland 各后端的 notify 时机）未在本机验证。

### 9.12.7 验证与回归

- **构建**：`Wails.Net.Cli`、`Wails.Net.Plugins.Updater`、`Wails.Net.Application.Linux` 及三个测试项目全部通过（0 警告）。
- **测试**：`Wails.Net.Cli.Tests` 479 项（**476 通过 / 3 跳过 / 0 失败**）；`Wails.Net.Plugins.Updater.Tests` **28/28 通过**；`Wails.Net.Application.Tests` 全量 1540 项、17 失败（与基线 worktree 完全一致，**无新增失败**）。
- **未验证**：MSIX 签名实跑（无 pfx 证书）、macOS 真实挂载探测与符号链接效果、Linux GUI 运行时的事件时机、Updater 自托管端到端。

---

## 9.13 细节项实现记录：P1-7 / P1-9 / P2-13 / P2-12（2026-10-18）

依据 Wails v3 `v3.0.0-beta.28`。本节收尾 §9.7.4 的剩余细节项（代码主体随提交 `5c695ca` 落地，此处补全验证与文档说明）。

### 9.13.1 P1-7 Linux GTK application id + single-instance bus name（beta.23 #5972 / #5973）

- **id 推导/校验（纯逻辑）**：`LinuxApplicationId.cs` —— `SanitizeAppName`（非法字符→`_`、折叠连续下划线、trim 首尾、前导数字加 `_`、转小写、空回退 `wailsapp`）、`ValidateApplicationId`（对齐 `g_application_id_is_valid()`：≥2 个 `.` 元素、元素不以数字开头、仅 `A-Za-z0-9_-`、≤255）、`ResolveApplicationId`（推导 `org.wails.<name>`，覆盖非法时回退并输出 `error`）、`ResolveProgramName`（设置覆盖时程序名继承实际 id）。
- **覆盖来源**：静态属性 `ApplicationIdOverride` / `ProgramNameOverride`（核心库 `LinuxOptions` 未新增字段，故置于平台项目）。
- **single-instance bus name**：`LinuxSingleInstanceNames.TryDerive` → `uniqueID + ".SingleInstance"`；`LinuxDbusSingleInstance.TryClaim` 经 `org.freedesktop.DBus.RequestName`（`DO_NOT_QUEUE`，`PRIMARY_OWNER`/`ALREADY_OWNER`→首实例、`EXISTS`→第二实例）；D-Bus 不可用降级为「锁文件 + UNIX socket」。
- **落地**：`LinuxPlatformApp` 构造期解析 id/程序名；`Run()` 经 `GLib.Functions.SetPrgname(ProgramName)` 应用程序名。
- **与 Go 的差异**：Wails.Net Linux 用 `GLib.MainLoop` 而非 `Gtk.Application`，id 无法挂到 `gtk_application_new()`，仅经程序名（`g_set_prgname()`，Wayland surface app_id）间接体现；D-Bus 不可用视为可降级（Go 为致命错误）。
- 测试：`LinuxApplicationIdTests.cs`、`LinuxSingleInstanceNamesTests.cs`、`LinuxPlatformAppTests.cs`。

### 9.13.2 P1-9 Windows scheme 请求逐请求取消（CDP Fetch/Network）

- `WindowsRequestCancellation.cs`：`Fetch.enable` 仅拦截 `http://wails.localhost/*` 的 Request 阶段；`Fetch.requestPaused` 时登记 `networkId → token` 并经 `Fetch.continueRequest` 注入桥接头 `X-Wails-Request-Id` 后立即放行；`Network.loadingFailed`/`loadingFinished` 按 `requestId` 取消对应令牌。
- 接入（`Win32WebviewWindow.cs`）：初始导航前 `StartRequestCancellationCdp()`；`OnWebResourceRequested` 读取并移除桥接头 → `Consume(token)` 取得跟踪令牌 → 与窗口级令牌 `CreateLinkedTokenSource` 传入管线；快/慢/异常三路径统一以 **204** 收尾；`Close` 释放。
- 降级：任一 CDP 能力不可用（含运行时未支持 `Fetch` 域）→ 静默退回既有「窗口级取消 + 204」语义，不向用户暴露错误。
- **未覆盖**：Go 的 Keepalive 绑定脚本与 `Target.setAutoAttach` 子会话（worker）跟踪未实现；本机无 GUI，**以构建 + 代码审查验收**（无单元测试，`Consume` 等纯逻辑虽可测但依赖 `CoreWebView2` 构造）。

### 9.13.3 P2-13 macOS Dock bounce（beta.6）

`MacOSWebviewWindow.Flash(bool enabled)`：`enabled` 时 `NSApplication.SharedApplication.RequestUserAttention(NSRequestUserAttentionType.InformationalRequest)`（Dock 图标弹跳），禁用为 no-op（对齐 Go `windowFlash` 不调 cancel）。`WebviewWindow.Flash` 委托到平台实现，委托已由 `WebviewWindowStateToggleTests` 覆盖。

### 9.13.4 P2-12 dev 退出清理（beta.25 #6048）+ dev 资产反向代理（beta.8）

**退出清理**：

- `DevProcessCleanup.cs`：`IDevProcessHandle`（`HasExited`/`KillTree`/`WaitForExitAsync`，均保证不抛）+ `ProcessTreeHandle`（`Kill(entireProcessTree: true)`）；`TerminateAsync` 跳过已退出、终止在运行、`Task.WhenAll` 限时等待（默认 1s，落在 System.CommandLine 退出宽限期内），返回 `DevCleanupResult(Killed, AlreadyExited, TimedOut)`。
- `DevCommand` `finally`：终止 `dotnet watch` 进程树（Cancel 路径不会自动终止，必须显式 Kill）+ `StopFrontendDevServerAsync`（取消前端源并限时等待），避免孤儿进程。
- 测试：`DevProcessCleanupTests.cs`。

**dev 资产反向代理**（对齐 Go `internal/assetserver/build_dev.go`：`devProxyMaxIdleConnsPerHost=512` + `retryTransport`）：

- 新增 `src/Wails.Net.Application/WebView/DevAssetProxy.cs`：把资源请求（GET）转发到前端 dev server。
  - 保留上游 `Content-Type`（vite 对 `.ts` / 模块返回的 MIME 与本地「按扩展名推断」不同，透传可避免 ES module 因 MIME 不符被拒）；同时透传 `Cache-Control`/`ETag`/`Last-Modified`/`Vary`/`Expires`。
  - 连接池 `MaxConnectionsPerServer=512`（对齐 `devProxyMaxIdleConnsPerHost=512`）；瞬时失败按指数退避重试（默认 3 次），耗尽后返回 `null` → 回退本地资源（不中断页面加载）；请求 `Accept-Encoding: identity` 避免二次解压。
- 接线：`Application.DevAssetProxy`（internal）→ `DesktopApplicationBuilder` 在 `DesktopHostOptions.DevServerUrl` 为合法 http(s) 时创建并注入 → [WebviewRequestPipeline.HandleAssetAsync](../../src/Wails.Net.Application/WebView/WebviewRequestPipeline.cs) 对 GET 资源请求优先委托代理，命中即返回，未命中 / 失败回退既有本地路径。
- `WebviewRequest.Query`：新增字段，Windows（`uri.Query`）/ macOS（`NSUrl.Query`）/ Android（`Uri.Query`）透传查询串（HMR 的 `?import` / `?t=`）；**Linux WebKitGTK `URISchemeRequest` 不暴露查询 API，保持 null**（限制）。
- CLI：`wails dev` 在启动 `dotnet watch` 前把解析到的前端 URL 导出为 `WAILS_DEV_SERVER_URL`；模板 [Program.cs](../../src/Wails.Net.Templates/content/App/Program.cs) 据此设置 `options.DevServerUrl`。
- **生产模式行为不变**：未配置 `DevServerUrl` 时不创建代理，资源请求仍走本地 AssetServer。
- 测试：`DevAssetProxyTests.cs`（12 项：URL 拼接 / 状态码·Content-Type·缓存头映射 / 重试 / 失败回退 / 取消 / Dispose）、`DevAssetProxyPipelineTests.cs`（3 项：有代理时转发且跳过本地、无代理走本地、IPC 不经代理）。

### 9.13.5 验证

- **构建**：`Wails.Net.Application`、`.Windows`、`.Linux`、`.MacOS`、`Wails.Net.Cli`、`Wails.Net.Application.Tests`、`.Windows.Tests` 均 **0 错误**（Windows 存在既有 `MSB3277` WindowsBase 版本冲突警告，与本批无关）。**Android 项目未构建**（本机未安装 `android` 工作负载）。
- **测试**：`Wails.Net.Application.Tests` 全量 **1555 项、17 失败**（较本批前基线 1540 项 +15，17 失败与基线完全一致，均为 `Plugins/Mobile` 既有问题，**无新增失败**）；`Wails.Net.Cli.Tests` 488 项（**485 通过 / 3 跳过 / 0 失败**）；`Wails.Net.Application.Windows.Tests` 179 项（**46 通过 / 133 跳过 / 0 失败**）；`Wails.Net.Application.Linux.Tests` 纯逻辑用例通过（2 项 `PlatformFactoryLinuxTests` 失败系 **Windows 主机环境**所致，需在 Linux/WSL 运行）。
- **未验证**：dev 资产代理端到端（真实 vite + GUI 会话）；Windows CDP 端到端（无 GUI 会话）；macOS Dock bounce 实跑、`NSUrl.Query`（无 mac 主机，`#if MACOS` 分支未编译）；Android `Uri.Query`（缺 workload）；D-Bus bus name 实际占位（无 session bus 环境）。


