# Wails.Net × Wails v3 最新开发分支对比：可参考项确认

> 分析日期：2026-08-07
> 对比对象：wailsapp/wails **master 分支 v3/ 目录**（Wails v3 的实际开发流），当前版本 **v3.0.0-beta.4**（2026-08-05）
> 基线：Wails.Net 的 AGENTS.md 标注参考 `wails v3.0.0-*`（alpha 时代）

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
