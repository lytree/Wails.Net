# Wails.Net × Wails v3 最新开发分支对比：可参考项确认

> 分析日期：2026-08-07（beta.4 基线）；2026-10-17 再确认：演进至 v3.0.0-beta.28（见 §9）；2026-10-22 再确认：beta.28 仍为最新（见 §9.14）；**2026-10-31 基线升级：Wails v3 `v3.0.0` 已于 2026-10-26 正式发布（GA），beta.29–beta.34 与 GA 差异、安全项及待对齐清单见 §11**；**2026-11-03 基线升级：最新 weekly 为 `v3.1.0`（2026-11-01，`v3.2.0-nightly.*` 为预发布不作基线），差异清单、实现与待办见 §12**
> **2026-10-17 源码级复核**：§9.2/§9.1 各项已对照 Wails v3 `v3.0.0-beta.28` 官方 Go 源码（tag `v3.0.0-beta.28`，`v3/` 目录）逐项确认，结论与更正见 **§9.7**。
> 对比对象：wailsapp/wails **v3 发布流**（`v3/` 目录；v3.0.0 GA 后按周更 minor + nightly 预发布）
> 基线：AGENTS.md 参考版本 **`v3.1.0`**（2026-11-01 weekly；§1–§10 为 beta 时代结论、§11 为 v3.0.0 GA 基线，均保留原文与日期，**以 §12 为当前基线**）

---

## 1. 版本现状与基线差距

| 维度 | Wails v3（master） | Wails.Net |
|------|--------------------|-----------|
| 当前版本 | **v3.1.0（2026-11-01 weekly）**；§1–§10 写作时为 v3.0.0-beta.4（2026-08-05），§11 为 v3.0.0 GA 基线，§12 为重估 | 参考 v3.1.0 基线（主体机制已对齐，剩余差异见 §12） |
| 里程碑 | 2026-08-02 alpha → beta；**2026-10-26 v3.0.0 GA（此后周更 minor + nightly）** | 8 阶段实现完成（含 Android） |
| 核心架构 | Go Services + 静态分析生成 TS 绑定；可安装插件（打包前端资源）；多窗口一等公民 | [Binding] 源生成器强类型调用器；IPlugin 插件体系（45+ BuiltIn）；多窗口支持 |
| 构建 | Taskfile + `wails3 build` + server 构建 | MSBuild/dotnet + `wails build` + Server 模式（WebSocketTransport） |
| 前端运行时 | @wailsio/runtime **3.1.0**（版本号与 Wails 发布一致） | @wails-net/runtime（自研，已迁 npm 包） |
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

> 用户询问"与 Tauri 最新 beta 版的差异"时需先澄清版本状态：**Tauri 2 已于 2024-10-02 GA**，当前稳定版 **2.12.5**（2026-10-21；npm `@tauri-apps/api` 为 2.12.4），**不存在 beta 阶段**；Tauri 3 仅有 `@tauri-apps/cli-cef-v3.0.0-cef.0` alpha 预览（2026-05-04，CEF 后端实验，不作参考）。因此 Wails.Net 的对比基线是 **Tauri v2 GA 2.12.5**（AGENTS.md §1 已同步）。
>
> 2.11.5（2026-07-01）→ 2.12.5（2026-10-21）主要变化（2.12.0 起）：`limitNavigationsToAppBoundDomains`（限制导航域，安全）、`appDirectoriesOverride`（统一数据目录）、`@tauri-apps/api/app` 新增 `exit` 命令、`convertFileSrc` 的 Rust 等价 API、`setFullscreenOnMonitor`、wry 权限处理器 API（摄像头/麦克风/字体/剪贴板等运行时决策）、macOS Liquid Glass `Regular`/`Clear` + `interactive`、ACL 拒绝作用域修复（此前一个 capability 的拒绝会影响所有 origin）、`asset://` 多范围响应与异步加载修复，以及 NSIS 降级拦截修复。

| # | Tauri 2.12.x 特性 | Wails.Net 现状 | 可参考性 |
|---|-------------------|----------------|----------|
| 1 | 插件生态（官方 30+ 插件，前端资源/权限分离打包） | IPlugin 后端命令注册 + Security/Capability 基础（学 Tauri 设计） | P1（对齐插件权限模型，已有基础） |
| 2 | 动态 ACL（`dynamic-acl` feature，可关闭减体积） | 无动态 ACL 概念（静态 Capability 声明） | 观望 |
| 3 | `data-tauri-drag-region` 拖拽区域 | ✅ 原生拖拽已有（HasNativeDrag=true） | 无需 |
| 4 | Linux D-Bus 主题检测 | ✅ Linux 平台层已有 D-Bus 系统主题事件监听 | 无需 |
| 5 | `Webview::eval_with_callback` | ❌ 需核对 WebView eval 是否支持回调 | P3 |
| 6 | Android 多窗口（activity embedding） | ❌ Android 单 Activity | P3（远期） |
| 7 | `limitNavigationsToAppBoundDomains`（2.12.0） | ❓ 未见导航域限制（仅有资产/IPC 源校验） | P2（安全） |
| 8 | ACL 拒绝作用域按 local/remote 区分（2.12.0 修复） | ❓ Capability 拒绝是否按源区分需核对 | P2 |
| 9 | wry 权限处理器 API（运行时 Allow/Deny） | ⚠️ 有 `WebViewPermissionRequest` 抽象，需核对是否覆盖 9 类权限 | P3 |
| 10 | `asset://` 多范围响应与异步加载（2.12.0 修复） | ✅ 分块/范围请求已有（`AssetServer` 分块传输、异步读取） | 无需（建议回归） |

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
| 4 | `wails3 mcp` 安全 MCP 服务器（beta.9 #5896）+ origin/token 认证硬化（beta.19 #6092） | ✅ **已实现（2026-10-22）**：`wails mcp`（stdio / loopback Streamable HTTP、token 常量时间比较、loopback Host/Origin 校验、路径根限制、后台任务）；见 §9.14 |
| 5 | macOS 媒体权限：WKUIDelegate media-capture 方法（beta.27），`PermissionAllow/Deny` 覆盖摄像头/麦克风 | ✅ **已实现（2026-10-18）**：`WebviewWindowOptions.Mac.CameraPermission/MicrophonePermission`（`Permission.Default/Allow/Deny`）+ `MacUiDelegate.RequestMediaCapturePermission` 覆写；相机/麦克风分别判定、取更严格者。macOS 原生代码未在本机编译验证（见 §9.10） |
| 6 | macOS notch 通知窗口 + NSPanel 窗口类型（beta.12 #6010/#6008） | ❌ 未实现 |
| 7 | Linux GTK application ID 覆盖（beta.23 #5972）+ single-instance 以应用自身 ID 占用 bus name（#5973） | ✅ **已实现（2026-10-18）**：`LinuxApplicationId` id 推导/校验/覆盖 + 程序名；`LinuxDbusSingleInstance` 以 `uniqueID+".SingleInstance"` 占 bus name；D-Bus 不可用降级锁文件 + socket。见 §9.13 |
| 8 | 运行时请求 64 MiB 上限 → HTTP 413（beta.19 #6091） | ✅ **已实现（2026-10-18）**：`HttpTransport.MaxRuntimeBodyBytes = 64 MiB`，Content-Length 预判 + 边读边计数 → 413（文案 `runtime request body exceeds 64 MiB`）；分块路径单块 1 MiB → 422。见 §9.10 |
| 9 | 自定义 scheme 请求中止/取消：Windows（beta.20 #5963/#5969）、Linux（beta.23 #6126）、macOS/iOS 请求上下文转发 | ✅ **已实现（2026-10-22 复核）**：macOS 每 task 独立 CTS + `StopUrlSchemeTask` 取消；Windows `WindowsRequestCancellation` 经 CDP `Fetch`/`Network` 逐请求中止 + 窗口关闭取消；Linux WebKitGTK 无取消钩子（平台限制，保持原行为，代码已注释说明）；令牌已贯穿管线。见 §9.10 / §9.13 |
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
| P1-4 MCP | ✅ **已实现（2026-10-22）** | `src/Wails.Net.Cli/Mcp/`（McpServer / McpToolCatalog / McpRootPolicy / McpJobs / McpProcessRunner / McpTransports / McpProtocol / McpToken）+ `Commands/McpCommand.cs`；`doctor --json`；见 §9.14 |
| P1-5 macOS 媒体权限 | ✅ **已实现（2026-10-18）** | `MacOSWebviewWindow.cs` `MacUiDelegate.RequestMediaCapturePermission` + `ResolveMediaCapturePermission/CaptureDecisionFor/StrictestCaptureDecision`；`WebviewWindowMacOptions.CameraPermission/MicrophonePermission`（MACOS 原生未本机编译验证） |
| P1-6 notch/NSPanel | ✅ **已实现（2026-10-22）** | `NotchWindowOptions`/`NotchWindow` + `Application.CreateNotchWindow`（映射见 `BuildNotchWindowOptions`）；`WebviewWindowMacOptions.WindowClass/PanelPreferences/CollectionBehavior`；macOS `MacOSWebviewWindow` 创建 `NSPanel`（非激活/浮动/工具窗口位）、`MacNotchBackgroundView` 贝塞尔刘海形状、相机刘海定位与滑入/滑出动画、悬停取焦；见 §10.9 |
| P1-7 GTK app ID/single-instance | ✅ **已实现（2026-10-18）** | `LinuxApplicationId.cs`（id 推导/校验/覆盖 + 程序名）、`LinuxDbusSingleInstance.cs`/`LinuxSingleInstanceNames.cs`（经 `org.freedesktop.DBus.RequestName` 以 `uniqueID+".SingleInstance"` 占 bus name，`DO_NOT_QUEUE`）；`LinuxPlatformApp.AcquireSingleInstanceLock` 占位、`Run()` 经 `GLib.Functions.SetPrgname` 应用程序名；D-Bus 不可用降级锁文件+socket。见 §9.13 |
| P1-8 64MiB→413 | ✅ **已实现（2026-10-18）** | `Transport/HttpTransport.cs` `MaxRuntimeBodyBytes = 64 MiB`；`HandleMessageAsync` Content-Length 预判 + 限量读取 → 413；分块单块 1 MiB → 422 |
| P1-9 scheme 取消 | ✅ **已实现（2026-10-22 复核确认）** | macOS：每 task CTS + `MarkStopped` 取消且不再回调；Windows：`WindowsRequestCancellation.cs`（CDP `Fetch`/`Network` 逐请求中止，`X-Wails-Request-Id` 桥接头）+ 窗口关闭取消；Linux：WebKitGTK 无钩子（平台限制，已注释）；管线令牌已透传。见 §9.13 |
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
3. ~~**P1-8 请求 64MiB 上限 → 413**~~ ✅ 已于 2026-10-18 实现；~~**P1-5 macOS 媒体权限**~~ ✅ 已于 2026-10-18 实现；~~**P1-9 scheme 请求取消/上下文转发**~~ ✅ **已于 2026-10-22 复核确认**（macOS 完整、Windows CDP 逐请求中止、Linux WebKitGTK 无钩子属平台限制）—— 见 §9.10 / §9.13
4. ~~**P1-4 MCP 服务器**~~ ✅ **已于 2026-10-22 实现**（手写最小 JSON-RPC 2.0，未引入 MCP C# SDK），见 §9.14
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

依据 Wails v3 `v3.0.0-beta.28`。本期完成 **P1-8**、**P1-5**；**P1-9** 已补齐（Windows CDP + macOS task 取消，Linux WebKitGTK 无钩子属平台限制，见 §9.13）；**P1-4 MCP** 已于 2026-10-22 实现（见 §9.14）。

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

- **构建**：`Wails.Net.Application`、`.Windows`、`.Linux`、`.MacOS`、`Wails.Net.Cli`、`Wails.Net.Application.Tests`、`.Windows.Tests` 及**全部 40 个 `Wails.Net.Plugins.*` 项目**均 **0 错误**（Windows 存在既有 `MSB3277` WindowsBase 版本冲突警告，与本批无关）。**Android 项目未构建**（本机未安装 `android` 工作负载）。
- **测试**：`Wails.Net.Application.Tests` 全量 **1558 项、0 失败**（较本批前 +18 项；并修复既有 17 项 `Plugins` 失败，见 §9.13.6）；`Wails.Net.Cli.Tests` 488 项（**485 通过 / 3 跳过 / 0 失败**）；`Wails.Net.Application.Windows.Tests` 179 项（**46 通过 / 133 跳过 / 0 失败**）；`Wails.Net.Application.Linux.Tests` 纯逻辑用例通过（2 项 `PlatformFactoryLinuxTests` 失败系 **Windows 主机环境**所致，需在 Linux/WSL 运行）。
- **未验证**：dev 资产代理端到端（真实 vite + GUI 会话）；Windows CDP 端到端（无 GUI 会话）；macOS Dock bounce 实跑、`NSUrl.Query`（无 mac 主机，`#if MACOS` 分支未编译）；Android `Uri.Query`（缺 workload）；D-Bus bus name 实际占位（无 session bus 环境）。

### 9.13.6 修复：MapCommand 的 ICommandContext 注入重载缺失（17 项既有测试失败）

**根因**：`MapCommandExtensions` 缺少 `MapCommand<TResult>(Func<ICommandContext, TResult>, CommandSecurity?)` 重载。凡以 `(Func<ICommandContext, TResult>)…` + `CommandSecurity` 注册的**无业务参数**命令，都被错误绑定到 `MapCommand<T, TResult>(Func<T, TResult>, CommandSecurity?)` —— 把 `ICommandContext` 当作业务参数反序列化，向 `handler` 传入 `null`。命令内使用 `ctx.Services` 者运行期抛 `NullReferenceException`（**生产代码同样受影响，非仅测试**）。

**修复**：在 [MapCommandExtensions.cs](../../src/Wails.Net.Application/Commands/MapCommandExtensions.cs) 补齐该重载。它与既有的 `MapCommand<T, TResult>` 并存；因参数类型更具特异性（首参 `ICommandContext` 为具体类型而非类型参数），C# 重载解析优先选中它，**无 CS0121 歧义**（已全量构建 40 个插件项目验证）。

**修复的 17 项既有失败**：`CameraPluginTests`×2、`GeolocationPluginTests`×2、`BiometricPluginTests`×2、`BarcodeScannerPluginTests`×4、`DpiScalePluginTests`×3、`CliPluginTests`×2、`NfcPluginTests`×2。

**回归测试**：`tests/Wails.Net.Application.Tests/Commands/MapCommandContextInjectionTests.cs`（3 项：同步 / 异步 ctx 注入非 null、注入实例与调用方一致）。

**验证**：`Wails.Net.Application.Tests` 全量 **1558 项、0 失败**；全部 **40 个 `Wails.Net.Plugins.*` 项目 0 错误**。

---

## 9.14 基线再确认与 P1-4 MCP 实现记录（2026-10-22）

### 9.14.1 基线再确认

- 官方 changelog（v3.wails.io/changelog）最新发布仍为 **`v3.0.0-beta.28`（2026-10-05）**，`Unreleased` 段落为空；页面最后更新 2026-10-20，**无 beta.29**。基线保持 beta.28，无需升级。
- ⚠️ **该结论已于 2026-10-31 作废**：beta.29（2026-10-13）起连续发布 beta.30–beta.34，并于 **2026-10-26 发布 `v3.0.0` GA**。当前基线以 **§11** 为准。
- 剩余差异（本次复核后仍未实现/受平台限制）：

| 项 | 状态 | 说明 |
|----|------|------|
| P1-6 macOS notch 通知窗口 + NSPanel 窗口类型（beta.12 #6010/#6008） | ✅ **已实现（2026-10-22）** | `CreateNotchWindow` + `NSPanel`/刘海形状/定位/动画已落地并通过 macOS 参考程序集离线编译；运行时需 mac 主机实机验证。见 §10.9 |
| P2-19 WebView2 自建消息泵 | ⚠️ 未实现 | 交由 WebView2 SDK 管理；超时（60s）与进程故障恢复已对齐 |
| Streams Android 长轮询 | ⚠️ 平台限制 | `ShouldInterceptRequest` 要求同步返回；仅 server 模式 WebSocket 可用，Android 端待后续事件通道方案 |
| 平台长轮询端到端验证 | ⚠️ 未验证 | Windows deferral / Linux 延迟完成 / macOS 延迟 `WKURLSchemeTask` 仅代码审查，未在真实设备/GUI 会话回归 |

> 各平台逐项实现状态与待补齐清单见 **§10 四平台功能矩阵**。

### 9.14.2 P1-4 实现决策

- **不引入 MCP C# SDK**：CLI 支持可选 NativeAOT（`WailsAot=true`），且项目禁止生产代码运行时反射；官方 C# SDK 的约定式序列化与反射会带来 AOT/裁剪风险。改为**手写最小 JSON-RPC 2.0**（initialize / ping / tools/list / tools/call / 通知），仅使用 `System.Text.Json.Nodes`。
- 与 Go 版本一致：CLI MCP 服务器管理**项目生命周期**（非运行中的 WebView）；stdio 模式 stdout 专用于协议流，诊断信息写 stderr；交互式运行自动选择 loopback Streamable HTTP。

### 9.14.3 交付内容

| 层 | 内容 | 位置 |
|----|------|------|
| 命令 | `wails mcp [--token] [--root] [--http] [--stdio] [--port]`；`--http`/`--stdio` 互斥；无终端时自动 stdio，交互式自动 HTTP | [McpCommand.cs](../../src/Wails.Net.Cli/Commands/McpCommand.cs) |
| 协议 | JSON-RPC 2.0 常量、版本协商（`2024-11-05` / `2025-03-26` / `2025-06-18`，未知回退最新）、错误码、UTF-8 非转义序列化 | [McpProtocol.cs](../../src/Wails.Net.Cli/Mcp/McpProtocol.cs) |
| 令牌 | 32 字节随机 Base64Url（43 字符）；`CryptographicOperations.FixedTimeEquals` 常量时间比较；`--token` → `WAILS_MCP_TOKEN` → 随机 | [McpToken.cs](../../src/Wails.Net.Cli/Mcp/McpToken.cs) |
| 工具 | 8 个：`wails_project_inspect` / `wails_project_new` / `wails_project_doctor` / `wails_project_build` / `wails_project_dev_start` / `wails_project_generate_bindings` / `wails_job_status` / `wails_job_stop`；含 JSON Schema 与 `readOnlyHint`/`openWorldHint`/`destructiveHint` 注解 | [McpToolCatalog.cs](../../src/Wails.Net.Cli/Mcp/McpToolCatalog.cs) |
| 分发 | initialize（含 instructions 携带 root + token）、ping、tools/list、tools/call；工具异常转为 `isError=true` 的文本结果；未知方法/工具返回 JSON-RPC 错误 | [McpServer.cs](../../src/Wails.Net.Cli/Mcp/McpServer.cs) |
| 安全 | 根目录解析（绝对化 + 符号链接最长前缀解析）；路径越界拒绝；路径标志（`-p/--project`、`-o/--output`、`-d/--directory` 等）限根；参数上限 64 条 / 4096 字符 / 禁 NUL；HTTP 强制 loopback Host + `Origin`（仅 http + loopback）、Bearer 校验、请求体 4 MiB 上限 | [McpRootPolicy.cs](../../src/Wails.Net.Cli/Mcp/McpRootPolicy.cs)、[McpTransports.cs](../../src/Wails.Net.Cli/Mcp/McpTransports.cs) |
| 任务 | 异步子进程任务（`build`/`dev`/`generate`），输出上限 2 MiB、任务上限 32 并回收最旧已完成任务；`stopping` → `stopped`，失败/成功分别 `failed`/`completed`；停止时终止进程树 | [McpJobs.cs](../../src/Wails.Net.Cli/Mcp/McpJobs.cs)、[McpProcessRunner.cs](../../src/Wails.Net.Cli/Mcp/McpProcessRunner.cs) |
| 传输 | stdio（按行 JSON-RPC，写入门控保证响应不交错）；HTTP（`POST /mcp`，非 POST → 405 + `Allow`，非 `/mcp` → 404，未授权 → 401 + `WWW-Authenticate`，非 loopback → 403，通知 → 202，正常 → 200 JSON） | [McpTransports.cs](../../src/Wails.Net.Cli/Mcp/McpTransports.cs) |
| 诊断 | `doctor --json`（`ok` / `checks[]` / `summary`，UTF-8 非转义），供 `wails_project_doctor` 子进程调用 | [DoctorCommand.cs](../../src/Wails.Net.Cli/Commands/DoctorCommand.cs) |
| 编码 | 标准输出被重定向时统一 UTF-8（修复 Windows 代码页导致子进程中文输出乱码）；子进程 stdout/stderr 显式按 UTF-8 读取 | [Program.cs](../../src/Wails.Net.Cli/Program.cs)、[McpProcessRunner.cs](../../src/Wails.Net.Cli/Mcp/McpProcessRunner.cs) |

### 9.14.4 与 Go 版本的差异

- Go 使用 `modelcontextprotocol/go-sdk` 的 `StdioTransport` 与 `StreamableHTTPHandler`（SSE / session 支持）；本实现为**无状态 JSON 响应**模式，不支持 `GET /mcp` 的服务端推送流（返回 405）。工具调用、初始化、通知语义一致。
- 工具命名与语义对齐，命令映射按 .NET CLI：`init → new`、`generate bindings → generate`、`task → 未提供`（.NET 无 Taskfile，改由 `build`/`dev` + `args` 覆盖）。
- Go 的 `wails3 task ...` 工具未移植；`args` 仍受路径根限制与参数上限约束。

### 9.14.5 测试与验证

- 新增 **43 项** TUnit 测试：`McpProtocolTests`（13：令牌 / 版本协商 / 信封 / loopback Host·Origin / 端口 / 令牌解析 / doctor JSON）、`McpRootPolicyTests`（14：根解析 / 路径越界 / 符号链接前缀 / 参数上限与路径标志）、`McpServerTests`（16：初始化 / 通知 / ping / 未知方法 / 解析错误 / 工具清单 / inspect / 越界 / 授权 / 任务状态机 / 任务上限回收 / 输出截断）。
- `Wails.Net.Cli.Tests` 全量 **531 项：528 通过 / 3 跳过 / 0 失败**；CLI 构建 **0 警告 0 错误**。
- 手工冒烟：stdio 初始化 / tools/list / inspect / doctor / build（返回 jobId；错误令牌拒绝）全部正常；HTTP `POST /mcp` 200、错误 Bearer 401、非 loopback Origin 403（当前 shell 非管理员，loopback `HttpListener` 可用）。
- 未覆盖：真实 MCP 客户端（Claude Desktop / Cursor）端到端握手；`wails_project_new` 的完整脚手架调用（子进程链路已由 doctor 覆盖）；HTTP 在启用严格 URL ACL 的 Windows 主机上可能需要 `netsh http add urlacl`（Go 使用裸 `net.Listen`，无此限制）。

---

## 10. 四平台功能矩阵（2026-10-22）

> 依据仓库源码逐项核对（`src/Wails.Net.Application.{Windows,Linux,MacOS,Android}/`、核心 `src/Wails.Net.Application/`）。
> 仅描述"代码是否提供实现"，不替代真实设备/GUI 回归；macOS 原生分支（`#if MACOS`）未在本机编译验证，Linux GUI 运行时行为未在 WSL 实机验证。
>
> 图例：**✅ 已实现** · **◐ 部分/受限** · **❌ 未实现** · **— 平台不适用**

### 10.1 核心运行时与生命周期

| 功能 | Windows | Linux | macOS | Android |
|------|---------|-------|-------|---------|
| 应用主循环 / 生命周期 | ✅ `WindowsPlatformApp` | ✅ `LinuxPlatformApp`（GLib.MainLoop） | ✅ `MacOSPlatformApp`（NSApplication） | ✅ `AndroidPlatformApp`（Activity 回调） |
| 优雅关闭（`ShutdownAsync`） | ✅ | ✅ | ✅ | ✅ |
| 单实例锁 + 参数转发 | ✅ Win32 Mutex + 消息窗口 | ✅ D-Bus `RequestName`（降级锁文件+socket） | ✅ 文件锁 + `NSDistributedNotificationCenter` | — 系统单进程，恒返回 true |
| 默认信号处理器（SIGINT/SIGTERM） | ✅ | ✅ | ✅ | — 移动端不适用 |
| 隐藏 / 显示应用 | ✅ | ✅ | ✅ | ◐ no-op（由系统管理） |
| 应用图标 | ✅ | ✅ | ✅ | ❌ 由 `AndroidManifest.xml` 配置 |
| 关于对话框 | ✅ WinForms | ✅ GTK4 | ✅ AppKit | ✅ AlertDialog |
| 应用菜单 | ✅ `Win32Menu` | ✅ `LinuxMenu` | ✅ `MacOSMenu` | — 无应用菜单概念 |
| 屏幕枚举 | ✅ WinForms Screen | ✅ Gdk.Display | ✅ NSScreen | ✅ DisplayManager |
| 暗色模式检测 | ✅ 注册表 `AppsUseLightTheme` | ✅ gsettings `color-scheme` | ✅ `NSAppearance` | ✅ `UiMode` |
| 系统强调色 | ✅ DWM `AccentColor` | ✅ gsettings | ✅ `NSColor.ControlAccent` | ❌ 返回空字符串 |
| 主题变化事件 | ✅ `PlatformMessageWindow` | ✅ `LinuxSystemEventWatcher` | ✅ NSAppearance KVO | ◐ 事件映射存在，依赖宿主 Activity 触发 |
| 原生父窗口嵌入（`SetParent`） | ✅ Win32 `SetParent` | ❌ 默认 no-op | — 无该概念 | — 无该概念 |

### 10.2 窗口系统

| 功能 | Windows | Linux | macOS | Android |
|------|---------|-------|-------|---------|
| 创建/显示/隐藏/关闭/聚焦 | ✅ | ✅ | ✅ | ✅ |
| 尺寸/位置/最小·最大尺寸 | ✅ | ✅ | ✅ | ◐ 仅缓存，实际由系统管理 |
| 最大化/最小化/恢复/全屏 | ✅ | ✅ | ✅ | — 由 Activity 全屏策略管理 |
| 置顶（AlwaysOnTop） | ✅ | ✅ | ✅ | — 无该概念 |
| 无边框 / 标题栏样式 | ✅ | ✅ | ✅ | ◐ no-op |
| 半透明 / 不透明度 | ✅ | ✅ | ✅ | ❌ |
| 窗口效果（Mica/Acrylic/Vibrancy） | ✅ `SetEffects` | ❌ | ❌ | ❌ |
| 窗口阴影 | ◐ 不支持单独控制 | ✅ | ✅ | — |
| 任务栏进度 / 覆盖图标 | ✅ `ITaskbarList3` | ◐ Unity LauncherEntry（D-Bus，兼容 Unity/Plasma/扩展） | ✅ Dock 进度（`NSDockTile.ContentView` + 自定义进度视图） | — |
| 徽章（Badge） | ✅ | ◐ Unity LauncherEntry `count`（仅数字） | ✅ `NSDockTile.BadgeLabel` | — |
| 任务栏/Dock 提醒（Flash） | ✅ | ❌ | ✅ Dock bounce | — |
| 跳过任务栏 / 所有工作区 | ✅ | ❌ | ✅ `CanJoinAllSpaces` | — |
| 模态窗口（AttachAsModal） | ✅ | ✅ | ✅ sheet 形式（`BeginSheet`） | ❌ no-op |
| 窗口拖动 / 调整（StartDrag/StartResize） | ✅ | ✅ | ◐ `StartDrag` 已实现（`PerformWindowDrag`），`StartResize` 系统原生 | ❌ no-op |
| 文件拖放（WindowFileDropped） | ✅ | ✅ Gtk.DropTarget | ✅ `MacDropView`（`NSDraggingDestination`） | ❌ |
| 截图（CapturePreview） | ✅ | ◐ 返回 null | ✅ `WKWebView.TakeSnapshot` | ❌ |
| 打印 | ✅ | ◐ WebKitGTK 打印受限 | ✅ `NSPrintOperation` | ✅ `PrintManager`（系统打印/另存为 PDF） |
| 导出 PDF | ✅ | ◐ 依赖 GTK 打印对话框 | ✅ `WKWebView.CreatePdf` | ◐ 仅系统“另存为 PDF”，无 UI 导出不支持 |
| 内容保护（防截屏） | ✅ | ✅ | ✅ | — 由 Activity `FLAG_SECURE` 管理 |
| 窗口级菜单栏 | ✅ | ✅ | — 由应用菜单统一管理 | — |
| 上下文菜单 | ✅ `Win32ContextMenu` | ✅ `LinuxContextMenu` | ✅ `NSMenu.PopUpMenu`（`MacOSMenu` 复用） | — |
| NSPanel 辅助面板 | — 无该概念 | — 无该概念 | ✅ `WindowClass=Panel` + `PanelPreferences`（非激活/浮动/工具窗口） | — |
| notch 窗口（相机刘海） | — 无该概念 | — 无该概念 | ✅ `Application.CreateNotchWindow`（刘海形状/定位/滑入滑出动画/悬停取焦） | — |

### 10.3 WebView 与传输

| 功能 | Windows | Linux | macOS | Android |
|------|---------|-------|-------|---------|
| URL/HTML 加载、前进/后退/刷新 | ✅ WebView2 | ✅ WebKitGTK | ✅ WKWebView | ✅ Android WebView |
| 执行 JS（ExecJS） | ✅ | ✅ | ✅ | ✅ |
| 注入 CSS | ✅ | ✅ | ✅ | ❌ 无覆盖实现 |
| 缩放 / 缩放级别 | ✅ | ✅ | ✅ | ✅ |
| DevTools | ✅ | ✅ | ✅ | ◐ 仅 `chrome://inspect` 远程调试 |
| 控制台消息 / 原生消息处理器 | ✅ | ❌ | ✅ | ◐ 经 `WebMessageListener`（IPC 通道） |
| 自定义 scheme 注册 | ✅ | ✅ | — `WKURLSchemeHandler` 内建 | — `ShouldInterceptRequest` 内建 |
| 资源服务（AssetServer） | ✅ scheme 处理 | ✅ scheme 处理 | ✅ scheme 处理 | ✅ `AndroidAssetServer` |
| IPC 传输 | ✅ 请求拦截 | ✅ 请求拦截 | ✅ 请求拦截 | ✅ `WailsWebMessageListener` |
| Streams 双向字节流 | ✅ 长轮询 | ✅ 长轮询 | ✅ 长轮询 | ❌ `ShouldInterceptRequest` 同步约束 |
| 自定义 scheme 请求取消 | ✅ CDP `Fetch`/`Network` | ❌ WebKitGTK 无钩子（平台限制） | ✅ `StopUrlSchemeTask` | — |

### 10.4 平台集成服务

| 功能 | Windows | Linux | macOS | Android |
|------|---------|-------|-------|---------|
| 剪贴板：文本 / HTML | ✅ `WindowsClipboard` | ✅ xsel/xclip | ✅ NSPasteboard | ✅ ClipboardManager |
| 剪贴板：图片 | ✅ | ✅ xclip（xsel 回退不支持） | ✅ | ✅ MediaStore content:// URI（API 29+；低版本需存储权限） |
| 剪贴板：文件列表 | ✅ CF_HDROP | ✅ text/uri-list | ✅ NSFilenamesPboardType | ✅ ClipData URI |
| 系统托盘 | ✅ `Win32SystemTray`（light/dark 图标） | ✅ StatusNotifierItem + 隐藏窗口回退 | ✅ `NSStatusItem` | — 无托盘概念 |
| 托盘 tooltip | ✅ | ✅ | ❌ `NSStatusItem` 不支持 | — |
| 全局快捷键 | ✅ `RegisterHotKey` | ✅ X11 `XGrabKey`（`LinuxX11GlobalHotkeys`；原生 Wayland 回退窗口内 `Gtk.ShortcutController`） | ✅ Carbon `RegisterEventHotKey` | — |
| 自启动 | ✅ 注册表（`WindowsAutostartManager`） | ✅ `.desktop`（`LinuxAutostartManager`） | ✅ LaunchAgent（`MacOSAutostartManager`） | — |
| 打开外部浏览器 | ✅ `ShellExecuteW` | ✅ `xdg-open` | ✅ `NSWorkspace` | ✅ `Intent.ActionView` |
| Keychain / 安全存储 | ✅ Credential Manager | ✅ `secret-tool`（Secret Service） | ✅ Keychain Services | ✅ `AndroidKeychain`（Android Keystore AES-GCM + 私有 SharedPreferences） |
| 系统通知（Notification 插件） | ✅ PowerShell Toast | ✅ `notify-send` / `dbus-send` | ✅ `osascript`（AppleScript `display notification`） | ✅ `NotificationManager`（渠道 + 待处理 Intent；API 33+ 需 `POST_NOTIFICATIONS`） |
| 移动端能力（Haptics/Biometric/NFC/扫码/定位/相机/权限） | — | — | — | ✅ `Android.*` 实现 |
| 媒体权限（摄像头/麦克风） | — WebView2 原生提示 | — WebKitGTK 原生提示 | ✅ `CameraPermission/MicrophonePermission` + WKUIDelegate | ◐ 宿主自行处理运行时权限 |

> 备注：`PlatformCapabilities.HasNativeDrag` 在四个平台均返回 true（Windows/macOS/Linux/Android 各自按原生能力声明）；macOS 已通过 `MacDropView` 接入窗口级 `WindowFileDropped` 事件。

### 10.5 构建与打包

| 功能 | Windows | Linux | macOS | Android |
|------|---------|-------|-------|---------|
| 构建目标 | ✅ `win-x64` 等 | ✅ `linux-x64` 等 | ✅ `osx-*`（需 mac 主机） | ✅ `android-arm64`（默认） |
| 打包格式 | ✅ MSIX/appx、NSIS、zip | ✅ deb、rpm、AppImage、targz | ✅ zip/targz + `.app` 输出准备（无 dmg/pkg） | ✅ APK（Android 工作负载产出） |

### 10.6 各平台待补齐清单

| 平台 | 缺口（按影响排序） |
|------|-------------------|
| **macOS** | 无阻断项。打印/PDF、截图、`StartDrag`、模态 sheet、Dock 徽章、Dock 进度、上下文菜单、文件拖放、NSPanel、notch 窗口、系统通知均已实现（macOS 参考程序集离线编译通过；运行时需 mac 主机实机验证） |
| **Linux** | 残留平台限制：① 窗口效果（Mica 等价物；GTK4 无模糊/亚克力 API）；② scheme 请求取消（WebKitGTK 无钩子）；③ PDF 导出需在真实 GTK/WebKitGTK 桌面回归。已完成：Keychain（`secret-tool`）、X11 全局快捷键（`XGrabKey`）、启动器进度/徽章（Unity LauncherEntry D-Bus） |
| **Android** | 残留平台限制：① Streams 双向字节流（`ShouldInterceptRequest` 同步约束，需事件通道方案）；② 剪贴板图片在 API 29 以下需存储权限（当前 no-op）；③ PDF 无 UI 导出（仅系统“另存为 PDF”）；④ 通知需宿主声明 `POST_NOTIFICATIONS` 权限。已完成：剪贴板图片（MediaStore）、Android Keystore、系统通知插件、打印（`PrintManager`）。桌面窗口语义（最大化/最小化/置顶/托盘/菜单）平台不适用 |
| **Windows** | 无阻断项；窗口阴影不支持单独控制（`Win32WebviewWindow.cs:3243`，系统自动加阴影） |

> 说明：上表"平台不适用"的项不需要移植；"未实现"项可按列内顺序排期。macOS 相关缺口需在 mac 主机编译并实机验证；Linux 缺口需在 X11 与 Wayland 两种后端分别回归。

### 10.7 按清单补全记录（第一批，2026-10-22）

| 平台 | 项 | 实现 | 验证 |
|------|----|------|------|
| macOS | 打印 | `MacOSWebviewWindow.Print` → `NSPrintOperation.FromView(_webView).RunOperation()` | 仅非 macOS 分支编译通过；原生分支未在本机编译 |
| macOS | 导出 PDF | `MacOSWebviewWindow.PrintToPDF(path)` → `WKWebView.CreatePdf` 回调写盘 | 同上 |
| macOS | 截图 | 新增 `CapturePreviewAsync` → `WKWebView.TakeSnapshot` + `NSBitmapImageRep` 转 PNG | 同上 |
| macOS | 无边框拖动 | `StartDrag` → `NSWindow.PerformWindowDrag(NSApplication.CurrentEvent)` | 同上 |
| macOS | 模态窗口 | `AttachAsModal` → 静态 `NativeWindows` 映射 + `NSWindow.BeginSheet` | 同上 |
| macOS | Dock 徽章 | `SetBadgeCount` / `SetBadgeLabel` → `NSDockTile.BadgeLabel` | 同上 |
| macOS | 所有工作区 | `SetVisibleOnAllWorkspaces` → `NSWindowCollectionBehavior.CanJoinAllSpaces` | 同上 |
| Linux | Keychain | 新增 `LinuxKeychain`（`secret-tool` store/lookup/clear，参数经 `ArgumentList` 直传，可注入运行器） | `LinuxKeychainTests` 8/8 通过 |
| 跨平台 | 系统通知（macOS） | `NotificationService` 新增 `osascript` 分支 + AppleScript 转义（引号/反斜杠/换行） | `NotificationServiceTests` 9/9 通过 |

**构建**：`Wails.Net.Application.Linux`、`Wails.Net.Plugins.Notification`、`Wails.Net.Application.MacOS`（非 macOS 分支）、`Wails.Net.Application.Linux.Tests`、`Wails.Net.Application.Tests` 均 0 警告 0 错误。

**说明**：第一批实现完成后，macOS 原生分支（`#if MACOS`）仍未在本机（Windows）参与正式构建；第二批已引入 `.NET 10` macOS 参考程序集（`Microsoft.macOS.Ref.net10.0_26.0` 26.0.11017）做离线编译校验，并修复了 macOS 平台项目的全部编译错误（详见 §10.8）。运行时行为仍需在 mac 主机实机验证。Android 项需在安装 `android` 工作负载后实施。

### 10.8 按清单补全记录（第二批，2026-10-22）

| 平台 | 项 | 实现 | 验证 |
|------|----|------|------|
| macOS | 上下文菜单 | `MacOSWebviewWindow.OpenContextMenu` → `MacOSMenu` 构建 `NSMenu` + `NSMenu.PopUpMenu(null, 点, 内容视图)`（AppKit 坐标转换 `ConvertPointToView`） | macOS 目标离线编译通过 |
| macOS | 文件拖放 | 新增 `MacDropView : NSView`（`RegisterForDraggedTypes("public.file-url")`、`DraggingEntered/DraggingUpdated/PerformDragOperation`），`SetFileDropEnabled` 控制注册状态，`HandleFilesDropped` 发出 `KnownEvents.WindowFileDropped` | 同上 |
| macOS | 编译修复 | 内容视图改为 `MacDropView`；`NSWindow.Opaque`→`IsOpaque`；`NSImage.FromData`→`new NSImage(NSData)`；`NSPasteboardType` 枚举 + `NSPasteboardTypeExtensions.GetConstant`；`NSEventModifierMask.*KeyMask`；`NSMenu.AutoEnablesItems`；`NSCellStateValue`；`NSWindowStyle.FullScreenWindow`；`NSWindowLevel` 强类型赋值；`NSUrlRequest.Headers`；`NSAppearance.GetAppearance(NSString)`；`UTType.CreateFromExtension` 替代弃用的 `AllowedFileTypes`；`NSColor.ControlAccent`/`UsingColorSpace`；`ActivateIgnoringOtherApps`→`Activate()`；`NSAlert` 按钮返回码常量；Carbon 事件处理改用委托（移除 `[UnmanagedCallersOnly]`）；`SecKeyChain.QueryAsData` 三参重载；`ObserveValue` 改为 `override` | `.NET 10 macOS` 参考程序集离线编译 0 警告 0 错误 |
| macOS | Keychain 命名空间 | `MacOSKeychain` 使用 `global::Security.*`，避免与 `Wails.Net.Application.Security` 命名空间冲突 | 同上 |

**构建**：`Wails.Net.Application.MacOS`（net10.0 骨架）0 警告 0 错误；macOS 原生分支经临时工程（`AssemblyName=Wails.Net.Application.MacOS`、`DefineConstants=MACOS`、引用 `Microsoft.macOS.Ref.net10.0_26.0`）全量编译 0 警告 0 错误。

**说明**：离线编译校验使用与本机 `.NET 10 SDK` 工作负载清单一致的 macOS 26.0 参考程序集，可校验 API 名称与签名；但 `P/Invoke`、AppKit 运行时行为、以及 `net10.0-macos` 目标上的 AOT/裁剪仍需 mac 主机验证。

### 10.9 按清单补全记录（第三批，2026-10-22）

| 平台 | 项 | 实现 | 验证 |
|------|----|------|------|
| macOS | Dock 进度 | `MacOSWebviewWindow.SetTaskbarProgress` → `NSDockTile.ContentView` + 新增 `MacDockProgressView`（绘制应用图标 + 圆角进度条；`Error` 红色、`Paused` 橙色、`Indeterminate` 固定 35%、`None` 清除视图） | macOS 目标离线编译通过 |
| macOS | NSPanel 窗口类 | `WebviewWindowMacOptions.WindowClass`（0=NSWindow、1=NSPanel）、`PanelPreferences`（`NonActivating`/`FloatingPanel`/`BecomesKeyOnlyIfNeeded`/`UtilityWindow`）、`CollectionBehavior`；`Create()` 按 `WindowClass` 实例化 `NSPanel` 并应用样式位与面板行为 | 同上 |
| macOS | notch 窗口 | 新增 `NotchWindowOptions`、`NotchWindow`、`Application.CreateNotchWindow`、内部 `NotchWindowInfo`；`BuildNotchWindowOptions` 复刻 Go `webviewOptionsForNotchWindow`（660×92 默认、水平 30pt/底部 12pt 边距、透明无边框、`PopUpMenu` 层级、跨 Space 行为）；平台侧 `MacNotchBackgroundView` 贝塞尔刘海形状、相机刘海定位（`auxiliaryTopLeftArea/RightArea` + `safeAreaInsets`）、滑入/滑出动画（`NSAnimationContext` + KVC `animator`）、悬停取焦、窗口关闭时移除跟踪区域 | 同上；`NotchWindowTests` 5/5 通过 |
| macOS | 窗口层级修正 | `WindowLevel` 序号（0..7）改为显式映射到 `NSWindowLevel`（原实现直接强转，2..7 会落到无效枚举值） | 同上 |

**构建**：`Wails.Net.Application`、`Wails.Net.Application.MacOS`（net10.0 骨架）、macOS 原生分支离线编译均 0 警告 0 错误；`Wails.Net.Application.Tests` 构建通过，新增 `NotchWindowTests` 5 项全部通过。

**说明**：notch 的动画在 AppKit 中通过 `NSWindow.Animator`（绑定为 `NSObject`）上的 KVC 键 `frame`/`alphaValue` 驱动，已按 Go 侧时长与缓动近似实现；显示器/刘海几何与 NSPanel 非激活行为需在带刘海的 mac 真机回归。

### 10.10 按清单补全记录（第四批：Linux，2026-10-22）

| 平台 | 项 | 实现 | 验证 |
|------|----|------|------|
| Linux | X11 全局快捷键 | 新增 `LinuxX11GlobalHotkeys`：`XGrabKey` 系统级抓取（`XInitThreads` 跨线程、CapsLock/NumLock 组合、临时替换 Xlib 错误处理器吞掉 BadAccess、后台事件线程 + `GLib.IdleAdd` 回主线程）；`LinuxKeyBindingManager` 优先全局注册，X11 不可用（原生 Wayland/无 DISPLAY/键被占用）时回退窗口内 `Gtk.ShortcutController` | `LinuxX11GlobalHotkeysTests` 7/7 通过；Linux 构建 0 警告 0 错误 |
| Linux | 启动器进度 / 徽章 | 新增 `LinuxLauncherEntry`：向 session bus 广播 `com.canonical.Unity.LauncherEntry.Update (s a{sv})`，`LinuxWebviewWindow` 实现 `SetTaskbarProgress`（0..1，`Indeterminate=-1`）、`SetBadgeCount`/`SetBadgeLabel`（数字计数）；appId 取自 `LinuxPlatformApp.ApplicationId`，不可用时按应用名推导 | `LinuxLauncherEntryTests` 8/8 通过；Linux 构建 0 警告 0 错误 |
| Linux | 平台限制确认 | 窗口效果（GTK4 无模糊/亚克力 API）、scheme 请求取消（WebKitGTK 无钩子）保持文档化限制；PDF 导出逻辑维持 WebKitGTK `PrintOperation` + Print-to-File，需真实桌面回归 | — |

**构建**：`Wails.Net.Application.Linux`、`Wails.Net.Application.Linux.Tests` 均 0 警告 0 错误；新增 15 项纯逻辑测试全部通过。

**说明**：Linux 原生行为（XGrabKey 抓取、Unity LauncherEntry 信号、PDF 导出对话框）需在 X11 与 Wayland 两种后端的真实桌面回归；Wayland 下全局快捷键自动回退窗口内 GTK 快捷键（xdg-desktop-portal GlobalShortcuts 需用户确认会话，本批未启用）。

### 10.11 按清单补全记录（第五批：Android，2026-10-22）

| 平台 | 项 | 实现 | 验证 |
|------|----|------|------|
| Android | 剪贴板图片 | `AndroidClipboard.SetImage` 写入 MediaStore（`IS_PENDING` 两阶段，API 29+）后以 `content://` URI 放入 `ClipData`；`GetImage` 经 `ContentResolver.OpenInputStream` 读取首个 URI | Android 参考程序集离线编译通过 |
| Android | Keychain | 新增 `AndroidKeychain`：`AndroidKeyStore` 按 service+account 生成 AES-256-GCM 密钥（别名取 SHA-256），密文与 IV 以 Base64 写入私有 SharedPreferences；删除时同时清理密钥条目 | 同上 |
| Android | 系统通知 | Notification 插件新增 `net10.0-android36.0` 目标（`WailsNetEnableAndroid=true` 时启用）；`SendAndroidNotification` 使用 `NotificationChannel` + `Notification.Builder` + 启动 `PendingIntent`，API 33+ 检查 `POST_NOTIFICATIONS` | 插件 net10.0 目标构建 0 警告 0 错误；Android 分支离线编译通过 |
| Android | 打印 | `AndroidWebviewWindow.Print` → `WebView.CreatePrintDocumentAdapter` + `PrintManager.Print`（系统打印界面，含“另存为 PDF”） | Android 离线编译通过 |
| Android | 平台限制确认 | Streams（同步拦截约束）、PDF 无 UI 导出（回调绑定仅支持派生）、API<29 剪贴板图片（需存储权限）保持文档化限制 | — |

**构建**：Android 源码经 `Microsoft.Android.Ref.36`（36.1.118）离线编译 0 警告 0 错误；`Wails.Net.Plugins.Notification`（net10.0）0 警告 0 错误。

**说明**：本机未安装 `android` 工作负载（`dotnet workload list` 为空），`net10.0-android36.0` 无法本地构建/运行测试；离线编译使用与工作负载清单一致的参考程序集（`Microsoft.Android.Ref.36`），仅校验 API 名称与签名。设备端行为（MediaStore 剪贴板、Keystore 加解密、通知展示、打印对话框）需在安装工作负载后通过 `tests/Wails.Net.Android.E2E/run-android-e2e.fsx` 回归。

---

## 11. 2026-10-31 基线升级：v3.0.0-beta.28 → v3.0.0（GA）

> 复核方式：GitHub Releases（`wailsapp/wails`、`tauri-apps/tauri`）+ npm registry（`@wailsio/runtime`、`@tauri-apps/api`）+ 本仓库 `src/` 代码逐项确认。**未做 v3.0.0 tag 源码逐行复核**（本地参考源码目录 `F:\Code\Dotnet\wails-v3-ref` 不存在），下表标 ❓ 的项为依据 changelog 与本仓库代码的初步判断，落地前需对照 tag 源码确认。

### 11.1 版本现状（2026-10-31）

| 项目 | 当前版本 | 发布日期 | 说明 |
|------|----------|----------|------|
| Wails v3（基线） | **`v3.0.0`**（GA） | 2026-10-26 | beta.28（2026-10-05）后连续发布 beta.29–beta.34 |
| Wails 预发布 | `v3.0.0-beta.29` … `v3.0.0-beta.34` | 2026-10-13 … 2026-10-20 | beta.31/GA 含安全修复，beta.30 及更早应升级 |
| Wails nightly | `v3.1.0-nightly.20261028` | 2026-10-28 | 预览下一个 weekly，不作基线 |
| `@wailsio/runtime` | `3.0.0` | 与 Wails 发布一致 | GA 起固定同版本号，生产需 pin 精确版本 |
| Tauri v2（对比基线） | **`tauri-v2.12.5`** | 2026-10-21 | npm `@tauri-apps/api` 为 `2.12.4` |

**发布节奏（GA 起）**：每周 bump minor（`v3.1.0`、`v3.2.0` …）→ 每日 `v3.X.0-nightly.YYYYMMDD` 预发布 → hotfix 走 patch。Go `@latest` 只解析 weekly/hotfix，**基线维护以 weekly/hotfix 为准，不跟 nightly**。同时 GA 明确：**Linux 默认 GTK4 + WebKitGTK 6.0**，GTK3 仅保留到 v3.1（Wails.Net 已用 `GirCore.Gtk-4.0` / `GirCore.WebKit-6.0`，`Directory.Packages.props` 第 44–47 行，方向一致，无迁移负担）。

### 11.2 beta.28 → v3.0.0 差异清单与 Wails.Net 现状

| # | 变更（版本 / PR） | Wails v3 内容 | Wails.Net 现状 | 建议 |
|---|------------------|---------------|----------------|------|
| 1 | Server 模式 IPC 收紧（beta.31 安全） | `/wails/runtime` 仅接受 **POST**，且仅服务自身 origin 或 `WebSocketOriginPatterns` 白名单内的 origin；其他方法 405、其他浏览器来源 403 | ✅ 已实现：新增 `IpcRequestGuard`（[IpcRequestGuard.cs](../../src/Wails.Net.Application/Security/IpcRequestGuard.cs)），Server 模式在 [IpcTransportStrategy.cs](../../src/Wails.Net.Application/Transport/IpcTransportStrategy.cs) / [ServerModeTransportExtensions.cs](../../src/Wails.Net.Application/Hosting/ServerModeTransportExtensions.cs) 装配；`/wails/message` 非 POST → 405 + `Allow: POST`，Origin 非同源/非白名单 → 403，无 Origin 时按 `Sec-Fetch-Site` 判定（[HttpTransport.cs](../../src/Wails.Net.Application/Transport/HttpTransport.cs)） | 已完成（2026-10-31） |
| 2 | Server 模式 DNS rebinding 防护（beta.31 安全） | 绑定 loopback 时，`Host` 头非 localhost/IP/绑定主机/白名单 → 403 | ✅ 已实现：`IpcRequestGuard.IsHostAllowed` 对齐 Go `serverHostAllowed`（仅 loopback 绑定启用，放行空 Host / localhost / 任意 IP 字面量 / 绑定主机 / 白名单模式；端口不参与判定），HTTP 与 WebSocket 传输层入口均校验（403）。规则细节见 §11.5 | 已完成（2026-10-31） |
| 3 | Linux 跨源子帧 `wails:` 消息拦截（v3.0.0 安全，缓解 GHSA-2p5f-2gx8-mh8m） | GTK3/GTK4 向所有帧注入 guard 脚本 | ✅ 已实现（2026-11-01）：[LinuxFrameGuard.cs](../../src/Wails.Net.Application.Linux/LinuxFrameGuard.cs) 守卫脚本经 `UserContentManager.AddScript(AllFrames, DocumentStart)` **先于**运行时标志注入——顶层帧/同源子帧标记 `__wailsFrameTrusted=true`，其余帧标记 false 并拦截 `_wails` 写入与 `fetch`/`XHR`/`sendBeacon` 到 `wails:` 端点；运行时标志脚本由 `WrapRuntimeScript` 包裹，不可信帧不注入。原生侧 [LinuxIpcOriginGuard.cs](../../src/Wails.Net.Application.Linux/LinuxIpcOriginGuard.cs) 在 `OnWailsSchemeRequest` 校验 IPC（`POST /wails/*`）的 `Origin` 与窗口同源（默认端口归一、`Origin: null` 仅在 wails 页面且非跨源上下文时容忍、无 Origin 时按 `Sec-Fetch-Site` 拒绝 cross-site），不通过返回 403。**平台限制**：WebKitGTK 的 `URISchemeRequest` 不暴露发起帧，且旧版本可能不透传 `Origin`（此时放行并由脚本守卫兜底）；同源子帧无法（也无需）区分——与上游结论一致 | 已完成（2026-11-01） |
| 4 | Windows 问题对话框自定义按钮 + 回调（beta.33/beta.34 #5794） | 自定义按钮走 TaskDialog，comctl32 v6 不可用时回退 MessageBox + 位置映射回调 | ✅ 已实现（2026-11-01）：新增 [MessageDialogOptions.cs](../../src/Wails.Net.Application/Dialogs/MessageDialogOptions.cs)（`MessageDialogButton.Label/IsDefault/IsCancel/Callback`），`IPlatformApp` / `IDialogManager` 以接口默认方法提供 options 重载（非 Windows 平台自动获得「标签透传 + 回调」）；Windows 由 [Win32TaskDialog.cs](../../src/Wails.Net.Application.Windows/Win32TaskDialog.cs) 调用 `TaskDialogIndirect`（按钮 ID 1000+i、`TDF_ALLOW_DIALOG_CANCELLATION`、TDN_CREATED 置顶），失败回退 MessageBox 并按 Go `messageBoxButtonFor` 的标签匹配 + Yes/No/Cancel 位置回退；[DialogPlugin.cs](../../src/Wails.Net.Plugins.Dialog/DialogPlugin.cs) 的 `dialog.question` 支持可选 `buttons`（默认 `["Yes","No"]，末按钮视为取消），[index.ts](../../packages/wails-net-plugin-dialog/src/index.ts) 同步签名 | 已完成（2026-11-01） |
| 5 | dev 就绪探测改 TCP（beta.33 #6209） | 自动前端就绪闸门由 HTTP GET 改 TCP 探测（旧实现无 `Accept` 头被 Vite 404，`wails3 dev` 卡 60s） | ✅ 已实现（2026-10-31）：[FrontendDevServerReadiness.cs](../../src/Wails.Net.Cli/Build/FrontendDevServerReadiness.cs) 改为 `TryCreateTarget`（URL→`host:port`，对齐 Go `frontendTCPTarget`：仅 http/https、无片段、缺省端口 80/443）+ `ProbeTcpOnceAsync`（连接成功即就绪，不再发 HTTP 请求、不受 404 影响）；[DevCommand.cs](../../src/Wails.Net.Cli/Commands/DevCommand.cs) 以「TCP 探测 + 60s/250ms/单次 5s」等待，未就绪仍放弃启动并清理前端进程 | 已完成（2026-10-31） |
| 6 | 绑定生成 `bindings/events` 名称常量（beta.33 #5645） | 为每个 `application.RegisterEvent` 常量名生成 TS 模块，可直接用于 `Events.On/Emit` | ⚠️ 已有事件**枚举** TS 生成（[EventGenerator.cs](../../src/Wails.Net.Generator/EventGenerator.cs)），无 `RegisterEvent` 名称常量模块 | P2 |
| 7 | Updater 归档解压 zip-slip 加固（beta.31） | `filepath.IsLocal` + 拼接路径包含性校验，覆盖 `..`/反斜杠/绝对路径/符号链接写穿 | ✅ `GetFullPath` + 包含性校验（[UpdateExtractor.cs:135-147](../../src/Wails.Net.Plugins.Updater/Services/Updater/UpdateExtractor.cs)、`:168-180`） | 无需（建议补反斜杠/符号链接用例） |
| 8 | Updater 跨设备 rename 回退（beta.30/beta.31 #6135） | Linux/tmpfs 场景 rename 失败时改为「就近暂存 + 跨文件系统复制」 | ⚠️ [UpdateExtractor.cs:109](../../src/Wails.Net.Plugins.Updater/Services/Updater/UpdateExtractor.cs) 直接 `File.Move(overwrite: true)`，无跨设备回退 | P2 |
| 9 | Updater 暂存安全与替换前校验（v3.0.0 #6350/#6360） | 重启待定时保护暂存文件；替换前校验更新内容，损坏则回滚而非破坏现有安装 | ⚠️ 有签名校验与暂存目录清理（[HelperProcess.cs:171](../../src/Wails.Net.Plugins.Updater/Services/Updater/HelperProcess.cs)），未见替换前完整性校验/回滚 | P2 |
| 10 | Windows `SetMenu` 行为（beta.30 #6103/#6104） | `SetMenu` 未配置菜单栏主题时也重绘菜单栏；`SetMenu(nil)` 为 no-op 而非 panic | ✅ `SetMenu(nil)` 直接返回（[Win32WebviewWindow.cs:2923-2929](../../src/Wails.Net.Application.Windows/Win32WebviewWindow.cs)），`SetMenuHandle` 调 `DrawMenuBar`（`:2191-2200`） | 无需 |
| 11 | `x-wails-window-id` 越界拒绝（beta.31） | HTTP runtime transport 拒绝越界窗口 ID，避免被截断成其他窗口 ID | ❌ 仅有头名常量（[AssetServer.cs:58](../../src/Wails.Net.AssetServer/AssetServer.cs)），未见范围校验 | P2（安全） |
| 12 | Windows WebView2 COM 瞬态错误容错（beta.33 #6302） | 运行时操作对瞬态 COM 错误重试/降级 | ➖ 由 WebView2 SDK 托管调用，无自维护 COM vtable（beta.30/34 的 AddRef/vtable 缺陷不适用） | 观察 |
| 13 | Windows WebView 激活焦点 / Tab 逃逸（beta.34 #3783） | 窗口激活时 WebView 重新获得焦点，Tab 可在原生窗口与 WebView 间往返 | ❓ 需 GUI 会话验证 | P2（GUI 用例） |
| 14 | Windows 菜单绘制崩溃修复（v3.0.0 #6311/#6358） | 修复部分 Windows 构建上 `SetTextColor` 失败 panic；修复 `uxtheme.dll` 卸载后仍使用其函数导致的崩溃 | ❓ [Win32Theme.cs:65-94](../../src/Wails.Net.Application.Windows/Win32Theme.cs) 解析序号后 `FreeLibrary`，缓存函数指针的后续调用需确认安全性 | P2 |
| 15 | Linux 原生菜单先初始化再设置（beta.29 #6233） | 避免在 GTK 初始化前设置原生菜单 | ❓ 需核对 `LinuxSystemTray.SetMenu`（[LinuxSystemTray.cs:297](../../src/Wails.Net.Application.Linux/LinuxSystemTray.cs)）时序 | P3 |
| 16 | Linux DMA-BUF / NVIDIA 判定（beta.31） | Wayland 下仅当 WebKit 实际渲染在 NVIDIA 时才关闭 DMA-BUF | ➖ 对应 Go 的 WebKit 环境变量策略；Wails.Net 未干预渲染后端 | P3（需 Linux 实机） |
| 17 | macOS 修复批次（beta.29–v3.0.0） | 按键盘布局的全局快捷键、`WindowDidUpdate` 噪声（约 120 次/秒空 `evaluateJavaScript`）、菜单分区标题、`SetRelativePosition` 相对工作区、重复 teardown | ❓ macOS 分支存在但无 mac 主机，未编译/未验证 | 远期（随 macOS 平台回归） |
| 18 | `InvokeSync*` panic 阻塞（beta.30 #6108） | 派发回调 panic 时不再永久阻塞调用方 | ➖ 无对应同步调度 API | 无需 |
| 19 | Go 1.26 最低版本（beta.31） | v3 模块声明 `go 1.26.0`，模板同步 | ➖ .NET 运行时无关 | 无需 |
| 20 | 远程模板 URL 校验（beta.31） | `wails3 init -t` 仅接受 `https/http/ssh/git` 与 `user@host:path`，并限制 git 传输 | ➖ CLI 仅内置模板（`--template` 为本地/内置名），无远程 clone 路径 | 无需 |
| 21 | 依赖安全更新（v3.0.0） | `klauspost/compress` 1.18.7、`golang.org/x/image` 0.45.0、Go 1.26.10（crypto/x509）、`vite` ≥ 8.2.4 | ➖ Go 依赖；前端 `vite` 版本需按现有模板/文档自查 | 观察 |
| 22 | `wails3 update cli` 找不到 v3 release（v3.0.0 修复） | 更新 CLI 时正确解析 v3 发布 | ❓ 需核对 `wails update` 命令的版本解析逻辑 | P3 |

### 11.3 本次基线维护变更

| 文件 | 变更 |
|------|------|
| `AGENTS.md` | §1 参考版本 → `v3.0.0`；§1 对比基线 → Tauri `2.12.5`；§1.1.1 参考基线新增周更节奏说明；§8 源码链接 → `tree/v3.0.0`；文末日期 |
| `docs/development/wails-v3-beta-comparison.md` | 顶部基线声明、§1 版本现状、§7 Tauri 2.12.x 基线、§9.14.1 结论作废批注、新增 §11 |
| `docs/README.md` | Wails v3 源码 tag、Tauri 版本 |
| `docs/development/contributing.md` | Wails v3 源码链接 |
| `docs/development/plugin-packaging.md`、`project-structure-and-modes.md` | 参考版本由 beta.4 更新为 v3.0.0（Tauri 2.12.5） |
| `docs/architecture/benchmark-audit-and-unified-sdk-plan.md`、`platform-abstraction.md` | 对标基线 / 参考版本更新为 v3.0.0（Tauri 2.12.5） |
| `docs/comparison-with-tauri2-wails3.md`、`docs/fusion-strategy-comparison.md` | 文档头版本声明更新 |

> 历史文档（`docs/ADR/0001-technology-selection.md` 的 alpha.102 决策上下文、`docs/architecture/refactor-and-hardening-plan.md` 的 2026-08-16 beta.4 审计）保留原始版本号，作为当时决策/审计记录，不回写。

### 11.4 后续待办（按优先级）

1. ✅ **P0 安全（已完成 2026-10-31）**：Server 模式 `/wails/message` 强制 POST + 同源/白名单校验、loopback 绑定 `Host` 白名单（防 DNS rebinding），实现于 `IpcRequestGuard` 并在 `HttpTransport` / `WebSocketTransport` 入口生效；单测 `IpcRequestGuardTests`（15 个）与端到端 `ServerModeRequestHardeningTests`（13 个）全部通过。
2. ✅ **P1（已完成 2026-10-31）**：`FrontendDevServerReadiness` 改为 TCP 探测（对齐 beta.33 #6209）——`TryCreateTarget` 解析 `host:port`（http/https、无片段、缺省 80/443）+ `ProbeTcpOnceAsync` 连接成功即就绪，修正 Vite 404 导致 `wails dev` 等满 60s 的缺陷；`DevCommand` 同步改用 TCP 等待。测试 `FrontendDevServerReadinessTests` **15 项**（含真实 `TcpListener` 连接用例）通过。
3. ✅ **P1（已完成 2026-11-01）**：Windows 问题对话框支持自定义按钮标签与回调——新增 `MessageDialogOptions` / `MessageDialogButton` 与 `IPlatformApp` / `IDialogManager` 的 options 重载（接口默认实现回退到 `string[]` 并触发回调），Windows 优先 `TaskDialogIndirect`（`Win32TaskDialog`），comctl32 v6 不可用或调用失败时回退 MessageBox 并按 Go `messageBoxButtonFor` 的标签匹配 + Yes/No/Cancel 位置回退；`DialogPlugin` 的 `dialog.question` 新增可选 `buttons` 参数（默认 `["Yes","No"]`）。测试 `Win32TaskDialogTests`（14 项）、`MessageDialogOptionsTests`（8 项）、`DialogManagerTests` 新增 2 项全部通过。
4. ✅ **P1（已完成 2026-11-01）**：Linux 子帧 `wails:` 消息来源校验——双层防护：(1) 脚本守卫 `LinuxFrameGuard`（所有帧注入，非顶层/非同源帧禁用 IPC 入口并跳过运行时标志注入）；(2) 原生 `LinuxIpcOriginGuard` 校验 IPC 请求 `Origin` 与窗口当前页面同源（`Origin: null` 仅在 wails 页面且非跨源上下文时容忍，无 Origin 时按 `Sec-Fetch-Site` 兜底），不合规返回 403。**平台限制**：WebKitGTK 不暴露发起帧且旧版本可能不透传 `Origin`，此时退化为脚本守卫；同源子帧无法区分（上游同样未解决）。测试 `LinuxIpcOriginGuardTests`（21 项）与 `LinuxFrameGuardTests`（9 项）在 Windows 上通过（纯逻辑，无 GTK 依赖）；真实 WebKitGTK 行为建议在 WSL/Linux 桌面会话中手动验证。
5. **P2**：绑定生成器新增 `bindings/events` 名称常量模块；Updater 跨设备回退与替换前校验/回滚；`x-wails-window-id` 范围校验；Windows 激活焦点与 Tab 往返用例；`Win32Theme` 的 uxtheme 生命周期确认。

> 基线维护节奏建议：每次 Wails weekly/hotfix 发布后，仅复核 §11.1 版本表 + §11.2 新增条目，历史章节（§1–§10）不再回写。

### 11.5 Host 校验（DNS rebinding 防护）实现说明

对应 Wails v3 `v3.0.0` 的 `v3/pkg/application/host_guard_server.go`（`isLoopbackBindHost` / `serverHostAllowed`），Wails.Net 实现为 [`IpcRequestGuard`](../../src/Wails.Net.Application/Security/IpcRequestGuard.cs)，由 `DefaultIpcTransportStrategy`（[IpcTransportStrategy.cs](../../src/Wails.Net.Application/Transport/IpcTransportStrategy.cs)）与 `UseServerModeTransport`（[ServerModeTransportExtensions.cs](../../src/Wails.Net.Application/Hosting/ServerModeTransportExtensions.cs)）在 **Server 模式**装配到 [`HttpTransport`](../../src/Wails.Net.Application/Transport/HttpTransport.cs) 与 [`WebSocketTransport`](../../src/Wails.Net.Application/Transport/WebSocketTransport.cs)，在路由与 CORS 之前校验，不通过返回 `403 Forbidden: Host not allowed`。

**启用条件**

- 绑定主机（`IpcRequestGuard.BindHost`，默认 `localhost`，与传输层 `http://localhost:{Port}` 前缀一致）为 `localhost` 或 loopback IP 字面量（`127.0.0.1`、`[::1]`）时启用。
- 绑定其它主机（如 `0.0.0.0`、域名）视为有意网络部署，**不做** Host 校验（与 Go `isLoopbackBindHost` 一致）。
- `RequestGuard` 为 `null`（裸构造传输层 / 既有单元测试）时不启用，保持向后兼容。

**放行规则（按判定顺序；端口不参与校验，IPv6 方括号先剥离）**

1. 空 Host —— 浏览器总会发送 Host，仅 HTTP/1.0 客户端会省略；
2. `localhost`（大小写不敏感，可带端口）；
3. 任意 IP 字面量（IPv4 / IPv6）—— IP 无法被 DNS 改写，天然安全；
4. 绑定主机；
5. 命中 `IpcOptions.AllowedOrigins` 模式 —— 模式先剥离 `scheme://` 前缀，随后 `*` 通配分别匹配「带端口 Host」与「不带端口 Host」，大小写不敏感（对应 Go `strings.Cut(pattern, "://")` + `path.Match`）。

**拒绝**

- 其它任何主机名，包括 `evil.example`（rebinding 目标）、`localhost.evil.example`（前缀陷阱）、`localhost.`（尾点变体）；尾点、简写 IP 等拼写变体均不放行。

**与 Origin 校验的边界**

- Host 校验独立于 `IpcOptions.RequireOriginValidation`：即使配置 `RequireOriginValidation=false`（映射 Go `WebSocketAllowAllOrigins`，仅跳过 Origin 校验），loopback 绑定的 Host 校验仍然生效。
- Origin 模式匹配与 Host 模式匹配规则不同：Origin 模式含 `scheme://` 时必须连同 scheme 匹配 `scheme://host:port`（对应 Go `authorizeRuntimeOrigin`），Host 模式则剥离 scheme 后匹配主机名。
- Windows HTTP.SYS 对「Host 与监听前缀不匹配」的请求可能在进入托管层之前直接返回 400；托管层闸门返回 403。两者都表示请求被拒绝，E2E 用例据此断言 `403 或 400`。

**测试覆盖**

- 单元：[IpcRequestGuardTests.cs](../../tests/Wails.Net.Application.Tests/Security/IpcRequestGuardTests.cs)（localhost / IP 字面量 / 空 Host / rebinding 变体 / 白名单 / 非 loopback 绑定共 8 个 Host 用例）。
- 端到端：[ServerModeRequestHardeningTests.cs](../../tests/Wails.Net.Application.Tests/Transport/ServerModeRequestHardeningTests.cs)（HTTP 与 WebSocket 各含伪造 Host 用例，返回 403/400）。

### 11.6 Linux 子帧来源校验（跨源 `wails:` 消息）实现说明

对应 Wails v3 `v3.0.0` 对 GHSA-2p5f-2gx8-mh8m 的缓解（GTK3/GTK4 向所有帧注入 guard 脚本），Wails.Net 分为**两层**：

**第 1 层：脚本守卫（`LinuxFrameGuard`，[LinuxFrameGuard.cs](../../src/Wails.Net.Application.Linux/LinuxFrameGuard.cs)）**

- `LinuxWebviewWindow.InjectRuntimeJs` 通过 `UserContentManager.AddScript(AllFrames, DocumentStart)` 先注册守卫脚本、再注册被 `WrapRuntimeScript` 包裹的运行时标志脚本（WebKit 按注册顺序在同一注入时机执行，故守卫总是先运行）。
- 守卫判定：`window.top === window.self`（顶层帧）或 `window.top.location.origin === window.location.origin`（同源子帧）→ `window.__wailsFrameTrusted = true`；读取顶层 `location` 抛异常（跨源）或其它异常 → `false`。
- 不可信帧处置：写入 `__wailsFrameTrusted=false`；用 `Object.defineProperty(window, '_wails', …)` 拦截运行时标志写入；包装 `fetch` / `XMLHttpRequest.open` / `navigator.sendBeacon`，对 `wails:` 开头的 URL 直接拒绝；运行时标志脚本因 `WrapRuntimeScript` 的条件直接跳过（不暴露平台/端点信息）。
- 守卫脚本缺失时（`__wailsFrameTrusted === undefined`）按可信处理，与非守卫注入路径行为一致（向后兼容）。

**第 2 层：原生请求校验（`LinuxIpcOriginGuard`，[LinuxIpcOriginGuard.cs](../../src/Wails.Net.Application.Linux/LinuxIpcOriginGuard.cs)）**

- 仅拦截 IPC 请求（`POST /wails/*`，含 Streams 的 `POST /wails/stream/*`）；静态资源与 GET 不受影响。
- `Origin` 头存在：
  1. `Origin: null`（不透明源，sandbox / `data:` 子帧）：`Sec-Fetch-Site` 为 `cross-site` / `same-site` → 拒绝；否则仅当窗口 URL 协议为 `wails` 时容忍（WebKitGTK 可能把自定义协议页面的 Origin 序列化为字面量 `null`，一律拒绝会破坏应用自身 IPC，此时由第 1 层脚本守卫兜底）；其它协议（`http`/`https`/`about`）或无窗口 URL → 拒绝；
  2. 归一化后与窗口当前页面（`WebView.GetUri()`，回退 `_currentUrl`）比较：scheme / host / port 大小写不敏感，忽略路径 / 查询 / 片段，去除 `http:80` / `https:443` 默认端口；不一致 → 拒绝。
- `Origin` 缺失：按 `Sec-Fetch-Site` 兜底，`cross-site` / `same-site` → 拒绝；两者都缺失（旧版 WebKitGTK 未透传）→ 放行，由第 1 层脚本守卫兜底。
- 拒绝时在 `OnWailsSchemeRequest` 直接返回 `403 Forbidden` 并记录 `LogWarning`（窗口名 / 方法 / 路径，不含请求体）。
- 合法请求的 `Origin` 会写入 `WebviewRequest.Origin`，供管线/后续端点复用。

**平台限制（与上游一致）**

- WebKitGTK 的 `URISchemeRequest` 不暴露发起帧，无法直接按帧判定；帧来源只能由脚本守卫判定。
- 旧版 WebKitGTK 可能不透传 `Origin` / `Sec-Fetch-Site`，此时请求层退化为放行，仅剩脚本守卫（非硬边界）。
- **同源**子帧与主帧共享 Origin，无法也不需要在请求层区分（同源子帧本就可访问主帧 DOM 与运行时）。

**测试覆盖**

- 单元（纯逻辑，无 GTK 依赖，可在任意平台运行）：
  [LinuxIpcOriginGuardTests.cs](../../tests/Wails.Net.Application.Linux.Tests/LinuxIpcOriginGuardTests.cs)（21 项：同源/跨源/不透明源（wails 页面容忍、http 页面拒绝、`cross-site` 拒绝、无 URL 拒绝）/默认端口/大小写/`Sec-Fetch-Site`/大小写头名/非法 Origin/归一化边界）；
  [LinuxFrameGuardTests.cs](../../tests/Wails.Net.Application.Linux.Tests/LinuxFrameGuardTests.cs)（9 项：信任标记、跨源判定、三类阻断、包裹条件、空参数异常）。
- 真实 WebKitGTK 行为（`Origin` 是否透传、自定义协议跨源请求是否被发送）需在 Linux 桌面会话中手动验证，无法在 Windows 自动化环境中覆盖。

---

## 12. v3.1.0 基线（2026-11-01）对比与实现记录

> 复核日期：2026-11-03。复核方式：GitHub Releases API（`wailsapp/wails`，`/releases`）+ npm registry（`@wailsio/runtime`）+ 本仓库 `src/` 与 `tests/` 代码逐项确认。**未做 v3.1.0 tag 源码逐行复核**（本地无参考源码目录），结论依据 release notes 与本仓库实现。

### 12.1 基线确认

| 项目 | 值 | 说明 |
|------|----|------|
| Wails v3 最新正式发布 | **`v3.1.0`**（2026-11-01） | weekly minor；`v3.0.0`（2026-10-26 GA）之后首个 minor |
| 预发布（不作基线） | `v3.2.0-nightly.20261102` | 每日 nightly，按 AGENTS.md §1.1.1 排除 |
| `@wailsio/runtime` | **`3.1.0`** | 与 Wails 发布版本号一致 |
| 上一基线 | `v3.0.0`（2026-10-26 GA，见 §11） | 本次升级目标：`v3.0.0` → `v3.1.0` |

**结论**：基线需要升级，由 `v3.0.0` 升级到 **`v3.1.0`**；AGENTS.md §1/§1.1.1/§8 已同步。

### 12.2 差异清单（v3.0.0 → v3.1.0）

状态说明：✅ 已实现 / 已具备（无需改动）；⏳ 待实现；⛔ 不适用（.NET 或本仓库无对应机制）。

| # | v3.1.0 变更（release notes 摘要） | 状态 | 说明 |
|---|----------------------------------|------|------|
| 1 | `#6276` Emit 在 hook 锁内快照事件钩子 | ✅ 已实现 | [EventProcessor.cs](../../src/Wails.Net.Application/Events/EventProcessor.cs) 新增 `_hooksLock`；`RegisterHook` 加锁，`EmitCore` 锁内 `ToArray()` 后遍历 |
| 2 | `#6287` Run 之前投递的主线程任务暂存至 Run 启动 | ✅ 已实现 | [Application.cs](../../src/Wails.Net.Application/Application.cs) 新增 `_pendingMainThreadActions`（`ConcurrentQueue`），`Run` 启动时先 `FlushPendingMainThreadActions()`（FIFO） |
| 3 | `#6390` Run 之前的对话框直接“无操作”，不再永久等待 | ✅ 已实现 | [DialogManager.cs](../../src/Wails.Net.Application/Managers/DialogManager.cs) 新增运行状态判定；未运行时消息框返回 `-1`、文件框返回 `null`；DI 路径经 `AttachRunningState` 补挂 |
| 4 | `#6072` 从 settings portal 读取暗色模式 | ✅ 已实现 | 新增 [LinuxAppearancePortal.cs](../../src/Wails.Net.Application.Linux/LinuxAppearancePortal.cs)（`org.freedesktop.portal.Settings.Read/ReadOne`）；`LinuxPlatformApp.IsDarkMode` 优先 portal，再回退 gsettings/环境变量 |
| 5 | `#6017` 系统托盘 `SetTooltip` | ✅ 已具备 | Windows `NOTIFYICONDATA.szTip`、Linux `Gtk.Window.SetTooltipText`、`SystemTrayManager.SetTooltip` 均已存在 |
| 6 | `#6166` `Window.SetZoom` 允许小于 1.0 | ✅ 已具备 | 无下限钳制（[Win32WebviewWindow.cs](../../src/Wails.Net.Application.Windows/Win32WebviewWindow.cs#L2670-L2688) 直接写 `ZoomFactor`；Linux 直接透传 `SetZoomLevel`）。平台限制：WebView2 合法区间 0.25–5.0 |
| 7 | `#6304` GitHub updater provider 使用正确的资产 URL | ✅ 已具备 | 使用 `browser_download_url`，未使用 API `url` |
| 8 | `#6395` 混合大小写资源扩展名 | ✅ 已具备 | `AssetServer.GetMimeType` 对扩展名 `ToLowerInvariant()` |
| 9 | `#6387`/`#6388` 全屏/最大化下不被新尺寸约束改变大小 | ✅ 已具备 | `SetMinSize`/`SetMaxSize` 仅记录，不在约束变更时 resize；全屏走 `SetWindowPos`，不经 `WM_GETMINMAXINFO` |
| 10 | `#6386` Explorer 重启后重建托盘图标（去抖 1 秒） | ✅ 已实现 | 新增 [TrayIconRebuildScheduler.cs](../../src/Wails.Net.Application.Windows/TrayIconRebuildScheduler.cs)（1 秒去抖合并）；[PlatformMessageWindow.cs](../../src/Wails.Net.Application.Windows/PlatformMessageWindow.cs) 顶层窗口接收 `TaskbarCreated`（仅消息窗口收不到 `HWND_BROADCAST`）后经 [Win32SystemTray.cs](../../src/Wails.Net.Application.Windows/Win32SystemTray.cs) 的 `NotifyTaskbarCreated` 分发重建；真实 Explorer 重启需 GUI 验证 |
| 11 | `#6371` 终端关闭时结束 dev 派生进程 | ✅ 已实现 | 新增 [DevProcessLifetimeGuard.cs](../../src/Wails.Net.Cli/Build/DevProcessLifetimeGuard.cs)：Windows 作业对象（`KILL_ON_JOB_CLOSE`，`[DllImport]` 手动声明，未引 `PInvoke.*`）+ Unix `PosixSignalRegistration`（`SIGHUP`/`SIGTERM`）；`DevCommand` 将 `dotnet watch` 与前端 dev server 纳入守护（[FrontendToolchain.cs](../../src/Wails.Net.Cli/Build/FrontendToolchain.cs) 新增 `onStarted` 回调）；`DevProcessCleanup.TerminateSync` 供信号处理器同步终止。真实关闭控制台窗口 / SIGHUP 投递需真机验证 |
| 12 | `#6346` GTK/macOS 屏幕坐标使用 DIP 空间 | ✅ 已实现（Linux + macOS） | 新增 [LinuxDipCoordinates.cs](../../src/Wails.Net.Application.Linux/LinuxDipCoordinates.cs)：DIP↔物理像素换算（1.0/1.25/1.5/2.0、负坐标、无效因子回退 1.0）与目标显示器选择；`SetPosition`/`Centre` 全程 DIP 计算，仅调用 `wmctrl` 前按目标显示器 `ScaleFactor` 换算。macOS 新增 [MacDipCoordinates.cs](../../src/Wails.Net.Application.MacOS/MacDipCoordinates.cs)（纯逻辑层，可单测）：AppKit point≡DIP 的 Y 轴翻转/逆运算、DIP↔物理换算、`NSScreen.VisibleFrame` 居中与多屏目标选择；`MacOSWebviewWindow` 的 `SetPosition`/`Centre`/`SetSize`/`GetPosition`/`GetSize` 与 `MacOSPlatformApp.EnumerateScreens` 已接线；`#if MACOS` 分支在 Windows 主机不参与编译，需 macOS 实机验证 |
| 13 | `#6401`/`#6425`/`#6451` HEAD 请求不返回响应体 | ✅ 已实现 | [AssetServer.cs](../../src/Wails.Net.AssetServer/AssetServer.cs) 按 `HttpMethod` 判定 HEAD：状态码与响应头（`Content-Length`/`Content-Type`/`ETag`/`Accept-Ranges`/`Content-Range`）与 GET 一致，200/206/304/404/500 均不写 body；[BytesResult.cs](../../src/Wails.Net.AssetServer/Results/BytesResult.cs) 新增 `includeBody` 重载 |
| 14 | `#6424` 缺失的打包资源返回 404 | ✅ 已实现 | SPA 回退仅限导航请求（`/` 结尾、无扩展名、`.html`/`.htm`，或 `Accept` 含 `text/html`）；缺失的 `.js`/`.css` 等子资源直接 404 |
| 15 | `#6483` 重复前导斜杠路径归一化 | ✅ 已实现 | 新增 `NormalizeRequestPath`：折叠连续 `/`（含前导）、解析 `.`/`..` 并阻止越出根路径；`GetAssetBytes`/`ExtractAssetContent`/HTTP 入口统一使用 |
| 16 | `#6400` 仅发现精确的 index 文件 | ✅ 已实现 | 目录索引仅在路径以 `/` 结尾时尝试精确 `index.html`，不再误匹配 `not-index.html` |
| 17 | `#6398`/`#6399`/`#6403`/`#6405`/`#6406`/`#6409` 资源回退头、范围、MIME 嗅探细节 | ✅ 已实现 | 回退资源忽略 Range（返回完整 200 + 正确长度/类型），缺失资源不再回退为 HTML；[ContentTypeSniffer.cs](../../src/Wails.Net.AssetServer/ContentTypeSniffer.cs) 逐条对齐 Go `net/http/sniff.go`（Go 1.24.0 源码；512 字节、签名表顺序、掩码匹配、HTML 标签终止符 `isTT`、UTF BOM、文本控制字符集合且不校验 UTF-8、charset 后缀），扩展名映射优先、失败回退 `application/octet-stream`；`Content-Type` 基于完整内容在 Range 切片前判定，HEAD 复用同一结果仅跳过 body；SPA 回退资源不嗅探 |
| 18 | CLI dev/打包/模板/缓存等大批修复（`#6412`–`#6498` 区间） | ⚠️ 部分实现 | ✅ `#6478` `icon --sizes`（1–256 校验、0 忽略、至少一个非零、失败退出码 1 且保留旧 `icon.ico`、按尺寸真实缩放生成多帧 ICO）；✅ `#6480` 打包失败保留旧产物（同目录暂存 → 成功后原子替换、Unix 权限保留、清理暂存）；⛔ 其余按组不适用（Taskfile/Go cache/`printf` 格式替换/gosod/symlink/DMG/syso/.icns/`dotnet tool` 安装等，见 §12.4 第 10 条）；`#6498` 文件权限保留待 Linux 实机复核 |
| 19 | `#6521` CGO-free macOS/Linux 后端实验 | ⛔ 不适用 | Go 独有（CGO）机制 |
| 20 | `#6531` 原生 YAML 构建、便携打包与签名 | ⛔ 不适用 | Wails.Net 使用 MSBuild + `wails` CLI，无 YAML 构建体系 |
| 21 | `#6367` 通知被关闭不算点击 | ⛔ 不适用 | 通知插件未上报点击事件（Windows 走 PowerShell Toast，无回调） |
| 22 | `#6284` 始终提供托盘 Menu 属性 | ⛔ 不适用 | Wails.Net 托盘直接绑定平台实现，无 Go 侧 Menu 序列化 |
| 23 | `#6326` WebView2 缺 `Content-Length` 时保留请求体 | ⛔ 不适用 | 未自建 WebView2 资源请求拦截；IPC 走 `wails://` scheme handler |
| 24 | `#6392`/`#6407`/`#6411` 桌面条目发现与 XDG 搜索路径 | ⛔ 不适用 | 本仓库无 Go CLI 的桌面条目启动逻辑（仅生成 `.desktop`） |
| 25 | Go 内部性能/分配优化（`#6391`/`#6394`/`#6402`/`#6419`/`#6420`/`#6428`/`#6443`/`#6449`/`#6453`–`#6455`/`#6478`/`#6482`/`#6490`/`#6499` 等） | ⛔ 不适用 | Go 实现细节，.NET 侧内存/序列化模型不同 |
| 26 | store/SQLite 系列（`#6444`–`#6456`） | ⛔ 不适用 | 本仓库无 Go store 子系统 |
| 27 | 发布/CI 工具链（`#6373`/`#6438`/`#6493`/`#6495`/`#6497`/`#6519`/`#6533`/`#6538`） | ⛔ 不适用 | 属 Wails 自身发布与 Go 工具链 |
| 28 | v2.17.1 `GHSA-r5j2-ghh9-2jqw`（Linux WebKit 安全） | ⛔ 不适用（v3 维度） | 属 v2 发布流；v3 对应的 Linux 子帧校验已在本仓库完成（见 §11.2/§11.6） |

### 12.3 受影响子系统矩阵

| 子系统 | v3.1.0 相关变更 | Wails.Net 状态 |
|--------|----------------|----------------|
| 事件系统 | `#6275` 监听器投递顺序、`#6276` 钩子锁 | 前端 mailbox 已串行；本地监听器同步按调用顺序分发；钩子锁已补齐 |
| 生命周期/主线程 | `#6287` Run 前任务暂存 | 已实现 |
| 对话框 | `#6390` Run 前不挂起 | 已实现 |
| Linux 外观 | `#6072` settings portal | 已实现（需 Linux 桌面验证） |
| Linux 窗口 | `#6346` 多 DPI DIP 坐标 | 已实现（换算为纯函数并有单测；真实 GTK4/wmctrl 需 Linux 桌面验证） |
| macOS 窗口/屏幕 | `#6346` Point/DIP 语义 | 已实现纯逻辑层（`MacDipCoordinates`）与窗口/屏幕接线；`#if MACOS` 分支需 macOS 实机编译验证 |
| Windows 托盘 | `#6386` Explorer 重启重建 | 已实现（去抖 1 秒；真实 Explorer 重启需 GUI 验证） |
| 资源服务器 | HEAD/404/斜杠/index/嗅探系列 | 已实现 HEAD、404、斜杠归一化、精确 index、回退 Range/头、MIME 内容嗅探 |
| CLI dev/构建 | 进程清理、打包、模板、缓存 | 终端关闭清理、图标尺寸校验、打包原子替换已实现；其余批次 ⛔ 不适用/待评估 |
| 通知/托盘 API | `#6017`（已有）、`#6367`/`#6284`（不适用） | 已具备 / 不适用 |

### 12.4 本次实现记录

1. **事件钩子并发安全（`#6276`）**
   - `EventProcessor`：新增 `_hooksLock`；`RegisterHook` 在锁内 `Add`；`EmitCore` 在锁内 `ToArray()` 快照后再执行钩子。
   - 测试：`RegisterHook_ConcurrentWithEmit_DoesNotThrow`（双线程 2000 次 Emit / 2000 次注册，零异常）。

2. **Run 前主线程任务延迟执行（`#6287`）**
   - `Application`：`DispatchOnMainThread` 在 `!_isRunning` 时入队；`Run` 置位运行标记后、`OnBeforeStart` 之前按 FIFO 执行；单个任务异常仅记录日志，不阻断后续。
   - 测试：`DispatchOnMainThread_BeforeRun_ExecutesFifoAfterRunStarts`、`DispatchOnMainThread_WhileRunning_InvokesImmediately`。

3. **Run 前对话框不挂起（`#6390`）**
   - `DialogManager`：新增可选 `Func<bool>` 运行状态判定与 `AttachRunningState`（DI 模式补挂）；未运行时消息框返回 `-1`、文件选择返回 `null`，且不调用平台层。
   - 测试：`DialogManagerTests` 新增 5 项（是否运行/选项重载/文件类对话框/运行中委托/补挂不覆盖）。

4. **Linux 暗色模式 settings portal（`#6072`）**
   - 新增 `LinuxAppearancePortal`：`Read` 失败时回退 `ReadOne`，500ms 超时，读取 `org.freedesktop.appearance` / `color-scheme`（0 无偏好、1 暗色、2 亮色）。
   - `LinuxPlatformApp.IsDarkMode`：portal 优先；不可用时保持 gsettings / `GTK_THEME` / `COLOR_SCHEME` 回退链。
   - 测试：`LinuxAppearancePortalTests`（6 项：映射 4 项 + 非 Linux 静默回退）。

5. **Explorer 重启托盘重建（`#6386`）**
   - 新增 `TrayIconRebuildScheduler`：窗口期内多次 `TaskbarCreated` 合并为一次重建，最后一次广播后延迟 1 秒执行；`Dispose` 后可安全取消。
   - `PlatformMessageWindow`（顶层窗口）接收 `TaskbarCreated`（托盘的仅消息窗口收不到 `HWND_BROADCAST`），调用 `Win32SystemTray.NotifyTaskbarCreated()` 向所有可见托盘实例分发；重建前二次校验可见性/销毁状态。
   - 测试：`TrayIconRebuildSchedulerTests`（6 项：合并、二次重建、释放取消、释放后忽略、无实例空操作、消息注册非 0）。

6. **终端关闭时清理 dev 派生进程（`#6371`）**
   - 新增 `DevProcessLifetimeGuard`（`IDevProcessLifetimeBackend` 可注入便于单测）：Windows 创建带 `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` 的作业并 `AssignProcessToJobObject`；Unix 用 `PosixSignalRegistration` 注册 `SIGHUP`/`SIGTERM`，执行同步清理后保留默认终止语义（`SIGINT` 仍由 System.CommandLine 处理）。
   - `DevCommand` 以 `using` 持有守卫，`dotnet watch` 启动后立即 `TryAttach`；前端 dev server 通过 `FrontendToolchain.DevAsync(onStarted)` 回调纳入守护。
   - `DevProcessCleanup.TerminateSync`：信号处理器内同步终止进程树（`KillTree` 对已退出进程为无效操作，重复调用安全）。
   - 测试：`DevProcessLifetimeGuardTests`（12 项）+ `DevProcessCleanupTests` 新增 2 项（共 11 项）。

7. **资源服务器 P2 批次（`#6401`/`#6425`/`#6451`/`#6424`/`#6483`/`#6400`/`#6398`）**
   - HEAD：`ServeAssetAsync` 判定 `HttpMethod == HEAD` 后仍走 GET 的读取/协商/日志流程，仅跳过响应体；`BytesResult.WriteAsync(includeBody:false)` 保留 206 的 `Content-Range`、`Content-Length` 等头；404/500 处理器同样按方法决定是否写 body。
   - 404：新增 `ShouldUseSpaFallback`（目录/无扩展名/`.html`/`Accept: text/html` 才回退），缺失子资源返回 404。
   - 归一化：`NormalizeRequestPath` 折叠重复斜杠并解析 `.`/`..`（越根丢弃），`GetAssetBytes`/`ExtractAssetContent`/HTTP 入口统一调用。
   - 精确 index：`GetDirectoryIndexPath` 仅对 `/` 结尾路径追加精确 `index.html`。
   - 回退资源：`CreateResult(isFallback:true)` 忽略 Range，返回完整 200 与正确 `Content-Type`/长度。
   - 测试：`AssetServerTests` 新增 12 项（HEAD、404、归一化、精确 index、回退 Range），资源服务器测试项目共 305 项全部通过。
   - ⛔ 不适用：`#6425` 中 `/wails/runtime.js` 专属路由——本移植前端运行时由 `@wails-net/runtime` npm 包提供，无该内置路由（维持不适用结论，不做硬凑）。

8. **资源服务器 MIME 内容嗅探（`#6398` 余项，2026-11-04 按 Go 源码重写）**
   - `ContentTypeSniffer`（internal 纯静态）逐条对齐 Go `net/http/sniff.go`（Go 1.24.0 源码）：前 512 字节、签名表顺序（HTML → XML → PDF/PS → BOM → 图像 → 音视频 → 字体 → 归档 → WASM → 文本）；`htmlSig` 使用 `firstNonWS` 且要求标签终止符（空格/`>`）；`maskedSig` 对 WebP/AIFF/ID3/Ogg/MThd/AVI/WAVE/EOT 按掩码比较（RIFF 子类型不再统一判为 `audio/wave`）；MP4 按大端盒长度校验并 4 字节步进扫描 `mp4`（跳过 offset 12）；文本嗅探仅排除 Go 控制字符集合（0x09/0x0A/0x0C/0x0D/0x1B/0x7F 允许，不校验 UTF-8），文本类返回带 `; charset=utf-8`（BOM 返回 utf-16be/utf-16le）。
   - `AssetServer.ServeAssetCoreAsync`：仅当 `GetMimeType` 返回默认 `application/octet-stream` 且非 SPA 回退时嗅探；已知扩展名优先；`Content-Type` 在 Range 切片前基于完整内容判定，206 的 `Content-Range`/`Content-Length` 不受影响；HEAD 返回嗅探后的类型但不写 body。
   - 测试：`ContentTypeSnifferTests` 重写并扩至 45 项（含 HTML 终止符、BOM、掩码签名、MP4 盒校验、文本控制字符、512 字节截断）；`AssetServerTests` 中扩展名缺失二进制用例补控制字节后全类 89/89
   - **收尾核对（2026-11-04）**：以 Go 1.24.0 `src/net/http/sniff.go` 源码逐条比对，签名表 31 项（17 个 HTML 标签 + XML + PDF/PS + 3 个 BOM + 7 个图像 + 8 个音视频/MP4 + 6 个字体 + 6 个归档/WASM + 文本）、顺序与边界字节完全一致 → **无缺口**。等价性说明：`boxSize < 12` 为提前返回（Go 对 4/8/0 的盒长度同样必然返回空）；UTF BOM 的 4 字节掩码中第 4 字节被 `\x00` 掩掉，与本实现的 3 字节/2 字节比较等价；`htmlSig` 的 `db &= 0xDF` 折叠等价于仅对 `a`–`z` 减 32；`maskedSig` 的 RIFF/FORM/EOT 掩码跳过位与本实现的偏移比较等价。

9. **GTK 多 DPI DIP 坐标（`#6346`）**
   - 新增 `LinuxDipCoordinates`（纯函数）：`NormalizeScaleFactor`（<=0/NaN/Infinity 回退 1.0）、`DipToPhysical`（floor）、`PhysicalToDip`（ceil）、`SelectTargetMonitor`（命中→最近→null）；`LinuxMonitorInfo` 描述 DIP 几何与缩放因子。
   - `LinuxWebviewWindow`：`SetPosition`/`Centre` 全程 DIP 计算并缓存（`GetPosition`/`GetSize` 保持 DIP 语义），抽出 `MoveWindowByDipBounds` 仅在 `wmctrl` 调用前按目标显示器缩放因子换算物理像素。
   - 测试：`LinuxDipCoordinatesTests`（27 项：多缩放往返、负坐标、无效因子回退、显示器选择/最近距离/负原点）。

10. **CLI 批次 `#6412`–`#6498` 适用性核对与实现**
   - ✅ `#6478`：[IconCommand.cs](../../src/Wails.Net.Cli/Commands/IconCommand.cs) 新增 `--sizes`（默认 `256,128,64,48,32,16`）：先校验尺寸再读源文件，非法尺寸/超范围（>256）退出码 1 且不写 `icon.ico`；`GenerateIcoAsync` 按尺寸缩放并生成多帧 PNG 帧 ICO。
   - ✅ `#6480`：[Packager.cs](../../src/Wails.Net.Cli/Build/Packager.cs) 改为“同目录暂存文件夹 → 全部格式打包成功 → 保留 Unix 权限并原子替换”，失败时保留上一次产物；暂存目录在 `finally` 中清理。
   - ⛔ 不适用分组（对照上游逐条核对）：Taskfile/Go cache/shell 链、`printf` 风格格式替换（C# 使用插值）、gosod/glob/symlink 语义、DMG/syso/.icns 生成、GitHub 安装器/HTTP 下载（.NET 走 `dotnet tool`/NuGet）、Go 运行时/服务端专属项。
   - 待评估：`#6498` 文件权限保留（已按 Unix 语义实现，需 Linux 实机复核）。

11. **macOS DIP 坐标补全（`#6346` macOS 维度，2026-11-04）**
   - 新增 `MacDipCoordinates`（`internal` 纯逻辑，`#if` 无关，Windows 主机可编译单测）：`NormalizeScaleFactor`、`DipToPhysical`（floor）、`PhysicalToDip`（ceil）、`ToAppKitBottomLeftY`/`ToDipTopLeftY`（严格互逆，`appKitY = primaryHeight - height - topY`）、`CentreDipBounds`、`SelectTargetMonitorIndex`（命中→最近→-1）；`MacMonitorInfo` 描述单屏 DIP 几何（top-left 空间）与 `backingScaleFactor`。
   - `MacOSWebviewWindow`：`SetSize` 先 `SetContentSize` 再按请求尺寸 `SetFrame`（对齐上游 `windowSetSize`）；`SetPosition`/`GetPosition` 在 top-left DIP 与 AppKit bottom-left 之间翻转；`Centre` 依据 `NSScreen.VisibleFrame`（转 top-left DIP）居中，目标屏优先 `_window.Screen`、回退窗口中心命中/最近屏、再回退 `NSScreen.MainScreen`；`GetSize` 返回 frame DIP 尺寸，与 `SetSize` 一致。
   - `MacOSPlatformApp.EnumerateScreens`：DIP 几何取 point 值，物理几何改用 `MacDipCoordinates.DipToPhysical`（修正负坐标截断方向）。
   - 测试：`MacDipCoordinatesTests` 37/37 通过（Windows 主机，`#else` 骨架程序集 + `InternalsVisibleTo`）。
   - **未验证**：`#if MACOS` 分支未在 macOS 主机编译/运行（Windows 主机不参与该分支编译）。

12. **本轮代码审查修复（2026-11-04）**
   - [PlatformMessageWindow.cs](../../src/Wails.Net.Application.Windows/PlatformMessageWindow.cs)：`TaskbarCreated` 消息 ID 为 0（`RegisterWindowMessage` 失败时返回 `WM_NULL`）时不再触发托盘重建，避免误判。
   - [TrayIconRebuildScheduler.cs](../../src/Wails.Net.Application.Windows/TrayIconRebuildScheduler.cs)：去抖回调改为在 `_gate` 锁内执行，`Dispose` 返回后不会再有 `NIM_ADD` 并发执行（消除竞态）。
   - `ContentTypeSniffer` 按 Go 源码整体重写（见第 8 条），修复上一轮与上游的语义偏差（RIFF 子类型误判、HTML 缺终止符、文本类型缺 charset、文本判定过严、Ogg/GZIP/ZIP/RAR/MP4 签名不一致）。
   - 其余审查范围（`EventProcessor`、`Application`、`DialogManager`、`LinuxAppearancePortal`、`Win32SystemTray`、`DevProcessLifetimeGuard`/`DevProcessCleanup`/`DevCommand`/`FrontendToolchain`、`LinuxDipCoordinates`/`LinuxWebviewWindow`、`AssetServer`/`BytesResult`、`IconCommand`、`Packager`）逐项复核：未发现阻塞性缺陷；`src/` 未新增反射违规（仅既有白名单 `PlatformFactory`、`CapabilityManifestValidator` 使用 `System.Reflection`）。

### 12.5 验证结果

- 构建（0 警告 0 错误）：`Wails.Net.Application`、`Wails.Net.Application.Linux`、`Wails.Net.Application.Tests`、`Wails.Net.Application.Linux.Tests`。
  - 2026-11-03 追加（本次 P1/P2 批次）：`Wails.Net.Application.Windows`（1 个既有 MSB3277 WebView2 版本冲突警告，非本次引入）、`Wails.Net.Application.Windows.Tests`、`Wails.Net.Cli`、`Wails.Net.Cli.Tests`、`Wails.Net.AssetServer`、`Wails.Net.AssetServer.Tests` 均编译通过，0 错误。
- 定向单元测试（TUnit，Windows 主机）：
  - `DialogManagerTests`：18/18 通过；
  - `EventProcessorTests`：22/22 通过（含并发用例）；
  - `ApplicationTests.DispatchOnMainThread_*`：2/2 通过；
  - `LinuxAppearancePortalTests`：6/6 通过；
  - `LinuxPlatformAppTests` 暗色相关：4/4 通过；
  - `TrayIconRebuildSchedulerTests`：6/6 通过（#6386）；
  - `DevProcessLifetimeGuardTests`：12/12、`DevProcessCleanupTests`：11/11 通过（#6371）；
  - `LinuxDipCoordinatesTests`：27/27 通过（#6346，Windows 主机编译 Linux 项目）；
  - `AssetServerTests`：85/85 通过（资源服务器批次；项目全量 305 项由实现方跑通）。
  - 2026-11-04 追加（MIME 嗅探 + CLI 批次）：`ContentTypeSnifferTests` 45/45、资源服务器测试项目 354/354（其中 `AssetServerTests` 类 89/89，含 PNG 嗅探/扩展名优先/Range 206/HEAD）、`IconCommandTests` 15/15、`PackagerTests` 37/37、`PackagerDebRpmTests` 6 通过 2 跳过（Linux 专属用例）、`PackagerBundleTests` 3/3。
  - 2026-11-04 追加（macOS DIP + 审查修复）：`MacDipCoordinatesTests` 37/37（`Wails.Net.Application.MacOS.Tests` 全量 56/56）、`TrayIconRebuildSchedulerTests` 6/6、`PlatformMessageWindowTests` 2 通过 2 跳过（GUI 用例）。
- **全量构建（2026-11-04，非 Android）**：`src/` 与 `tests/` 全部非 Android 项目编译通过，0 错误；仅 `Wails.Net.Plugins.Mobile.Tests` 失败（`CS0246: MobilePlugin`，`src/Wails.Net.Plugins.Mobile` 中无该类型，**既有问题**，本轮未触碰）。Android 4 个项目（`Application.Android`、`Bundle.Android`、`Application.Android.Tests`、`Demo.Android`）因本机未安装 `android` 工作负载报 `NETSDK1147`，按既有约定跳过。
- **全量单元测试（2026-11-04，Windows 主机，GUI 默认跳过）**：共 3380 项，通过 3129、失败 7、跳过 244。
  - 失败 7 项均为**既有环境/预期问题**，非本轮引入：`Application.Linux.Tests` 2 项（Windows 主机无 GirCore/GTK 与 Linux 平台注册）、`Plugins.FileSystem.Tests` 1 项（测试期望名称 `fs`，实现为 `filesystem`）、`Plugins.FsWatch.Tests` 1 项（期望 `fswatch`，实现为 `fs-watch`）、`SourceGenerators.Tests` 2 项（分析器/测试工程既有断言）、`Application.Windows.Tests` 1 项 `IsOnMainThread_ReturnsFalse_WhenCalledFromDifferentThread`（线程池线程复用导致全量运行时偶发；单独运行该类 8/8 通过）。
  - `Wails.Net.Application.GuiContract.Tests` 为共享库（无测试主机入口），其契约用例由 Windows/Linux/Android 测试工程执行，不计入独立运行。

- GUI 用例按约定默认跳过（未设置 `WAILS_RUN_GUI_TESTS`）。
- **未验证**：真实 Explorer 重启后的托盘重建、真实关闭控制台窗口 / 终端 SIGHUP 的派生进程清理、真实 WebKitGTK 下 portal 读取与 GTK4/wmctrl 多 DPI 坐标、macOS `#if MACOS` 分支（Windows 主机不参与编译，需 macOS 实机编译/运行验证）、`#6498` 在 Linux 实机的权限保留行为。

### 12.6 基线维护结论

- 当前基线：**`v3.1.0`**（weekly，不含 nightly）。
- 2026-11-04 复核：GitHub releases/tags 中最新正式版仍为 `v3.1.0`（2026-11-01），其上方仅有 `v3.2.0-nightly.20261101`/`0211`/`0311` 预发布，无 `v3.1.x` hotfix → **基线不变**。
- 建议节奏：每次 weekly 发布后复核 release notes，按本表结构追加小节；本轮 P1（托盘重建、dev 终端关闭清理、GTK DIP）、资源服务器 P2 批次（含 MIME 内容嗅探）、CLI 批次核对、macOS DIP 补全与代码审查修复均已完成，并完成一次非 Android 全量构建/测试（3380 项，失败 7 项均为既有环境/预期问题）；后续待办为 `#6498` Linux 实机复核、macOS `#if MACOS` 实机编译验证，以及真实桌面环境下的托盘重建/信号清理/多 DPI 验证。


